#!/usr/bin/env python3
"""Verify the catch-up of the campaign layer (LE:1 10E19: the time spent in other regions is applied to the entered region — relations are restored, the casualties go to 10FDE — and 10ECE:
the budget of world updates in chunks of 400h through 11087) with Unicorn; 10FDE, 1E6C2, 1E740 and 11087 are recorded stubs."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle
from verify_campaign_regions import fill, REGION, RECORD, PARTICIPANT, STATE
from forestry_source import source_constants


class CatchupOracle(CombatOracle):
    RANGES = {'catchup': ([(0x10e19, 0x10fde), (0x10ece, 0x10fde)], [])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=200000)


def cases():
    rng = random.Random(0x10e19)
    out = []
    for n in range(300):
        regions = rng.choice([2, 3]); armies = rng.choice([2, 3, 4])
        out.append({'fn': rng.choice(['away', 'away', 'budget']), 'regions': regions, 'armies': armies, 'budget': rng.choice([0, 0x400, 0x800, 0x801, 0xc00, 0x1000, rng.randrange(0, 0x3000)]), 'tick': rng.randrange(1 << 20),
                    'current': [rng.randrange(2), rng.randrange(2), rng.randrange(2)], 'player': rng.randrange(armies), 'seed': rng.randrange(1 << 30),
                    'v11': [rng.choice([0, 100, 5000, rng.randrange(1 << 20)]) for _ in range(16)], 'flag': rng.choice([0, 1, 1])})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = CatchupOracle(code, report['fs_assignments'], constants)
    log = []
    def reg(r): return o.cpu.reg_read(r)
    def loss(): log.append(b'L' + struct.pack('<II', reg(UC_X86_REG_EAX), reg(UC_X86_REG_ESI) - o.MAP - 0x20000)); o.ret()
    def reset(): log.append(b'R'); o.ret()
    def system(): log.append(b'Y' + struct.pack('<BB', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255)); o.ret()
    def run_world(): log.append(b'W' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.ret()
    o.stubs = {0x10fde: loss, 0x1e6c2: reset, 0x1e740: system, 0x11087: run_world}
    digest = hashlib.sha256(); all_cases = cases(); m = o.MAP
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'catchup'
        R = case['regions']; A = case['armies']
        o.cpu.mem_write(0x6c9, bytes([R])); o.cpu.mem_write(0x6c6, bytes([A])); o.cpu.mem_write(0x3e87, struct.pack('<I', m)); o.cpu.mem_write(0x1972, bytes([case['player']]))
        o.cpu.mem_write(0x3e83, struct.pack('<I', case['budget'])); o.cpu.mem_write(0x1ca9, struct.pack('<I', case['tick']))
        o.cpu.mem_write(0x1cc8, bytes([case['current'][0]])); o.cpu.mem_write(0x1cc5, bytes([1])); o.cpu.mem_write(0x1cc7, bytes([case['current'][2]]))
        blob = bytearray(fill(case['seed'], R * REGION))
        for r in range(R):
            blob[r * REGION] |= case['flag'] if r == case['current'][0] + case['current'][2] else 0
            for a in range(A): struct.pack_into('<I', blob, r * REGION + 1 + a * RECORD + 0x11, case['v11'][(r * 4 + a) % 16])
        o.cpu.mem_write(m, bytes(0x20000)); o.cpu.mem_write(m, bytes(blob)); o.cpu.mem_write(0x6ba, struct.pack('<I', m + 0x20000))
        states = bytearray(fill(case['seed'] + 1, STATE * A))
        for a in range(A): struct.pack_into('<I', states, a * STATE + 0x19e9, case['v11'][(a * 5 + 3) % 16])
        o.cpu.mem_write(m + 0x20000, bytes(states)); o.cpu.mem_write(0x97a, fill(case['seed'] + 2, PARTICIPANT * A))
        entry = 0x10e19 if case['fn'] == 'away' else 0x10ece
        o.cpu.reg_write(UC_X86_REG_EBX, 0); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.CODE + entry)
        out = bytes(o.cpu.mem_read(0x97a, PARTICIPANT * A)) + bytes(o.cpu.mem_read(0x3e83, 4)) + bytes(o.cpu.mem_read(0x1ca9, 4)) + b''.join(log)
        import os
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'catchup': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
