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
    'wall': {'entry': 0x3f8, 'end': 0x522},
    'head': {'entry': 0x522, 'end': 0x624},
    'tree': {'entry': 0x624, 'end': 0x6ba},
    'raven': {'entry': 0x6ba, 'end': 0x6ed},
    'chest': {'entry': 0x2a6b, 'end': 0x2b63, 'dat': True},
    'generator': {'entry': 0x29f1, 'end': 0x2a6b, 'dat': True},
    'phantom32': {'entry': 0x2ba7, 'end': 0x2bfc, 'regs_effect': True, 'extra2': True},
    'phantom32idle': {'entry': 0x2c1d, 'end': 0x2c61, 'regs_effect': True, 'extra2': True, 'stop_jumps': True},
    'jugphantom': {'entry': 0x2c6d, 'end': 0x2d08, 'regs_effect': True, 'extra2': True},
    'talking': {'entry': 0x2d08, 'end': 0x2d52, 'dat': True, 'extra2': True},
    'faceb': {'entry': 0x2d52, 'end': 0x2e19, 'dat': True, 'extra2': True},
    'rockfallq': {'entry': 0x2e19, 'end': 0x2e4c, 'extra2': True, 'dat': True, 'more': [(0x189, 0x212)]},
    'switch': {'entry': 0x52, 'end': 0x86, 'module': 'ARMY__00.WRK', 'extra': True},
    'switch2': {'entry': 0x371, 'end': 0x3a9, 'module': 'ARMY__00.WRK', 'extra': True},
    'exit': {'entry': 0x3a9, 'end': 0x3f0, 'module': 'ARMY__00.WRK', 'extra2': True},
    'itemobj': {'entry': 0x86, 'end': 0x4f3, 'module': 'ARMY__00.WRK', 'visitor': True, 'dat': True, 'entries': [0x86, 0xf1, 0xfb, 0x105, 0x10f, 0x120, 0x131, 0x142, 0x153, 0x164, 0x1f2, 0x203, 0x214, 0x488, 0x29b, 0x311, 0x3f0, 0x46a, 0x477, 0x499]},
    'fx54a': {'entry': 0x54a, 'end': 0x593, 'module': 'ARMY__00.WRK', 'effect': True},
    'fx6fe': {'entry': 0x6fe, 'end': 0x735, 'module': 'ARMY__00.WRK', 'effect': True},
    'fx735': {'entry': 0x735, 'end': 0x762, 'module': 'ARMY__00.WRK', 'effect': True},
    'fx7fe': {'entry': 0x7fe, 'end': 0x85a, 'module': 'ARMY__00.WRK', 'effect': True},
    'fx8da': {'entry': 0x8da, 'end': 0x986, 'module': 'ARMY__00.WRK', 'effect': True, 'regs_effect': True},
    'fxa6c': {'entry': 0xa6c, 'end': 0xaeb, 'module': 'ARMY__00.WRK', 'effect': True, 'regs_effect': True},
    'fxab4': {'entry': 0xab4, 'end': 0xaeb, 'more': [(0xa6c, 0xab4)], 'module': 'ARMY__00.WRK', 'effect': True, 'regs_effect': True},
    'fxaeb': {'entry': 0xaeb, 'end': 0xb62, 'module': 'ARMY__00.WRK', 'effect': True, 'extra': True},
    'fxb62': {'entry': 0xb62, 'end': 0xbab, 'module': 'ARMY__00.WRK', 'effect': True},
    'fxfd1': {'entry': 0xfd1, 'end': 0x1012, 'module': 'ARMY__00.WRK', 'effect': True},
    'fx1012': {'entry': 0x1012, 'end': 0x10b3, 'module': 'ARMY__00.WRK', 'effect': True},
    'manuscript': {'entry': 0x2710, 'end': 0x279a, 'entries': [0x2710, 0x2780, 0x278d], 'visitor': True},
    'dragon': {'entry': 0x279a, 'end': 0x29f1, 'regs_effect': True, 'extra': True},
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
        out.append({'ah': rng.choice([0, 1, 1, 1, 1, 8, 8, 8]) if NAME == 'manuscript' else rng.choice([0, 1, 1, 1, 1]), 'x': rng.randrange(0, 4000), 'y': rng.randrange(0, 4000), 'f15': rng.choice([0, 0, 1, 2, 3, rng.randrange(256)]), 'f26': rng.choice([0, 1, 3, 0x1f, 0x20, rng.randrange(1 << 32)]),
                    'f2a': rng.choice([0, 9, 10, 0x14, 0x15, 0xb, 1, rng.randrange(256)]), 'w1': rng.choice([0x1b2, 0x1b3, 0x1b6, 0x1ab, rng.randrange(65536)]), 'f14': rng.choice([0, 0x80, 0x81, rng.randrange(256)]), 'f0d': rng.randrange(4), 'f19': rng.choice([100, 0, 0x80, 0xff, 0xfb, 0xfa, 1, rng.randrange(256)]), 'f27': rng.choice([0x18, 0x20, 0x28, rng.randrange(256)]), 'f28': rng.choice([0, 3, 0xb, 0x18, rng.randrange(256)]), 'f2d': rng.randrange(256), 'tick': rng.choice([0, 1, 7, 8, 0x1f, 0x20, rng.randrange(256)]), 'f22': rng.randrange(1 << 32), 'seed': rng.randrange(256),
                    'area': [[rng.choice([0, 0, 1, 5]), rng.choice([0, 0, 3]), rng.choice([0, 0, 1, 2]), rng.randrange(1 << 30), rng.randrange(1 << 30)] for _ in range(3)], 'claim': [rng.choice([0, 1]) for _ in range(3)], 'zf': [rng.choice([0, 1, 1]) for _ in range(2)], 'spot': [[rng.choice([0, 1]), rng.randrange(4000), rng.randrange(4000)] for _ in range(10)], 'beside': [[rng.choice([0, 1]), rng.randrange(256), rng.randrange(256), rng.randrange(256)] for _ in range(2)], 'create': [rng.choice([0, 1]) for _ in range(10)], 'units': [rng.choice([8, 0x1c, 3, 8, 5]) for _ in range(rng.randrange(0, 14))], 'finds': [rng.choice([0, 1, 2]) for _ in range(12)], 'objbytes': bytes(rng.randrange(256) for _ in range(0x80)).hex(), 'part12': rng.randrange(1 << 32), 'dat3': rng.randrange(256), 'dat48': rng.randrange(65536), 'k': rng.randrange(20 if NAME == 'itemobj' else 3), 'bl': rng.randrange(256), 'vowner': rng.randrange(8), 'mask28': rng.randrange(256), 'svc11': [[rng.choice([0, 1]), rng.choice([0, 4, 4, 1, 7])] for _ in range(2)], 'svca5': [rng.choice([0, 1]) for _ in range(3)], 'parent25': rng.randrange(256), 'e0': rng.randrange(256), 'e12': rng.choice([0, 1, 2, rng.randrange(1 << 32)]), 'a1': [[rng.choice([0, 1]), rng.choice([0, 1, 2, 5]), rng.choice([0, 1, 2])] for _ in range(2)], 'a9': [rng.choice([0, 1, 2, 3, 4]) for _ in range(2)], 'ef1': rng.randrange(256), 'ef5': rng.choice([0, 0, 1, 2]), 'ef7': rng.randrange(1 << 20), 'efc': rng.randrange(1 << 20), 'ef16': rng.choice([0, 1, 2, 9, rng.randrange(256)]), 'ef17': rng.randrange(256), 'ef18': rng.choice([0, 0x100, 0x140, 0x180]), 'ef1c': rng.randrange(1 << 20), 'f31': rng.randrange(256), 'strength': rng.choice([0, 0x7cf, 0x7d0, rng.randrange(1 << 20)]), 'count': rng.randrange(2, 8), 'datsound': rng.randrange(1 << 20), 'dist': [rng.randrange(256) for _ in range(3)]})
    if NAME == 'switch':
        for c in out:
            c['f22'] = rng.randrange(16) | (rng.randrange(4) << 8) | (rng.randrange(8) << 16) | (rng.randrange(9) << 24); c['f26'] = rng.randrange(32); c['ah'] = rng.choice([1, 1, 1, 0])
    if NAME == 'generator':
        for c in out:
            if rng.random() < 0.6: c.update(ah=1, tick=0, units=[0x1c] * rng.randrange(1, 4) + [8] * rng.randrange(0, 5), beside=[[0, rng.randrange(256), rng.randrange(256), rng.randrange(256)], [0, 1, 2, 3]])
    if NAME == 'chest':   # reach the stage of the birds
        for c in out:
            if rng.random() < 0.5:
                c.update(ah=1, f14=1, f15=rng.choice([1, 2, 5]), f19=rng.randrange(1, 100), w1=(c['dat48'] + 9) & 0xffff, zf=[0, 0], spot=[[1, rng.randrange(4000), rng.randrange(4000)] for _ in range(10)], create=[rng.choice([0, 1, 1]) for _ in range(10)])
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    ObjectOracle.RANGES = {'object': ([(0x9e41, 0x9e69)], [(PRESET['entry'], PRESET['end'])] + PRESET.get('more', []))}
    o = ObjectOracle(code, report['fs_assignments'], constants)
    wrk = (game / PRESET.get('module', 'ARMY__03.WRK')).read_bytes(); o.cpu.mem_write(o.WRK, wrk)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def area():   # FS:14B = 11DDE
        log.append(b'A' + struct.pack('<II', reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        a = state['area'].pop(0) if state['area'] else [0, 0, 0, 0, 0]
        o.cpu.reg_write(UC_X86_REG_EAX, a[0]); o.cpu.reg_write(UC_X86_REG_EBX, a[1])
        o.cpu.reg_write(UC_X86_REG_EDI, 0 if a[2] == 0 else o.POOL + 0x100 + 0x40 * a[2])
        o.ret()
    def effect_regs():   # FS:19 called with registers only (the dragon)
        log.append(b'E' + struct.pack('<BBBII', reg(UC_X86_REG_EAX) & 255, reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX))); o.ret()
    def find():   # FS:14F = 11BE0
        log.append(b'F' + struct.pack('<BBBB', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255, reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255))
        k = state['finds'].pop(0) if state['finds'] else 0
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x100 + 0x40 * k)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if k == 0 else 0)); o.ret()
    def effect():   # FS:19
        esp = reg(UC_X86_REG_ESP); args = struct.unpack('<III', bytes(o.cpu.mem_read(esp + 4, 12)))
        log.append(b'E' + struct.pack('<BBIII', reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255, args[0] - o.POOL, args[1], args[2])); o.ret()
    def claim():    # FS:157
        log.append(b'C' + struct.pack('<IIH', reg(UC_X86_REG_EAX), reg(UC_X86_REG_EBX), reg(UC_X86_REG_ECX) & 0xffff))
        o.cpu.reg_write(UC_X86_REG_EAX, state['claim'].pop(0) if state['claim'] else 0); o.ret()
    def distance():  # FS:15B
        log.append(b'L' + struct.pack('<II', reg(UC_X86_REG_EAX), reg(UC_X86_REG_EBX)))
        o.cpu.reg_write(UC_X86_REG_EAX, (reg(UC_X86_REG_EAX) & ~0xff) | (state['dist'].pop(0) if state['dist'] else 0)); o.ret()
    def spawn():     # FS:69
        log.append(b'N' + struct.pack('<HBHI', reg(UC_X86_REG_EAX) & 0xffff, reg(UC_X86_REG_EBX) & 255, reg(UC_X86_REG_ECX) & 0xffff, reg(UC_X86_REG_EDX))); o.ret()
    def redraw(): log.append(b'D'); o.ret()
    def appearance(): log.append(b'N' + struct.pack('<B', reg(UC_X86_REG_EAX) & 255)); o.ret()
    def scan_masked():   # FS:17B = D895 called with the mask of the participant's +12 changed
        owner = bytes(o.cpu.mem_read(reg(UC_X86_REG_ESI) + 3, 1))[0] % 3
        log.append(b'F' + struct.pack('<BI', reg(UC_X86_REG_EAX) & 255, struct.unpack('<I', bytes(o.cpu.mem_read(0x97a + 0xf5 * owner + 0x12, 4)))[0]))
        k = state['finds'].pop(0) if state['finds'] else 0
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x100 + 0x40 * k)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if k == 0 else 0)); o.ret()
    def strength():   # FS:1B7: EDX such that [EDX + 19F1h] is the strength
        log.append(b'T' + struct.pack('<B', reg(UC_X86_REG_EAX) & 255)); o.cpu.reg_write(UC_X86_REG_EDX, 0x7000 - 0x19f1); o.ret()
    def stopper(letter):
        def f(): log.append(letter); o.cpu.emu_stop()
        return f
    def zf_service(letter, key):
        def f():
            log.append(letter + struct.pack('<I', reg(UC_X86_REG_EDX)))
            z = state[key].pop(0) if state[key] else 1
            o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if z else 0)); o.ret()
        return f
    def spot():   # FS:187 = 10AD2 (keeps AL, AH, BL; result in ECX, EDX)
        log.append(b'P' + struct.pack('<BBBII', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255, reg(UC_X86_REG_EBX) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        sp = state['spot'].pop(0) if state['spot'] else [1, 0, 0]
        o.cpu.reg_write(UC_X86_REG_ECX, sp[1]); o.cpu.reg_write(UC_X86_REG_EDX, sp[2])
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if sp[0] else 0)); o.ret()
    def beside():  # FS:16B = 9064: AL, AH, BL results
        log.append(b'B' + struct.pack('<BB', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255))
        sp = state['beside'].pop(0) if state['beside'] else [1, 0, 0, 0]
        o.cpu.reg_write(UC_X86_REG_EAX, sp[1] | (sp[2] << 8)); o.cpu.reg_write(UC_X86_REG_EBX, sp[3])
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if sp[0] else 0)); o.ret()
    def create():  # FS:45 = A758
        log.append(b'K' + struct.pack('<BBBBII', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255, reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x600)
        z = state['create'].pop(0) if state['create'] else 1
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if z else 0)); o.ret()
    def occupy(): log.append(b'O' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.ret()
    def participant():  # FS:6D = 6877: ECX the participant record
        o.cpu.reg_write(UC_X86_REG_ECX, 0x97a + 0xf5 * (bytes(o.cpu.mem_read(reg(UC_X86_REG_ESI) + 3, 1))[0] % 3)); o.ret()
    def remove(): log.append(b'R'); o.ret()
    def vitals(): log.append(b'V'); o.ret()
    def actions(): log.append(b'U'); o.cpu.emu_stop()
    def svc11():   # FS:11
        log.append(b'Q' + struct.pack('<I', reg(UC_X86_REG_EAX)))
        zf, al = state['svc11'].pop(0) if state['svc11'] else (1, 0)
        o.cpu.reg_write(UC_X86_REG_EAX, (reg(UC_X86_REG_EAX) & ~0xff) | al)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if zf else 0)); o.ret()
    def svca5():   # FS:A5
        log.append(b'W')
        z = state['svca5'].pop(0) if state['svca5'] else 1
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if z else 0)); o.ret()
    def svcad(): log.append(b'M' + struct.pack('<BB', reg(UC_X86_REG_EAX) & 255, reg(UC_X86_REG_EBX) & 255)); o.ret()
    def sound35(): log.append(b'S' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.ret()
    def svca1():   # FS:A1
        log.append(b'V')
        zf, al, k = state['a1'].pop(0) if state['a1'] else (1, 0, 0)
        o.cpu.reg_write(UC_X86_REG_EAX, (reg(UC_X86_REG_EAX) & ~0xff) | al); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x100 + 0x40 * k)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if zf else 0)); o.ret()
    def svca9():   # FS:A9
        log.append(b'Y' + struct.pack('<H', reg(UC_X86_REG_EAX) & 0xffff))
        al = state['a9'].pop(0) if state['a9'] else 0
        o.cpu.reg_write(UC_X86_REG_EAX, (reg(UC_X86_REG_EAX) & ~0xff) | al)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if al == 0 else 0)); o.ret()
    def svc25(): log.append(b'a' + struct.pack('<IB', reg(UC_X86_REG_ESI) - o.POOL, reg(UC_X86_REG_EBX) & 255)); o.ret()
    def svc29(): log.append(b'b' + struct.pack('<B', reg(UC_X86_REG_EBX) & 255)); o.ret()
    def level_exit(): log.append(b'X' + struct.pack('<B', reg(UC_X86_REG_EAX) & 255)); o.ret()
    def sound():    # FS:31
        log.append(b'S' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.ret()
    o.stubs = {}
    slots = {x['fs_slot']: x['target_offset'] for x in report['fs_assignments']}
    o.stubs[slots[0x14b]] = area; o.stubs[slots[0x19]] = effect_regs if PRESET.get('regs_effect') else effect; o.stubs[slots[0x14f]] = find; o.stubs[slots[0x31]] = sound; o.stubs[slots[0x157]] = claim; o.stubs[slots[0x15b]] = distance; o.stubs[slots[0x69]] = spawn
    o.stubs[slots[0x1bf]] = zf_service(b'Z', 'zf'); o.stubs[slots[0x187]] = spot; o.stubs[slots[0x16b]] = beside; o.stubs[slots[0x45]] = create; o.stubs[slots[0x19b]] = occupy; o.stubs[slots[0x6d]] = participant
    o.stubs[slots[0x1a3]] = level_exit; o.stubs[slots[0x25]] = svc25; o.stubs[slots[0x29]] = svc29; o.stubs[slots[0x11]] = svc11; o.stubs[slots[0xa1]] = svca1; o.stubs[slots[0xa9]] = svca9; o.stubs[slots[0xa5]] = svca5; o.stubs[slots[0xad]] = svcad; o.stubs[slots[0x35]] = sound35; o.stubs[slots[0x1d]] = redraw; o.stubs[slots[0x4d]] = appearance; o.stubs[slots[0x17b]] = scan_masked; o.stubs[slots[0x1b7]] = strength
    if PRESET.get('stop_jumps'): o.stubs[slots[0xed]] = stopper(b'H')
    o.stubs[slots[0x1d]] = redraw; o.stubs[slots[0x15]] = remove; o.stubs[slots[0xdd]] = vitals; o.stubs[slots[0xe1]] = actions
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state['area'] = [list(a) for a in case['area']]; state['claim'] = list(case['claim']); state['svc11'] = [list(x) for x in case['svc11']]; state['svca5'] = list(case['svca5']); state['a1'] = [list(x) for x in case['a1']]; state['a9'] = list(case['a9']); state['finds'] = list(case['finds']); state['zf'] = list(case['zf']); state['spot'] = [list(x) for x in case['spot']]; state['beside'] = [list(x) for x in case['beside']]; state['create'] = list(case['create']); state['dist'] = list(case['dist']); o.prepare(32, 32); o.mode = 'object'
        o.cpu.mem_write(o.POOL, bytes(0x2000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x19eb, bytes([case['seed']])); o.cpu.mem_write(o.FS + 0x10, bytes([case['tick']])); o.cpu.mem_write(o.FS + 8, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x19ec, bytes([case['seed'] ^ 0x5a]))
        r = bytearray(0x50); struct.pack_into('<II', r, 4, case['x'], case['y']); r[0x15] = case['f15']; struct.pack_into('<I', r, 0x26, case['f26']); r[0x2a] = case['f2a']; r[0x14] = case['f14']; r[0x0d] = case['f0d']; r[0x19] = case['f19']; r[0x27] = case['f27']; r[0x28] = case['f28']; r[0x2d] = case['f2d']; struct.pack_into('<H', r, 1, case['w1']); struct.pack_into('<I', r, 0x22, case['f22'])
        r[0] = case['e0']; struct.pack_into('<I', r, 0x12, case['e12'])
        r[1] = case['ef1']; r[5] = case['ef5']; struct.pack_into('<I', r, 7, case['ef7']); struct.pack_into('<I', r, 0xc, case['efc']); r[0x16] = case['ef16']; r[0x17] = case['ef17']; struct.pack_into('<I', r, 0x18, case['ef18']); struct.pack_into('<I', r, 0x1c, case['ef1c'])
        pr = bytearray(0x40); pr[0x25] = case['parent25']; o.cpu.mem_write(o.POOL + 0xa00, bytes(pr))
        o.cpu.mem_write(o.POOL + 0x800, bytes(r))
        if PRESET.get('extra'):
            o.cpu.mem_write(o.POOL + 0x140, bytes.fromhex(case['objbytes'])); o.cpu.mem_write(0x97a + 0x12, struct.pack('<I', case['part12']))
        else:
            pass
        for k in range(1, 3): (None if PRESET.get('extra') else o.cpu.mem_write(o.POOL + 0x100 + 0x40 * k, struct.pack('<II', 0, 0)[:0] + bytes(4) + struct.pack('<II', case['area'][0][3] >> k, case['area'][0][4] >> k)))
        o.cpu.mem_write(o.POOL + 0x800 + 0x31, bytes([case['f31']])); o.cpu.mem_write(0x7000, struct.pack('<I', case['strength'])); o.cpu.mem_write(o.FS + 0x142, bytes([case['count']]))
        dat = bytearray(0xab); struct.pack_into('<I', dat, 0xa3, case['datsound']); dat[3] = case['dat3']; struct.pack_into('<H', dat, 0x48, case['dat48']); o.cpu.mem_write(o.DAT, bytes(dat))
        units = case['units']
        for k, t in enumerate(units):
            ur = bytearray(0x40); ur[0x0e] = t; struct.pack_into('<I', ur, 0x10, 0x40); o.cpu.mem_write(o.POOL + 0x1000 + 0x40 * k, bytes(ur))
        for pi in range(3):
            part = bytearray(0xf5); struct.pack_into('<I', part, 0x0e, 0x1000); struct.pack_into('<I', part, 0x09, 0x1000 + 0x40 * len(units)); o.cpu.mem_write(0x97a + 0xf5 * pi, bytes(part))
        if PRESET.get('extra') or PRESET.get('extra2'): o.cpu.mem_write(0x97a + 0x12, struct.pack('<I', case['part12']))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x800); o.cpu.mem_write(o.POOL + 0x900, bytes([0, 0, 0, case['vowner']])); o.cpu.mem_write(o.POOL + 0x800 + 0x28, bytes([case['mask28']]))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x900 if (PRESET.get('visitor') and case['ah'] == 8) else (o.DAT if (PRESET.get('dat') and case['ah'] != 0) else o.POOL + 0x800))
        o.cpu.reg_write(UC_X86_REG_EAX, case['ah'] << 8); o.cpu.reg_write(UC_X86_REG_EBX, (o.POOL + 0xa00) if (PRESET.get('effect') and case['ah'] == 0 and PRESET['entry'] in (0x54a, 0xb62, 0xfd1, 0x1012)) else case['bl']); o.cpu.reg_write(UC_X86_REG_ECX, 0); o.cpu.reg_write(UC_X86_REG_EDX, 0)
        o.run(o.WRK + (PRESET['entries'][case['k']] if PRESET.get('entries') else PRESET['entry']))
        out = (struct.pack('<H', (reg(UC_X86_REG_EAX) & 0xffff) if case['ah'] == 8 else 0) if PRESET.get('visitor') else b'') + bytes(o.cpu.mem_read(o.POOL + 0x800, 0x50)) + bytes(o.cpu.mem_read(0x19eb, 2)) + (bytes(o.cpu.mem_read(o.POOL + 0x140, 0x80)) + bytes(o.cpu.mem_read(0x97a + 0x12, 4)) if PRESET.get('extra') else (bytes(o.cpu.mem_read(0x97a + 0x12, 4)) if PRESET.get('extra2') else b'')) + b''.join(log)
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']} | {PRESET.get('module', 'ARMY__03.WRK'): hashlib.sha256(wrk).hexdigest()}, 'random_values': constants['random_values'],
            'cases': all_cases, 'results': {NAME: {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--script', choices=sorted(PRESETS), default='rockfall')
    args = parser.parse_args()
    NAME = args.script; PRESET = PRESETS[NAME]
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
