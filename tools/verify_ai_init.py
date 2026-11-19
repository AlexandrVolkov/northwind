#!/usr/bin/env python3
"""Execute the AI block initialisation LE:1 1E53A..1E6C1 (with 1EB33, 1ECA0, 219C3) under Unicorn.

Each generated case has 1..8 participants with a character index (participant +16), the player participant [1972],
the map size and a chain of objects per participant. The LE:3 character table (fixup 1E654 -> 3:ED43) is placed at its
flat address ED43, which does not overlap LE:2 (3E00h bytes). Cases are stored in the fixture for C# replay.
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, unit, STRIDE
from forestry_source import source_constants
from ai_source import extract

TEMPLATES = [0, 1, 2, 3, 4, 6, 9, 10, 13, 18, 20, 24]
STATS = 0x1a35


class InitOracle(CombatOracle):
    RANGES = {'init': ([(0x1e53a, 0x1e6c2), (0x1eb33, 0x1eb40), (0x1eca0, 0x1ecf8), (0x68bd, 0x68d8), (0x6885, 0x68a0),
                        (0x219c3, 0x21a00)], [])}


def cases():
    rng = random.Random(1999)
    out = []
    for _ in range(400):
        n = rng.randint(1, 8); w = rng.choice([32, 32, 48, 64]); h = rng.choice([32, 32, 40, 64])
        parts = []
        for _ in range(n):
            objs = []
            for _ in range(rng.choice([0, 1, 2, 4, 6])):
                objs.append([rng.choice(TEMPLATES), rng.randrange(w) * 32 + 16, rng.randrange(h) * 32 + 16,
                             rng.choice([0, 1, 9, 10, 70, 200]), rng.choice([0, 0x80, 0x81])])
            parts.append({'character': rng.randrange(9), 'objects': objs})
        out.append({'player': rng.choice([0, 1, 2, 7, 0xff]), 'width': w, 'height': h, 'participants': parts})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = InitOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    table = b''.join(bytes.fromhex(c['raw_hex']) for c in extract(game)['characters'])
    o.cpu.mem_write(0xed43, table)
    all_cases = cases(); digest = hashlib.sha256()
    for case in all_cases:
        o.prepare(case['width'], case['height'])
        n = len(case['participants'])
        o.cpu.mem_write(0x6c6, bytes([n])); o.cpu.mem_write(0x1972, bytes([case['player']]))
        o.cpu.mem_write(0x97a, bytes(0xf5 * 8)); o.cpu.mem_write(o.PARTICIPANTS, b'\x5a' * (STATS * 8))
        at = 0x100
        for p, part in enumerate(case['participants']):
            base = 0x97a + 0xf5 * p
            o.cpu.mem_write(base + 0x16, bytes([part['character']])); o.cpu.mem_write(base + 0xe, struct.pack('<I', at))
            for t, x, y, hp, state in part['objects']:
                rec = unit(t, p, hp, 0, x, y, 1); rec[0x14] = state; struct.pack_into('<I', rec, 0x10, 0x40)
                o.cpu.mem_write(o.POOL + at, bytes(rec)); at += 0x40
            o.cpu.mem_write(base + 9, struct.pack('<I', at))
        o.mode = 'init'; o.boundary = None; o.run(o.CODE + 0x1e53a)
        digest.update(b''.join(bytes(o.cpu.mem_read(o.PARTICIPANTS + STATS * p, STATS)) + bytes(o.cpu.mem_read(0x97a + 0xf5 * p + 0x16, 1))
                               for p in range(n)))
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES},
            'character_table_sha256': hashlib.sha256(table).hexdigest(), 'cases': all_cases,
            'results': {'init': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
