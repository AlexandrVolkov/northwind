#!/usr/bin/env python3
"""Verify the volcano (ARMY__04.WRK 0..9F) with Unicorn. The services it calls — the random streams FS:109 (LE:1 9E41) and FS:10D (9E55) and the effect FS:19 (9727) —
are recorded stubs; the check covers the WRK state machine and the arguments of its calls."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, unit
from forestry_source import source_constants


class VolcanoOracle(CombatOracle):
    RANGES = {'volcano': ([], [(0, 0xa0)])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=20000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('volcano did not return')


def cases():
    rng = random.Random(0x404)
    out = []
    for n in range(1500):
        out.append({'state': rng.choice([0, 0, 1, 1]), 'count': rng.choice([0, 1, 2, 3, 0x20, 0x21, 0x40, 0x400, rng.randrange(1, 0x4000)]), 'army': rng.randrange(0, 6),
                    'x': rng.randrange(32, 4000), 'y': rng.randrange(32, 4000), 'init': rng.choice([0, 0, 1]),
                    'ticks': [{'a': rng.choice([0, 1, 5, 0xf, 0x10, rng.randrange(1 << 32)]), 'b': rng.choice([0, 0x20, 0x40, 1, rng.randrange(1 << 32), rng.randrange(1 << 32) & ~0x1f]),
                               'step': rng.choice([0, 0, 1, 2, 0x1f, 0x20])} for _ in range(rng.choice([4, 12, 30]))]})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = VolcanoOracle(code, report['fs_assignments'], constants)
    wrk = (game / 'ARMY__04.WRK').read_bytes()
    o.cpu.mem_write(o.WRK, wrk)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def rand_a(): o.cpu.reg_write(UC_X86_REG_EAX, state['a']); o.ret()
    def rand_b(): o.cpu.reg_write(UC_X86_REG_EAX, state['b']); o.ret()
    def effect():
        log.append(b'E' + bytes([reg(UC_X86_REG_EAX) & 255, reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255]) + struct.pack('<II', reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX))); o.ret()
    o.stubs = {0x9e41: rand_a, 0x9e55: rand_b, 0x9727: effect}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'volcano'
        record = bytearray(unit(0, 0, 100, 0, case['x'], case['y'], 1)); record[0] = case['army']
        record[0x15] = case['state']; struct.pack_into('<I', record, 0x22, case['count']); struct.pack_into('<II', record, 4, case['x'], case['y'])
        o.cpu.mem_write(o.POOL + 0x100, bytes(record))
        if case['init']:
            state.update(a=case['ticks'][0]['a'], b=0); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EAX, 0)
            o.run(o.WRK + 0); log.append(b'I')
        for tick in case['ticks']:
            state.update(a=tick['a'], b=tick['b'])
            o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EAX, 0x100)
            o.run(o.WRK + 0)
            log.append(b'T')
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x30)) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']} | {'ARMY__04.WRK': hashlib.sha256(wrk).hexdigest()},
            'cases': all_cases, 'results': {'volcano': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
