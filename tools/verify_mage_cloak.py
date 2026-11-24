#!/usr/bin/env python3
"""Verify the per-update continuation of the mages (ARMY__03.WRK: Rufin D3D..DA6, the first mage 79..C5; the other hermits have the same code with their own thresholds) with Unicorn.
The code after the table of FS:E1 (the unit step) lets a hurt mage vanish: below a health threshold and with the charge (+31) at 10 or more it asks the unit machine for state 3
(FS:105, the vanishing action), and while hidden (+32 != 0, visibility mask +20 = 0) it recovers the charge and asks for the return when the health is back. FS:197 (12588,
the dead mage drops his items) is a recorded stub."""
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

ENTRIES = {'rufin': 0xd3d, 'mage': 0x79}


class CloakOracle(CombatOracle):
    RANGES = {'cloak': ([], [(0x79, 0xc6), (0xd3d, 0xda7)])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('cloak did not return')


def cases():
    rng = random.Random(0xd3d)
    out = []
    for n in range(3000):
        out.append({'kind': rng.choice(['rufin', 'mage']), 'condition': rng.choice([0, 1, 0x1d, 0x1e, 0x2d, 0x2e, 0x3b, 0x3c, 0x5a, 0x5b, 100, 0x80, 0xff, rng.randrange(256)]),
                    'charge': rng.choice([0, 5, 9, 10, 11, 0x7e, 0x7f, 0x80, rng.randrange(256)]), 'hidden': rng.choice([0, 0, 0xff, 1]), 'tick': rng.choice([0, 0x80, 1, 0x7f, rng.randrange(256)]),
                    'state': rng.randrange(256), 'mask': rng.choice([0xff, 0]), 'x': rng.randrange(64, 4000), 'y': rng.randrange(64, 4000), 'owner': rng.randrange(8)})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = CloakOracle(code, report['fs_assignments'], constants)
    wrk = (game / 'ARMY__03.WRK').read_bytes(); o.cpu.mem_write(o.WRK, wrk)
    log = []
    def drop():   # FS:197 = 12588: ECX the cell (CL, CH), AH the owner
        r = o.cpu.reg_read
        log.append(b'D' + struct.pack('<BBB', r(UC_X86_REG_ECX) & 255, (r(UC_X86_REG_ECX) >> 8) & 255, (r(UC_X86_REG_EAX) >> 8) & 255)); o.ret()
    o.stubs = {0x12588: drop}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'cloak'
        record = bytearray(0x40); record[3] = case['owner']; record[0x19] = case['condition']; record[0x31] = case['charge']; record[0x32] = case['hidden']; record[0x2e] = case['tick']
        record[0x14] = case['state']; record[0x20] = case['mask']; struct.pack_into('<II', record, 4, case['x'], case['y'])
        o.cpu.mem_write(o.POOL + 0x100, bytes(record)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EAX, 0xaa55)
        o.run(o.WRK + ENTRIES[case['kind']])
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x40)) + bytes([o.cpu.reg_read(UC_X86_REG_EAX) & 255]) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']} | {'ARMY__03.WRK': hashlib.sha256(wrk).hexdigest()},
            'cases': all_cases, 'results': {'cloak': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
