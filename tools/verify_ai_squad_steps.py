#!/usr/bin/env python3
"""Execute the AI squad step loop LE:1 1ED26 (table LE:3 F06A, steps 0..5) under Unicorn.

Covered: census 205FB every 32 ticks, the step switch (+4A/+4B/+4C), step 1 stand (B8A2), step 2 gather (B765, B977),
step 3 clear the area (20458/20408/2055C enemy search over the three grid planes, 207CB), step 5 formation march (B7B9).
Step 4 (1EF26) is stubbed at its entry: it returns the case's flag and its calls are counted.
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
STATS, LIST, SQUAD = 0x1a35, 0x9df, 0x52


class StepOracle(CombatOracle):
    RANGES = {'steps': ([(0x1ed26, 0x1f078), (0x205fb, 0x206d5), (0x20408, 0x205fb), (0x207cb, 0x2087c), (0xb765, 0xb9eb),
                         (0x6877, 0x68d8)], [])}


def cases():
    rng = random.Random(1926)
    out = []
    for _ in range(3000):
        p = rng.randrange(4)
        ids = rng.sample(range(1, 256), rng.randint(1, 4))
        def cell(): return rng.randrange(32 * 32)
        own = [[rng.choice(TEMPLATES), rng.choice(ids + [0]), rng.randrange(256), rng.choice([0, 1, 2, 2, 0x40, 0x48, 0x50, rng.randrange(256)]),
                rng.choice([0, 0, rng.randrange(256)]), rng.randrange(65536), cell(), rng.randrange(256), rng.randrange(16), rng.randrange(3)]
               for _ in range(rng.randint(0, 10))]
        enemies = [[rng.choice(TEMPLATES), rng.randrange(8), rng.randrange(256), rng.choice([0, 1, 5, 10, 60, 127, 128, 200]), cell(), rng.randrange(3)]
                   for _ in range(rng.randint(0, 10))]
        def rect():
            a, b = rng.randrange(32), rng.randrange(32)
            return [a, b, min(31, a + rng.randrange(14)), min(31, b + rng.randrange(14))]
        squads = [[ident, rng.choice([0, 1, 7, 8, 31, 32, 0x17, rng.randrange(1 << 16)]), rng.randrange(6), rng.choice([0, 1, 2, 3, 4, 5, 0x80, 0xff, 0xff]),
                   rng.choice([0, 0, 1]), rect(), rng.choice([0, 1, 2, 5]), rng.choice([0, 1, 3, 9]), rng.choice([1, 2, 3]), rng.randrange(8),
                   [rng.randrange(32), rng.randrange(32)]] for ident in ids]
        out.append({'participant': p, 'salt': rng.randrange(256), 'own': own, 'enemies': enemies, 'squads': squads,
                    'hostile': [rng.choice([0, 0xff, rng.randrange(256)]) for _ in range(8)], 'ticks': rng.randint(1, 4),
                    'four': rng.randrange(2), 'limits': [rng.choice([0x400, 0xffffffff]), rng.choice([0x400, 0xffffffff])]})
    # Fights: an active step 3 with visible hostile enemies inside the area, so retargeting (50h/58h) is exercised.
    for _ in range(1500):
        p = rng.randrange(4); ids = rng.sample(range(1, 256), rng.randint(1, 2))
        x0, y0 = rng.randrange(24), rng.randrange(24); w, h = rng.randrange(2, 9), rng.randrange(2, 9)
        def inside(): return min(31, y0 + rng.randrange(h + 1)) * 32 + min(31, x0 + rng.randrange(w + 1))
        enemies = [[rng.choice([4, 6, 13, 16, 0, 18, 3, 20, 26]), rng.choice([q for q in range(8) if q != p]), rng.choice([0xff, 0xff, 1 << p, rng.randrange(256)]),
                    rng.choice([60, 100, 127, 5]), inside() if rng.random() < 0.85 else rng.randrange(1024), rng.choice([1, 1, 2, 0])] for _ in range(rng.randint(1, 8))]
        own = [[rng.choice([0, 1, 17, 18, 27, 13]), rng.choice(ids), rng.choice([0xff, 0xff, 4, 8, 0x0c, rng.randrange(256)]), rng.choice([0, 0, 1, 2, 0x50]),
                rng.choice([0, 0, 0, 3]), rng.randrange(65536), inside() if rng.random() < 0.7 else rng.randrange(1024), rng.randrange(256),
                rng.choice([0] + list(range(1, 9))), rng.randrange(3)] for _ in range(rng.randint(1, 10))]
        squads = [[ident, rng.choice([0, 1, 2, 5, 6, 8, 9, 30]), 3, 0xff, 0, [x0, y0, min(31, x0 + w), min(31, y0 + h)], rng.randrange(3), rng.randrange(9),
                   rng.choice([1, 2, 3]), rng.choice([2, 6, 7, 3]), [x0 + w // 2, y0 + h // 2]] for ident in ids]
        out.append({'participant': p, 'salt': rng.randrange(256), 'own': own, 'enemies': enemies, 'squads': squads,
                    'hostile': [rng.choice([0xff, 0xff, rng.randrange(256)]) for _ in range(8)], 'ticks': rng.randint(1, 4),
                    'four': rng.randrange(2), 'limits': [0x400, 0x400]})
    return out


def build_list(case):
    raw = bytearray([len(case['squads'])])
    for k, (ident, timer, step, nxt, stage, rect, moving, count, tries, planes, centre) in enumerate(case['squads']):
        s = bytearray((i * 7 + k * 31 + case['salt']) & 255 for i in range(SQUAD)); s[0] = case['participant']; s[1] = ident
        struct.pack_into('<I', s, 0x1b, timer); s[0x4a] = step; s[0x4b] = nxt; s[0x4c] = stage; s[0x4d:0x51] = bytes(rect)
        s[0x43] = moving; struct.pack_into('<I', s, 0x22, count); s[0x51] = tries; s[0x3a] = planes; s[0x20], s[0x21] = centre
        raw += s
    return raw


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = StepOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    stats = o.PARTICIPANTS; all_cases = cases(); digest = hashlib.sha256(); four_calls = [0]; started = {}

    def four():
        four_calls[0] += 1
        flags = o.cpu.reg_read(UC_X86_REG_EFLAGS) & ~0x40
        o.cpu.reg_write(UC_X86_REG_EFLAGS, flags | (0 if started['four'] else 0x40)); o.ret()
    o.stubs[0x1ef26] = four
    steps = [0x1ed77, 0x1ed78, 0x1ed87, 0x1ee02, 0x1ef26, 0x1efcc]  # LE:3 F06A
    o.cpu.mem_write(0xf06a, b''.join(struct.pack('<I', o.CODE + a) for a in steps))
    for case in all_cases:
        o.prepare(32, 32); p = case['participant']; started['four'] = case['four']
        o.cpu.mem_write(stats, bytes(STATS)); o.cpu.mem_write(stats + LIST, bytes(build_list(case)))
        o.cpu.mem_write(0x18e6, struct.pack('<II', *case['limits']))
        for q in range(8):
            o.cpu.mem_write(0x97a + 0xf5 * q + 9, struct.pack('<I', 0x100) + bytes(1) + struct.pack('<I', 0x100) + struct.pack('<I', case['hostile'][q]))
        at = 0x100; size = 32 * 32 * 4
        def place(rec, c, plane):
            o.cpu.mem_write(o.GRID + plane * size + c * 4, struct.pack('<I', at))
        for t, sq, f0f, f14, f15, f17, c, f20, target, plane in case['own']:
            rec = unit(t, p, 10, f17, (c & 31) * 32 + 5, (c >> 5) * 32 + 9, 1) + bytes(10)
            rec[0x1b] = sq; rec[0x0f] = f0f; rec[0x14] = f14; rec[0x15] = f15; rec[0x20] = f20; struct.pack_into('<I', rec, 0x10, 0x40)
            struct.pack_into('<I', rec, 0x1c, 0 if target == 0 else 0x100 + 0x40 * target)
            o.cpu.mem_write(o.POOL + at, bytes(rec)); place(rec, c, plane); at += 0x40
        end = at
        for t, owner, f20, hp, c, plane in case['enemies']:
            rec = unit(t, owner, hp, 0, (c & 31) * 32 + 17, (c >> 5) * 32 + 3, 1) + bytes(10)
            rec[0x20] = f20; struct.pack_into('<I', rec, 0x10, 0x40)
            o.cpu.mem_write(o.POOL + at, bytes(rec)); place(rec, c, plane); at += 0x40
        o.cpu.mem_write(o.POOL + at, bytes(0x40 * 16))
        participant = 0x97a + 0xf5 * p
        o.cpu.mem_write(participant + 0xe, struct.pack('<I', 0x100)); o.cpu.mem_write(participant + 9, struct.pack('<I', end))
        four_calls[0] = 0; o.mode = 'steps'
        for _ in range(case['ticks']):
            o.cpu.reg_write(UC_X86_REG_ESI, stats); o.run(o.CODE + 0x1ed26)
        count = bytes(o.cpu.mem_read(stats + LIST, 1))[0]
        out = bytes(o.cpu.mem_read(stats + LIST, 1 + SQUAD * count)) + bytes(o.cpu.mem_read(o.POOL + 0x100, at - 0x100)) + bytes([four_calls[0]])
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'steps': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
