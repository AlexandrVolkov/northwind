#!/usr/bin/env python3
"""Match every complete 32x32 fog tile against an actual DOSBox screenshot.

The camera is supplied, not guessed. Corner masks are inferred from the image;
this verifies raster graphics, not the scenario's complete exploration history.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from PIL import Image
from interface_source import spr_records


def match(game, screenshot, camera, output, underlay_path):
    names = ['MAP_1.MAP', 'MAPSEG01.SEG', 'WORK.SPR', 'FADE_COL.TAB', 'COLOR1.PAL']
    sources = {name: (game / name).read_bytes() for name in names}
    raw, seg = sources['MAP_1.MAP'], sources['MAPSEG01.SEG']
    if raw[:4] != b'MAP2' or raw[4:17].rstrip(b'\0').lower() != b'mapseg01.seg' or seg[:4] != b'SEG2':
        raise ValueError('Expected the first map and segment profile')
    width, height = struct.unpack_from('<II', raw, 17)
    cells = struct.unpack_from('<' + str(width * height) + 'I', raw, 25)
    count = struct.unpack_from('<I', seg, 4)[0]; base = 21 + count * 9
    palette = bytes((v & 252) | (v >> 6) for v in sources['COLOR1.PAL'])
    rgb = [palette[i * 3:i * 3 + 3] for i in range(256)]
    fade = sources['FADE_COL.TAB']; sprites = spr_records(sources['WORK.SPR'])
    with Image.open(screenshot) as image: shot = image.convert('RGB')
    underlay = Image.new('L', (448, 448)); masks = []
    for row in range(14):
        for col in range(14):
            x, y = camera[0] + col, camera[1] + row
            if not (0 <= x < width and 0 <= y < height): raise ValueError('Camera outside map')
            tile = cells[y * width + x] & 65535
            indices = seg[base + tile * 1024:base + (tile + 1) * 1024]
            if len(indices) != 1024: raise ValueError('Missing terrain tile')
            underlay.paste(Image.frombytes('L', (32, 32), indices), (col * 32, row * 32))
            actual = shot.crop((16 + col * 32, 16 + row * 32, 48 + col * 32, 48 + row * 32)).tobytes()
            matches = []
            for mask in range(16):
                if mask == 0: pixels = indices
                elif mask == 15: pixels = bytes(1024)
                else:
                    weights = sprites[164 + 2 * mask + ((x + y) & 1)]
                    pixels = bytes(fade[0x700 + 256 * weight + i] for weight, i in zip(weights, indices))
                if b''.join(rgb[i] for i in pixels) == actual: matches.append(mask)
            if len(matches) != 1: raise ValueError(f'Tile {col},{row}: expected one complete match, found {matches}')
            masks.append(matches[0])
    underlay.save(underlay_path)
    result = dict(format_version=1, screenshot=screenshot.name, underlay=underlay_path.name, camera=list(camera), viewport=[16, 16, 448, 448],
                  source_sha256={name: hashlib.sha256(value).hexdigest() for name, value in sources.items()},
                  corner_masks=masks, matched_tiles=196, partial_tiles=sum(0 < mask < 15 for mask in masks),
                  viewport_rgb_sha256=hashlib.sha256(shot.crop((16, 16, 464, 464)).tobytes()).hexdigest(),
                  limitation='Corner masks inferred from screenshot; complete scenario exploration history is not reconstructed by this image match.')
    output.write_text(json.dumps(result, indent=2) + '\n')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--screenshot', type=Path, required=True)
    parser.add_argument('--camera', type=int, nargs=2, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--underlay', type=Path, required=True)
    args = parser.parse_args()
    result = match(args.game, args.screenshot, args.camera, args.output, args.underlay)
    print(f'Exact original match: {result["matched_tiles"]} tiles, {result["partial_tiles"]} partially shaded')
