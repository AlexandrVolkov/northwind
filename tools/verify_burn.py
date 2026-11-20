#!/usr/bin/env python3
"""Execute the fire (ARMY__00.WRK effect types 13h..15h, CE3), its embers (16h, FA4) and the smoke column (0Bh, 9D5)
under Unicorn.

Real: 982B (object under the point), 97C6 (object in the cell), 68BD, the WRK handlers. Stubbed and recorded: 9727 (new
effects; returns a scratch record so the copied height lands there), A47E (sounds), CFAC (scorched ground).
FS:[08] is the pool base, FS:[0C] points at the drift vector DS:1C8E. Cases are random and stored for C# replay.
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, unit, pattern, STRIDE
from forestry_source import source_constants

W, H = 16, 16
TEMPLATES = [0, 1, 2, 3, 4, 9, 18]
SCRATCH = 0x3000


class BurnOracle(CombatOracle):
    RANGES = {'burn': ([(0x982b, 0x98cc), (0x97c6, 0x982b), (0x68bd, 0x68d8)], [(0xce3, 0xfd1), (0x9d5, 0xa44)])}


def cases():
    rng = random.Random(3299)
    out = []
    for n in range(6000):
        mode = rng.choice(['init', 'step', 'step', 'step', 'step', 'column', 'embers'])
        x, y = rng.randrange(8, W * 32 - 8), rng.randrange(8, H * 32 - 8)
        c = {'mode': mode, 'x': x, 'y': y, 'layer': rng.choice([1, 1, 2]), 'army': rng.choice([0, 0, 2]),
             'type': rng.choice([0x13, 0x14, 0x15]), 'f16': rng.choice([0, 1, 1, 2]), 'f0b': rng.choice([0x1a, 0x1b, 0x37, 1, 2, 0x20]),
             'f06': rng.choice([1, 1, 2, 0x10, 0x20, 0x5a, 0x41]), 'f17': rng.choice([0, 2, 0xff]), 'f1e': rng.choice([0x10, 1, 2, 50, 0x21]),
             'f1c': rng.choice([0, 1, 0x50]), 'f11': rng.choice([0, 1, 5, 25]), 'f10': rng.choice([0, 0x200]), 'f20': rng.choice([0, 0x30, 0x300]),
             'drift': rng.choice([[0, 0], [0x200, 0], [-0x300, 0x100], [0x100, 0x1ff], [5, -7]]), 'forest': rng.choice([0, 1]),
             'object': None, 'cell_object': None}
        if rng.random() < 0.75:
            t = rng.choice(TEMPLATES)
            c['object'] = {'t': t, 'hp': rng.choice([0, 1, 2, 10, 60, 127, 200]), 'frame': rng.choice([0, 0x50, 0x400, 0xffff]),
                           'dx': rng.choice([0, 0, 3, -9, 40]), 'dy': rng.choice([0, 0, 5, -12, 40]), 'owner': rng.choice([0, 1])}
        if rng.random() < 0.5:
            c['cell_object'] = {'t': rng.choice(TEMPLATES), 'hp': rng.choice([0, 3, 10, 127])}
        out.append(c)
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = BurnOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat); o.cpu.mem_write(o.WRK, (game / 'ARMY__00.WRK').read_bytes())
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    log = []
    def spawn():
        r = o.cpu.reg_read
        log.append(['S', r(UC_X86_REG_EAX) & 255, r(UC_X86_REG_EBX) & 0xffff, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX)])
        o.cpu.mem_write(o.POOL + SCRATCH, bytes(0x22)); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + SCRATCH); o.ret()
    o.stubs = {0x9727: spawn, 0xa47e: lambda: (log.append(['A', o.cpu.reg_read(UC_X86_REG_EAX)]), o.ret()),
               0xcfac: lambda: (log.append(['C', o.cpu.reg_read(UC_X86_REG_EDX) & 0xffff, o.cpu.reg_read(UC_X86_REG_EAX) & 0xffff,
                                            o.cpu.reg_read(UC_X86_REG_ECX) & 0xffff]), o.ret())}
    all_cases = cases(); digest = hashlib.sha256()
    for c in all_cases:
        log.clear()
        o.terrain(W, H, [c['forest']] * (W * H)); o.prepare(W, H)
        o.cpu.mem_write(0x18e6, struct.pack('<II', W * 32, H * 32))
        o.cpu.mem_write(o.FS, struct.pack('<IIII', H * 32, W * 32, o.POOL, 0x1c8e))
        o.cpu.mem_write(0x1c8e, struct.pack('<ii', *c['drift']))
        o.cpu.mem_write(o.GRID, bytes(W * H * 12)); o.cpu.mem_write(o.POOL + SCRATCH, bytes(0x22))
        cell = (c['y'] >> 5) * W + (c['x'] >> 5)
        e = pattern(0x22, 5, 9)
        e[0] = {'init': 0x13, 'step': c['type'], 'column': 0x0b, 'embers': 0x16}[c['mode']]; e[1] = c['army']; e[5] = c['layer']
        struct.pack_into('<I', e, 7, c['x']); struct.pack_into('<I', e, 0xc, c['y'])
        e[6] = c['f06']; e[0xb] = c['f0b']; e[0x16] = c['f16']; e[0x17] = c['f17']; e[0x1e] = c['f1e']; struct.pack_into('<H', e, 0x1c, c['f1c'])
        e[0x11] = c['f11']; struct.pack_into('<H', e, 0x10, c['f10']); struct.pack_into('<H', e, 0x20, c['f20'])
        struct.pack_into('<I', e, 0x18, 0)
        refs = []
        if c['object']:
            ob = c['object']
            rec = unit(ob['t'], ob['owner'], ob['hp'], 0x0f, max(0, c['x'] + ob['dx']), max(0, c['y'] + ob['dy']), c['layer'])
            struct.pack_into('<H', rec, 1, ob['frame'])
            o.cpu.mem_write(o.POOL + 0x100, bytes(rec)); refs.append(0x100)
            o.cpu.mem_write(o.GRID + ((c['layer'] & 3) * W * H + cell) * 4, struct.pack('<I', 0x100))
            if c['mode'] == 'step' and c['f17'] != 0xff: struct.pack_into('<I', e, 0x18, 0x100)
        if c['cell_object'] and not c['object']:
            ob = c['cell_object']
            rec = unit(ob['t'], 1, ob['hp'], 0, c['x'], c['y'], c['layer'])
            o.cpu.mem_write(o.POOL + 0x140, bytes(rec)); refs.append(0x140)
            o.cpu.mem_write(o.GRID + ((c['layer'] & 3) * W * H + cell) * 4, struct.pack('<I', 0x140))
        o.cpu.mem_write(o.POOL + 1, bytes(e))
        handler = {'init': 0xce3, 'step': 0xce3, 'column': 0x9d5, 'embers': 0xfa4}[c['mode']]
        ah = 0 if c['mode'] == 'init' else 1
        o.mode = 'burn'
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EAX, ah << 8)
        o.run(o.WRK + handler)
        out = bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + b''.join(bytes(o.cpu.mem_read(o.POOL + r, 0x36)) for r in refs) + \
            bytes(o.cpu.mem_read(o.POOL + SCRATCH + 0x11, 1)) + json.dumps(log).encode()
        digest.update(out); c['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT', 'ARMY__00.WRK']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'burn': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
