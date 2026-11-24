#!/usr/bin/env python3
"""Verify the application of casualties to an army (LE:1 10FDE: the army's population and strength fall and the weakened living units drop to the minimum condition of their template) with
Unicorn. 68BD (the template of a unit) is a stub."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle
from forestry_source import source_constants

STATE = 0x1a35; PARTICIPANT = 0xf5


class LossOracle(CombatOracle):
    RANGES = {'losses': ([(0x10fde, 0x11087)], [])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=20000)


def cases():
    rng = random.Random(0x10fde)
    out = []
    for n in range(600):
        units = rng.randrange(0, 9)
        out.append({'loss': rng.choice([0, 1, 100, 5000, rng.randrange(1 << 20)]), 'population': rng.choice([0, 1, 100, 2000, 5000, rng.randrange(1 << 20)]), 'strength': rng.randrange(256),
                    'army': rng.randrange(3),
                    'units': [{'cond': rng.randrange(-128, 128), 'tmpl': rng.randrange(4), 'link': rng.choice([0x40, 0x40, 0x40])} for _ in range(units)],
                    'templates': [{'flags': rng.choice([0, 8, 0x28, 0x20, 0x18008, rng.randrange(1 << 20)]), 'minimum': rng.randrange(-20, 60), 'weight': rng.choice([0, 0, 1, 2, 5, 20])} for _ in range(4)]})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = LossOracle(code, report['fs_assignments'], constants)
    def reg(r): return o.cpu.reg_read(r)
    def template():
        unit = reg(UC_X86_REG_EDI); k = bytes(o.cpu.mem_read(unit + 0x0e, 1))[0]
        o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + 0x400 + 0x100 * k); o.ret()
    o.stubs = {0x68bd: template}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        o.prepare(32, 32); o.mode = 'losses'
        o.cpu.mem_write(o.POOL, bytes(0x1000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL))
        o.cpu.mem_write(0x6ba, struct.pack('<I', o.MAP))   # the army states
        state = bytearray(STATE * 3); a = case['army']; state[a * STATE] = a
        struct.pack_into('<I', state, a * STATE + 0x19e9, case['population']); state[a * STATE + 0x5c] = case['strength']
        o.cpu.mem_write(o.MAP, bytes(state))
        n = len(case['units']); start = 0x100
        for i, u in enumerate(case['units']):
            rec = bytearray(0x40); rec[0x0e] = u['tmpl']; rec[0x19] = u['cond'] & 255; struct.pack_into('<I', rec, 0x10, u['link']); o.cpu.mem_write(o.POOL + start + 0x40 * i, bytes(rec))
        for k, t in enumerate(case['templates']):
            raw = bytearray(0xab); struct.pack_into('<I', raw, 0x40, t['flags']); raw[0x46] = t['minimum'] & 255; raw[0x76] = t['weight']; o.cpu.mem_write(o.POOL + 0x400 + 0x100 * k, bytes(raw))
        p = 0x97a + PARTICIPANT * a
        o.cpu.mem_write(p + 0x0e, struct.pack('<I', start)); o.cpu.mem_write(p + 0x09, struct.pack('<I', start + 0x40 * n))
        o.cpu.reg_write(UC_X86_REG_ESI, o.MAP + a * STATE); o.cpu.reg_write(UC_X86_REG_EAX, case['loss'])
        o.run(o.CODE + 0x10fde)
        out = bytes(o.cpu.mem_read(o.POOL + start, 0x40 * max(n, 1))) + bytes(o.cpu.mem_read(o.MAP + a * STATE + 0x19e9, 4))
        import os
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'losses': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
