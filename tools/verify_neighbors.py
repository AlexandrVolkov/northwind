#!/usr/bin/env python3
"""Execute the neighbour rule of lines LE:1 BC2E (with BADF/BB26, the dispatcher D19C and the WRK entries AH=9) under Unicorn:
a segment of a fence or bridge may be placed when every same-template neighbour (one footprint away in eight directions) and
the new segment itself have a neighbour pattern that the template's table (frame, mask) knows. Real: BC2E, BADF, BB26, D19C,
6885, 68A0 and the WRK handler. Compared: the result flag."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_combat import CombatOracle, unit, STRIDE
from forestry_source import source_constants

TEMPLATES = [5, 7, 25]
W = H = 32


class NeighborOracle(CombatOracle):
    RANGES = {'neighbors': ([(0xbc2e, 0xbd0f), (0xbadf, 0xbb8b), (0xd19c, 0xd1ba), (0x6885, 0x68bd)], [(0, 0x1b00)])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('neighbors did not return')


def cases():
    rng = random.Random(0xbc2e)
    out = []
    dat = None
    for n in range(3000):
        t = rng.choice(TEMPLATES); px, py = rng.randrange(0, 32), rng.randrange(0, 32)
        out.append({'t': t, 'p': [px, py], 'density': rng.choice([0.2, 0.5, 0.8]), 'other': rng.choice([0, 0.1, 0.3]), 'center': rng.random() < 0.2, 'rng': rng.randrange(1 << 30)})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    o = NeighborOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); wrk = (game / 'ARMY__02.WRK').read_bytes()
    o.cpu.mem_write(o.DAT, dat); o.cpu.mem_write(o.WRK, wrk); o.cpu.mem_write(0x19c7, objects[1]['payload'][0x19c7:0x19d7])
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    all_cases = cases(); digest = hashlib.sha256(); stats = {}
    for case in all_cases:
        t = case['t']; tp = template(t); fw, fh = tp[3], tp[4]; variant = tp[0x77] & 3; rng = random.Random(case.pop('rng'))
        o.terrain(W, H, [0] * (W * H)); o.prepare(W, H)
        o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
        o.cpu.mem_write(0x6ca + 2 * 0x56 + 9, struct.pack('<I', o.WRK))
        at = 0x200; case['objects'] = []
        for i in range(-3, 4):
            for j in range(-3, 4):
                if (i, j) == (0, 0) and not case['center']: continue
                cx, cy = (case['p'][0] + i * fw) & 255, (case['p'][1] + j * fh) & 255
                if cx >= W or cy >= H or rng.random() > case['density']: continue
                bt = t if rng.random() >= case['other'] else rng.choice([0, 3, 9])
                case['objects'].append([cx, cy, bt])
                rec = unit(bt, 3, 50, 0, cx * 32 + 16, cy * 32 + 16, 1) + bytes(10); rec[0] = 2; rec[0x0e] = bt; rec[0x0d] = variant
                o.cpu.mem_write(o.POOL + at, bytes(rec))
                o.cpu.mem_write(o.GRID + (variant * W * H + cy * W + cx) * 4, struct.pack('<I', at)); at += 0x40
        o.mode = 'neighbors'
        o.cpu.reg_write(UC_X86_REG_EAX, t | 2 << 8); o.cpu.reg_write(UC_X86_REG_EBX, 0)
        o.cpu.reg_write(UC_X86_REG_ECX, case['p'][0] * 32 + 16); o.cpu.reg_write(UC_X86_REG_EDX, case['p'][1] * 32 + 16)
        o.run(o.CODE + 0xbc2e)
        out = bytes([1 if o.cpu.reg_read(UC_X86_REG_EFLAGS) & 0x40 else 0])
        stats[out[0]] = stats.get(out[0], 0) + 1
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    print(stats)
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT', 'ARMY__02.WRK']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'neighbors': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
