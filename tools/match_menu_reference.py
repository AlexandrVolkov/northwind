#!/usr/bin/env python3
"""Rank prepared bush frames against one unmodified DOSBox main-menu screenshot.

This is an observational best fit; the screenshot's RGB palette conversion and
other sprites prevent an exact whole-frame equality assertion.
"""
import json
from pathlib import Path
from PIL import Image, ImageChops, ImageStat


def bush_scores(root):
    with Image.open(root / 'docs/reference/original-main-menu.png') as source:
        reference = source.convert('RGB').crop((1, 269, 248, 389))
    with Image.open(root / 'assets/imported/interface/main_menu.png') as source:
        background = source.convert('RGBA').crop((1, 269, 248, 389))
    with Image.open(root / 'assets/imported/interface/menu_bush.png') as source:
        atlas = source.convert('RGBA')
    values = []
    for frame in range(62):
        tile = atlas.crop(((frame % 8) * 247, (frame // 8) * 120,
                           (frame % 8 + 1) * 247, (frame // 8 + 1) * 120))
        image = Image.alpha_composite(background, tile).convert('RGB')
        values.append(round(sum(ImageStat.Stat(ImageChops.difference(reference, image)).sum)))
    return values


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[1]
    scores = bush_scores(root)
    print(json.dumps({'best_frames': sorted(range(len(scores)), key=scores.__getitem__)[:5],
                      'best_scores': sorted(scores)[:5]}, indent=2))
