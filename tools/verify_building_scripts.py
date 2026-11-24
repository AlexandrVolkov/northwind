#!/usr/bin/env python3
"""Verify the building scripts of ARMY__01.WRK (A 0FE, B 24D, C 354, D 3ED: init AH=0, step AH=1, voice AH=2, visitor test AH=5, table AH=6) with Unicorn.
FS:41 (C88C) is emulated as far as its front half: the state +14 and 7 picks the handler from the inline table, BL = +14 and 40h, BH = +19 for the state 0, EDX = the handler; the handler runs as
original code, its flags are logged, then the script goes on after the table with the flags of the case. The services the scripts call (FS:85, 121, 197, 3D, 51, 49, 129, 99, 12D, CD, 19, 1D)
are recorded stubs; FS:6D (the participant record in ECX, the template in EDI) and FS:71 (the template of an object in EDX) are stubs with fixed buffers."""
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

ENTRIES = [('ARMY__01.WRK', 0xfe, 0x24d), ('ARMY__01.WRK', 0x24d, 0x354), ('ARMY__01.WRK', 0x354, 0x3ed), ('ARMY__01.WRK', 0x3ed, 0x4f0), ('ARMY__01.WRK', 0x6d4, 0x7a0), ('ARMY__01.WRK', 0x7a0, 0x896), ('ARMY__01.WRK', 0x896, 0x986), ('ARMY__01.WRK', 0x986, 0xa77), ('ARMY__01.WRK', 0xce3, 0xdef), ('ARMY__02.WRK', 0xfe, 0x263), ('ARMY__02.WRK', 0x263, 0x354), ('ARMY__02.WRK', 0x354, 0x454), ('ARMY__02.WRK', 0x756, 0x7f5), ('ARMY__02.WRK', 0x524, 0x6a1), ('ARMY__02.WRK', 0xcb9, 0xd85), ('ARMY__02.WRK', 0xd85, 0xe7b), ('ARMY__02.WRK', 0xe7b, 0xf86), ('ARMY__02.WRK', 0x122d, 0x131e), ('ARMY__02.WRK', 0x131e, 0x137a), ('ARMY__02.WRK', 0x137a, 0x13d6), ('ARMY__02.WRK', 0xf86, 0x1078), ('ARMY__02.WRK', 0x1462, 0x154e), ('ARMY__02.WRK', 0x1078, 0x10f1), ('ARMY__02.WRK', 0x154e, 0x161d), ('ARMY__02.WRK', 0x161d, 0x16dd), ('ARMY__01.WRK', 0x102d, 0x10ed), ('ARMY__01.WRK', 0xdef, 0xf64), ('ARMY__01.WRK', 0xf64, 0xfd1), ('ARMY__01.WRK', 0xfd1, 0x102d)]
TABLES = [0x179, 0x2a1, 0x381, 0x441, 0x71e, 0, 0x8e6, 0x9d4, 0xd37, 0, 0, 0, 0, 0, 0, 0, 0xf54, 0x130c, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]      # the inline table after the call of FS:41 in the step of each building
SLOTS = [0x85, 0x121, 0x197, 0x3d, 0x51, 0x49, 0x129, 0x99, 0x12d, 0xcd, 0x19, 0x1d, 0xbd, 0x9d, 0x8d, 0x91, 0x95, 0xc1, 0x15, 0x19f, 0xd5, 0xa1, 0x147, 0x187, 0x45, 0x19b, 0x75, 0x59, 0x5d, 0xd9, 0x65]
KEEP = {0x19, 0x1d}                        # services that leave the registers alone
TMPL, PART, OTHER, TFLAGS, FOUND = 0xc00, 0xd00, 0x900, 0xb00, 0xe00


class BuildingOracle(CombatOracle):
    RANGES = {'unit': ([(0x9e41, 0x9e69)], [(0, 0x4000)])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=4000)


def cases():
    rng = random.Random(0x51)
    out = []
    for n in range(1500):
        k = rng.randrange(len(ENTRIES))
        ahs = {0: [0, 1, 1, 1, 1, 2, 5, 6], 1: [0, 1, 1, 1, 1, 2, 6], 2: [0, 1, 1, 1, 1], 3: [0, 1, 1, 1, 1, 2, 6], 4: [0, 1, 1, 1, 1, 5, 5], 5: [0, 1, 1, 1, 1, 1], 6: [0, 1, 1, 1, 1, 5], 7: [0, 1, 1, 1, 1, 2, 6], 8: [0, 1, 1, 1, 1, 2, 6], 9: [0, 1, 1, 1, 1, 2, 5, 6], 10: [0, 1, 1, 1, 1, 2, 6], 11: [0, 1, 1, 1, 1, 5], 12: [0, 1, 1, 1, 1], 13: [0, 1, 1, 1, 1, 2, 4, 5], 14: [0, 1, 1, 1, 1, 5, 5], 15: [0, 1, 1, 1, 1, 1], 16: [0, 1, 1, 1, 1, 2, 6], 17: [0, 1, 1, 1, 1, 2, 6], 18: [0, 1, 1, 1, 1], 19: [0, 1, 1, 1, 1], 20: [0, 1, 1, 1, 1, 2, 5], 21: [0, 1, 1, 1, 1, 2, 5], 22: [0, 1, 1, 1, 1, 1, 4], 23: [0, 1, 1, 1, 1, 2], 24: [0, 1, 1, 1], 25: [0, 1, 1, 1], 26: [0, 1, 1, 1, 1, 1], 27: [0, 1, 1, 1, 1], 28: [0, 1, 1, 1, 1]}[k]
        out.append({'entry': k, 'ah': rng.choice(ahs), 'bl': rng.randrange(256), 'f14': rng.choice([0x80, 0x81, 0x40, 0x41, 0x42, 0, 1, 2, 3, 4, 5, 0x43, 0xc4, 0xc5, 0xc2]) if rng.random() < 0.9 else (rng.randrange(256) & ~7) | rng.randrange(6),
                    'f0f': rng.choice([0, 0x10, 0x20, rng.randrange(256)]), 'f22': rng.choice([0xff, 0xff, 0, rng.randrange(256)]), 'f26': rng.randrange(256), 'f27': rng.randrange(256), 'f17': rng.choice([0, 0x100, 0x180, 4, 4, 0x104, rng.randrange(1 << 16)]),
                    'f19': rng.choice([0, 1, 0x10, 0x80, 0xff, rng.randrange(256)]), 'f1a': rng.choice([0, 1, 0x10, 0x7f, 0x80, 0xff, rng.randrange(256)]), 'f3': rng.randrange(8), 'f0': rng.randrange(256),
                    'x': rng.randrange(1 << 16), 'y': rng.randrange(1 << 16), 't45': rng.choice([0, 0x10, 0x20, 0xff, rng.randrange(256)]), 't46': rng.choice([0, 0x10, 0x20, 0x7f, 0x80, rng.randrange(256)]), 't78': rng.choice([0, 5, 100, rng.randrange(1 << 32)]),
                    'p2c': rng.choice([0, 3, 100, 0xffffffff, rng.randrange(1 << 32)]), 'o3': rng.choice([0, 1, 2, 3, rng.randrange(8)]), 'tflags': rng.choice([0, 0x20000, 0x20000, rng.randrange(1 << 32)]),
                    'svc': [[rng.choice([0, 1, 1]), rng.randrange(1 << 32) if rng.random() < 0.4 else rng.randrange(256), rng.choice([0, 1, 0x40, rng.randrange(1 << 16)])] for _ in range(8)], 'zf': rng.choice([0, 1, 1]), 'seed': rng.randrange(256), 'f24': rng.choice([0, 1, 2, rng.randrange(256)]), 'f23': rng.choice([0, 1, 2, rng.randrange(256)]), 't79': rng.choice([0, 1, 5, rng.randrange(256)]), 'fo31': rng.choice([0, 3, 3, rng.randrange(256)]), 'fo17': rng.choice([0, 0x40, 0xffff, rng.randrange(1 << 16)]), 'ftf': rng.choice([0, 0x20000, 0x20000, rng.randrange(1 << 32)]), 'p1c': rng.randrange(1 << 32), 'e1a10': rng.randrange(1 << 32), 'f31': rng.choice([0xff, 0xff, 0, rng.randrange(256)]), 'f15': rng.choice([4, 4, 0, rng.randrange(256)]), 'f48': rng.randrange(1 << 32), 'pb': [rng.choice([5, 0xa, 0xb, 0x10, 0, rng.randrange(256)]) for _ in range(8)], 'f2e': rng.choice([0, 0x20, rng.randrange(256)]), 'oraw': bytes(rng.randrange(256) for _ in range(0x40)).hex(), 'f6': rng.randrange(256), 'f18b': rng.choice([1, 1, 0, 2, rng.randrange(256)]), 'f0e': rng.randrange(256), 'f1b': rng.choice([0, 0, 1, rng.randrange(256)]), 'f32': rng.choice([0, 1, rng.randrange(256)]), 'tick': rng.choice([0, 0, 1, 4, rng.randrange(256)]), 'count': rng.randrange(1, 6)})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = BuildingOracle(code, report['fs_assignments'], constants)
    slots = {x['fs_slot']: x['target_offset'] for x in report['fs_assignments']}
    wrks = {m: (game / m).read_bytes() for m in {e[0] for e in ENTRIES}}
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def rel(v):
        if o.WRK <= v < o.WRK + 0x8000: return v - o.WRK
        if o.POOL <= v < o.POOL + 0x2000: return v - o.POOL
        return v
    def setzf(z): o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if z else 0))
    def service(slot):
        def f():
            log.append(b'S' + struct.pack('<HIIII', slot, reg(UC_X86_REG_EAX), reg(UC_X86_REG_EBX), rel(reg(UC_X86_REG_ECX)), rel(reg(UC_X86_REG_EDX))))
            if slot == 0xa1: o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + TFLAGS)
            if slot not in KEEP or (slot == 0x1d and state['entry'] == 22):
                zf, eax, ebx = state['svc'].pop(0) if state['svc'] else (1, 0, 0)
                o.cpu.reg_write(UC_X86_REG_EAX, eax & 0xffffffff); o.cpu.reg_write(UC_X86_REG_EBX, ebx); setzf(zf)
                if slot == 0x187: o.cpu.reg_write(UC_X86_REG_ECX, (eax ^ 0x5a5a5a5a) & 0xffffffff); o.cpu.reg_write(UC_X86_REG_EDX, (ebx ^ 0xa5a5a5a5) & 0xffffffff)
                if slot == 0x45: o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0xf00)
                if slot == 0x75: o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x1040)
            o.ret()
        return f
    def svc6d():
        log.append(b'P'); o.cpu.reg_write(UC_X86_REG_ECX, o.POOL + PART); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + TMPL); o.ret()
    def svc79():
        log.append(b'N'); zf = state['svc'].pop(0)[0] if state['svc'] else 1
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + FOUND); o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + TFLAGS + 0x100); setzf(zf); o.ret()
    def note(letter, with_eax=False):
        def f():
            log.append(letter + (struct.pack('<I', reg(UC_X86_REG_EAX) & 255) if with_eax else b'')); o.ret()
        return f
    def actions(): log.append(b'U'); o.cpu.emu_stop()
    def svcc5():
        log.append(b'5' + bytes(o.cpu.mem_read(reg(UC_X86_REG_EDI) + 0x48, 4))); o.ret()
    def svc1b7():
        log.append(b'Q' + struct.pack('<B', reg(UC_X86_REG_EAX) & 255)); o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + 0x400); o.ret()
    def svc71():
        log.append(b'T'); o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + TFLAGS); o.ret()
    def dispatch():
        esp = reg(UC_X86_REG_ESP); ret = struct.unpack('<I', bytes(o.cpu.mem_read(esp, 4)))[0]
        esi = reg(UC_X86_REG_ESI); f14 = bytes(o.cpu.mem_read(esi + 0x14, 1))[0]
        st = f14 & 7; bl = f14 & 0x40; bh = 0
        if st == 0:
            f = bytes(o.cpu.mem_read(esi + 0x0f, 1))[0] | 2; bh = bytes(o.cpu.mem_read(esi + 0x19, 1))[0]
            if bh == bytes(o.cpu.mem_read(o.POOL + TMPL + 0x45, 1))[0]: f &= 0xfd
            o.cpu.mem_write(esi + 0x0f, bytes([f]))
        eb = (reg(UC_X86_REG_EBX) & 0xffff0000) | (bh << 8) | bl
        handler = ret + struct.unpack('<i', bytes(o.cpu.mem_read(ret + 4 * st, 4)))[0]
        o.cpu.mem_write(esp, struct.pack('<I', ret + 0x18)); o.cpu.mem_write(esp - 4, struct.pack('<I', o.CODE + 0x20))
        o.cpu.reg_write(UC_X86_REG_ESP, esp - 4); o.cpu.reg_write(UC_X86_REG_EAX, st); o.cpu.reg_write(UC_X86_REG_EBX, eb); o.cpu.reg_write(UC_X86_REG_EDX, handler)
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + TMPL); o.cpu.reg_write(UC_X86_REG_EIP, handler)
        log.append(b'C' + struct.pack('<BIII', st, eb, rel(handler), rel(reg(UC_X86_REG_ECX))))
    def handler_return():
        log.append(b'H' + struct.pack('<B', o.zf())); setzf(state['zf']); o.ret()
    o.stubs = {slots[s]: service(s) for s in SLOTS}
    o.stubs[slots[0x6d]] = svc6d; o.stubs[slots[0x71]] = svc71; o.stubs[slots[0x79]] = svc79; o.stubs[slots[0x1b7]] = svc1b7; o.stubs[slots[0xc5]] = svcc5; o.stubs[slots[0x4d]] = note(b'N', True); o.stubs[slots[0xd1]] = note(b'Y'); o.stubs[slots[0xdd]] = note(b'V'); o.stubs[slots[0x115]] = note(b'I'); o.stubs[slots[0xe1]] = actions; o.stubs[slots[0x41]] = dispatch; o.stubs[0x20] = handler_return
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'unit'
        module, a, e = ENTRIES[case['entry']]
        o.cpu.mem_write(o.WRK, wrks[module]); o.cpu.ctl_flush_tb(); o.cpu.mem_write(o.POOL, bytes(0x2000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x19eb, bytes([case['seed']]))
        state['svc'] = [list(x) for x in case['svc']]; state['zf'] = case['zf']; state['entry'] = case['entry']
        r = bytearray(0x50); r[0] = case['f0']; r[3] = case['f3']; r[0x14] = case['f14']; r[0x0f] = case['f0f']; r[0x22] = case['f22']; r[0x26] = case['f26']; r[0x27] = case['f27']; struct.pack_into('<H', r, 0x17, case['f17']); r[0x18] = case['f18b']; r[0x19] = case['f19']; r[0x1a] = case['f1a']; r[0x23] = case['f23']; r[0x24] = case['f24']; r[0x31] = case['f31']; r[0x2e] = case['f2e']; r[6] = case['f6']; r[0x15] = case['f15']; r[0x0e] = case['f0e']; r[0x1b] = case['f1b']; r[0x32] = case['f32']; struct.pack_into('<I', r, 0x48, case['f48'])
        struct.pack_into('<II', r, 4, case['x'], case['y'])
        o.cpu.mem_write(o.POOL + 0x800, bytes(r)); o.cpu.mem_write(o.FS + 0x10, bytes([case['tick']])); o.cpu.mem_write(o.FS + 0x142, bytes([case['count']])); o.cpu.mem_write(o.FS + 8, struct.pack('<I', o.POOL))
        t = bytearray(0x100); t[0x79] = case['t79']; t[0x45] = case['t45']; t[0x46] = case['t46']; struct.pack_into('<I', t, 0x78, case['t78']); o.cpu.mem_write(o.POOL + TMPL, bytes(t))
        p = bytearray(0xf5)
        for q in range(8): p[0x34 + 14 * q] = case['pb'][q]
        struct.pack_into('<I', p, 0x2c, case['p2c']); struct.pack_into('<I', p, 0x1c, case['p1c']); o.cpu.mem_write(o.POOL + PART, bytes(p))
        ob = bytearray(bytes.fromhex(case['oraw']) + bytes(0x10)); ob[3] = case['o3']; o.cpu.mem_write(o.POOL + OTHER, bytes(ob)); o.cpu.mem_write(o.POOL + 0x400 + 0x1a10, struct.pack('<I', case['e1a10'])); o.cpu.mem_write(o.POOL + TFLAGS + 0x40, struct.pack('<I', case['tflags'])); o.cpu.mem_write(o.POOL + TFLAGS + 0x140, struct.pack('<I', case['ftf'])); fo = bytearray(0x50); fo[0x31] = case['fo31']; struct.pack_into('<H', fo, 0x17, case['fo17']); o.cpu.mem_write(o.POOL + FOUND, bytes(fo))
        for q in (UC_X86_REG_ESI, UC_X86_REG_EDI): o.cpu.reg_write(q, o.POOL + 0x800)
        for q in (UC_X86_REG_ECX, UC_X86_REG_EDX): o.cpu.reg_write(q, 0)
        if case['ah'] == 5: o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + OTHER)
        ebx0 = o.POOL + OTHER if case['entry'] in (24, 25) and case['ah'] != 1 else case['bl']
        o.cpu.reg_write(UC_X86_REG_EAX, case['ah'] << 8); o.cpu.reg_write(UC_X86_REG_EBX, ebx0)
        o.run(o.WRK + a)
        out = bytes(o.cpu.mem_read(o.POOL + 0x800, 0x50)) + bytes(o.cpu.mem_read(o.POOL + PART + 0x28, 8)) + bytes(o.cpu.mem_read(0x19eb, 1)) + (struct.pack('<I', (reg(UC_X86_REG_EDX) - o.WRK) & 0xffffffff) if case['ah'] == 6 else b'') + (bytes([reg(UC_X86_REG_EAX) & 255, o.zf()]) if case['ah'] == 5 else b'') + (bytes([o.zf()]) if case['ah'] == 4 else b'') + bytes(o.cpu.mem_read(o.POOL + FOUND, 0x50)) + bytes(o.cpu.mem_read(o.POOL + PART + 0x18, 8)) + bytes(o.cpu.mem_read(o.POOL + 0x400 + 0x1a10, 4)) + bytes(o.cpu.mem_read(o.POOL + 0xf00, 0x50)) + b''.join(log)
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
