#!/usr/bin/env python3
"""Export CP866 messages and all 32 HELP.PIC pages with the verified game palette."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
from PIL import Image
from analyze_executable import EXE_SHA256, INNER_MZ, read_le_objects
from music_source import checked_fixup


def unpack_rle(data, expected):
    out = bytearray()
    at = 0
    while at < len(data) and len(out) < expected:
        token = data[at]; at += 1
        if token >= 0xc0:
            if at == len(data): raise ValueError('Dangling PIC run token')
            run = (token - 0xc0) or 256  # Native DEC AL loop at 14E40 wraps C0h to 256 writes.
            out += bytes([data[at]]) * run; at += 1
        else:
            out.append(token)
        if len(out) > expected: raise ValueError('PIC run overflows declared image')
    if len(out) != expected: raise ValueError('PIC record does not fill declared image')
    return bytes(out), data[at:]


def pictures(data):
    if len(data) < 2: raise ValueError('Truncated PIC count')
    count = struct.unpack_from('<H', data)[0]
    base = 2 + count * 4
    if not count or base > len(data): raise ValueError('Invalid PIC offset table')
    offsets = list(struct.unpack_from(f'<{count}I', data, 2))
    if offsets[0] != base or any(a >= b for a, b in zip(offsets, offsets[1:])):
        raise ValueError('PIC offsets must follow the header in order')
    pages = []
    for at, end in zip(offsets, offsets[1:] + [len(data)]):
        if not 0 <= at < at + 8 <= end <= len(data): raise ValueError('PIC record outside file')
        size, width, height = struct.unpack_from('<IHH', data, at)
        if at + 8 + size != end or not (0 < width <= 8192 and 0 < height <= 8192):
            raise ValueError('Invalid PIC size/dimensions')
        pixels, trailer = unpack_rle(data[at + 8:end], width * height)
        pages.append(dict(offset=at, packed_bytes=size, width=width, height=height, pixels=pixels, trailer_hex=trailer.hex()))
    return pages


def text_runs(raw):
    runs = []
    color = 0
    text = bytearray()
    at = 0
    while at < len(raw):
        byte = raw[at]; at += 1
        if byte == 0x7e:
            if at == len(raw): raise ValueError('Dangling message color marker')
            if text: runs.append(dict(palette_offset=color, text=bytes(text).decode('cp866'))); text.clear()
            color = raw[at]; at += 1
        else:
            text.append(byte)
    if text: runs.append(dict(palette_offset=color, text=bytes(text).decode('cp866')))
    return runs


def messages(data):
    records = []
    sections = []
    unknown = []
    section = None
    seen = set()
    offset = 0
    for line_no, line in enumerate(data.splitlines(keepends=True), 1):
        raw = line.rstrip(b'\r\n')
        match = re.fullmatch(rb'\[([0-9A-Fa-f]{2})\](.*)', raw)
        if match:
            code = int(match[1], 16)
            if code in seen: raise ValueError('Duplicate message code')
            seen.add(code)
            runs = text_runs(match[2])
            records.append(dict(code=code, code_hex=f'{code:02X}', section=section, line=line_no, source_offset=offset,
                                original_text=match[2].decode('cp866'), text=''.join(r['text'] for r in runs), runs=runs))
        elif re.fullmatch(rb'\[[^\]]+\]', raw):
            section = raw[1:-1].decode('cp866')
            sections.append(dict(name=section, line=line_no, source_offset=offset))
        elif raw:
            unknown.append(dict(line=line_no, source_offset=offset, text=raw.decode('cp866')))
        offset += len(line)
    return dict(encoding='cp866', source_sha256=hashlib.sha256(data).hexdigest(), sections=sections,
                message_count=len(records), messages=records, unparsed_lines=unknown)


def source_evidence(game):
    executable = (game / 'NORWIND.EXE').read_bytes()
    if hashlib.sha256(executable).hexdigest() != EXE_SHA256: raise ValueError('Unknown original executable version')
    le, objects = read_le_objects(executable, INNER_MZ)
    for args in [(0x695d, 2, 0x1ed4), (0x15480, 2, 0x2a8)]: checked_fixup(executable, le, *args)
    if objects[1]['payload'][0x1ed4:0x1ee1] != b'help    .pic\0': raise ValueError('Unexpected padded DOS help filename')
    if objects[1]['payload'][0x2a8:0x2b5] != b'messages.txt\0': raise ValueError('Unexpected message resource name')
    return dict(executable_sha256=EXE_SHA256, help_name_fixup=[0x695d, 2, 0x1ed4], messages_name_fixup=[0x15480, 2, 0x2a8])


def export(game, output, message_output):
    evidence = source_evidence(game)
    raw = (game / 'HELP.PIC').read_bytes()
    pages = pictures(raw)
    if len(pages) != 32 or any((p['width'], p['height']) != (448, 448) for p in pages):
        raise ValueError('Unexpected original help layout')
    source_palette = (game / 'COLOR1.PAL').read_bytes()
    if len(source_palette) != 768: raise ValueError('Invalid game palette')
    palette = bytes((v & 252) | (v >> 6) for v in source_palette)
    output.mkdir(parents=True, exist_ok=True)
    records = []
    for i, page in enumerate(pages):
        image = Image.frombytes('P', (page['width'], page['height']), page['pixels'])
        image.putpalette(palette)
        filename = f'page_{i:02d}.png'
        image.save(output / filename)
        records.append(dict(page=i, file=filename, offset=page['offset'], packed_bytes=page['packed_bytes'],
                            width=page['width'], height=page['height'],
                            trailer_hex=page['trailer_hex'],
                            indices_sha256=hashlib.sha256(page['pixels']).hexdigest(),
                            rgb_sha256=hashlib.sha256(image.convert('RGB').tobytes()).hexdigest()))
    manifest = dict(format_version=1, source='HELP.PIC', source_sha256=hashlib.sha256(raw).hexdigest(),
                    palette='COLOR1.PAL', palette_sha256=hashlib.sha256(source_palette).hexdigest(),
                    vga_dac_quantized=True, source_evidence=evidence, display_rect=[16, 16, 448, 448], pages=records)
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    catalog = dict(format_version=1, source='MESSAGES.TXT', source_evidence=evidence, **messages((game / 'MESSAGES.TXT').read_bytes()))
    message_output.parent.mkdir(parents=True, exist_ok=True)
    message_output.write_text(json.dumps(catalog, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    return manifest, catalog


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('assets/imported/help'))
    parser.add_argument('--messages', type=Path, default=Path('data/original_messages.json'))
    args = parser.parse_args()
    help_manifest, catalog = export(args.game, args.output, args.messages)
    print(f'Exported {len(help_manifest["pages"])} help pages and {catalog["message_count"]} CP866 messages')
