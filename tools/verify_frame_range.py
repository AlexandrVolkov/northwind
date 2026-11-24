#!/usr/bin/env python3
"""Verify the playback of a frame range (LE:1 9210 state 5: 94D1 the start, 94EA one update) with Unicorn: +22 the first and +23 the last frame (offsets from DAT[48]), +26 the current,
+27 the end, +2C the timer (2 updates per frame); a range given backwards (+22 > +23) plays backwards. The finishing 9275 is a recorded stub."""
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


class RangeOracle(CombatOracle):
    RANGES = {'range': ([(0x94d1, 0x951d), (0x934e, 0x9354)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('range did not return')


def cases():
    rng = random.Random(0x94d1)
    out = []
    for n in range(2000):
        a = rng.randrange(0, 120); b = a + rng.choice([-11, -3, 0, 1, 5, 11]) 
        b = max(0, min(255, b))
        out.append({'first': a, 'last': b, 'base': rng.randrange(0, 3000), 'state': rng.choice([0x55, 0x05, 0x50, 0x45]), 'steps': rng.choice([30, 60])})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = RangeOracle(code, report['fs_assignments'], constants)
    log = []
    def finish():
        log.append(b'F'); o.cpu.emu_stop(); o.stopped = True
    o.stubs = {0x9275: finish}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'range'
        record = bytearray(0x40); record[0x22] = case['first']; record[0x23] = case['last']; record[0x2d] = case['state']
        o.cpu.mem_write(o.POOL + 0x100, bytes(record)); o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT)); o.cpu.mem_write(o.DAT + 0x48, struct.pack('<H', case['base']))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EBP, o.DAT)
        frames = []
        o.run(o.CODE + 0x94d1); frames.append(bytes(o.cpu.mem_read(o.POOL + 0x101, 2)))
        for step in range(case['steps']):
            if o.stopped and b'F' in log: break
            o.run(o.CODE + 0x94ea); frames.append(bytes(o.cpu.mem_read(o.POOL + 0x101, 2)))
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x40)) + b''.join(frames) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'range': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
