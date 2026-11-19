#!/usr/bin/env python3
"""Execute the complete original 9F38 fog viewport, including real SPR lookup 1E133.

Native 640x480 outputs are hashed both as palette indices and COLOR1 DAC RGB.
Only synthetic parameters and hashes are saved; input code and raw banks stay local.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from analyze_executable import analyze
from verify_visibility import VisibilityOracle


class FogOracle(VisibilityOracle):
    SPRITES = VisibilityOracle.BANK + 0x100000

    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if not (0x9f38 <= at < 0xa10b or 0x1e133 <= at < 0x1e153):
            raise ValueError(f'Unexpected fog rendering escape: {at:X}')


def cases():
    out = []
    for width, height in [(16, 16), (31, 23), (160, 160)]:
        for x, y in [(0, 0), (width - 14, 0), (0, height - 14), (width - 14, height - 14), ((width - 14) // 2, (height - 14) // 2)]:
            for participant in [0, 7, 8]:
                for salt in [0, 1, 2]:
                    out.append([width, height, x, y, participant, salt])
    return out


def exploration(width, height, salt):
    return bytes(255 if salt == 0 else
                 (0 if width // 3 <= i % width < 2 * width // 3 and height // 3 <= i // width < 2 * height // 3 else 255)
                 if salt == 1 else (i * 37 + 19) & 255 for i in range(width * height))


def generate(game):
    report, code = analyze(game); o = FogOracle(code); c = o.cpu
    raw = (game / 'WORK.SPR').read_bytes(); fade = (game / 'FADE_COL.TAB').read_bytes()
    palette = bytes((v & 252) | (v >> 6) for v in (game / 'COLOR1.PAL').read_bytes())
    c.mem_write(o.SPRITES, raw); c.mem_write(o.COLORS, fade)
    o.put32(0x191a, o.SPRITES); o.put32(0x19f6, o.COLORS); o.put32(0x68e, o.FRAME)
    digest, rgb_digest = hashlib.sha256(), hashlib.sha256(); all_cases = cases(); preview = None
    for number, (width, height, x, y, participant, salt) in enumerate(all_cases):
        o.put32(0x18de, width, height); o.put32(0x18da, o.MAP); o.put32(0x1906, x, y)
        c.mem_write(0x1972, bytes([participant]))
        c.mem_write(o.MAP - 256, b'\xff' * (width * height + 512))
        c.mem_write(o.MAP, exploration(width, height, salt))
        c.mem_write(o.FRAME, bytes((i * 37 + salt * 19) & 255 for i in range(640 * 480)))
        o.run('viewport', 0x9f38)
        pixels = bytes(c.mem_read(o.FRAME, 640 * 480)); digest.update(pixels)
        rgb = b''.join(palette[i * 3:i * 3 + 3] for i in pixels)
        rgb_digest.update(rgb)
        if number == 82: preview = dict(case=all_cases[number], rgb_sha256=hashlib.sha256(rgb).hexdigest())
        if number % 15 == 14: print(f'Native fog viewport: {number + 1}/{len(all_cases)}', flush=True)
    return dict(format_version=1, source_sha256={name: hashlib.sha256((game / name).read_bytes()).hexdigest()
                for name in ['NORWIND.EXE', 'WORK.SPR', 'FADE_COL.TAB', 'COLOR1.PAL']},
                cases=all_cases, case_count=len(all_cases), indices_sha256=digest.hexdigest(), rgb_sha256=rgb_digest.hexdigest(), preview=preview)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game), indent=2) + '\n', encoding='utf-8')
