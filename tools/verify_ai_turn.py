#!/usr/bin/env python3
"""Execute the AI turn LE:1 2323A..23550 (vassal tribute, collapse, relation decisions every 128 turns) under Unicorn.

Real: 23B10 (D7B1), 23A95, 23B6A, EE69, 1EAD8, 1EB5C/1EB40/1EAED. EDD5 is stubbed and recorded (type, other, owner, BH, EBP).
The construction stage 23551 is the boundary. Plan bytes +5..+7 hold leftovers of address registers (CH = byte 1 of the
participant address, DL/DH) and are masked; +8 (EBP) is compared. LE:3 variables (ED20, ED2C, ED2D, F149) live at their flat
addresses, which do not overlap LE:2 (3E00h bytes).
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
PLANS = 0x697


class TurnOracle(CombatOracle):
    RANGES = {'turn': ([(0x2323a, 0x23551), (0x2365c, 0x23667), (0x23b10, 0x23b8e), (0x23a95, 0x23abb), (0xd7b1, 0xd85e),
                        (0x68bd, 0x68d8), (0x6885, 0x68a0), (0xee69, 0xee92), (0x1ead8, 0x1eb68)], [])}

    def run(self, address):
        # A whole-map territory scan (D7B1) exceeds the default instruction budget.
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('turn did not return')


def cases():
    rng = random.Random(1234)
    out = []
    for _ in range(1500):
        n = rng.randint(2, 8); me = rng.randrange(n)
        turn = rng.choice([(k * 128 - 0x42) & 0xffffffff for k in [1, 2, 3, 4]] + [rng.randrange(1 << 32)])
        parts = []
        for p in range(n):
            plans = [[rng.choice([1, 2, 3, 5, 7]), rng.randrange(n), rng.randrange(n)] for _ in range(rng.choice([0, 0, 1, 3]))]
            parts.append({
                'relations': [0 if q == p else rng.choice([0, 1, 1, 2, 3, 4, 5]) for q in range(n)],
                'queue': [[rng.choice([3, 5, 0xc, 0xd, 0x13]), rng.randrange(n)] for _ in range(rng.choice([0, 0, 1, 3]))],
                'resources': [rng.choice([0, 100, 5000]) for _ in range(3)],
                'previous': [rng.choice([0, 50, 6000]) for _ in range(3)],
                'strength': rng.choice([0, 1999, 2000, 5000, 15000, 25000]), 'loss': rng.choice([0, 199, 200, 1000]),
                'territory': rng.choice([[0, 31, 0, 31], [4, 27, 2, 30], [0, 31, 0, 12], [0, 31, 12, 31], [3, 20, 12, 12], sorted(rng.sample(range(32), 2)) + sorted(rng.sample(range(32), 2))]),
                'flags': rng.choice([0, 1, 1, 3, 2]), 'limits': [rng.choice([10, 12000, 22000]), rng.choice([10, 12000, 22000])],
                'bytes': [rng.randrange(256) for _ in range(6)], 'plans': plans})
        objs = [[rng.choice(TEMPLATES), rng.randrange(n), rng.randrange(32) * 32 + 16, rng.randrange(32) * 32 + 16,
                 rng.choice([0, 10, 70, 127])] for _ in range(rng.choice([0, 3, 10, 60, 200]))]
        out.append({'me': me, 'turn': turn, 'ebp': rng.randrange(1 << 32), 'participants': parts, 'objects': objs})
    return out


def block(part, p, turn):
    b = bytearray(STATS); b[0] = p; struct.pack_into('<I', b, 1, turn)
    t = part['territory']; b[0x63:0x67] = bytes([t[0], t[2], t[1], t[3]])
    struct.pack_into('<I', b, 0x35, part['limits'][0]); struct.pack_into('<I', b, 0x3d, part['limits'][1])
    struct.pack_into('<I', b, 0x6f, part['loss']); struct.pack_into('<I', b, 0x19f1, part['strength'])
    struct.pack_into('<3I', b, 0x1a28, *part['previous'])
    b[0x1a27] = part['flags']
    for at, v in zip([0x19e4, 0x19fd, 0x19fe, 0x19ff, 0x1a15, 0x1a34], part['bytes']): b[at] = v
    for k in range(20): b[PLANS + k * 0x15] = 0xff
    for k, (mode, first, target) in enumerate(part['plans']): b[PLANS + k * 0x15] = mode; b[PLANS + k * 0x15 + 1] = first; b[PLANS + k * 0x15 + 4] = target
    return b


def participant(part):
    r = bytearray(0xf5)
    g, m, w = part['resources']; struct.pack_into('<3I', r, 0x18, m, w, g)
    r[0xb9:0xb9 + len(part['relations'])] = bytes(part['relations'])
    q = bytearray(b'\xff' + bytes(13)) * 8
    for s, (t, other) in enumerate(part['queue']): q[s * 14] = t; q[s * 14 + 9] = other
    r[0x34:0x34 + 112] = q
    return r


def masked(b):
    b = bytearray(b)
    for k in range(20): b[PLANS + k * 0x15 + 5:PLANS + k * 0x15 + 8] = b'\0\0\0'
    return bytes(b)


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = TurnOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    calls = []

    def send():
        r = o.cpu.reg_read
        calls.append([r(UC_X86_REG_EAX) & 255, (r(UC_X86_REG_EAX) >> 8) & 255, r(UC_X86_REG_EBX) & 255, (r(UC_X86_REG_EBX) >> 8) & 255, r(UC_X86_REG_EBP)])
        o.ret()
    o.stubs = {0xedd5: send}
    o.prepare(32, 32)
    all_cases = cases(); digest = hashlib.sha256()
    for case in all_cases:
        calls.clear(); n = len(case['participants']); me = case['me']
        o.cpu.mem_write(0x6c6, bytes([n])); o.cpu.mem_write(0x97a, bytes(0xf5 * 8)); o.cpu.mem_write(o.PARTICIPANTS, bytes(STATS * 8))
        for p, part in enumerate(case['participants']):
            o.cpu.mem_write(0x97a + 0xf5 * p, bytes(participant(part)))
            o.cpu.mem_write(o.PARTICIPANTS + STATS * p, bytes(block(part, p, case['turn'] if p == me else 0)))
        o.cpu.mem_write(o.GRID, bytes(32 * 32 * 12)); at = 0x100
        for t, owner, x, y, hp in case['objects']:
            rec = unit(t, owner, hp, 0, x, y, 1); o.cpu.mem_write(o.POOL + at, bytes(rec))
            o.cpu.mem_write(o.GRID + (1024 + (y >> 5) * 32 + (x >> 5)) * 4, struct.pack('<I', at)); at += 0x40
        stats = o.PARTICIPANTS + STATS * me
        o.cpu.mem_write(0xed20, struct.pack('<I', stats)); o.cpu.mem_write(0xed2c, bytes([me])); o.cpu.mem_write(0xed2d, struct.pack('<I', 0x97a + 0xf5 * me))
        o.cpu.mem_write(0xf149, b'\x55' * 0x40)
        o.cpu.reg_write(UC_X86_REG_ESI, stats); o.cpu.reg_write(UC_X86_REG_EBP, case['ebp'])
        o.mode = 'turn'; o.boundary = 0x23551; o.run(o.CODE + 0x2323a)
        state = b''.join(masked(o.cpu.mem_read(o.PARTICIPANTS + STATS * p, STATS)) + bytes(o.cpu.mem_read(0x97a + 0xf5 * p, 0xf5)) for p in range(n))
        digest.update(state + bytes(o.cpu.mem_read(0xf149, 0x40)) + json.dumps([o.stopped, calls]).encode())
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'turn': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
