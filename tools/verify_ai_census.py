#!/usr/bin/env python3
"""Execute the AI settlement census LE:1 1E87A..1EAD7 and the territory threat 23B10 under Unicorn.

Settlements are generated deterministically and stored in the fixture for C# replay. 1EC3F (the explored share +1A01 from the
fog map [18DA], every 256 turns after the census) runs for real. LE:3 operands of 23B10 (F149, ED20) are relocated.
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

TEMPLATES = [0, 1, 2, 3, 4, 6, 9, 10, 13, 18, 20, 24]
STATS = 0x1a35


class CensusOracle(CombatOracle):
    RANGES = {'census': ([(0x1e87a, 0x1ead8), (0x1ec3f, 0x1ec6e), (0x68bd, 0x68d8), (0x6885, 0x68a0), (0x219c3, 0x21a00)], []),
              'threat': ([(0x23b10, 0x23b6a), (0xd7b1, 0xd85e), (0x68bd, 0x68d8), (0x6885, 0x68a0)], [])}


def cases():
    rng = random.Random(1987)
    out = []
    for _ in range(1200):
        objs = []
        for _ in range(rng.randint(0, 9)):
            t = rng.choice(TEMPLATES)
            objs.append([t, rng.choice([0, 0, 0, 3]), rng.randint(0, 31) * 32 + 16, rng.randint(0, 31) * 32 + 16,
                         rng.choice([0, 1, 9, 10, 15, 70, 127, 200]), rng.choice([0, 0x80, 0x81]), rng.choice([0, 1, 2]), rng.choice([0, 2048])])
        out.append({'turn': rng.choice([0, 1, 0x100, 0x2ff]), 'mode': rng.choice([0, 0, 2]), 'previous_gold': rng.choice([0, 500, 2000]),
                    'resources': [rng.choice([0, 7, 1000, 60000]) for _ in range(4)], 'support': [rng.randrange(1 << 16), rng.randrange(1 << 16)],
                    'objects': objs})
    extra = random.Random(1988)   # separate stream: the fog of 1EC3F
    for case in out: case['fog'] = [extra.choice([0, 1, 1, 2]), extra.randrange(1 << 16)]
    return out


FOG = 0x7000


def fog_map(case):
    mode, seed = case['fog']
    if mode == 0: return bytes(1024)
    if mode == 2: return b'\xff' * 1024
    return bytes(((c * 167 + seed) ^ (c >> 3) ^ (seed >> 8)) & 255 for c in range(1024))


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = CensusOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    OBJ3 = 0x9000
    for at, value in [(0x23b12, 0xf149), (0x23b20, 0xed20), (0x23b57, 0xf149)]:
        window = bytes(o.cpu.mem_read(o.CODE + at, 10)); k = window.index(struct.pack('<I', value))
        o.cpu.mem_write(o.CODE + at + k, struct.pack('<I', OBJ3 + value - 0xe000))
    o.prepare(32, 32)
    all_cases = cases(); census = hashlib.sha256(); threat = hashlib.sha256()
    for case in all_cases:
        participant = 0x97a
        o.cpu.mem_write(participant, bytes(0xf5)); o.cpu.mem_write(o.PARTICIPANTS, bytes(STATS))
        g, m, w, people = case['resources']
        o.cpu.mem_write(participant + 0x18, struct.pack('<4I', m, w, g, people))
        o.cpu.mem_write(participant + 0x28, struct.pack('<2I', case['support'][1], case['support'][0]))
        stats = o.PARTICIPANTS
        o.cpu.mem_write(stats + 1, struct.pack('<I', case['turn'])); o.cpu.mem_write(stats + 5, bytes([case['mode']]))
        o.cpu.mem_write(stats + 0x6b, struct.pack('<I', case['previous_gold']))
        o.cpu.mem_write(o.GRID, bytes(32 * 32 * 12))
        refs = []; at = 0x100
        for t, owner, x, y, hp, state, kind, stock in case['objects']:
            rec = unit(t, owner, hp, 0, x, y, 1); rec[0x14] = state; rec[0x23] = kind; struct.pack_into('<I', rec, 0x24, stock)
            struct.pack_into('<I', rec, 0x10, 0x40); o.cpu.mem_write(o.POOL + at, bytes(rec)); refs.append(at)
            o.cpu.mem_write(o.GRID + (256 * 0 + 1024 * 1 + (y >> 5) * 32 + (x >> 5)) * 4, struct.pack('<I', at)); at += 0x40
        o.cpu.mem_write(participant + 0xe, struct.pack('<I', 0x100)); o.cpu.mem_write(participant + 9, struct.pack('<I', at))
        o.cpu.mem_write(0x18da, struct.pack('<I', FOG)); o.cpu.mem_write(FOG, fog_map(case))
        o.mode = 'census'; o.boundary = None; o.cpu.reg_write(UC_X86_REG_ESI, stats); o.run(o.CODE + 0x1e87a)
        census.update(bytes(o.cpu.mem_read(stats, 0x80)) + bytes(o.cpu.mem_read(stats + 0x19e0, 0x50)) + bytes(o.cpu.mem_read(0x1c6e, 16)))
        o.mode = 'threat'; o.boundary = None
        o.cpu.mem_write(OBJ3 + 0xed20 - 0xe000, struct.pack('<I', stats)); o.cpu.mem_write(OBJ3 + 0xf149 - 0xe000, b'\x55' * 0x40)
        o.run(o.CODE + 0x23b10)
        threat.update(bytes(o.cpu.mem_read(OBJ3 + 0xf149 - 0xe000, 0x40)))
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'census': {'case_count': len(all_cases), 'sha256': census.hexdigest()},
                        'threat': {'case_count': len(all_cases), 'sha256': threat.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
