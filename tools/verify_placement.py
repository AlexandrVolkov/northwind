#!/usr/bin/env python3
"""Execute the creation placement check LE:1 AC63 (with its footprint scan 7B79) and the free-spot search 10AD2 under Unicorn.

AC63: may an object of template AL (army AH) stand at the point (ECX, EDX) in the plane BL & 3: the plane must be empty under the
whole footprint (otherwise blocked, EDI = the blocker), and every cell needs a terrain value above zero for the template (on the
plane 1 a water cell is also allowed over a supporting object of plane 0). 10AD2 walks rings of random points around a point and
calls AC63; 9E41/9E55 are replaced by a recorded sequence (the random generators are ported elsewhere).
Compared: flags, EDI, ECX/EDX after the call."""
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

TEMPLATES = [0, 1, 2, 3, 4, 6, 7, 9, 13]
W = H = 32


class PlacementOracle(CombatOracle):
    RANGES = {'placement': ([(0xac63, 0xad5f), (0x7b79, 0x7c63), (0x68a0, 0x68bd), (0x10ad2, 0x10b75)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=3000000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('placement did not return')


def terrain_class(case, cell):
    x, y = cell % W, cell // W
    if case['river'][0] <= (x if case['river'][2] == 0 else y) < case['river'][0] + case['river'][1]: return 2
    return 3 if (x * 5 + y * 3 + case['river'][3]) % 29 == 0 else 1 if (x + y + case['river'][3]) % 17 == 0 else 0


def cases():
    rng = random.Random(10752)
    out = []
    for n in range(2500):
        t = rng.choice(TEMPLATES); army = 2; px, py = rng.randrange(0, 34 * 32), rng.randrange(0, 34 * 32)
        search = n % 2 == 1
        blockers = []
        for _ in range(rng.choice([0, 1, 3, 6, 10])):
            blockers.append({'plane': rng.choice([0, 0, 1, 2]), 'cell': [min(31, max(0, (px >> 5) + rng.randrange(-1, 3))), min(31, max(0, (py >> 5) + rng.randrange(-1, 3)))] if rng.random() < 0.5 else [rng.randrange(0, 32), rng.randrange(0, 32)], 't': rng.choice(TEMPLATES + [7, 7]),
                             'health': rng.choice([0, 1, 20, 100, 200])})
        out.append({'t': t, 'army': army, 'plane': rng.choice([0, 1, 1, 2]), 'x': px, 'y': py, 'search': search,
                    'river': [rng.randrange(4, 24), rng.randrange(1, 5), rng.randrange(2), rng.randrange(40)], 'blockers': blockers,
                    'seed': rng.randrange(1 << 16)})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = PlacementOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    state = {'seed': 0}
    def rnd():
        state['seed'] = (state['seed'] * 25173 + 13849) & 0xffff; v = state['seed'] >> 8   # the sequence is shared with the C# check
        o.cpu.reg_write(UC_X86_REG_EAX, (o.cpu.reg_read(UC_X86_REG_EAX) & 0xffffff00) | v); o.ret()
    o.stubs = {0x9e41: rnd, 0x9e55: rnd}
    all_cases = cases(); digest = hashlib.sha256(); stats = {}
    for case in all_cases:
        state['seed'] = case['seed']
        o.terrain(W, H, [terrain_class(case, c) for c in range(W * H)]); o.prepare(W, H)
        o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2)); o.cpu.mem_write(0x18e6, struct.pack('<II', W * 32, H * 32))
        at = 0x200
        for b in case['blockers']:
            rec = unit(b['t'], 5, b['health'], 0, b['cell'][0] * 32, b['cell'][1] * 32, 1) + bytes(10)
            rec[0] = 2; rec[0x0e] = b['t']; rec[0x19] = b['health']
            o.cpu.mem_write(o.POOL + at, bytes(rec))
            o.cpu.mem_write(o.GRID + (b['plane'] * W * H + b['cell'][1] * W + b['cell'][0]) * 4, struct.pack('<I', at)); at += 0x40
        o.mode = 'placement'
        o.cpu.reg_write(UC_X86_REG_EAX, case['t'] | case['army'] << 8); o.cpu.reg_write(UC_X86_REG_EBX, case['plane'])
        o.cpu.reg_write(UC_X86_REG_ECX, case['x']); o.cpu.reg_write(UC_X86_REG_EDX, case['y'])
        o.cpu.reg_write(UC_X86_REG_EDI, 0)
        o.run(o.CODE + (0x10ad2 if case['search'] else 0xac63))
        fl = o.cpu.reg_read(UC_X86_REG_EFLAGS)
        out = struct.pack('<BBIII', 1 if fl & 0x40 else 0, 1 if fl & 1 else 0, o.cpu.reg_read(UC_X86_REG_EDI) - o.POOL if (fl & 1) and not case['search'] else 0,
                          o.cpu.reg_read(UC_X86_REG_ECX) if case['search'] else 0, o.cpu.reg_read(UC_X86_REG_EDX) if case['search'] else 0)
        stats[(case['search'], (out[0], out[1]))] = stats.get((case['search'], (out[0], out[1])), 0) + 1
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    print(stats)
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'placement': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
