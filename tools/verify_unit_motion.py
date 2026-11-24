#!/usr/bin/env python3
"""Verify the unit motion machine (LE:1 9210..95D3: the states of +2D — 0 rest, 1 turn, 2 walk, 3 swing, 4 move by 12930, 5 frame range, 6 and 7 resting animation — and the switch of
states 9275) with Unicorn. The services are recorded stubs with answers from the case: 70B0 (one step of the walk), 12930, 9653 (one step of the turn), D12C and 10799 (border
events of the idle state), 9727 (effect creation by the swing) and the death 95D3."""
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


class MotionOracle(CombatOracle):
    RANGES = {'motion': ([(0x9210, 0x95d3)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('motion did not return')


def cases():
    rng = random.Random(0x9210)
    out = []
    for n in range(1500):
        out.append({'cur': rng.randrange(8), 'req': rng.choice([0, 0, 1, 2, 3, 4, 5, 6, 7, rng.randrange(8)]), 'flag17': rng.choice([0, 0, 0x80]), 'f24': rng.choice([0, 1, 2, 5, rng.randrange(256)]),
                    'f25': rng.randrange(256), 'f26': rng.choice([0, 1, 2, rng.randrange(256)]), 'f27': rng.choice([0, 1, 5, rng.randrange(256)]), 'f2c': rng.choice([0, 1, 2]), 'f2e': rng.randrange(256),
                    'f31': rng.randrange(256), 'f22': rng.choice([0xff, 0xff, rng.randrange(64)]), 'f23': rng.randrange(64), 'f28': rng.randrange(65536), 'f2a': rng.randrange(65536), 'f2f': rng.randrange(256),
                    'target': rng.choice([0, 0, 0x100, 0x140]), 'owner': rng.choice([0, 1, 2]), 'player': rng.choice([0, 1, 2]), 'x': rng.randrange(0, 4000), 'y': rng.randrange(0, 4000), 'layer': rng.randrange(4),
                    'template': {'flags': rng.choice([0, 0, 0x100000, 0x8]), 'base': rng.randrange(3000), 'n4a': rng.choice([1, 4, 8, 13]), 'n4b': rng.choice([1, 4, 10]), 'n4c': rng.choice([1, 2, 3]),
                                 'n4d': rng.choice([1, 2, 4]), 'n4e': rng.choice([0, 1, 2, 3]), 'sum': [rng.randrange(4) for _ in range(4)], 'f70': rng.choice([0, 0x82, 0x88]), 'f78': rng.choice([1, 4, 12]),
                                 'muzzle': [rng.randrange(256) for _ in range(16)]},
                    'map': [rng.choice([32, 64]), rng.choice([32, 48])],
                    'stubs': {'walk': rng.choice([0, 0x40, 0x40, 0xc0]), 'step4': rng.choice([0, 0x40, 0xc0]), 'turn_zf': rng.choice([0, 1]), 'border': rng.choice([0, 1, 2]), 'death_zf': rng.choice([0, 1])}})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = MotionOracle(code, report['fs_assignments'], constants)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def walk():
        log.append(b'W' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.cpu.reg_write(UC_X86_REG_EBX, (reg(UC_X86_REG_EBX) & ~0xff) | state['walk']); o.ret()
    def move4():
        log.append(b'M' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.cpu.reg_write(UC_X86_REG_EBX, (reg(UC_X86_REG_EBX) & ~0xff) | state['step4']); o.ret()
    def turn():
        log.append(b'T' + struct.pack('<B', (reg(UC_X86_REG_EAX) >> 8) & 255))
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if state['turn_zf'] else 0)); o.ret()
    def border():
        log.append(b'B'); o.cpu.reg_write(UC_X86_REG_EAX, state['border']); o.ret()
    def death_check():   # 10799
        log.append(b'K'); o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if state['death_zf'] else 0)); o.ret()
    def effect():
        esp = reg(UC_X86_REG_ESP); args = struct.unpack('<III', bytes(o.cpu.mem_read(esp + 4, 12)))
        log.append(b'E' + struct.pack('<BBBII', reg(UC_X86_REG_EAX) & 255, reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)) + struct.pack('<III', args[0] - o.POOL, args[1], args[2])); o.ret()
    def death():
        log.append(b'X'); o.cpu.emu_stop(); o.stopped = True
    o.stubs = {0x70b0: walk, 0x12930: move4, 0x9653: turn, 0xd12c: border, 0x10799: death_check, 0x9727: effect, 0x95d3: death}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state.update(case['stubs']); o.prepare(*case['map']); o.mode = 'motion'
        o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x1972, bytes([case['player']]))
        t = case['template']; raw = bytearray(0xab); struct.pack_into('<I', raw, 0x40, t['flags']); struct.pack_into('<H', raw, 0x48, t['base']); raw[0x4a] = t['n4a']; raw[0x4b] = t['n4b']; raw[0x4c] = t['n4c']
        raw[0x4d] = t['n4d']; raw[0x4e] = t['n4e']; raw[0x4f:0x53] = bytes(t['sum']); raw[0x60:0x70] = bytes(t['muzzle']); raw[0x70] = t['f70']; raw[0x78] = t['f78']
        o.cpu.mem_write(o.DAT, bytes(raw)); o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT))
        o.cpu.mem_write(o.POOL, bytes(0x1000)); target = bytearray(0x40); struct.pack_into('<II', target, 4, case['x'] ^ 0x55, case['y'] ^ 0xaa); target[0x0d] = case['layer']
        o.cpu.mem_write(o.POOL + 0x100, bytes(target)); o.cpu.mem_write(o.POOL + 0x140, bytes(target))
        r = bytearray(0x40); r[0x2d] = case['cur'] | (case['req'] << 4); r[0x17] = case['flag17']; r[0x24] = case['f24']; r[0x25] = case['f25']; r[0x26] = case['f26']; r[0x27] = case['f27']; r[0x2c] = case['f2c']
        r[0x2e] = case['f2e']; r[0x31] = case['f31']; r[0x22] = case['f22']; r[0x23] = case['f23']; struct.pack_into('<H', r, 0x28, case['f28']); struct.pack_into('<H', r, 0x2a, case['f2a']); r[0x2f] = case['f2f']
        struct.pack_into('<I', r, 0x1c, case['target']); r[3] = case['owner']; struct.pack_into('<II', r, 4, case['x'], case['y']); r[0x0d] = case['layer']
        o.cpu.mem_write(o.POOL + 0x800, bytes(r)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x800); o.cpu.reg_write(UC_X86_REG_EBX, 0); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.CODE + 0x9210)
        out = bytes(o.cpu.mem_read(o.POOL + 0x800, 0x40)) + bytes([reg(UC_X86_REG_EAX) & 255, reg(UC_X86_REG_EBX) & 255]) + b''.join(log)
        import os
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'motion': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
