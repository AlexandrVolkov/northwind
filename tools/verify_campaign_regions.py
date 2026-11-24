#!/usr/bin/env python3
"""Verify the regions of the campaign layer (the table at DS:3E87, 841h bytes per region, 84h bytes per army record) with Unicorn: the periodic change of every 1024 updates
(LE:1 10BD4), the snapshot of the running mission into its region (10CA0, calls 1ECA0 as a stub) and the check of the campaign's end (110C8)."""
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

REGION = 0x841; RECORD = 0x84; PARTICIPANT = 0xf5; STATE = 0x1a35


class RegionOracle(CombatOracle):
    RANGES = {'regions': ([(0x10bd4, 0x10ca0), (0x10ca0, 0x10e19), (0x110c8, 0x11150)], [])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=20000)


def fill(seed, count):
    # the same generator is replayed by tests/OriginalCampaignRegionChecks.cs: small values (1..7) are frequent, as are the thresholds
    x = seed & 0xffffffff; out = bytearray()
    for _ in range(count):
        x = (x * 1103515245 + 12345) & 0xffffffff; b = (x >> 16) & 255
        out.append(b & 7 if b < 192 else b)
    return bytes(out)


def cases():
    rng = random.Random(0x10bd4)
    out = []
    for n in range(300):
        regions = rng.choice([1, 2, 3]); armies = rng.choice([2, 3, 4])
        fn = rng.choice(['tick', 'tick', 'snapshot', 'end', 'end'])
        if fn == 'end': regions = rng.choice([3, 8, 9])
        out.append({'fn': fn, 'regions': regions, 'armies': armies, 'campaign': rng.choice([0, 1, 1, 1]), 'counter': rng.choice([0x3ff, 0x3ff, 0x7ff, 5, rng.randrange(1 << 16)]),
                    'current': [rng.randrange(8), rng.randrange(8), rng.randrange(4)], 'player': rng.randrange(armies), 'seed': rng.randrange(1 << 30), 'found': rng.choice([0, 1]), 'force': rng.choice([0, 1]), 'v17': [rng.choice([0, 0x7cf, 0x7d0, 5000, 0x5572, 0x5573, rng.randrange(1 << 20)]) for _ in range(40)], 'v11': [rng.choice([0, 100, 5000, rng.randrange(1 << 20)]) for _ in range(40)], 'obj': [rng.randrange(0, 4000), rng.randrange(0, 4000)]})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = RegionOracle(code, report['fs_assignments'], constants)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def find():
        log.append(b'S' + struct.pack('<BII', reg(UC_X86_REG_EAX) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x800)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0 if state['found'] else 0x40)); o.ret()
    o.stubs = {0x1eca0: find}
    digest = hashlib.sha256(); all_cases = cases(); m = o.MAP
    for case in all_cases:
        log.clear(); state.update(case); o.prepare(32, 32); o.mode = 'regions'
        R = case['regions']; A = case['armies']
        o.cpu.mem_write(0x6c9, bytes([R])); o.cpu.mem_write(0x6c6, bytes([A])); o.cpu.mem_write(0x1c4e, bytes([case['campaign']])); o.cpu.mem_write(0x3e8b, struct.pack('<I', case['counter']))
        o.cpu.mem_write(0x3e87, struct.pack('<I', m)); o.cpu.mem_write(0x1972, bytes([case['player']])); o.cpu.mem_write(0x1ec6, bytes([case['seed'] & 255]))
        o.cpu.mem_write(0x1cc8, bytes([case['current'][0]])); o.cpu.mem_write(0x1cc5, bytes([case['current'][1]])); o.cpu.mem_write(0x1cc7, bytes([case['current'][2]]))
        blob = bytearray(fill(case['seed'], R * REGION))
        for r in range(R):
            for a in range(A):
                base = r * REGION + 1 + a * RECORD; blob[base] = a
                struct.pack_into('<I', blob, base + 0x17, case['v17'][r * 4 + a]); struct.pack_into('<I', blob, base + 0x11, case['v11'][r * 4 + a])
                if case['force'] and case['fn'] == 'end': blob[base + 0x59] = 0; blob[base + 1 + case['player']] = 3
            if case['force'] and case['fn'] == 'end': blob[r * REGION] |= 1
        o.cpu.mem_write(m, bytes(blob))
        o.cpu.mem_write(0x6ba, struct.pack('<I', m + 0x20000))
        o.cpu.mem_write(m + 0x20000, fill(case['seed'] + 1, STATE * A))
        o.cpu.mem_write(0x97a, fill(case['seed'] + 2, PARTICIPANT * A))
        o.cpu.mem_write(o.POOL, bytes(0x1000)); o.cpu.mem_write(o.POOL + 0x800, struct.pack('<II', 0, 0)[:0] + bytes(4) + struct.pack('<II', case['obj'][0], case['obj'][1]))
        o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL))
        entry = {'tick': 0x10bd4, 'snapshot': 0x10ca0, 'end': 0x110c8}[case['fn']]
        o.cpu.reg_write(UC_X86_REG_EBX, 0); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.CODE + entry)
        out = bytes(o.cpu.mem_read(m, R * REGION)) + bytes(o.cpu.mem_read(0x1ec6, 1)) + bytes(o.cpu.mem_read(0x3e8b, 4)) + b''.join(log)
        import os
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'regions': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
