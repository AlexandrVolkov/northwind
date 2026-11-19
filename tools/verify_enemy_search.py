#!/usr/bin/env python3
"""Execute the original enemy search LE:1 D895 (with D85E/219C3/D7B1, C344, D157) under Unicorn.

Cases are generated deterministically and stored in the fixture, so C# replays the same layouts.
EDD5 (first-contact event) is stubbed and its arguments recorded.
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

TEMPLATES = [0, 1, 2, 6, 18, 20]


class SearchOracle(CombatOracle):
    RANGES = {'search': ([(0xd895, 0xda45), (0xd85e, 0xd895), (0x219c3, 0x21a00), (0xd7b1, 0xd85e), (0xc344, 0xc35f),
                          (0xd157, 0xd182), (0x6877, 0x68a0)], [])}


def cases():
    rng = random.Random(1999)
    out = []
    for n in range(2400):
        objs = []
        for _ in range(rng.randint(0, 7)):
            objs.append([rng.choice(TEMPLATES), rng.choice([0, 1, 2, 3, 3]), rng.randint(0, 15) * 32 + rng.choice([0, 16]),
                         rng.randint(0, 15) * 32 + rng.choice([0, 16]), rng.choice([1, 1, 1, 0, 2]), rng.choice([0, 1, 10, 50, 200]),
                         rng.choice([0, 1, 255]), rng.choice([0, 1])])
        out.append({'mode': rng.choice([0, 1]), 'experience': rng.choice([0, 39, 40, 100]),
                    'self': [rng.choice([0, 1, 18]), rng.randint(0, 15) * 32 + 16, rng.randint(0, 15) * 32 + 16],
                    'hostile': rng.choice([0, 8, 0xe, 0xffffffff]), 'contacts': [rng.choice([0, 1]) for _ in range(4)], 'objects': objs})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = SearchOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    o.prepare(16, 16); o.terrain(16, 16, [1 if (i % 16) % 8 == 1 else 0 for i in range(256)])
    calls = []
    o.stubs = {0xedd5: lambda: (calls.append([o.cpu.reg_read(UC_X86_REG_EAX) & 0xffff, o.cpu.reg_read(UC_X86_REG_EBX) & 0xffff,
                                              o.cpu.reg_read(UC_X86_REG_EBP)]), o.ret())}
    o.mode = 'search'; ran = {}

    def mark(cpu, address, size, user):
        if address == o.CODE + 0xd8ed: ran['loop'] = True
    from unicorn import UC_HOOK_CODE
    o.cpu.hook_add(UC_HOOK_CODE, mark)
    all_cases = cases(); digest = hashlib.sha256()
    for case in all_cases:
        calls.clear(); ran.clear()
        o.cpu.mem_write(o.GRID, bytes(16 * 16 * 12)); o.cpu.mem_write(0x1f41, bytes(0x800))
        participant = 0x97a  # owner 0
        o.cpu.mem_write(participant + 0x12, struct.pack('<I', case['hostile'])); o.cpu.mem_write(participant + 0xb9, bytes(case['contacts']))
        tpl, sx, sy = case['self']; me = unit(tpl, 0, 10, 0, sx, sy, 1); me[0x16] = case['experience']
        refs = [0x100 + 0x40 * i for i in range(len(case['objects']) + 1)]
        o.cpu.mem_write(o.POOL + refs[0], bytes(me))
        o.cpu.mem_write(o.GRID + (64 * 0 + 256 * 1 + (sy >> 5) * 16 + (sx >> 5)) * 4 - 256 * 0, b'')
        def occupy(ref, record, t):
            w, h = t[3], t[4]; x0 = (struct.unpack_from('<I', record, 4)[0] >> 5) - w // 2; y0 = (struct.unpack_from('<I', record, 8)[0] >> 5) - h // 2
            plane = record[0xd] & 3
            for yy in range(y0, y0 + h):
                for xx in range(x0, x0 + w):
                    if 0 <= xx < 16 and 0 <= yy < 16: o.cpu.mem_write(o.GRID + (plane * 256 + yy * 16 + xx) * 4, struct.pack('<I', ref))
        occupy(refs[0], me, template(tpl))
        for ref, (t, owner, x, y, layer, hp, vis, aims) in zip(refs[1:], case['objects']):
            rec = unit(t, owner, hp, 0, x, y, layer); rec[0x20] = vis
            struct.pack_into('<I', rec, 0x1c, refs[0] if aims else 0); o.cpu.mem_write(o.POOL + ref, bytes(rec)); occupy(ref, rec, template(t))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + refs[0]); o.cpu.reg_write(UC_X86_REG_EAX, case['mode'])
        o.cpu.reg_write(UC_X86_REG_EDI, o.DAT + 2 + tpl * STRIDE); o.run(o.CODE + 0xd895)
        edi = o.cpu.reg_read(UC_X86_REG_EDI); zf = o.zf()
        found = refs.index(edi - o.POOL) if not zf and (edi - o.POOL) in refs else -1
        result = [found, zf, o.cpu.reg_read(UC_X86_REG_EAX) if ran else 0, o.cpu.reg_read(UC_X86_REG_EBX) if ran else 0, calls]
        digest.update(json.dumps(result).encode())
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES},
            'cases': all_cases, 'results': {'search': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--trace', type=Path)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
