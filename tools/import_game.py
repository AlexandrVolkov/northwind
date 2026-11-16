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
    manifest = {'importer_version': 1, 'files': used, 'decoded_sprite_records': len(frames),
                'notes': 'Graphics decoded; gameplay and terrain blocking are experimental.'}
    (destination / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path, help='Extracted game folder or Horde.ISO')
    parser.add_argument('--output', type=Path, default=ROOT / 'assets' / 'imported')
    parser.add_argument('--map', type=int, default=1, dest='map_number')
    parser.add_argument('--unshield', default='unshield', help='InstallShield extractor (only for ISO input)')
    args = parser.parse_args()
    if args.source.is_dir():
        manifest = import_directory(args.source, args.output, args.map_number)
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
            manifest = import_directory(executables[0].parent, args.output, args.map_number)
    print(f'Imported map {args.map_number} into {args.output.resolve()}')
    print(f'Decoded {manifest["decoded_sprite_records"]} sprite records; wrote terrain, unit frames and manifest.')


if __name__ == '__main__':
    main()
