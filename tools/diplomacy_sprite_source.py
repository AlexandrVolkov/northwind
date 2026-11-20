#!/usr/bin/env python3
"""Export the two source-confirmed diplomacy relation icons from WORK.SPR."""
import argparse
import hashlib
import json
from pathlib import Path

from analyze_executable import EXE_SHA256, INNER_MZ, read_le_objects
from interface_source import spr_image, spr_records


def export(game: Path, output: Path):
    exe = (game / 'NORWIND.EXE').read_bytes()
    if hashlib.sha256(exe).hexdigest() != EXE_SHA256:
        raise ValueError('Unknown executable')
    _, objects = read_le_objects(exe, INNER_MZ)
    code, data = objects[0]['payload'], objects[2]['payload']
    widget = data[0x3c01:0x3c01 + 30]
    if widget[0] != 0x85 or widget[0x12:0x16] != bytes((32, 32, 255, 0)):
        raise ValueError('Diplomacy icon widget dimensions or sprite bank changed')
    if code[0xeb59:0xeb62] != bytes.fromhex('3c 01 66 15 66 01 66 89 47'):
        raise ValueError('Relation icon selector changed')
    raw = (game / 'WORK.SPR').read_bytes()
    records = spr_records(raw)
    raw_palette = (game / 'COLOR1.PAL').read_bytes()
    palette = bytes((v & 252) | (v >> 6) for v in raw_palette)
    output.mkdir(parents=True, exist_ok=True)
    entries = []
    for number, label in ((359, 'голубь'), (360, 'скрещённые мечи')):
        image = spr_image(records[number], 32, 32, palette)
        filename = f'{number}.png'
        image.save(output / filename)
        entries.append(dict(sprite=number, file=filename, label=label, width=32, height=32,
                            record_bytes=len(records[number]), rgba_sha256=hashlib.sha256(image.tobytes()).hexdigest()))
    manifest = dict(format_version=1, source='WORK.SPR', source_sha256=hashlib.sha256(raw).hexdigest(),
                    palette='COLOR1.PAL', palette_sha256=hashlib.sha256(raw_palette).hexdigest(),
                    executable_sha256=EXE_SHA256, widget_le3=0x3c01, selector_le1=0xeb45, icons=entries)
    (output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', required=True, type=Path)
    parser.add_argument('--output', type=Path, default=Path('assets/imported/diplomacy'))
    args = parser.parse_args()
    result = export(args.game, args.output)
    print(f'Exported {len(result["icons"])} diplomacy icons')
