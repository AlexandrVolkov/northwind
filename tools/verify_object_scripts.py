#!/usr/bin/env python3
"""Verify the scripts of the map objects of ARMY__03.WRK (rockfall 189, stone face 2B9, ...) with Unicorn. The FS services that act on the world are recorded stubs: FS:14B (11DDE, the
objects in a rectangle: EAX count, EBX hostile), FS:19 (the effect with three stack arguments), FS:31 (a sound); the random bytes FS:109/FS:10D run as original code."""
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

PRESETS = {
    'rockfall': {'entry': 0x189, 'end': 0x212},
    'face': {'entry': 0x2b9, 'end': 0x3f8},
}
PRESET = PRESETS['rockfall']; NAME = 'rockfall'


class ObjectOracle(CombatOracle):
    RANGES = {'object': ([(0x9e41, 0x9e69)], [(0, 0x3200)])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=5000)


def cases():
    rng = random.Random(PRESET['entry'])
    out = []
    for n in range(800):
        out.append({'ah': rng.choice([0, 1, 1, 1, 1]), 'x': rng.randrange(0, 4000), 'y': rng.randrange(0, 4000), 'f15': rng.choice([0, 0, 1, 2, 3, rng.randrange(256)]), 'f26': rng.choice([0, 1, 3, 0x1f, 0x20, rng.randrange(1 << 32)]),
                    'f2a': rng.choice([0, 9, 10, 0x14, 0x15, 0xb, 1, rng.randrange(256)]), 'w1': rng.randrange(65536), 'f22': rng.randrange(1 << 32), 'seed': rng.randrange(256),
                    'area': [[rng.choice([0, 0, 1, 5]), rng.choice([0, 0, 3])] for _ in range(3)]})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    ObjectOracle.RANGES = {'object': ([(0x9e41, 0x9e69)], [(PRESET['entry'], PRESET['end'])])}
    o = ObjectOracle(code, report['fs_assignments'], constants)
    wrk = (game / 'ARMY__03.WRK').read_bytes(); o.cpu.mem_write(o.WRK, wrk)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def area():   # FS:14B = 11DDE
        log.append(b'A' + struct.pack('<II', reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        a = state['area'].pop(0) if state['area'] else [0, 0]
        o.cpu.reg_write(UC_X86_REG_EAX, a[0]); o.cpu.reg_write(UC_X86_REG_EBX, a[1]); o.ret()
    def effect():   # FS:19
        esp = reg(UC_X86_REG_ESP); args = struct.unpack('<III', bytes(o.cpu.mem_read(esp + 4, 12)))
        log.append(b'E' + struct.pack('<BBIII', reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255, args[0] - o.POOL, args[1], args[2])); o.ret()
    def sound():    # FS:31
        log.append(b'S' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.ret()
    o.stubs = {}
    slots = {x['fs_slot']: x['target_offset'] for x in report['fs_assignments']}
    o.stubs[slots[0x14b]] = area; o.stubs[slots[0x19]] = effect; o.stubs[slots[0x31]] = sound
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state['area'] = [list(a) for a in case['area']]; o.prepare(32, 32); o.mode = 'object'
        o.cpu.mem_write(o.POOL, bytes(0x2000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x19eb, bytes([case['seed']])); o.cpu.mem_write(0x19ec, bytes([case['seed'] ^ 0x5a]))
        r = bytearray(0x50); struct.pack_into('<II', r, 4, case['x'], case['y']); r[0x15] = case['f15']; struct.pack_into('<I', r, 0x26, case['f26']); r[0x2a] = case['f2a']; struct.pack_into('<H', r, 1, case['w1']); struct.pack_into('<I', r, 0x22, case['f22'])
        o.cpu.mem_write(o.POOL + 0x800, bytes(r))
        for q in (UC_X86_REG_ESI, UC_X86_REG_EDI): o.cpu.reg_write(q, o.POOL + 0x800)
        o.cpu.reg_write(UC_X86_REG_EAX, case['ah'] << 8); o.cpu.reg_write(UC_X86_REG_EBX, 7)
        o.run(o.WRK + PRESET['entry'])
        out = bytes(o.cpu.mem_read(o.POOL + 0x800, 0x50)) + bytes(o.cpu.mem_read(0x19eb, 2)) + b''.join(log)
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']} | {'ARMY__03.WRK': hashlib.sha256(wrk).hexdigest()}, 'random_values': constants['random_values'],
            'cases': all_cases, 'results': {NAME: {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--script', choices=sorted(PRESETS), default='rockfall')
    args = parser.parse_args()
    NAME = args.script; PRESET = PRESETS[NAME]
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
