#!/usr/bin/env python3
"""Measure a declared red-pixel probe in captured frames; not a general unit tracker."""
import argparse
import json
from pathlib import Path
from PIL import Image


def color_probe(image, roi):
    x, y, width, height = roi
    if min(x, y) < 0 or min(width, height) <= 0 or x+width > image.width or y+height > image.height:
        raise ValueError('ROI outside screenshot')
    image = image.convert('RGB')
    points = []
    for yy in range(y, y+height):
        for xx in range(x, x+width):
            r, g, b = image.getpixel((xx, yy))
            if r > 100 and r > 2*g and r > 2*b:
                points.append((xx, yy))
    if not points:
        return {'pixel_count': 0, 'centroid': None, 'bounds': None}
    return {'pixel_count': len(points),
            'centroid': [sum(p[i] for p in points)/len(points) for i in (0, 1)],
            'bounds': [min(p[0] for p in points), min(p[1] for p in points), max(p[0] for p in points), max(p[1] for p in points)]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--capture', type=Path, required=True)
    parser.add_argument('--roi', type=int, nargs=4, required=True)
    args = parser.parse_args()
    data = json.loads(args.capture.read_text())
    output = []
    previous = -1
    for frame in data['captures']:
        name = frame['file']
        if Path(name).name != name or not name.lower().endswith('.png'):
            raise ValueError('Expected a local PNG filename')
        at = frame['seconds_after_input_return']
        if at < previous:
            raise ValueError('Capture times must be monotonic')
        previous = at
        with Image.open(args.capture.parent / name) as image:
            output.append({'file': name, 'seconds_after_input_return': at, **color_probe(image, args.roi)})
    print(json.dumps({'roi': args.roi, 'frames': output,
                      'limitations': 'Color centroid is a diagnostic proxy, not the object anchor. Requires a declared unobstructed ROI; cannot establish game speed or general movement rules.'}, indent=2))


if __name__ == '__main__':
    main()
