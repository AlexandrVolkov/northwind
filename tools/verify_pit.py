#!/usr/bin/env python3
"""Verify the pit trap (ARMY__03.WRK 7F0, template 12) with Unicorn. The services it calls — FS:14B (LE:1 11DDE, objects in a 3x3
square), FS:157 (LE:1 D2D1, claim or crush the square), FS:1D (81D9) — are recorded stubs; the check covers the WRK state machine and the arguments of its calls."""
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


class PitOracle(CombatOracle):
    RANGES = {'pit': ([], [(0x7f0, 0x8ec)])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=20000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('pit did not return')


def cases():
    rng = random.Random(0x7f0)
    out = []
    for n in range(1500):
        ticks = rng.choice([4, 8, 12])
        out.append({'state': rng.choice([0, 0, 1, 1, 2, 3]), 'frame': rng.randrange(0x289, 0x29c), 'count': rng.choice([0, 1, 5, 0x12, 0x13, 0x13, 0x14]),
                    'flags': rng.choice([0, 0x80, 0x81]), 'layer': rng.choice([0, 1, 2]), 'x': rng.randrange(32, 4000), 'y': rng.randrange(32, 4000),
                    'counter': rng.randrange(256),
                    'ticks': [{'counter_step': rng.choice([1, 1, 1, 2, 16, 31]), 'found': rng.choice([0, 1, 3]), 'other': rng.choice([0, 0, 2]),
                               'area': rng.choice([0, 0, 1, 4])} for _ in range(ticks)]})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = PitOracle(code, report['fs_assignments'], constants)
    wrk = (game / 'ARMY__03.WRK').read_bytes()
    o.cpu.mem_write(o.WRK, wrk)
    log = []; state = {}
    def search():
        r = o.cpu.reg_read; log.append(b'F' + struct.pack('<III', r(UC_X86_REG_ECX), r(UC_X86_REG_EDX), r(UC_X86_REG_ESI) - o.POOL))
        o.cpu.reg_write(UC_X86_REG_EAX, state['found']); o.cpu.reg_write(UC_X86_REG_EBX, state['other']); o.ret()
    def area():
        r = o.cpu.reg_read; log.append(b'A' + struct.pack('<IIII', r(UC_X86_REG_EAX), r(UC_X86_REG_EBX), r(UC_X86_REG_ECX), r(UC_X86_REG_ESI) - o.POOL)
                                       + bytes(o.cpu.mem_read(r(UC_X86_REG_ESI) + 0x0d, 1)))
        o.cpu.reg_write(UC_X86_REG_EAX, state['area']); o.ret()
    def service():
        log.append(b'S'); o.ret()
    o.stubs = {0x11dde: search, 0xd2d1: area, 0x81d9: service, 0x1d: service}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'pit'
        # AH=0 on a fresh record, then the updates.
        record = bytearray(unit(12, 6, 100, 0, case['x'], case['y'], case['layer']) + bytes(0)); record[0] = 4
        record[0x15] = case['state']; struct.pack_into('<H', record, 1, case['frame']); record[0x2a] = case['count']; record[0x14] = case['flags']
        o.cpu.mem_write(o.POOL + 0x100, bytes(record))
        counter = case['counter']
        o.cpu.mem_write(o.FS + 0x10, bytes([counter]))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        if case['x'] % 5 == 0: o.run(o.WRK + 0x7f0)
        out = b''
        for tick in case['ticks']:
            counter = (counter + tick['counter_step']) & 255
            o.cpu.mem_write(o.FS + 0x10, bytes([counter]))
            state.update(found=tick['found'], other=tick['other'], area=tick['area'])
            o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EAX, 0x100)
            o.run(o.WRK + 0x7f0)
            log.append(b'T')
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x2b)) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']} | {'ARMY__03.WRK': hashlib.sha256(wrk).hexdigest()},
            'cases': all_cases, 'results': {'pit': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
