#!/usr/bin/env python3
"""Verify the script of the phantom (ARMY__03.WRK 212: AH = 0 the initial record, AH = 1 the update up to the unit step FS:E1, the continuation 27F: the lifetime counter) with Unicorn.
FS:4D (AE3A), FS:1D, FS:DD (vitals) and FS:E1 (the unit step; its return into the continuation is replaced by a direct call of 27F) are recorded stubs; FS:109 is original code."""
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


class PhantomOracle(CombatOracle):
    RANGES = {'phantom': ([(0x9e41, 0x9e55)], [(0x212, 0x2b9)])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('phantom did not return')


def cases():
    rng = random.Random(0x212)
    out = []
    for n in range(2500):
        out.append({'ah': rng.choice([0, 1, 1, 2, 3, 5]), 'kind': rng.choice(['script', 'tail', 'tail']), 'condition': rng.choice([0, 1, 100, 0x80, 0xff, rng.randrange(256)]),
                    'life': rng.choice([0, 1, 2, 0x7f, 0x80, rng.randrange(256)]), 'tick': rng.choice([0, 8, 7, 0x10, 255, rng.randrange(256)]), 'state': rng.randrange(256), 'flags17': rng.randrange(65536),
                    'seed': rng.randrange(256), 'bl': rng.randrange(256)})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = PhantomOracle(code, report['fs_assignments'], constants)
    wrk = (game / 'ARMY__03.WRK').read_bytes(); o.cpu.mem_write(o.WRK, wrk)
    log = []
    def stub(letter, *regs, mask=0xff):
        def f():
            log.append(letter + b''.join(struct.pack('<I', o.cpu.reg_read(r) & mask) for r in regs)); o.ret()
        return f
    def unit_step():   # FS:E1: its return goes to the byte after the inline table; the tail 27F is entered directly
        log.append(b'U'); o.cpu.emu_stop(); o.stopped = True
    o.stubs = {0xae3a: stub(b'N', UC_X86_REG_EAX), 0x81d9: stub(b'D'), 0x90e9: stub(b'V'), 0x8227: unit_step}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'phantom'
        o.cpu.mem_write(0x19eb, bytes([case['seed']]))
        record = bytearray(0x40); record[0x19] = case['condition']; record[0x31] = case['life']; record[0x2e] = case['tick']; record[0x14] = case['state']; struct.pack_into('<H', record, 0x17, case['flags17'])
        o.cpu.mem_write(o.POOL + 0x100, bytes(record))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EBX, case['bl']); o.cpu.reg_write(UC_X86_REG_EAX, (case['ah'] << 8) | 0x55)
        if case['kind'] == 'script': o.run(o.WRK + 0x212)
        else: o.cpu.reg_write(UC_X86_REG_EAX, 0x55); o.run(o.WRK + 0x27f)
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x40)) + bytes([o.cpu.reg_read(UC_X86_REG_EAX) & 255]) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']} | {'ARMY__03.WRK': hashlib.sha256(wrk).hexdigest()}, 'random_values': constants['random_values'],
            'cases': all_cases, 'results': {'phantom': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
