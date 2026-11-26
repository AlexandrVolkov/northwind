#!/usr/bin/env python3
"""Verify the small helpers of the unit action handlers (LE:1 8CA1 move request, 8CBB follow the referenced object, 8D0B state-5 request, 8D1A and 8D3E enemy scans of the idle action, 8D89
decoding of the motion nibbles) with Unicorn. 8120 (release the route), D895 (search an enemy) and DA7F are recorded stubs."""
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

ENTRIES = {'move': 0x8ca1, 'follow': 0x8cbb, 'state5': 0x8d0b, 'scan': 0x8d1a, 'scan2': 0x8d3e, 'decode': 0x8d89}


class OrdersOracle(CombatOracle):
    RANGES = {'orders': ([(0x8ca1, 0x8cf3), (0x8d0b, 0x8db0)], [])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000)


def cases():
    rng = random.Random(0x8ca1)
    out = []
    for n in range(2000):
        out.append({'fn': rng.choice(list(ENTRIES)), 'al': rng.randrange(256), 'ah': rng.randrange(256), 'f2d': rng.randrange(256), 'f2e': rng.randrange(256), 'f30': rng.randrange(256), 'f15': rng.randrange(256),
                    'f22': rng.randrange(256), 'f23': rng.randrange(256), 'f03': rng.randrange(8), 'target': rng.choice([0, 0x100, 0x140]), 'tx': rng.randrange(0, 4000), 'ty': rng.randrange(0, 4000),
                    'x': rng.randrange(0, 4000), 'y': rng.randrange(0, 4000), 'zf': rng.choice([0, 1]), 'found': [rng.randrange(1 << 20), rng.randrange(1 << 20), rng.randrange(1 << 20)], 'edi': rng.randrange(1 << 20),
                    'bl': rng.randrange(256), 'f2d_low': rng.randrange(16), 'old': [rng.randrange(1 << 30) for _ in range(4)]})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = OrdersOracle(code, report['fs_assignments'], constants)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def release(): log.append(b'R'); o.ret()
    def search():   # D895
        log.append(b'S' + struct.pack('<B', reg(UC_X86_REG_EAX) & 255))
        f = state['found']; o.cpu.reg_write(UC_X86_REG_EAX, f[0]); o.cpu.reg_write(UC_X86_REG_EBX, f[1]); o.cpu.reg_write(UC_X86_REG_EDI, f[2])
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if state['zf'] else 0)); o.ret()
    def near():   # DA7F
        log.append(b'N' + struct.pack('<BII', reg(UC_X86_REG_EAX) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX))); o.cpu.reg_write(UC_X86_REG_EDI, state['edi']); o.ret()
    o.stubs = {0x8120: release, 0xd895: search, 0xda7f: near}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state.update(case); o.prepare(32, 32); o.mode = 'orders'
        o.cpu.mem_write(o.POOL, bytes(0x1000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL))
        o.cpu.mem_write(0x512, struct.pack('<IIII', *case['old']) + bytes(1))
        tgt = bytearray(0x40); struct.pack_into('<II', tgt, 4, case['tx'], case['ty']); o.cpu.mem_write(o.POOL + 0x100, bytes(tgt)); o.cpu.mem_write(o.POOL + 0x140, bytes(tgt))
        r = bytearray(0x40); r[0x2d] = case['f2d']; r[0x2e] = case['f2e']; r[0x30] = case['f30']; r[0x15] = case['f15']; r[0x22] = case['f22']; r[0x23] = case['f23']; r[3] = case['f03']
        struct.pack_into('<I', r, 0x1c, case['target']); struct.pack_into('<II', r, 4, case['x'], case['y'])
        o.cpu.mem_write(o.POOL + 0x800, bytes(r))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x800); o.cpu.reg_write(UC_X86_REG_EAX, case['al'] | (case['ah'] << 8)); o.cpu.reg_write(UC_X86_REG_EBX, case['bl']); o.cpu.reg_write(UC_X86_REG_ECX, 0x55aa); o.cpu.reg_write(UC_X86_REG_EDX, 0x66bb)
        o.run(o.CODE + ENTRIES[case['fn']])
        regs = struct.pack('<IIII', reg(UC_X86_REG_EAX) & 0xffffffff, reg(UC_X86_REG_EBX) & 0xffffffff, reg(UC_X86_REG_ECX) & 0xffffffff, reg(UC_X86_REG_EDX) & 0xffffffff)
        out = bytes(o.cpu.mem_read(o.POOL + 0x800, 0x40)) + bytes(o.cpu.mem_read(0x512, 17)) + regs + bytes([(reg(UC_X86_REG_EFLAGS) >> 6) & 1]) + b''.join(log)
        import os
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'orders': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
