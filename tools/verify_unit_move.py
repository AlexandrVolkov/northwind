#!/usr/bin/env python3
"""Verify the move step of units (LE:1 12930..12AE1, state 4 of the motion machine: a unit that moves toward a cell by pixels along the heading of the 32-step circle) with Unicorn.
Services are recorded stubs: 6996 (direction byte to the goal), 43B0 (occupancy update), AC63 (is the place free), 813E (fog), 12BF2 (the end of the move). The sine table at DS:4790 is filled at
run time, so its 64 signed bytes are part of the case."""
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


class MoveOracle(CombatOracle):
    RANGES = {'move': ([(0x12930, 0x12ae1)], [])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000)


def cases():
    rng = random.Random(0x12930)
    out = []
    for n in range(1500):
        limit = rng.choice([1024, 2048, 100])
        out.append({'al': rng.randrange(64), 'ah': rng.randrange(64), 'bl': rng.choice([0, 0x40, 0x80, 0xc0, rng.randrange(256)]), 'f24': rng.choice([0, 1, 2, 5, rng.randrange(256)]),
                    'f25': rng.randrange(256), 'f26': rng.randrange(256), 'f27': rng.choice([0, 1, 5, rng.randrange(256)]), 'f2c': rng.randrange(256), 'f2e': rng.randrange(256),
                    'f31': rng.randrange(32), 'x': rng.randrange(0, limit + 40), 'y': rng.randrange(0, limit + 40), 'f0d': rng.randrange(256), 'f0e': rng.randrange(256), 'army': rng.randrange(256),
                    'limit': [limit, rng.choice([1024, 2048, 100])], 'angle': rng.randrange(256), 'free_zf': rng.choice([0, 1, 1]),
                    'trig': [rng.randrange(-90, 91) for _ in range(64)],
                    'template': {'base': rng.randrange(3000), 'n4a': rng.choice([1, 4, 8, 13]), 'n4b': rng.choice([1, 4, 10]), 'n4f': rng.randrange(4), 'n58': rng.randrange(256)}})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = MoveOracle(code, report['fs_assignments'], constants)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def angle():
        log.append(b'A' + struct.pack('<IIII', reg(UC_X86_REG_ECX), reg(UC_X86_REG_EAX), reg(UC_X86_REG_EDX), reg(UC_X86_REG_EBX))); o.cpu.reg_write(UC_X86_REG_EAX, state['angle']); o.ret()
    def occupancy(): log.append(b'O' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.ret()
    def free():
        log.append(b'P' + struct.pack('<BBBII', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255, reg(UC_X86_REG_EBX) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if state['free_zf'] else 0)); o.ret()
    def fog(): log.append(b'V'); o.ret()
    def finish(): log.append(b'R'); o.ret()
    o.stubs = {0x6996: angle, 0x43b0: occupancy, 0xac63: free, 0x813e: fog, 0x12bf2: finish}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state.update(case); o.prepare(32, 32); o.mode = 'move'
        t = case['template']; raw = bytearray(0xab); raw[0x4a] = t['n4a']; raw[0x4b] = t['n4b']; raw[0x4f] = t['n4f']; raw[0x58] = t['n58']; struct.pack_into('<H', raw, 0x48, t['base'])
        o.cpu.mem_write(o.DAT, bytes(raw)); o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL))
        o.cpu.mem_write(0x4790, bytes(v & 255 for v in case['trig'])); o.cpu.mem_write(0x18e6, struct.pack('<I', case['limit'][0])); o.cpu.mem_write(0x18ea, struct.pack('<I', case['limit'][1]))
        o.cpu.mem_write(o.POOL, bytes(0x1000))
        r = bytearray(0x40); r[0] = case['army']; r[0x24] = case['f24']; r[0x25] = case['f25']; r[0x26] = case['f26']; r[0x27] = case['f27']; r[0x2c] = case['f2c']; r[0x2e] = case['f2e']; r[0x31] = case['f31']
        r[0x0d] = case['f0d']; r[0x0e] = case['f0e']; struct.pack_into('<II', r, 4, case['x'], case['y'])
        o.cpu.mem_write(o.POOL + 0x800, bytes(r)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x800); o.cpu.reg_write(UC_X86_REG_EBX, case['bl']); o.cpu.reg_write(UC_X86_REG_EAX, case['al'] | (case['ah'] << 8))
        o.run(o.CODE + 0x12930)
        out = bytes(o.cpu.mem_read(o.POOL + 0x800, 0x40)) + bytes(o.cpu.mem_read(0x1c9f, 1)) + bytes([reg(UC_X86_REG_EBX) & 255]) + b''.join(log)
        import os
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'move': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
