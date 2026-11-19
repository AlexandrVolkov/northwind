#!/usr/bin/env python3
"""Execute the map scroll step LE:1 3FA1..40C3 under Unicorn on generated pointer, key, timer and «Панорама» values.

The view sits in the middle of a 128x128 map so the clamps at 0 and width-14 do not interfere; they are checked separately
by the C# clamp of the camera. Recorded: cursor frame bounds [1946], the step dx/dy from [1906]/[190A] and the timer [19C1].
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from analyze_executable import analyze
from verify_combat import CombatOracle
from forestry_source import source_constants

KEYS = [0, 0xe04b, 0xe04d, 0xe048, 0xe050]


class ScrollOracle(CombatOracle):
    RANGES = {'scroll': ([(0x3fa1, 0x40c4)], [])}


def cases():
    rng = random.Random(3)
    xs = [0, 1, 2, 320, 638, 639, 640]; ys = [0, 1, 2, 240, 476, 477, 478, 479]
    return [{'x': rng.choice(xs), 'y': rng.choice(ys), 'key': rng.choice(KEYS), 'timer': rng.randrange(256), 'last': rng.randrange(256),
             'pan': rng.randrange(1, 17)} for _ in range(4000)]


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = ScrollOracle(code, report['fs_assignments'], constants)
    all_cases = cases(); digest = hashlib.sha256()
    for c in all_cases:
        o.cpu.mem_write(0x19c2, b'\x01'); o.cpu.mem_write(0x908f, bytes([c['pan']]))
        o.cpu.mem_write(0x19e4, bytes([c['timer']])); o.cpu.mem_write(0x19c1, bytes([c['last']]))
        o.cpu.mem_write(0x1932, struct.pack('<II', c['x'], c['y'])); o.cpu.mem_write(0x1c35, struct.pack('<H', c['key']))
        o.cpu.mem_write(0x1906, struct.pack('<II', 50, 50)); o.cpu.mem_write(0x18de, struct.pack('<II', 128, 128))
        o.cpu.mem_write(0x1946, struct.pack('<I', 0xe0000))
        o.mode = 'scroll'; o.boundary = None; o.run(o.CODE + 0x3fa1)
        first, last = struct.unpack('<HH', bytes(o.cpu.mem_read(0x1946, 4)))
        x, y = struct.unpack('<II', bytes(o.cpu.mem_read(0x1906, 8)))
        digest.update(json.dumps([first, last, x - 50, y - 50, o.cpu.mem_read(0x19c1, 1)[0]]).encode())
    return {'format_version': 1, 'source_sha256': {'NORWIND.EXE': report['source_sha256']['NORWIND.EXE']}, 'cases': all_cases,
            'results': {'scroll': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
