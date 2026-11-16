#!/usr/bin/env python3
"""Find camera candidates by exact terrain patches in an unscaled DOSBox screenshot.

No units, movement rules or anchors are inferred from a terrain match.
"""
import argparse
import json
from pathlib import Path
import struct
from PIL import Image


def dac6(image):
    """Observed VGA DAC conversion: floor 8-bit channels to 6-bit, replicate bits."""
    return image.convert('RGB').point([(v & 252) | (v >> 6) for v in range(256)] * 3)


def find_patch_positions(image, patch, limit=10000):
    image, patch = image.convert('RGB'), patch.convert('RGB')
    if not patch.width or not patch.height or patch.width > image.width or patch.height > image.height:
        raise ValueError('Patch must fit inside the map')
    data = image.tobytes()
    row = patch.crop((0, 0, patch.width, 1)).tobytes()
    expected = patch.tobytes()
    start, result = 0, set()
    while True:
        offset = data.find(row, start)
        if offset < 0:
            return result
        start = offset + 1
        if offset % 3:
            continue
        x, y = offset // 3 % image.width, offset // (image.width * 3)
        if x + patch.width > image.width or y + patch.height > image.height:
            continue
        if image.crop((x, y, x + patch.width, y + patch.height)).tobytes() == expected:
            result.add((x, y))
            if len(result) > limit:
                raise ValueError('Patch is too ambiguous; choose a more distinctive region')


def match_patches(image, screenshot, viewport, patches, size=32):
    vx, vy, width, height = viewport
    if min(vx, vy) < 0 or min(width, height, size) <= 0 or vx + width > screenshot.width or vy + height > screenshot.height:
        raise ValueError('Viewport outside screenshot')
    cameras, results = None, []
    for px, py in patches:
        if min(px, py) < 0 or px + size > width or py + size > height:
            raise ValueError('Patch outside viewport')
        patch = screenshot.crop((vx + px, vy + py, vx + px + size, vy + py + size))
        positions = find_patch_positions(image, patch)
        candidates = {(x - px, y - py) for x, y in positions}
        cameras = candidates if cameras is None else cameras & candidates
        results.append({'viewport_patch': [px, py], 'match_count': len(positions)})
    if cameras is None:
        raise ValueError('At least one patch is required')
    return {'patches': results, 'camera_candidates': [list(p) for p in sorted(cameras)],
            'status': 'unique' if len(cameras) == 1 else 'ambiguous' if cameras else 'no_match',
            'limitations': 'Terrain only; unit positions, animation, anchors and rules are not validated.'}


def load_map(game, number):
    files = {p.name.upper(): p for p in game.iterdir() if p.is_file()}
    data = files[f'MAP_{number}.MAP'].read_bytes()
    if data[:4] != b'MAP2' or len(data) < 25:
        raise ValueError('Invalid MAP2')
    w, h = struct.unpack_from('<II', data, 17)
    if not 0 < w <= 1024 or not 0 < h <= 1024 or len(data) != 25 + w * h * 4:
        raise ValueError('Invalid MAP2 dimensions')
    name = data[4:17].split(b'\0')[0].decode('ascii').upper()
    segment = files[name].read_bytes()
    if len(segment) < 21 or segment[:4] != b'SEG2':
        raise ValueError('Invalid SEG2')
    n = struct.unpack_from('<I', segment, 4)[0]
    base = 21 + 9 * n
    if not n or base + n * 1024 > len(segment):
        raise ValueError('Truncated SEG2')
    palette = files['COLOR1.PAL'].read_bytes()
    if len(palette) != 768:
        raise ValueError('Invalid palette')
    tiles = []
    for index in range(n):
        tile = Image.frombytes('P', (32, 32), segment[base + index * 1024:base + (index + 1) * 1024])
        tile.putpalette(palette); tiles.append(tile.convert('RGB'))
    result = Image.new('RGB', (w * 32, h * 32))
    for index, value in enumerate(struct.unpack_from(f'<{w*h}I', data, 25)):
        tile = value & 65535
        if tile >= n:
            raise ValueError('Missing tile')
        result.paste(tiles[tile], (index % w * 32, index // w * 32))
    return dac6(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--screenshot', type=Path, required=True)
    parser.add_argument('--map', type=int, default=1)
    parser.add_argument('--viewport', type=int, nargs=4, required=True, metavar=('X', 'Y', 'W', 'H'))
    parser.add_argument('--patch', type=int, nargs=2, action='append', required=True, metavar=('X', 'Y'))
    args = parser.parse_args()
    with Image.open(args.screenshot) as screenshot:
        report = match_patches(load_map(args.game, args.map), screenshot.convert('RGB'), args.viewport, args.patch)
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
