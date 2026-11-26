#!/usr/bin/env python3
"""Verify the jug and chest scripts (ARMY__03.WRK E4, 71A, 7CE, 7DF) with Unicorn. FS services 1D (redraw), 6D (pointer set-up), 15 (removal) and 19 (effect creation)
are recorded stubs; the check covers AH = 0 (init), 1 (update) and 8 (visit) and the arguments of the service calls."""
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

ENTRIES = {'jug': 0xe4, 'chest': 0x71a, 'chest10': 0x7ce, 'chest11': 0x7df}


class ContainerOracle(CombatOracle):
    RANGES = {'container': ([], [(0xe4, 0x189), (0x71a, 0x7f0)])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=20000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('container did not return')


def cases():
    rng = random.Random(0xe4)
    out = []
    for n in range(2500):
        kind = rng.choice(list(ENTRIES))
        out.append({'kind': kind, 'state': rng.choice([0, 0, 1, 2, 3, 0x80, 0x81, 0x82]), 'timer': rng.choice([0, 1, 2, 0x31, 0xce, rng.randrange(256)]),
                    'condition': rng.choice([100, 100, 99, 70, 50, 20, 1, 0, 0xff, 0xfd, 0xfb, 0xfa, 0xf9, rng.randrange(256)]),
                    'frame': rng.choice([0x110, 0x111, 0x11a, 0x11b, 0x112, 0x11c, 0x121, 0x125, 0x126, 0x127, rng.randrange(0x100, 0x130)]), 'item': rng.choice([0, 6, 0xc, 0xe, 0x22, 0x23]),
                    'base': rng.choice([0x110, 0x11c, 0x128]), 'x': rng.randrange(32, 4000), 'y': rng.randrange(32, 4000), 'army': rng.choice([2, 3]),
                    'steps': [rng.choice(['u', 'u', 'u', 'u', 'v', 'i']) for _ in range(rng.choice([1, 3, 12, 40]))]})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = ContainerOracle(code, report['fs_assignments'], constants)
    wrk = (game / 'ARMY__03.WRK').read_bytes()
    o.cpu.mem_write(o.WRK, wrk)
    log = []; state = {}
    def service():
        log.append(b'D'); o.ret()
    def removal():
        log.append(b'R'); o.ret()
    def template():
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x2000); o.ret()
    def effect():
        r = o.cpu.reg_read
        log.append(b'E' + struct.pack('<BBBII', r(UC_X86_REG_EAX) & 255, r(UC_X86_REG_EBX) & 255, (r(UC_X86_REG_EBX) >> 8) & 255, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX))); o.ret()
    o.stubs = {0x81d9: service, 0x98cc: removal, 0x6877: template, 0x9727: effect}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'container'
        record = bytearray(50); record[0] = case['army']; record[0x14] = case['state']; record[0x15] = case['timer']; record[0x19] = case['condition']
        struct.pack_into('<H', record, 1, case['frame']); record[0x31] = case['item']; struct.pack_into('<II', record, 4, case['x'], case['y'])
        o.cpu.mem_write(o.POOL + 0x2000, bytes(0x100)); o.cpu.mem_write(o.POOL + 0x2000 + 0x48, struct.pack('<H', case['base']))
        o.cpu.mem_write(o.POOL + 0x100, bytes(record)); entry = o.WRK + ENTRIES[case['kind']]
        visitor = bytearray(50); o.cpu.mem_write(o.POOL + 0x300, bytes(visitor))
        # AH = 0 on a fresh record first for a quarter of the cases
        if case['x'] % 4 == 0:
            o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EAX, 0); o.run(entry)
        results = []
        for step in case['steps']:
            o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x300)
            o.cpu.reg_write(UC_X86_REG_EAX, {'u': 0x100, 'v': 0x800, 'i': 0x100}[step]); o.run(entry)
            results.append(bytes([o.cpu.reg_read(UC_X86_REG_EAX) & 255, (o.cpu.reg_read(UC_X86_REG_EFLAGS) >> 6) & 1]) if step == 'v' else b'')
            log.append(b'|')
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 50)) + b''.join(results) + b''.join(log)
        if __import__('os').environ.get('DEBUG_CASE') == str(all_cases.index(case)): print(out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']} | {'ARMY__03.WRK': hashlib.sha256(wrk).hexdigest()},
            'cases': all_cases, 'results': {'container': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
