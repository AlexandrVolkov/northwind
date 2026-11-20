#!/usr/bin/env python3
"""Execute the wind LE:1 9E7D under Unicorn: vector [1C8E]/[1C92] += acceleration [1C97]/[1C9B] within +-1000, direction
[1C96], and a new acceleration from 9E41/9E55 when the timer byte [19E8] is 0."""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from analyze_executable import analyze
from verify_combat import CombatOracle
from forestry_source import source_constants


class WindOracle(CombatOracle):
    RANGES = {'wind': ([(0x9e7d, 0x9f38), (0x9e41, 0x9e69)], [])}


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = WindOracle(code, report['fs_assignments'], constants)
    spec = {'vectors': [[0, 0], [999, -3], [-1000, 1000], [300, 40], [-40, -700], [255, 256], [-256, 1], [5, -255], [700, 700], [-3, 0]],
            'accelerations': [[0, 0], [3, -3], [-2, 1], [1, 3]], 'directions': [0, 9], 'timers': [0, 1, 255], 'random_indices': [0, 1, 2, 127, 128, 255]}
    digest = hashlib.sha256(); count = 0; o.mode = 'wind'
    for (vx, vy), (ax, ay), direction, timer, index in product(*(spec[k] for k in ['vectors', 'accelerations', 'directions', 'timers', 'random_indices'])):
        o.cpu.mem_write(0x1c8e, struct.pack('<iiBii', vx, vy, direction, ax, ay))
        o.cpu.mem_write(0x19e8, bytes([timer])); o.cpu.mem_write(0x19eb, bytes([index, (index * 5 + 3) & 255]))
        o.run(o.CODE + 0x9e7d)
        digest.update(bytes(o.cpu.mem_read(0x1c8e, 17)) + bytes(o.cpu.mem_read(0x19eb, 2))); count += 1
    spec.update(case_count=count, sha256=digest.hexdigest())
    return {'format_version': 1, 'source_sha256': {'NORWIND.EXE': report['source_sha256']['NORWIND.EXE']}, 'wind': spec}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game), indent=2) + '\n', encoding='utf-8')
