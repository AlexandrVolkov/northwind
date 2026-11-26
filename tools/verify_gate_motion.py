#!/usr/bin/env python3
"""Verify the gates' actions (LE:1 D5B1 raise one step, D61D lower one step, D689 the unit's update) with Unicorn. AC63 (is the place free: ZF free, CF blocked/limit),
43B0 (occupancy) and D895 (search an enemy) are recorded stubs."""
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


class BridgeOracle(CombatOracle):
    RANGES = {'bridge': ([(0xd5b1, 0xd6e6)], [])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000)


def cases():
    rng = random.Random(0xd5b1)
    out = []
    for n in range(1500):
        out.append({'fn': rng.choice(['raise', 'lower', 'step', 'step', 'step']), 'f15': rng.choice([0, 0, 0, 0, 1, 3, 0x1f, rng.randrange(256)]), 'f22': rng.choice([0, 1, 0xf, 0x10, 0x10, 0x10, 0x11, 0x1f, 0x20, 0x1f, rng.randrange(256)]),
                    'f23': rng.choice([0, 1, 2, 0, 1, 2, rng.randrange(256)]), 'f0c': rng.randrange(256), 'f0d': rng.randrange(256), 'f0f': rng.randrange(256), 'f0e': rng.randrange(64), 'army': rng.randrange(4),
                    'x': rng.randrange(0, 4000), 'y': rng.randrange(0, 4000), 'f17': rng.randrange(65536), 'blocker': rng.randrange(256), 'zf': rng.choice([0, 1, 1]), 'cf': rng.choice([0, 1]),
                    'search_zf': rng.choice([0, 1])})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = BridgeOracle(code, report['fs_assignments'], constants)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def place():   # AC63
        log.append(b'P' + struct.pack('<BBBII', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255, reg(UC_X86_REG_EBX) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x100)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x41) | (0x40 if state['zf'] else 0) | (1 if state['cf'] else 0)); o.ret()
    def occupancy(): log.append(b'O' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.ret()
    def search():
        log.append(b'S' + struct.pack('<B', reg(UC_X86_REG_EAX) & 255))
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if state['search_zf'] else 0)); o.ret()
    o.stubs = {0xac63: place, 0x43b0: occupancy, 0xd895: search}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state.update(case); o.prepare(32, 32); o.mode = 'bridge'
        o.cpu.mem_write(o.POOL, bytes(0x1000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT)); o.cpu.mem_write(o.DAT, bytes(0xab))
        r = bytearray(0x40); r[0] = case['army']; r[0x0c] = case['f0c']; r[0x0d] = case['f0d']; r[0x0e] = case['f0e']; r[0x0f] = case['f0f']; r[0x15] = case['f15']; r[0x22] = case['f22']; r[0x23] = case['f23']
        struct.pack_into('<H', r, 0x17, case['f17']); struct.pack_into('<II', r, 4, case['x'], case['y'])
        o.cpu.mem_write(o.POOL + 0x800, bytes(r)); blocker = bytearray(0x40); blocker[0x19] = case['blocker']; o.cpu.mem_write(o.POOL + 0x100, bytes(blocker))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x800); o.cpu.reg_write(UC_X86_REG_EBX, 0); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.CODE + {'raise': 0xd5b1, 'lower': 0xd61d, 'step': 0xd689}[case['fn']])
        out = bytes(o.cpu.mem_read(o.POOL + 0x800, 0x40)) + bytes(o.cpu.mem_read(o.POOL + 0x100, 0x40)) + bytes([reg(UC_X86_REG_EBX) & 255]) + b''.join(log)
        import os
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'bridge': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
