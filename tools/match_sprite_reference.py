#!/usr/bin/env python3
"""Verify an explicit SPR frame/palette against a screenshot, without guessing identity."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image
from import_game import decode_spr


def frame_pixels(data, frame):
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


def match_frame(screenshot, pixels, size, palette, table, viewport, search):
    width, height = size
    if width <= 0 or height <= 0 or len(pixels) != width * height or len(palette) != 768 or len(table) != 256:
        raise ValueError('Invalid dimensions, palette or translation table')
    if any(c < -1 or c > 255 for c in pixels):
        raise ValueError('Pixel index outside palette')
    screenshot = screenshot.convert('RGB')
    vx, vy, vw, vh = viewport
    sx, sy, sw, sh = search
    if min(vx, vy, sx, sy) < 0 or min(vw, vh, sw, sh) <= 0 or vx+vw > screenshot.width or vy+vh > screenshot.height or sx+sw > screenshot.width or sy+sh > screenshot.height:
        raise ValueError('Viewport/search outside screenshot')
    colors = [tuple((v & 252) | (v >> 6) for v in palette[table[c]*3:table[c]*3+3]) if c >= 0 else None for c in pixels]
    opaque = [(i % width, i // width, c) for i, c in enumerate(colors) if c is not None]
    if not opaque:
        raise ValueError('Empty sprite cannot establish a match')
    runs = []
    for y in range(height):
        start = 0
        for x in range(width + 1):
            if x == width or colors[y*width+x] is None:
                if x > start:
                    runs.append((x-start, start, y))
                start = x + 1
    length, rx, ry = max(runs)
    # Small exact seed finds candidates; ALL opaque pixels must then match.
    rx += max(0, length - 8)
    length = min(length, 8)
    seed = bytes(v for c in colors[ry*width+rx:ry*width+rx+length] for v in c)
    region = screenshot.crop((sx, sy, sx+sw, sy+sh))
    raw, start, candidates = region.tobytes(), 0, set()
    while True:
        at = raw.find(seed, start)
        if at < 0:
            break
        start = at + 1
        if at % 3:
            continue
        x, y = at//3 % sw - rx + sx, at//(sw*3) - ry + sy
        if any(not (vx <= x+dx < vx+vw and vy <= y+dy < vy+vh) for dx, dy, _ in opaque):
            continue  # Never certify a sprite from a clipped subset.
        if all(screenshot.getpixel((x+dx, y+dy)) == c for dx, dy, c in opaque):
            candidates.add((x, y))
    return {'status': 'unique' if len(candidates) == 1 else 'ambiguous' if candidates else 'no_match',
            'screen_top_left_candidates': [list(c) for c in sorted(candidates)],
            'opaque_pixels': len(opaque),
            'limitations': 'Explicit frame/palette match only; does not identify memory state or generalize object anchors.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--screenshot', type=Path, required=True)
    parser.add_argument('--spr', required=True)
    parser.add_argument('--table', required=True)
    parser.add_argument('--frame', type=int, required=True)
    parser.add_argument('--size', type=int, nargs=2, required=True)
    parser.add_argument('--viewport', type=int, nargs=4, required=True)
    parser.add_argument('--search', type=int, nargs=4, required=True)
    args = parser.parse_args()
    files = {p.name.upper(): p for p in args.game.iterdir() if p.is_file()}
    spr = files[args.spr.upper()].read_bytes()
    table = files[args.table.upper()].read_bytes()
    palette = files['COLOR1.PAL'].read_bytes()
    with Image.open(args.screenshot) as image:
        result = match_frame(image, frame_pixels(spr, args.frame), args.size, palette, table, args.viewport, args.search)
    result.update({'frame': args.frame, 'spr': args.spr, 'table': args.table,
                   'screenshot_sha256': hashlib.sha256(args.screenshot.read_bytes()).hexdigest()})
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
