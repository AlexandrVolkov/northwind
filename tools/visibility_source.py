#!/usr/bin/env python3
"""Convert the original fog weights and palette-index mapping to prepared PNG assets.

WORK 166..193 are raw 32x32 weights, not ordinary SPR RLE images.
FADE_COL rows 7..15 are stored as a semantic 256x9 lookup image, not a raw TAB file.
"""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image
from analyze_executable import EXE_SHA256
from interface_source import spr_records


def convert(game, output):
    names = ['NORWIND.EXE', 'WORK.SPR', 'FADE_COL.TAB', 'COLOR1.PAL']
    source = {name: (game / name).read_bytes() for name in names}
    if hashlib.sha256(source['NORWIND.EXE']).hexdigest() != EXE_SHA256:
        raise ValueError('Unknown EXE profile for the fog layout')
    records = spr_records(source['WORK.SPR'])
    table, palette = source['FADE_COL.TAB'], source['COLOR1.PAL']
    if len(table) != 8192 or len(palette) != 768: raise ValueError('Invalid fog table or palette dimensions')
    atlas = Image.new('L', (256, 128)); entries = []
    for i, sprite in enumerate(range(166, 194)):
        weights = records[sprite]
        if len(weights) != 1024 or max(weights) > 8: raise ValueError(f'Invalid WORK fog table {sprite}')
        left, top = (i % 8) * 32, (i // 8) * 32
        atlas.paste(Image.frombytes('L', (32, 32), weights), (left, top))
        entries.append(dict(sprite=sprite, corners=(sprite - 164) // 2, parity=sprite & 1,
                            rectangle=[left, top, 32, 32], sha256=hashlib.sha256(weights).hexdigest()))
    lookup = table[0x700:0x1000]
    rgb = bytes((v & 252) | (v >> 6) for v in palette)
    output.mkdir(parents=True, exist_ok=True)
    atlas.save(output / 'fog_weights.png')
    Image.frombytes('L', (256, 9), lookup).save(output / 'shade_indices.png')
    Image.frombytes('RGB', (256, 1), rgb).save(output / 'palette.png')
    manifest = dict(format_version=1, source_sha256={name: hashlib.sha256(data).hexdigest() for name, data in source.items()},
                    weights_atlas='fog_weights.png', weights=entries, shade_indices='shade_indices.png', palette='palette.png',
                    shade_sha256=hashlib.sha256(lookup).hexdigest(), palette_sha256=hashlib.sha256(rgb).hexdigest())
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('assets/imported/visibility'))
    args = parser.parse_args(); result = convert(args.game, args.output)
    print(f'Exported {len(result["weights"])} fog weights and nine palette lookup rows')
