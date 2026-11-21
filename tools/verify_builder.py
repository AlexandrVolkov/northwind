#!/usr/bin/env python3
"""Execute the builder engine of a building command, LE:1 AFDB from its body B030 (command 58h of the castle: any building
and the lines of bridges and fences), under Unicorn.

Real: 68A0, AD5F (placement probe), 69FE (step along a line) and 6996 (direction). Stubbed and recorded: A758 (new object:
returns a scratch record, may fail), 43E5 (registration), A6AE (development tree) and D453 (population recount).
Compared: the builder record, the participant's resources, the new records, the blocking objects and the hook calls."""
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

TEMPLATES = [3, 4, 5, 6, 7, 9, 12, 15, 25]
W = H = 32
SCRATCH = 0x3000


class BuilderOracle(CombatOracle):
    RANGES = {'builder': ([(0xb030, 0xb1ec), (0xad5f, 0xae3a), (0x69fe, 0x6a73), (0x6996, 0x69fe), (0x68a0, 0x68bd)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('builder did not return')


def terrain_class(case, cell):
    x, y = cell % W, cell // W
    if case['river'][0] <= (x if case['river'][2] == 0 else y) < case['river'][0] + case['river'][1]: return 2
    return 3 if (x * 5 + y * 3 + case['river'][3]) % 29 == 0 else 1 if (x + y + case['river'][3]) % 17 == 0 else 0


def cases():
    rng = random.Random(1588)
    out = []
    for n in range(3000):
        t = rng.choice(TEMPLATES); owner = rng.choice([0, 1, 2, 3])
        x0, y0 = rng.randrange(3, 28), rng.randrange(3, 28)
        if t in (5, 7, 25) and rng.random() < 0.8:
            if rng.random() < 0.5: x1, y1 = rng.randrange(3, 29), y0
            else: x1, y1 = x0, rng.randrange(3, 29)
            if rng.random() < 0.3: x1, y1 = rng.randrange(3, 29), rng.randrange(3, 29)
        else: x1, y1 = x0, y0
        blockers = []
        for _ in range(rng.choice([0, 0, 1, 2, 4])):
            blockers.append({'plane': rng.choice([0, 1, 1, 2]), 'cell': [rng.randrange(2, 30), rng.randrange(2, 30)], 'same': rng.random() < 0.4,
                             'army': rng.choice([2, 2, 3]), 't': t if rng.random() < 0.7 else rng.choice(TEMPLATES)})
        out.append({'t': t, 'owner': owner, 'p0': [x0, y0], 'p1': [x1, y1], 'river': [rng.randrange(4, 24), rng.randrange(1, 5), rng.randrange(2), rng.randrange(40)],
                    'resources': [rng.choice([0, 80, 100, 160, 1000, 100000]) for _ in range(4)], 'fail_create': rng.choice([0, 0, 0, 0, 1]),
                    'support': rng.choice([0, 0x27, 0x28, 0x30, 0x32, 0x33]), 'blockers': blockers, 'toggle': rng.randrange(2)})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    o = BuilderOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    o.cpu.mem_write(0x287e, objects[1]['payload'][0x287e:0x297e]); o.cpu.mem_write(0x19c7, objects[1]['payload'][0x19c7:0x19d7])
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    log = []; created = [0]
    def flag(z): o.cpu.reg_write(UC_X86_REG_EFLAGS, (o.cpu.reg_read(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if z else 0))
    def create():
        r = o.cpu.reg_read
        log.append(b'A' + struct.pack('<HHII', r(UC_X86_REG_EAX) & 0xffff, r(UC_X86_REG_EBX) & 0xffff, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX)))
        record = o.POOL + SCRATCH + 0x40 * created[0]; created[0] += 1
        o.cpu.mem_write(record, bytes(0x40)); o.cpu.reg_write(UC_X86_REG_EDI, record)
        flag(not state['fail']); o.ret()
    def register():
        log.append(b'R' + struct.pack('<I', o.cpu.reg_read(UC_X86_REG_EAX))); o.ret()
    def tree():
        log.append(b'T' + struct.pack('<H', o.cpu.reg_read(UC_X86_REG_EAX) & 0xffff)); o.ret()
    def recount():
        log.append(b'D' + struct.pack('<I', o.cpu.reg_read(UC_X86_REG_ESI))); o.ret()
    state = {'fail': False}
    o.stubs = {0xa758: create, 0x43e5: register, 0xa6ae: tree, 0xd453: recount}
    all_cases = cases(); digest = hashlib.sha256(); TABLE = 0x2000
    for case in all_cases:
        log.clear(); created[0] = 0; o.cpu.mem_write(o.POOL + SCRATCH, bytes(0x40 * 64)); state['fail'] = bool(case['fail_create'])
        o.terrain(W, H, [terrain_class(case, c) for c in range(W * H)]); o.prepare(W, H)
        o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
        owner = case['owner']; base = 0x97a + 0xf5 * owner
        o.cpu.mem_write(base, bytes(0xf5)); o.cpu.mem_write(base + 0x18, struct.pack('<4I', *case['resources'])); o.cpu.mem_write(base + 0x2c, struct.pack('<I', case['support']))
        t = case['t']; tp = template(t); ox = (tp[3] & 1) << 4; oy = (tp[4] & 1) << 4
        builder = bytearray(unit(2, owner, 100, 0, 200, 200, 1) + bytes(10)); builder[0] = 2; builder[3] = owner; builder[0x22] = t
        builder[0x14] = 0x5b
        struct.pack_into('<HHHH', builder, 0x29, case['p0'][0] * 32 + ox, case['p0'][1] * 32 + oy, case['p1'][0] * 32 + ox, case['p1'][1] * 32 + oy)
        o.cpu.mem_write(o.POOL + 0x100, bytes(builder))
        at = 0x200
        for b in case['blockers']:
            rec = unit(b['t'], owner if b['same'] else 5, 50, 0, case['p0'][0] * 32 + ox if b['same'] else b['cell'][0] * 32, case['p0'][1] * 32 + oy if b['same'] else b['cell'][1] * 32, 1) + bytes(10)
            rec[0] = b['army']; rec[0x0e] = b['t']
            o.cpu.mem_write(o.POOL + at, bytes(rec))
            cell = (case['p0'][1] if b['same'] else b['cell'][1]) * W + (case['p0'][0] if b['same'] else b['cell'][0])
            o.cpu.mem_write(o.GRID + (b['plane'] * W * H + cell) * 4, struct.pack('<I', at)); at += 0x40
        o.cpu.mem_write(0x50d, bytes([case['toggle']]))
        # The scan of B055: the template number at +7 of the inline table, stride 4.
        o.cpu.mem_write(TABLE, bytes(0x10) + bytes([t, 0, 0, 0]) * 3)
        o.mode = 'builder'; o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EDX, TABLE)
        o.run(o.CODE + 0xb030)
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x40)) + bytes(o.cpu.mem_read(base + 0x18, 0x10)) + \
            bytes(o.cpu.mem_read(o.POOL + SCRATCH, 0x40 * max(1, created[0]))) + bytes(o.cpu.mem_read(o.POOL + 0x200, 0x40 * len(case['blockers']))) + \
            b''.join(log) + bytes(o.cpu.mem_read(0x50d, 1))
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'arctangent_hex': objects[1]['payload'][0x287e:0x297e].hex(),
            'results': {'builder': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
