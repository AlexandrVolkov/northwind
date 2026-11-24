#!/usr/bin/env python3
"""Verify the effect scripts of ARMY__01.WRK 10ED..1357 (kinds 9, 0Ah, 16h..1Eh) with Unicorn. AH = 0 creates the effect from the record of its creator (EBX), AH = 1 steps. FS:A5, FS:A9, FS:AD are recorded stubs
(FS:A5 returns ZF, FS:A9 returns AL), FS:71 gives a template buffer and the object behind the pointer +12 is a fixed buffer."""
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

ENTRIES = [('ARMY__01.WRK', a, e) for a, e in [(0x10ed, 0x114c), (0x114c, 0x117b), (0x117b, 0x11a2), (0x11a2, 0x11ac), (0x11ac, 0x11b6), (0x11b6, 0x11c0), (0x11c0, 0x11ca), (0x11ca, 0x120e), (0x120e, 0x1258), (0x1258, 0x12e5), (0x12e5, 0x1357)]] + \
    [('ARMY__02.WRK', a, e) for a, e in [(0x16dd, 0x170c), (0x170c, 0x1775), (0x1775, 0x1795), (0x1795, 0x1828), (0x1828, 0x18b8), (0x18b8, 0x18fc), (0x18fc, 0x1989), (0x1989, 0x1a06), (0x1a06, 0x1a65), (0x1a65, 0x1a8c), (0x1a8c, 0x1a96), (0x1a96, 0x1aa0), (0x1aa0, 0x1aaa), (0x1aaa, 0x1ab4), (0x1ab4, 0x1abe), (0x1abe, 0x1ac8), (0x1ac8, 0x1b12)]] + \
    [('ARMY__00.WRK', a, e) for a, e in [(0x1267, 0x129d), (0x129d, 0x12ec), (0x12ec, 0x13a3), (0x13a3, 0x13f9), (0x13f9, 0x143d), (0x143d, 0x1481), (0x1481, 0x14ce), (0x14ce, 0x151b)]]
PARENT, FOUND, TEMPLATE, STATE = 0xa00, 0xb00, 0xc00, 0xd00


class EffectOracle(CombatOracle):
    RANGES = {'unit': ([(0x9e41, 0x9e69)], [(0, 0x4000)])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=4000)


def cases():
    rng = random.Random(0x77)
    out = []
    for n in range(1500):
        k = rng.randrange(len(ENTRIES))
        out.append({'entry': k, 'ah': rng.choice([0, 1, 1, 1, 1]), 'f0': rng.randrange(256), 'f2': rng.randrange(256), 'f3': rng.randrange(1 << 16), 'f5': rng.choice([0, 0, 1, rng.randrange(256)]), 'bl7': rng.randrange(256), 't45': rng.randrange(256), 't40': rng.choice([0, 0x10, 0x10, rng.randrange(1 << 32)]), 'dim0': rng.choice([100, 0x800, 0x10000, rng.randrange(1 << 20)]), 'dim4': rng.choice([100, 0x800, 0x10000, rng.randrange(1 << 20)]), 'r10d': [rng.randrange(256) for _ in range(5)], 'f6': rng.randrange(256), 'x': rng.randrange(1 << 20), 'y': rng.randrange(1 << 20),
                    'f16': rng.choice([0, 1, 2, 9, 0xa, 0xff, rng.randrange(256)]), 'f17': rng.choice([0, 1, 2, 4, 5, 7, 9, rng.randrange(256)]), 'f18': rng.choice([0, 1, 2, 0x258, rng.randrange(1 << 20)]), 'f1c': rng.randrange(1 << 20),
                    'f12': rng.choice([0, 0x200, 0xb00, 0xb00, 0xb00, 0xb00]), 'p1': rng.randrange(1 << 16), 'p3': rng.randrange(256), 'p17': rng.randrange(1 << 16), 'p25': rng.randrange(256),
                    'o17': rng.choice([0, 0x80, 0x180, 0x100, rng.randrange(1 << 16)]), 'o19': rng.choice([0, 1, 5, 0x80, 0xff, rng.randrange(256)]), 'o1': rng.choice([0, 1, 100, 0xffff, rng.randrange(1 << 16)]),
                    't48': rng.choice([0, 1, 100, rng.randrange(1 << 16)]), 't4a': rng.randrange(256), 't4b': rng.randrange(256), 'wind': rng.randrange(256),
                    'svc': [[rng.choice([0, 1, 1]), rng.randrange(256) if rng.random() < 0.7 else rng.choice([0, 1, 2, 3]), rng.randrange(256)] for _ in range(6)]})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = EffectOracle(code, report['fs_assignments'], constants)
    slots = {x['fs_slot']: x['target_offset'] for x in report['fs_assignments']}
    wrks = {m: (game / m).read_bytes() for m in {e[0] for e in ENTRIES}}
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def rel(v): return v - o.POOL if o.POOL <= v < o.POOL + 0x2000 else v
    def setzf(z): o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if z else 0))
    def service(slot):
        def f():
            log.append(b'S' + struct.pack('<HIIII', slot, rel(reg(UC_X86_REG_EAX)), rel(reg(UC_X86_REG_EBX)), rel(reg(UC_X86_REG_ECX)), rel(reg(UC_X86_REG_ESI))))
            zf, eax, ebx = state['svc'].pop(0) if state['svc'] else (1, 0, 0)
            o.cpu.reg_write(UC_X86_REG_EAX, eax); o.cpu.reg_write(UC_X86_REG_EBX, ebx); setzf(zf)
            if slot == 0xa1: o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + FOUND)
            if slot in (0xa1, 0x119): o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + TEMPLATE)
            o.ret()
        return f
    def svc71():
        log.append(b'T' + struct.pack('<I', (reg(UC_X86_REG_EDI) - o.POOL) & 0xffffffff)); o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + TEMPLATE); o.ret()
    def svc19():
        esp = reg(UC_X86_REG_ESP); args = struct.unpack('<III', bytes(o.cpu.mem_read(esp + 4, 12)))
        log.append(b'W' + struct.pack('<IIIIII', rel(reg(UC_X86_REG_EBX)), rel(reg(UC_X86_REG_ECX)), rel(reg(UC_X86_REG_EDX)), args[0], args[1], args[2]))
        zf = state['svc'].pop(0)[0] if state['svc'] else 1
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + FOUND); setzf(zf); o.ret()
    def svc10d():
        v = state['r10d'].pop(0) if state['r10d'] else 0
        log.append(b'R' + bytes([v])); o.cpu.reg_write(UC_X86_REG_EAX, (reg(UC_X86_REG_EAX) & 0xffffff00) | v); o.ret()
    def jump(target):
        def f():
            log.append(b'J' + struct.pack('<III', target, reg(UC_X86_REG_EAX), rel(reg(UC_X86_REG_EBX)))); o.cpu.emu_stop()
        return f
    def sound(): log.append(b'Z' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.ret()
    o.stubs = {slots[s]: service(s) for s in (0xa5, 0xa9, 0xad)}; o.stubs[slots[0x35]] = sound; o.stubs[slots[0xa1]] = service(0xa1); o.stubs[slots[0x119]] = service(0x119)
    for sl in (0x25, 0x29, 0x39, 0x69): o.stubs[slots[sl]] = service(sl)
    o.stubs[slots[0x19]] = svc19; o.stubs[slots[0x10d]] = svc10d
    for t in (0x6fe, 0x735, 0xa44): o.stubs[0x800000 - o.CODE + t] = jump(t)
    o.stubs[slots[0x71]] = svc71
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'unit'
        module, a, e = ENTRIES[case['entry']]
        o.cpu.mem_write(o.WRK, wrks[module]); o.cpu.ctl_flush_tb(); o.cpu.mem_write(o.POOL, bytes(0x2000))
        state['svc'] = [list(x) for x in case['svc']]; state['r10d'] = list(case['r10d'])
        o.cpu.mem_write(o.FS, struct.pack('<I', case['dim0'])); o.cpu.mem_write(o.FS + 4, struct.pack('<I', case['dim4'])); o.cpu.mem_write(0x19eb, bytes([case['f6'] if False else 0x31]))
        o.cpu.mem_write(o.FS + 8, struct.pack('<I', o.POOL)); o.cpu.mem_write(o.FS + 0xc, struct.pack('<I', o.POOL + STATE)); o.cpu.mem_write(o.POOL + STATE + 8, bytes([case['wind']]))
        r = bytearray(0x50); r[0] = case['f0']; r[2] = case['f2']; struct.pack_into('<H', r, 3, case['f3']); r[5] = case['f5']; r[6] = case['f6']; struct.pack_into('<I', r, 7, case['x']); struct.pack_into('<I', r, 0xc, case['y']); r[0x16] = case['f16']; r[0x17] = case['f17']; struct.pack_into('<I', r, 0x18, case['f18']); struct.pack_into('<I', r, 0x1c, case['f1c'])
        struct.pack_into('<I', r, 0x12, case['f12'] if case['f12'] != 0xb00 else FOUND)
        o.cpu.mem_write(o.POOL + 0x800, bytes(r))
        p = bytearray(0x40); struct.pack_into('<H', p, 1, case['p1']); p[3] = case['p3']; struct.pack_into('<H', p, 0x17, case['p17']); p[0x25] = case['p25']; o.cpu.mem_write(o.POOL + PARENT, bytes(p))
        f = bytearray(0x50); struct.pack_into('<H', f, 1, case['o1']); struct.pack_into('<H', f, 0x17, case['o17']); f[0x19] = case['o19']; o.cpu.mem_write(o.POOL + FOUND, bytes(f))
        t = bytearray(0x60); t[0x45] = case['t45']; struct.pack_into('<I', t, 0x40, case['t40']); struct.pack_into('<H', t, 0x48, case['t48']); t[0x4a] = case['t4a']; t[0x4b] = case['t4b']; o.cpu.mem_write(o.POOL + TEMPLATE, bytes(t))
        for q in (UC_X86_REG_ESI, UC_X86_REG_EDI): o.cpu.reg_write(q, o.POOL + 0x800)
        for q in (UC_X86_REG_ECX, UC_X86_REG_EDX): o.cpu.reg_write(q, 0)
        o.cpu.reg_write(UC_X86_REG_EAX, case['ah'] << 8); o.cpu.reg_write(UC_X86_REG_EBX, (case['bl7'] if case['entry'] in (31, 32, 33) else o.POOL + PARENT) if case['ah'] != 1 else 0)
        o.run(o.WRK + a)
        out = bytes(o.cpu.mem_read(o.POOL + 0x800, 0x50)) + bytes(o.cpu.mem_read(o.POOL + FOUND, 0x50)) + b''.join(log)
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
