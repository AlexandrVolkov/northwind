#!/usr/bin/env python3
"""Verify the standard unit scripts of the WRK files (swordsman, archer, horseman: init AH=0, step AH=1, voice AH=2; ARMY__01 and ARMY__02) with Unicorn. FS:4D (appearance), FS:D1 (voice),
FS:1D (redraw) and FS:DD (vitals) are recorded stubs; FS:E1 (the unit step) ends the run; FS:109 runs as original code."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle
from forestry_source import source_constants

ENTRIES = [('ARMY__01.WRK', 0x0, 0x7f), ('ARMY__01.WRK', 0x7f, 0xfe), ('ARMY__01.WRK', 0x4f0, 0x569), ('ARMY__02.WRK', 0x0, 0x7f), ('ARMY__02.WRK', 0x7f, 0xfe), ('ARMY__02.WRK', 0x11b4, 0x122d)]


class UnitOracle(CombatOracle):
    RANGES = {'unit': ([(0x9e41, 0x9e69)], [(0, 0x4000)])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000)


def cases():
    rng = random.Random(0x4d)
    out = []
    for n in range(900):
        k = rng.randrange(len(ENTRIES))
        out.append({'entry': k, 'ah': rng.choice([0, 1, 1, 2]), 'bl': rng.randrange(256), 'seed': rng.randrange(256), 'f14': rng.choice([0, 0x80, 0x81, rng.randrange(256)]), 'f2d': rng.randrange(256), 'f0f': rng.randrange(256), 'f2e': rng.randrange(256)})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = UnitOracle(code, report['fs_assignments'], constants)
    slots = {x['fs_slot']: x['target_offset'] for x in report['fs_assignments']}
    wrks = {m: (game / m).read_bytes() for m in {e[0] for e in ENTRIES}}
    log = []
    def reg(r): return o.cpu.reg_read(r)
    def note(letter, with_eax=False):
        def f():
            log.append(letter + (struct.pack('<I', reg(UC_X86_REG_EAX)) if with_eax else b'')); o.ret()
        return f
    def appearance(): log.append(b'N' + struct.pack('<I', reg(UC_X86_REG_EAX) & 255)); o.ret()
    def actions(): log.append(b'U'); o.cpu.emu_stop()
    o.stubs = {slots[0x4d]: appearance, slots[0xd1]: note(b'Y'), slots[0x1d]: note(b'D'), slots[0xdd]: note(b'V'), slots[0xe1]: actions}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'unit'
        module, a, e = ENTRIES[case['entry']]
        o.cpu.mem_write(o.WRK, wrks[module]); o.cpu.mem_write(o.POOL, bytes(0x1000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x19eb, bytes([case['seed']]))
        r = bytearray(0x50); r[0x14] = case['f14']; r[0x2d] = case['f2d']; r[0x0f] = case['f0f']; r[0x2e] = case['f2e']
        o.cpu.mem_write(o.POOL + 0x800, bytes(r))
        for q in (UC_X86_REG_ESI, UC_X86_REG_EDI): o.cpu.reg_write(q, o.POOL + 0x800)
        o.cpu.reg_write(UC_X86_REG_EAX, case['ah'] << 8); o.cpu.reg_write(UC_X86_REG_EBX, case['bl'])
        o.run(o.WRK + a)
        out = bytes(o.cpu.mem_read(o.POOL + 0x800, 0x50)) + bytes(o.cpu.mem_read(0x19eb, 1)) + b''.join(log)
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']} | {m: hashlib.sha256(b).hexdigest() for m, b in wrks.items()}, 'random_values': constants['random_values'],
            'entries': [list(e) for e in ENTRIES], 'cases': all_cases, 'results': {'unit': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
