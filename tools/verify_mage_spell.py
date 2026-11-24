#!/usr/bin/env python3
"""Verify the discharge of the mages (ARMY__03 effect types 0 and 8, ARMY__03.WRK 2E4C and 30D1) with Unicorn. Type 0 is what a mage's strike creates (9727 with BL = DAT[70] = 0:
the effect table of the attacker's army): a 72x72 flash whose frame follows the attacker's heading (F0h + 4·heading); on creation it counts the items of the attacker's settlement and
chooses the effect that follows: one item — fire (ARMY__00 type 13h), otherwise — type 8, the blast; one time in eight (0 or 3+ items) a phantom is summoned next to the target (FS:16B,
FS:45, FS:19B). Type 8 hits the area around the point with BD0F (attack 14h, kind 2, all planes), its side the distance between attacker and point (1..3 cells).
FS:19 (9727), FS:35, FS:69 (BD0F), FS:16B (9064), FS:45 (A758), FS:19B (43E5) are recorded stubs; FS:6D and FS:109 run as original code."""
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


class SpellOracle(CombatOracle):
    RANGES = {'spell': ([(0x6877, 0x68a0), (0x9e41, 0x9e55)], [(0x2e4c, 0x2f4e), (0x30d1, 0x3118)])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=20000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('spell did not return')


def cases():
    rng = random.Random(0x2e4c)
    out = []
    for n in range(3000):
        kind = rng.choice(['create', 'create', 'create', 'step', 'blast'])
        out.append({'kind': kind, 'template': rng.choice([13, 13, 14, 3, 0x20, 0x16, 0x17, 5, 0x15]), 'army': 3, 'owner': rng.randrange(3), 'heading': rng.randrange(256), 'charge': rng.choice([0, 5, 10, 11, 0x7f]),
                    'inventory': [rng.choice([0, 0, 0, 0x10, 0x13, 6, 7, 9, 12]) for _ in range(20)] if rng.random() < 0.8 else [0] * 20,
                    'x': rng.randrange(64, 4000), 'y': rng.randrange(64, 4000), 'tx': rng.randrange(64, 4000), 'ty': rng.randrange(64, 4000), 'close': rng.random() < 0.5,
                    'seed': rng.randrange(256), 'spot': rng.choice([0, 1]), 'spot_direction': rng.randrange(8), 'layer': rng.randrange(4),
                    'timer': rng.choice([1, 2, 3]), 'frames': rng.choice([1, 2, 4]), 'frame': rng.randrange(0xf0, 0x110)})
    for c in out:
        c['ex'] = (c['x'] + ((c['tx'] - c['x']) if not c['close'] else (c['seed'] % 200) - 100)) & 0xffffffff
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = SpellOracle(code, report['fs_assignments'], constants)
    wrk = (game / 'ARMY__03.WRK').read_bytes(); o.cpu.mem_write(o.WRK, wrk)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def logger(letter, *specs):
        def f():
            log.append(letter + b''.join(struct.pack('<I', (reg(r) >> sh) & mask) for r, sh, mask in specs)); o.ret()
        return f
    A, B, C, D = UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX
    def effect():   # FS:19 = 9727: AL army, BL type, BH layer, ECX, EDX the point
        log.append(b'E' + struct.pack('<BBBII', reg(A) & 255, reg(B) & 255, (reg(B) >> 8) & 255, reg(C), reg(D))); o.ret()
    def spot():     # FS:16B = 9064
        log.append(b'F' + struct.pack('<BB', reg(A) & 255, (reg(A) >> 8) & 255))
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0 if state['spot'] else 0x40))
        o.cpu.reg_write(B, state['spot_layer'] | (state['spot_direction'] << 8)); o.cpu.reg_write(C, state['spot_x']); o.cpu.reg_write(D, state['spot_y']); o.ret()
    def create():   # FS:45 = A758
        log.append(b'C' + struct.pack('<BBBBII', reg(A) & 255, (reg(A) >> 8) & 255, reg(B) & 255, (reg(B) >> 8) & 255, reg(C), reg(D))); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x600); o.ret()
    o.stubs = {0x9727: effect, 0xa47e: logger(b'S', (A, 0, 0xffffffff)), 0xbd0f: logger(b'H', (A, 0, 255), (A, 8, 255), (B, 0, 255), (C, 0, 255), (C, 8, 255), (D, 0, 255), (D, 8, 255)),
               0x9064: spot, 0xa758: create, 0x43e5: logger(b'I', (A, 0, 0xffffffff))}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state.update(spot=case['spot'], spot_direction=case['spot_direction'], spot_layer=case['layer'], spot_x=case['x'] ^ 0x20, spot_y=case['y'] ^ 0x40); o.prepare(32, 32); o.mode = 'spell'
        o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(o.FS + 8, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x19eb, bytes([case['seed']]))
        o.cpu.mem_write(o.POOL, bytes(0x1000))
        for p in range(3):
            part = bytearray(0xf5); part[0xcd:0xcd + 20] = bytes(case['inventory'] if p == case['owner'] else [0] * 20); o.cpu.mem_write(0x97a + 0xf5 * p, bytes(part))
        attacker = bytearray(0x40); attacker[0] = case['army']; attacker[3] = case['owner']; attacker[0x0e] = case['template']; attacker[0x25] = case['heading']; attacker[0x31] = case['charge']
        struct.pack_into('<II', attacker, 4, case['x'], case['y'])
        o.cpu.mem_write(o.POOL + 0x100, bytes(attacker))
        effect_record = bytearray(0x22); effect_record[1] = case['army']; effect_record[5] = case['layer']
        if case['kind'] == 'blast':
            struct.pack_into('<I', effect_record, 7, case['ex']); struct.pack_into('<I', effect_record, 0xc, case['ty'])
        else:
            struct.pack_into('<I', effect_record, 7, case['tx']); struct.pack_into('<I', effect_record, 0xc, case['ty'])
        if case['kind'] == 'step': effect_record[0x16] = case['timer']; effect_record[0x17] = case['frames']; struct.pack_into('<H', effect_record, 3, case['frame']); effect_record[0] = 0
        o.cpu.mem_write(o.POOL + 0x200, bytes(effect_record))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x200); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x300); o.cpu.reg_write(UC_X86_REG_EBX, o.POOL + 0x100)
        o.cpu.reg_write(UC_X86_REG_ECX, case['tx']); o.cpu.reg_write(UC_X86_REG_EDX, case['ty']); o.cpu.reg_write(UC_X86_REG_EAX, case['army'])
        if case['kind'] == 'create':
            o.cpu.reg_write(UC_X86_REG_EAX, case['army']); o.run(o.WRK + 0x2e4c)
        elif case['kind'] == 'step':
            o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x200); o.cpu.reg_write(UC_X86_REG_EAX, 0x100); o.run(o.WRK + 0x2e4c)
        else:
            o.run(o.WRK + 0x30d1)
        out = bytes(o.cpu.mem_read(o.POOL + 0x200, 0x22)) + bytes(o.cpu.mem_read(o.POOL + 0x600, 0x22)) + b''.join(log)
        if __import__('os').environ.get('DEBUG_CASE') == str(all_cases.index(case)): print(out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']} | {'ARMY__03.WRK': hashlib.sha256(wrk).hexdigest()},
            'random_values': constants['random_values'], 'cases': all_cases, 'results': {'spell': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


def rng_small(case):
    return (case['seed'] % 200) - 100


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
