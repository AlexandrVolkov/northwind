#!/usr/bin/env python3
"""Verify the participant statistics (LE:1 D3E5..D40C, D40C..D453, D453..D5B1) with Unicorn: the strength +28 (sum of DAT[88]), the worker weight +2C (DAT[78] of class 108h), the bounding box
of the buildings and the territory +C9..CC/+30, and the growth +24 every update. 68BD (template of an object), 219C3, EEBB and 1188F are recorded stubs."""
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

PARTICIPANT = 0xf5


class StatsOracle(CombatOracle):
    RANGES = {'stats': ([(0xd3e5, 0xd5b1)], [])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=200000)


def cases():
    rng = random.Random(0xd3e5)
    out = []
    for n in range(500):
        armies = rng.choice([1, 2, 3, 4])
        out.append({'fn': rng.choice(['all', 'all', 'recompute', 'grow']), 'armies': armies, 'campaign': rng.choice([0, 1, 1, 1]), 'g19e9': rng.choice([0, 0x20, 0x40, rng.randrange(256)]), 'tick': rng.choice([0, 1, 0x3ff, 0x400, rng.randrange(1 << 16)]),
                    'seed': rng.randrange(1 << 30), 'city': [rng.randrange(0, 64), rng.randrange(0, 64), rng.randrange(0, 40), rng.randrange(0, 40)],
                    'participants': [{'strength': rng.choice([0, 10, 1000, rng.randrange(1 << 16)]), 'weight': rng.choice([0, 5, 90, 200, rng.randrange(1 << 12)]), 'growth': rng.randrange(0, 200), 'limit': rng.randrange(0, 4000),
                                      'units': [{'tmpl': rng.randrange(4), 'cond': rng.randrange(-20, 100), 'x': rng.randrange(0, 4000), 'y': rng.randrange(0, 4000)} for _ in range(rng.randrange(0, 7))]} for _ in range(armies)],
                    'templates': [{'flags': rng.choice([0, 0x108, 0x8, 0x28, 0x68, 0x1108, 0xf08, rng.randrange(1 << 16)]), 'minimum': rng.randrange(0, 30), 'v78': rng.randrange(0, 50), 'v88': rng.randrange(0, 300)} for _ in range(4)]})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = StatsOracle(code, report['fs_assignments'], constants)
    log = []
    def reg(r): return o.cpu.reg_read(r)
    def template():
        unit = reg(UC_X86_REG_EDI); k = bytes(o.cpu.mem_read(unit + 0x0e, 1))[0]
        o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + 0x800 + 0x100 * k); o.ret()
    def area():   # 219C3
        log.append(b'A' + struct.pack('<II', reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        o.cpu.reg_write(UC_X86_REG_ECX, (reg(UC_X86_REG_ECX) & ~0xffff) | (state['city'][0] | (state['city'][1] << 8))); o.cpu.reg_write(UC_X86_REG_EDX, (reg(UC_X86_REG_EDX) & ~0xffff) | (state['city'][2] | (state['city'][3] << 8))); o.ret()
    def note(letter):
        def f(): log.append(letter); o.ret()
        return f
    state = {}
    o.stubs = {0x68bd: template, 0x219c3: area, 0xeebb: note(b'E'), 0x1188f: note(b'G')}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state.update(case); o.prepare(32, 32); o.mode = 'stats'
        A = case['armies']
        o.cpu.mem_write(0x1c6e, bytes(16)); o.cpu.mem_write(o.POOL, bytes(0x1000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x6c6, bytes([A])); o.cpu.mem_write(0x1c4e, bytes([case['campaign']]))
        o.cpu.mem_write(0x19e9, bytes([case['g19e9']])); o.cpu.mem_write(0x1ca9, struct.pack('<I', case['tick']))
        for k, t in enumerate(case['templates']):
            raw = bytearray(0xab); struct.pack_into('<I', raw, 0x40, t['flags']); raw[0x46] = t['minimum']; struct.pack_into('<I', raw, 0x78, t['v78']); struct.pack_into('<I', raw, 0x88, t['v88'])
            o.cpu.mem_write(o.POOL + 0x800 + 0x100 * k, bytes(raw))
        at = 0x100
        for i, p in enumerate(case['participants']):
            part = bytearray(PARTICIPANT); struct.pack_into('<I', part, 0x28, p['strength']); struct.pack_into('<I', part, 0x2c, p['weight']); struct.pack_into('<I', part, 0x24, p['growth']); struct.pack_into('<I', part, 0x30, p['limit'])
            start = at
            for u in p['units']:
                rec = bytearray(0x40); rec[0x0e] = u['tmpl']; rec[0x19] = u['cond'] & 255; struct.pack_into('<I', rec, 0x10, 0x40); struct.pack_into('<II', rec, 4, u['x'], u['y'])
                o.cpu.mem_write(o.POOL + at, bytes(rec)); at += 0x40
            struct.pack_into('<I', part, 0x0e, start); struct.pack_into('<I', part, 0x09, at)
            o.cpu.mem_write(0x97a + PARTICIPANT * i, bytes(part))
        entry = {'all': 0xd3e5, 'recompute': 0xd453, 'grow': 0xd40c}[case['fn']]
        o.run(o.CODE + entry)
        out = b''.join(bytes(o.cpu.mem_read(0x97a + PARTICIPANT * i, PARTICIPANT)) for i in range(A)) + bytes(o.cpu.mem_read(0x1c6e, 16)) + b''.join(log)
        import os
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'stats': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
