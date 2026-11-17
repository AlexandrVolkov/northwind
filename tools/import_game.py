#!/usr/bin/env python3
"""Import original MAP2/SEG2 terrain and a verified SPR frame range.

Input: extracted game directory, or an ISO with InstallShield DATA1.CAB.
Game binaries are never executed. Generated assets belong in assets/imported,
which is deliberately excluded from Git. See docs/formats.md for evidence.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import struct
import subprocess
import tempfile
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]


def u32(data: bytes, offset: int) -> int:
    return struct.unpack_from('<I', data, offset)[0]


def extract_iso(source: Path, destination: Path) -> None:
    """Read the primary ISO9660 tree; reject unsafe/ambiguous file paths."""
    with source.open('rb') as stream:
        stream.seek(16 * 2048)
        descriptor = stream.read(2048)
        if descriptor[:7] != b'\x01CD001\x01':
            raise ValueError('Expected an ISO9660 primary volume descriptor')
        root = descriptor[156:190]
        visited = set()
        def walk(extent: int, size: int, relative: PurePosixPath) -> None:
            if extent in visited:
                raise ValueError('Cyclic ISO directory')
            visited.add(extent)
            if size > 8 * 1024 * 1024:
                raise ValueError('Oversized ISO directory')
            stream.seek(extent * 2048)
            data = stream.read(size)
            cursor = 0
            while cursor < len(data):
                length = data[cursor]
                if not length:
                    cursor = (cursor // 2048 + 1) * 2048
                    continue
                record = data[cursor:cursor + length]
                cursor += length
                if len(record) < 34:
                    raise ValueError('Truncated ISO directory record')
                name = record[33:33 + record[32]]
                if name in (b'\x00', b'\x01'):
                    continue
                name = name.decode('ascii').split(';')[0].rstrip('.')
                if not name or name in ('.', '..') or '/' in name or '\\' in name:
                    raise ValueError('Unsafe ISO path')
                path = relative / name
                extent, size = u32(record, 2), u32(record, 10)
                if record[25] & 2:
                    walk(extent, size, path)
                else:
                    target = destination.joinpath(*path.parts)
                    target.parent.mkdir(parents=True, exist_ok=True)
                    stream.seek(extent * 2048)
                    content = stream.read(size)
                    if len(content) != size:
                        raise ValueError('Truncated ISO file')
                    target.write_bytes(content)
        walk(u32(root, 2), u32(root, 10), PurePosixPath())


def decode_spr(data: bytes) -> list[list[int]]:
    """u16 count, count relative u24 offsets, then skip/literal RLE."""
    if len(data) < 2:
        raise ValueError('Truncated SPR')
    count = int.from_bytes(data[:2], 'little')
    base = 2 + count * 3
    if base > len(data):
        raise ValueError('Truncated SPR offsets')
    offsets = [int.from_bytes(data[2 + i * 3:5 + i * 3], 'little') for i in range(count)]
    offsets.append(len(data) - base)
    if not offsets or offsets[0] != 0 or any(a > b for a, b in zip(offsets, offsets[1:])):
        raise ValueError('Invalid SPR offset table')
    frames = []
    for start, end in zip(offsets, offsets[1:]):
        if not 0 <= start <= end <= len(data) - base:
            raise ValueError('SPR offset outside file')
        record = data[base + start:base + end]
        pixels, cursor = [], 0
        while cursor < len(record):
            token = record[cursor]
            cursor += 1
            count = token & 127
            if token & 128:
                if cursor + count > len(record):
                    raise ValueError('Truncated SPR literal run')
                pixels.extend(record[cursor:cursor + count])
                cursor += count
            else:
                pixels.extend([-1] * count)
            if len(pixels) > 4_000_000:
                raise ValueError('Oversized SPR frame')
        frames.append(pixels)
    return frames


def decode_spr_frame(data, frame):
    if len(data) < 2:
        raise ValueError('Truncated SPR')
    count = int.from_bytes(data[:2], 'little')
    base = 2 + count * 3
    if not 0 <= frame < count or base > len(data):
        raise ValueError('Invalid SPR frame or table')
    offsets = [int.from_bytes(data[2+i*3:5+i*3], 'little') for i in range(count)] + [len(data)-base]
    if offsets[0] != 0 or any(a > b or b > len(data)-base for a, b in zip(offsets, offsets[1:])):
        raise ValueError('Invalid SPR offsets')
    record = data[base+offsets[frame]:base+offsets[frame+1]]
    return decode_spr(b'\x01\x00\x00\x00\x00' + record)[0]


def dos_filename(raw: bytes) -> str:
    """SCN names use space-padded DOS 8.3 stems, unlike host filenames."""
    name = raw.split(b'\0')[0].decode('ascii')
    if any(c in name for c in '/\\:') or name in ('', '.', '..'):
        raise ValueError('Unsafe scenario reference')
    if '.' in name:
        stem, extension = name.rsplit('.', 1)
        name = stem.rstrip(' ') + '.' + extension.rstrip(' ')
    return name.upper()


def scenario_text(raw: bytes) -> str:
    # Unlike README (CP1251), SCN names are DOS CP866, padded with FF.
    return raw.rstrip(b'\xff\0 ').decode('cp866')


def decode_scenario(data: bytes) -> dict:
    """Observed SCN layout: 20-byte header + count * 114-byte slot records.

    Export unknown bytes without assigning economy/diplomacy/army semantics.
    """
    if len(data) < 20:
        raise ValueError('Truncated SCN header')
    count = data[0]
    if data[1] > 5:
        raise ValueError('Unsupported SCN army mapping length')
    if len(data) != 20 + count * 114:
        raise ValueError('Unexpected SCN record count or size')
    slots = []
    for index in range(count):
        offset = 20 + index * 114
        record = data[offset:offset + 114]
        slots.append({
            'slot_index': index, 'file_offset': offset,
            'object_file': dos_filename(record[2:15]),
            'leader_name': scenario_text(record[37:57]),
            'settlement_name': scenario_text(record[94:114]),
            'raw_record_hex': record.hex(),
        })
    return {'format_version': 1, 'slot_count': count,
            'map_file': dos_filename(data[2:15]),
            'army_files': [f'ARMY__{value:02d}.DAT' for value in data[15:15 + data[1]]],
            'raw_header_hex': data[:20].hex(), 'slots': slots,
            'limitations': ['SCN boundaries, filenames, text and OBE common headers are decoded.',
                            'Slot order is not a proven team, nation or player identifier.',
                            'OBE common headers are decoded; coordinates and byte codes have unconfirmed semantics.']}


def decode_objects(data: bytes) -> list[dict]:
    """OBE chain: observed 34-byte common prefix, u32 length at +16.

    Coordinates and byte codes are candidates, not recovered game semantics.
    """
    records = []
    offset = 0
    while offset < len(data):
        if len(data) - offset < 34:
            raise ValueError(f'Truncated OBE prefix at {offset}')
        size = u32(data, offset + 16)
        if size < 34 or offset + size > len(data):
            raise ValueError(f'Invalid OBE record length {size} at {offset}')
        raw = data[offset:offset + size]
        records.append({'record_index': len(records), 'file_offset': offset,
                        'record_bytes': size, 'raw_code': raw[0], 'raw_byte_3': raw[3],
                        'raw_x': u32(raw, 4), 'raw_y': u32(raw, 8),
                        'raw_layer': raw[13], 'raw_record_hex': raw.hex()})
        offset += size
    return records


def decode_dat_label(raw: bytes) -> str:
    """EXE 0x7028 skips the two-byte '~' + style escape."""
    text = bytearray()
    offset = 0
    while offset < len(raw):
        if raw[offset] == 0x7e:
            if offset + 1 >= len(raw):
                raise ValueError('Truncated DAT text style escape')
            offset += 2
        else:
            text.append(raw[offset])
            offset += 1
    return text.decode('cp866')


def decode_template_commands(data: bytes, record_offset: int, boundary: int) -> list[dict]:
    commands = []
    for slot in range(6):
        offset = record_offset + 0xb + slot * 8
        raw = data[offset:offset+8]
        relative = u32(raw, 2)
        command = {'slot': slot, 'file_offset': offset, 'icon_frame': int.from_bytes(raw[:2], 'little'),
                   'mode': raw[6], 'raw_group': raw[7], 'raw_record_hex': raw.hex(),
                   'present': bool(relative), 'label': '', 'hotkey_scan_code': None}
        if relative:
            pointer = offset + 2 + relative
            if not boundary <= pointer < len(data)-2:
                raise ValueError('DAT command pointer outside tail')
            end = pointer + 2  # FF may itself be the hotkey's low byte or a style byte.
            while end < len(data) and data[end] != 0xff:
                end += 2 if data[end] == 0x7e else 1
            if end >= len(data):
                raise ValueError('Unterminated DAT command text')
            command.update({'tooltip_offset': pointer, 'hotkey_scan_code': int.from_bytes(data[pointer:pointer+2], 'little'),
                            'label': decode_dat_label(data[pointer+2:end]), 'raw_tooltip_hex': data[pointer:end+1].hex()})
        commands.append(command)
    return commands


def decode_templates(data: bytes) -> dict:
    """EXE 0x3593: u16 table byte count, then count/171 actual records."""
    if len(data) < 2:
        raise ValueError('Truncated DAT table')
    table_bytes = int.from_bytes(data[:2], 'little')
    boundary = 2 + table_bytes
    if table_bytes == 0 or table_bytes % 171 or boundary > len(data):
        raise ValueError('Invalid DAT table boundary')
    templates = []
    for offset in range(2, boundary, 171):
        record = data[offset:offset + 171]
        name_offset = offset + 7 + u32(record, 7)
        if not boundary <= name_offset < len(data):
            raise ValueError('DAT name pointer outside string section')
        end = data.find(b'\xff', name_offset)
        if end < 0:
            raise ValueError('Unterminated DAT name')
        templates.append({'template_index': len(templates), 'file_offset': offset,
                          'name_offset': name_offset, 'name': data[name_offset:end].decode('cp866'),
                          'width': record[1], 'height': record[2],
                          'sprite_base_candidate': int.from_bytes(record[5:7], 'little'),
                          'wrk_entry_offset': u32(record, 0x3b),
                          'runtime_record_bytes': record[0],
                          'initial_condition': record[0x45],
                          'initial_frame': int.from_bytes(record[0x48:0x4a], 'little'),
                          'construction': {'footprint_width': record[3], 'footprint_height': record[4],
                                           'terrain_mask': list(record[0x58:0x60]),
                                           'upfront_costs': list(struct.unpack_from('<4I', record, 0x7c))},
                          'commands': decode_template_commands(data, offset, boundary),
                          'raw_record_hex': record.hex()})
    return {'raw_header_hex': data[:2].hex(), 'table_bytes': table_bytes,
            'templates': templates, 'raw_tail_hex': data[boundary:].hex()}


def link_templates(records: list[dict], tables: dict, army_files: list[str]) -> None:
    for record in records:
        raw = bytes.fromhex(record['raw_record_hex'])
        record['template_index'] = raw[14]
        if record['raw_code'] >= len(army_files):
            raise ValueError('OBE army slot outside scenario mapping')
        filename = army_files[record['raw_code']]
        if filename not in tables:
            raise ValueError(f'Missing DAT table: {filename}')
        templates = tables[filename]['templates']
        if raw[14] >= len(templates):
            raise ValueError(f'OBE template index outside {filename}')
        template = templates[raw[14]]
        record.update({'template_file': filename, 'template_name': template['name'],
                       'template_offset': template['file_offset']})


def write_template_previews(tables: dict, read, palette: bytes, destination: Path) -> None:
    """Representative candidates only: strict RLE and exact DAT dimensions.

    Search at most 16 frames in each template's sprite range. Rejections remain
    visible; this does not prove the original animation state or anchor.
    """
    total = sum(len(table['templates']) for table in tables.values())
    atlas = Image.new('RGBA', (8 * 256, max(1, (total + 7) // 8) * 256))
    slot = 0
    for filename, table in tables.items():
        data = read(filename.replace('.DAT', '.SPR'))
        if len(data) < 2:
            raise ValueError('Truncated SPR')
        count = int.from_bytes(data[:2], 'little')
        base = 2 + count * 3
        if base > len(data):
            raise ValueError('Truncated SPR offsets')
        offsets = [int.from_bytes(data[2 + i * 3:5 + i * 3], 'little') for i in range(count)] + [len(data) - base]
        if not count or offsets[0] != 0 or any(a > b or b > len(data) - base for a, b in zip(offsets, offsets[1:])):
            raise ValueError('Invalid SPR offsets')
        templates = table['templates']
        for template in templates:
            start = template['sprite_base_candidate']
            next_bases = [t['sprite_base_candidate'] for t in templates if t['sprite_base_candidate'] > start]
            end = min([count, start + 16] + next_bases)
            preview = {'status': 'unresolved', 'rejections': [], 'frame': None,
                       'atlas_x': slot % 8 * 256, 'atlas_y': slot // 8 * 256,
                       'width': template['width'], 'height': template['height']}
            slot += 1
            template['preview'] = preview
            for index in range(start, end):
                record = data[base + offsets[index]:base + offsets[index + 1]]
                try:
                    pixels = decode_spr(b'\x01\x00\x00\x00\x00' + record)[0]
                except ValueError as error:
                    preview['rejections'].append({'frame': index, 'reason': str(error)})
                    continue
                if len(pixels) != template['width'] * template['height'] or not template['width'] or not template['height']:
                    preview['rejections'].append({'frame': index, 'reason': 'DAT dimensions mismatch'})
                    continue
                image = Image.new('RGBA', (template['width'], template['height']))
                image.putdata([tuple(palette[v * 3:v * 3 + 3]) + (255,) if v >= 0 else (0, 0, 0, 0) for v in pixels])
                atlas.paste(image, (preview['atlas_x'], preview['atlas_y']))
                preview.update({'status': 'candidate', 'frame': index})
                break
    atlas.save(destination / 'template_previews.png')


def audit_directory(source: Path) -> dict:
    """Validate every discovered SCN/MAP/SEG chain, including empty OBE files."""
    files = {p.name.upper(): p for p in source.iterdir() if p.is_file()}
    def read_reference(name: str) -> bytes:
        if name not in files:
            raise ValueError(f'Missing referenced file: {name}')
        return files[name].read_bytes()
    tables = {name: decode_templates(read_reference(name)) for name in sorted(files) if name.startswith('ARMY__') and name.endswith('.DAT')}
    scenarios = []
    for name in sorted(files):
        if not name.startswith('SCENA_') or not name.endswith('.SCN'):
            continue
        content = files[name].read_bytes()
        scenario = decode_scenario(content)
        map_bytes = read_reference(scenario['map_file'])
        if len(map_bytes) < 25 or map_bytes[:4] != b'MAP2':
            raise ValueError(f'Invalid MAP2 for {name}')
        width, height = struct.unpack_from('<II', map_bytes, 17)
        if not 0 < width <= 1024 or not 0 < height <= 1024 or len(map_bytes) != 25 + width * height * 4:
            raise ValueError(f'Invalid map length for {name}')
        segment_name = dos_filename(map_bytes[4:17])
        segment = read_reference(segment_name)
        if len(segment) < 21 or segment[:4] != b'SEG2':
            raise ValueError(f'Invalid SEG2 for {name}')
        count = u32(segment, 4)
        end = 21 + count * (9 + 1024)
        if not count or end > len(segment):
            raise ValueError(f'Truncated SEG2 for {name}')
        cells = struct.unpack_from(f'<{width * height}I', map_bytes, 25)
        if any((cell & 65535) >= count for cell in cells):
            raise ValueError(f'Invalid tile reference for {name}')
        objects = []
        for slot in scenario['slots']:
            payload = read_reference(slot['object_file'])
            records = decode_objects(payload)
            if scenario['army_files']:
                link_templates(records, tables, scenario['army_files'])
            objects.append({'slot_index': slot['slot_index'], 'file': slot['object_file'],
                            'bytes': len(payload), 'record_count': len(records), 'sha256': hashlib.sha256(payload).hexdigest()})
        scenarios.append({'file': name, 'sha256': hashlib.sha256(content).hexdigest(),
                          'map_file': scenario['map_file'], 'width': width, 'height': height,
                          'segment_file': segment_name, 'tile_count': count,
                          'segment_unknown_tail_bytes': len(segment) - end,
                          'slot_count': scenario['slot_count'], 'objects': objects})
    inventory = []
    for name in sorted(files):
        content = files[name].read_bytes()
        inventory.append({'file': name, 'bytes': len(content),
                          'sha256': hashlib.sha256(content).hexdigest()})
    return {'format_version': 1, 'files': inventory, 'scenarios': scenarios, 'template_count': sum(len(t['templates']) for t in tables.values()),
            'limitations': ['Structural consistency is not gameplay validation.',
                           'OBE record counts describe the serialized chain, not proven live game entities.']}


def import_directory(source: Path, destination: Path, map_number: int = 1) -> dict:
    files = {p.name.upper(): p for p in source.iterdir() if p.is_file()}
    used = {}
    def read(name: str) -> bytes:
        path = files.get(name.upper())
        if path is None:
            raise ValueError(f'Missing game file: {name}')
        data = path.read_bytes()
        used[name.upper()] = {'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}
        return data
    tables = {name: decode_templates(read(name)) for name in sorted(files) if name.startswith('ARMY__') and name.endswith('.DAT')}
    scenario_name = f'SCENA_{map_number:02d}.SCN'
    scenario = decode_scenario(read(scenario_name)) if scenario_name in files else None
    if scenario is not None:
        expected_map = f'MAP_{map_number}.MAP'
        if scenario['map_file'] != expected_map:
            raise ValueError(f'Scenario references {scenario["map_file"]}, expected {expected_map}')
        for slot in scenario['slots']:
            payload = read(slot['object_file'])
            slot['objects'] = decode_objects(payload)
            link_templates(slot['objects'], tables, scenario['army_files'])
            slot['object_bytes'] = len(payload)
            slot['object_sha256'] = hashlib.sha256(payload).hexdigest()
    map_bytes = read(f'MAP_{map_number}.MAP')
    if map_bytes[:4] != b'MAP2' or len(map_bytes) < 25:
        raise ValueError('Expected MAP2')
    segment_name = map_bytes[4:17].split(b'\0')[0].decode('ascii')
    width, height = struct.unpack_from('<II', map_bytes, 17)
    if not 0 < width <= 1024 or not 0 < height <= 1024 or len(map_bytes) != 25 + width * height * 4:
        raise ValueError('Unexpected MAP2 dimensions or length')
    cells = list(struct.unpack_from(f'<{width * height}I', map_bytes, 25))
    segments = read(segment_name)
    if segments[:4] != b'SEG2':
        raise ValueError('Expected SEG2')
    count = u32(segments, 4)
    # 21-byte serialized header, n minimap bytes, n opaque 8-byte properties,
    # n raw 32x32 indexed tiles. A trailing section remains uninterpreted.
    base = 21 + count * 9
    end = base + count * 1024
    if not count or end > len(segments):
        raise ValueError('Truncated SEG2 tile section')
    if any((cell & 0xffff) >= count for cell in cells):
        raise ValueError('MAP2 references a missing tile')
    palette = read('COLOR1.PAL')
    if len(palette) != 768:
        raise ValueError('Expected a 256-color RGB palette')
    destination.mkdir(parents=True, exist_ok=True)
    columns = 32
    atlas = Image.new('RGB', (columns * 32, ((count + columns - 1) // columns) * 32))
    tile_images = []
    terrain = []
    for index in range(count):
        image = Image.frombytes('P', (32, 32), segments[base + index * 1024:base + (index + 1) * 1024])
        image.putpalette(palette)
        tile_images.append(image.convert('RGB'))
        atlas.paste(tile_images[-1], ((index % columns) * 32, (index // columns) * 32))
        r, g, b = palette[segments[21 + index] * 3:segments[21 + index] * 3 + 3]
        # Prototype heuristic, NOT a decoded collision flag. See formats.md.
        if b > r + 10 and b > g:
            terrain.append('water')
        elif max(r, g, b) - min(r, g, b) < 12 and max(r, g, b) < 100:
            terrain.append('rock')
        elif g > r * 1.5 and g < 65:
            terrain.append('forest')
        else:
            terrain.append('ground')
    atlas.save(destination / 'terrain.png')
    preview = Image.new('RGB', (width * 32, height * 32))
    mini = Image.new('RGB', (width, height))
    for index, cell in enumerate(cells):
        tile = cell & 0xffff
        preview.paste(tile_images[tile], ((index % width) * 32, (index // width) * 32))
        color = segments[21 + tile] * 3
        mini.putpixel((index % width, index // width), tuple(palette[color:color + 3]))
    preview.resize((1024, 1024), Image.Resampling.NEAREST).save(destination / 'map_preview.png')
    mini.save(destination / 'minimap.png')
    sprite_data = read('ARMY__01.SPR')
    frames = decode_spr(sprite_data)
    if len(frames) < 37:
        raise ValueError('Missing verified unit frame range')
    sheet = Image.new('RGBA', (62 * 8, 62 * 4))
    for index, frame in enumerate(frames[5:37]):
        if len(frame) != 62 * 62:
            raise ValueError('Unexpected dimensions in the verified unit frame range')
        image = Image.new('RGBA', (62, 62))
        image.putdata([tuple(palette[v * 3:v * 3 + 3]) + (255,) if v >= 0 else (0, 0, 0, 0) for v in frame])
        sheet.paste(image, ((index % 8) * 62, (index // 8) * 62))
    sheet.save(destination / 'warrior.png')
    result = {
        'format_version': 1, 'map_number': map_number, 'width': width, 'height': height,
        'tile_size': 32, 'atlas_columns': columns, 'tile_count': count, 'cells': cells,
        'terrain': terrain, 'terrain_classification': 'experimental_palette_heuristic',
        'segment_properties_hex': segments[21 + count:base].hex(),
        'segment_unknown_tail_bytes': len(segments) - end,
        'unit_frames': {'source': 'ARMY__01.SPR', 'first': 5, 'count': 32, 'width': 62, 'height': 62},
    }
    (destination / 'map.json').write_text(json.dumps(result, separators=(',', ':')), encoding='utf-8')
    write_template_previews(tables, read, palette, destination)
    (destination / 'templates.json').write_text(json.dumps({'format_version': 4, 'armies': tables}, ensure_ascii=False, indent=2), encoding='utf-8')
    scenario_path = destination / 'scenario.json'
    if scenario is not None:
        from occupancy_profile import attach_initial_occupancy
        attach_initial_occupancy(scenario, scenario_name, read)
        scenario_path.write_text(json.dumps(scenario, ensure_ascii=False, indent=2), encoding='utf-8')
    elif scenario_path.exists():
        scenario_path.unlink()  # Never leave a previous map's scenario in the output.
    from reference_frames import render_observations
    render_observations(source, destination, map_number, decode_spr_frame)
    manifest = {'importer_version': 10, 'files': used, 'decoded_sprite_records': len(frames),
                'notes': 'Graphics decoded; gameplay and terrain blocking are experimental.'}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path, help='Extracted game folder or Horde.ISO')
    parser.add_argument('--output', type=Path, default=ROOT / 'assets' / 'imported')
    parser.add_argument('--map', type=int, default=1, dest='map_number')
    parser.add_argument('--audit', action='store_true', help='Validate all SCN/MAP/SEG/OBE links; write catalog.json without textures')
    parser.add_argument('--unshield', default='unshield', help='InstallShield extractor (only for ISO input)')
    args = parser.parse_args()
    def process(source: Path) -> dict:
        if args.audit:
            report = audit_directory(source)
            if not report['scenarios']:
                raise ValueError('No SCENA_*.SCN files found')
            args.output.mkdir(parents=True, exist_ok=True)
            (args.output / 'catalog.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
            return report
        return import_directory(source, args.output, args.map_number)
    if args.source.is_dir():
        manifest = process(args.source)
    else:
        unshield = shutil.which(args.unshield)
        if unshield is None:
            parser.error('ISO import requires unshield. Install it or pass an extracted game directory.')
        with tempfile.TemporaryDirectory(prefix='northwind-import-') as scratch:
            scratch = Path(scratch)
            extract_iso(args.source, scratch / 'iso')
            cabinets = list((scratch / 'iso').rglob('DATA1.CAB'))
            if len(cabinets) != 1:
                raise ValueError('Expected one InstallShield DATA1.CAB')
            subprocess.run([unshield, '-d', str(scratch / 'game'), 'x', str(cabinets[0])], check=True,
                           stdout=subprocess.DEVNULL)
            executables = list((scratch / 'game').rglob('NORWIND.EXE'))
            if len(executables) != 1:
                raise ValueError('Expected one NORWIND.EXE after extraction')
            manifest = process(executables[0].parent)
    if args.audit:
        print(f'Validated {len(manifest["scenarios"])} scenarios; catalog: {args.output.resolve() / "catalog.json"}')
        return
    print(f'Imported map {args.map_number} into {args.output.resolve()}')
    print(f'Decoded {manifest["decoded_sprite_records"]} sprite records; wrote terrain, unit frames and manifest.')


if __name__ == '__main__':
    main()
