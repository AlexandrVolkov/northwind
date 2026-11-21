#!/usr/bin/env python3
"""Execute the «may this be built here» decision LE:1 AF07 with the build-range rule B1EC under Unicorn.

AF07: placement probe AD5F (a blocker is accepted only as the same segment of a line), the range rule B1EC (a centre building of
the participant with condition above DAT[46] and no flags 60h within DAT[4E] cells; FFh = unlimited), the template's own check
(WRK entry AH=4, recorded stub), for lines the neighbour rule BC2E (recorded stub) and the explored cell (the participant's
fog bit of [18DA] must be clear). Real: AD5F, B1EC, 6885, 68A0. Compared: the result flag and the recorded calls."""
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

TEMPLATES = [0, 1, 3, 4, 5, 6, 7, 9, 12, 13, 15, 25]
W = H = 32
FOG = 0x7000
HOOK = 0x3000


class BuildCheckOracle(CombatOracle):
    RANGES = {'buildcheck': ([(0xaf07, 0xafdb), (0xb1ec, 0xb319), (0xad5f, 0xae3a), (0x6885, 0x68bd)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('buildcheck did not return')


def terrain_class(case, cell):
    x, y = cell % W, cell // W
    if case['river'][0] <= (x if case['river'][2] == 0 else y) < case['river'][0] + case['river'][1]: return 2
    return 3 if (x * 5 + y * 3 + case['river'][3]) % 29 == 0 else 1 if (x + y + case['river'][3]) % 17 == 0 else 0


def cases():
    rng = random.Random(0xaf07)
    out = []
    for n in range(3000):
        t = rng.choice(TEMPLATES); owner = rng.choice([0, 1, 2, 3]); px, py = rng.randrange(2, 30), rng.randrange(2, 30)
        objects = []
        for _ in range(rng.choice([0, 1, 1, 2, 4])):
            far = rng.choice([1, 3, 10, 30]) if rng.random() < 0.5 else 30
            objects.append({'t': rng.choice([3, 9, 12, 13, 15, 3, 9, 0, 1, 5, 7, 14, 16]), 'cell': [min(31, max(0, px + rng.randrange(-30, 31) // far * 3 // 3)), min(31, max(0, py + rng.randrange(-30, 31) // far))],
                            'health': rng.choice([0, 1, 20, 100, 200]), 'army': 2})
        if rng.random() < 0.6:
            objects.append({'t': rng.choice([3, 9, 12, 13, 15]), 'cell': [min(31, max(0, px + rng.randrange(-4, 5))), min(31, max(0, py + rng.randrange(-4, 5)))], 'health': rng.choice([100, 200]), 'army': 2})
        blockers = []
        for _ in range(rng.choice([0, 0, 1, 2])):
            blockers.append({'plane': rng.choice([0, 1, 1, 2]), 'same': rng.random() < 0.5, 'cell': [px, py] if rng.random() < 0.7 else [rng.randrange(2, 30), rng.randrange(2, 30)],
                             'army': rng.choice([2, 2, 3]), 't': t if rng.random() < 0.7 else rng.choice(TEMPLATES)})
        out.append({'t': t, 'owner': owner, 'p': [px, py], 'river': [rng.randrange(4, 24), rng.randrange(1, 5), rng.randrange(2), rng.randrange(40)], 'objects': objects, 'blockers': blockers,
                    'hook': rng.choice([0, 1, 1, 1]), 'line': rng.choice([0, 1, 1, 1]), 'fog': [rng.random() < 0.3 for _ in range(1)][0], 'seed': rng.randrange(1 << 16)})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = BuildCheckOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    log = []; state = {'hook': 1, 'line': 1}
    def flag(z): o.cpu.reg_write(UC_X86_REG_EFLAGS, (o.cpu.reg_read(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if z else 0))
    def hook():
        r = o.cpu.reg_read; log.append(b'H' + struct.pack('<HII', r(UC_X86_REG_EAX) & 0xff00,   # AL is what the range rule B28A left in EAX (not used by the entries here)
 r(UC_X86_REG_ECX), r(UC_X86_REG_EDX)))
        flag(not state['hook']); o.ret()
    def line():
        r = o.cpu.reg_read; log.append(b'L' + struct.pack('<HII', r(UC_X86_REG_EAX) & 0xffff, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX)))
        flag(state['line']); o.ret()
    o.stubs = {HOOK: hook, 0xbc2e: line}
    all_cases = cases(); digest = hashlib.sha256()
    for case in all_cases:
        log.clear(); state['hook'] = case['hook']; state['line'] = case['line']
        o.terrain(W, H, [terrain_class(case, c) for c in range(W * H)]); o.prepare(W, H)
        t = case['t']; tp = template(t); owner = case['owner']
        o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
        o.cpu.mem_write(0x6ca + 2 * 0x56 + 9, struct.pack('<I', (o.CODE + HOOK - struct.unpack_from('<I', tp, 0x3b)[0]) & 0xffffffff))
        base = 0x97a + 0xf5 * owner
        o.cpu.mem_write(base, bytes(0xf5))
        at = 0x400
        for obj in case['objects']:
            rec = unit(obj['t'], owner, obj['health'], 0, obj['cell'][0] * 32 + 16, obj['cell'][1] * 32 + 16, 1) + bytes(10)
            rec[0] = obj['army']; rec[0x0e] = obj['t']; rec[0x19] = obj['health']; struct.pack_into('<I', rec, 0x10, 0x40)
            o.cpu.mem_write(o.POOL + at, bytes(rec)); at += 0x40
        o.cpu.mem_write(base + 0xe, struct.pack('<I', 0x400)); o.cpu.mem_write(base + 9, struct.pack('<I', at))
        at = 0x200
        for b in case['blockers']:
            x = case['p'][0] * 32 + 16 if b['same'] else b['cell'][0] * 32 + 16; y = case['p'][1] * 32 + 16 if b['same'] else b['cell'][1] * 32 + 16
            rec = unit(b['t'], owner, 50, 0, x, y, 1) + bytes(10); rec[0] = b['army']; rec[0x0e] = b['t']
            o.cpu.mem_write(o.POOL + at, bytes(rec))
            cell = (case['p'][1] if b['same'] else b['cell'][1]) * W + (case['p'][0] if b['same'] else b['cell'][0])
            o.cpu.mem_write(o.GRID + (b['plane'] * W * H + cell) * 4, struct.pack('<I', at)); at += 0x40
        o.cpu.mem_write(0x18da, struct.pack('<I', FOG)); o.cpu.mem_write(FOG, bytes(W * H))
        if case['fog']: o.cpu.mem_write(FOG + case['p'][1] * W + case['p'][0], bytes([1 << owner]))
        o.mode = 'buildcheck'
        o.cpu.reg_write(UC_X86_REG_EAX, t | 2 << 8); o.cpu.reg_write(UC_X86_REG_EBX, owner)
        o.cpu.reg_write(UC_X86_REG_ECX, case['p'][0] * 32 + 16); o.cpu.reg_write(UC_X86_REG_EDX, case['p'][1] * 32 + 16)
        o.run(o.CODE + 0xaf07)
        out = bytes([1 if o.cpu.reg_read(UC_X86_REG_EFLAGS) & 0x40 else 0]) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'buildcheck': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
