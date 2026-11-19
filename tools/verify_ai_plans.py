#!/usr/bin/env python3
"""Execute the AI plan executor LE:1 23667..23A6D (with 23ABB, 23B10, EE69, 1EB5C) under Unicorn.

EDD5 is stubbed and records type, other (AH) and queue owner (BL). Register leftovers are not compared: message parameters
(EBP), and in the military list +1AB the bytes +3 (BH: 96h for mode 5, a leftover for mode 7, kept after cancellation) and
+5..+0B of every record.
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import unit, STRIDE
from verify_ai_turn import TurnOracle, participant, TEMPLATES, STATS

PLANS, MILITARY = 0x697, 0x1ab


class PlanOracle(TurnOracle):
    RANGES = {'plans': ([(0x23667, 0x23a6e), (0x23abb, 0x23b10), (0x23b10, 0x23b6a), (0xd7b1, 0xd85e), (0x68bd, 0x68d8), (0x6885, 0x68a0),
                         (0xee69, 0xee92), (0x1eaed, 0x1eb68)], [])}


def cases():
    rng = random.Random(4242)
    out = []
    for _ in range(2000):
        n = rng.randint(2, 8); me = rng.randrange(n)
        parts = []
        for p in range(n):
            parts.append({'relations': [0 if q == p else rng.choice([0, 1, 2, 2, 3, 4, 5]) for q in range(n)],
                          'queue': [[rng.choice([0, 6, 0, 6, 3, 5]), rng.randrange(n)] for _ in range(rng.choice([0, 1, 3]))],
                          'resources': [0, 0, 0], 'strength': rng.choice([0, 1999, 2000, 5000, 15000, 25000]),
                          'territory': rng.choice([[0, 31, 0, 31], sorted(rng.sample(range(32), 2)) + sorted(rng.sample(range(32), 2))])})
        plans = [[rng.choice([1, 1, 2, 3, 0xa, 0xa, 5, 7, 0xff]), rng.randrange(n), rng.choice([0, 1, 2, 3]),
                  rng.choice([0, 1, 2, 500, 3000, 0xffffffff]), rng.choice([0, 1, 0x1f, 0x20, 0x40])] for _ in range(rng.choice([1, 2, 5, 20]))]
        military = [[rng.choice([5, 6, 7, 9, 2]), rng.randrange(n), rng.randrange(n), rng.randrange(8)] for _ in range(rng.choice([0, 1, 4, 20]))]
        objs = [[rng.choice(TEMPLATES), rng.randrange(n), rng.randrange(32) * 32 + 16, rng.randrange(32) * 32 + 16, rng.choice([0, 10, 70, 127])]
                for _ in range(rng.choice([0, 10, 60, 200]))]
        if objs and rng.random() < 0.3:   # mirrored armies give equal threats (ties in the strongest-neighbour test)
            objs = objs[:40] + [[t, (owner + 1) % n, x ^ 0x200, y, hp] for t, owner, x, y, hp in objs[:40]]
        out.append({'me': me, 'flags': rng.choice([0, 1, 1, 1, 3]), 'limits': [rng.choice([10, 3000, 12000, 22000]) for _ in range(2)],
                    'factor': [rng.choice([8, 16]), rng.choice([0, 7, 300, 1000])], 'base': rng.choice([0, 100, 5000]),
                    'participants': parts, 'plans': plans, 'military': military, 'objects': objs})
    for _ in range(400):   # truce plans whose timer expires against a strong neighbour (messages 6 and 0, ties)
        n = rng.randint(3, 8); me = rng.randrange(n); target = rng.choice([q for q in range(n) if q != me])
        parts = [{'relations': [2 if q == target and p == me else rng.choice([0, 2]) for q in range(n)], 'queue': [], 'resources': [0, 0, 0],
                  'strength': rng.choice([0, 5000, 25000]), 'territory': [0, 31, 0, 31]} for p in range(n)]
        other = rng.choice([q for q in range(n) if q != target])
        objs = []
        for _ in range(rng.choice([4, 8, 12])):
            t, x, y = rng.choice([10, 13, 18, 20, 24]), rng.randrange(16) * 32 + 16, rng.randrange(32) * 32 + 16
            objs.append([t, target, x, y, 127])
            if rng.random() < 0.8: objs.append([t, other, x + 0x200, y, 127])
        out.append({'me': me, 'flags': 1, 'limits': [rng.choice([3000, 12000]), 3000], 'factor': [8, 0], 'base': 0,
                    'participants': parts, 'plans': [[0xa, target, rng.choice([1, 2]), 1, 0]], 'military': [], 'objects': objs})
    return out


def masked(b):
    b = bytearray(b)
    for k in range(20):
        at = MILITARY + k * 0x15
        b[at + 5:at + 12] = bytes(7); b[at + 3] = 0
    return bytes(b)


def generate(game):
    from forestry_source import source_constants
    constants = source_constants(game); report, code = analyze(game)
    o = PlanOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    calls = []

    def send():
        r = o.cpu.reg_read
        calls.append([r(UC_X86_REG_EAX) & 255, (r(UC_X86_REG_EAX) >> 8) & 255, r(UC_X86_REG_EBX) & 255]); o.ret()
    o.stubs = {0xedd5: send}
    o.prepare(32, 32)
    all_cases = cases(); digest = hashlib.sha256()
    for case in all_cases:
        calls.clear(); n = len(case['participants']); me = case['me']
        o.cpu.mem_write(0x6c6, bytes([n])); o.cpu.mem_write(0x97a, bytes(0xf5 * 8)); o.cpu.mem_write(o.PARTICIPANTS, bytes(STATS * 8))
        for p, part in enumerate(case['participants']):
            o.cpu.mem_write(0x97a + 0xf5 * p, bytes(participant(part)))
            b = bytearray(STATS); b[0] = p; t = part['territory']; b[0x63:0x67] = bytes([t[0], t[2], t[1], t[3]])
            struct.pack_into('<I', b, 0x19f1, part['strength'])
            for k in range(20): b[PLANS + k * 0x15] = 0xff; b[MILITARY + k * 0x15] = 0xff
            if p == me:
                b[0x1a27] = case['flags']; struct.pack_into('<I', b, 0x31, case['limits'][0]); struct.pack_into('<I', b, 0x41, case['limits'][1])
                struct.pack_into('<I', b, 0x49, case['factor'][0]); struct.pack_into('<I', b, 0x5d, case['factor'][1]); struct.pack_into('<I', b, 0x19f9, case['base'])
                for k, (mode, target, stage, budget, timer) in enumerate(case['plans']):
                    at = PLANS + k * 0x15; b[at] = mode; b[at + 1] = target; b[at + 2] = stage; b[at + 4] = target
                    struct.pack_into('<I', b, at + 8, budget); struct.pack_into('<I', b, at + 0x10, timer)
                for k, (mode, first, fourth, obj) in enumerate(case['military']):
                    at = MILITARY + k * 0x15; b[at] = mode; b[at + 1] = first; b[at + 4] = fourth; struct.pack_into('<I', b, at + 8, 0x100 + obj * 0x40)
            o.cpu.mem_write(o.PARTICIPANTS + STATS * p, bytes(b))
        o.cpu.mem_write(o.GRID, bytes(32 * 32 * 12)); at = 0x100
        for t, owner, x, y, hp in case['objects']:
            rec = unit(t, owner, hp, 0, x, y, 1); o.cpu.mem_write(o.POOL + at, bytes(rec))
            o.cpu.mem_write(o.GRID + (1024 + (y >> 5) * 32 + (x >> 5)) * 4, struct.pack('<I', at)); at += 0x40
        for k in range(len(case['objects']), 8):   # referenced by military records of mode 9
            o.cpu.mem_write(o.POOL + 0x100 + k * 0x40, bytes(unit(0, k % n, 0, 0, 16, 16, 1)))
        stats = o.PARTICIPANTS + STATS * me
        o.cpu.mem_write(0xed20, struct.pack('<I', stats)); o.cpu.mem_write(0xed2c, bytes([me])); o.cpu.mem_write(0xed2d, struct.pack('<I', 0x97a + 0xf5 * me))
        o.cpu.mem_write(0xf149, b'\x55' * 0x40)
        o.cpu.reg_write(UC_X86_REG_ESI, stats)
        o.mode = 'plans'; o.boundary = None; o.run(o.CODE + 0x23667)
        state = b''.join((masked(o.cpu.mem_read(o.PARTICIPANTS + STATS * p, STATS)) if p == me else bytes(o.cpu.mem_read(o.PARTICIPANTS + STATS * p, STATS)))
                         + bytes(o.cpu.mem_read(0x97a + 0xf5 * p, 0xf5)) for p in range(n))
        digest.update(state + bytes(o.cpu.mem_read(0xf149, 0x40)) + json.dumps(calls).encode())
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'plans': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
