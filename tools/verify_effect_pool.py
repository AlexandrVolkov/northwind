#!/usr/bin/env python3
"""Verify the effect pool of LE:1: 9727 (FS:19, create an effect: the first free record of 22h bytes in the pool [6B2], the fields, the script of the type with AH = 0 and the three stack arguments, the
high-water mark [19E0]) and 96CD (update every live effect with AH = 1) with Unicorn. The scripts are a recorded stub."""
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

BASE, TT, STUB = 0x4000, 0x1000, 0x30
RECORDS = 0x7fde // 0x22 + 2


class PoolOracle(CombatOracle):
    RANGES = {'pool': ([(0x96cd, 0x97c6)], [])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=300000)


def cases():
    rng = random.Random(0x9727)
    out = []
    for n in range(500):
        fn = 'create' if n % 3 else 'loop'
        full = fn == 'create' and rng.random() < 0.08
        used = RECORDS if full else rng.randrange(0, 40)
        live = [rng.choice([0, 1, 2, 3, 0xff, 0xff]) for _ in range(rng.randrange(0, 30))]
        out.append({'fn': fn, 'used': used, 'live': live, 'armies': [rng.randrange(3) for _ in range(30)], 'ax': (rng.randrange(1 << 32) & ~0xff) | rng.randrange(3), 'bx': (rng.randrange(1 << 16) & ~0xff) | rng.randrange(4) | rng.choice([0, 0x80]), 'cx': rng.randrange(1 << 12), 'dx': rng.randrange(1 << 12),
                    'l1': rng.choice([0x100, 0x1000, 0x10000]), 'l2': rng.choice([0x100, 0x1000, 0x10000]), 'hw': rng.randrange(RECORDS), 'args': [rng.randrange(1 << 30) for _ in range(3)], 'bytes': rng.randrange(1 << 32), 'esi': rng.randrange(1 << 20)})
    return out


def prng(seed, n):
    x = seed & 0xffffffff; out = bytearray()
    for _ in range(n):
        x = (x * 1664525 + 1013904223) & 0xffffffff; out.append(x >> 24)
    return bytes(out)


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = PoolOracle(code, report['fs_assignments'], constants)
    log = []
    def reg(r): return o.cpu.reg_read(r)
    def rel(v): return v - o.POOL if o.POOL <= v < o.POOL + 0x20000 else v
    def script():
        log.append(b'S' + struct.pack('<IIIIII', reg(UC_X86_REG_EAX), reg(UC_X86_REG_EBX), reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX), rel(reg(UC_X86_REG_EDI)), rel(reg(UC_X86_REG_ESI)))); o.ret()
    o.stubs = {STUB: script}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'pool'
        o.cpu.mem_write(o.POOL, bytes(0x20000)); o.cpu.mem_write(0x6b2, struct.pack('<I', o.POOL + BASE))
        pool = bytearray(b'\xff' * (RECORDS * 0x22 + 0x40))
        filler = prng(case['bytes'], 0x22)
        if case['fn'] == 'create':
            for i in range(case['used']): pool[i * 0x22:(i + 1) * 0x22] = filler[:1].replace(b'\xff', b'\x00') + filler[1:]
        else:
            for i, t in enumerate(case['live']):
                rec = bytearray(filler); rec[0] = t; rec[1] = case['armies'][i % 30]; pool[i * 0x22:(i + 1) * 0x22] = rec
        o.cpu.mem_write(o.POOL + BASE, bytes(pool))
        for a in range(3):
            tb = bytearray(0x56); struct.pack_into('<I', tb, 9, 0); struct.pack_into('<I', tb, 0xd, o.POOL + TT + 0x100 * a); o.cpu.mem_write(0x6ca + 0x56 * a, bytes(tb))
            tt = bytearray(0x100)
            for t in range(4): struct.pack_into('<I', tt, 7 * t + 2, o.CODE + STUB)
            o.cpu.mem_write(o.POOL + TT + 0x100 * a, bytes(tt))
        hw = o.POOL + BASE + case['hw'] * 0x22
        if case['fn'] == 'loop': hw = o.POOL + BASE + (len(case['live'])) * 0x22
        o.cpu.mem_write(0x19e0, struct.pack('<I', hw)); o.cpu.mem_write(0x19c3, bytes(4)); o.cpu.mem_write(0x1c4e, bytes([0 if (case['fn'] == 'loop' and case['esi'] & 7 == 0) else 1]))
        o.cpu.mem_write(0x18e6, struct.pack('<II', case['l1'], case['l2']))
        o.cpu.mem_write(o.STACK + 0xf04, struct.pack('<III', *case['args']))
        for q in (UC_X86_REG_ESI, UC_X86_REG_EDI): o.cpu.reg_write(q, 0x1234 + case['esi'])
        o.cpu.reg_write(UC_X86_REG_EAX, case['ax']); o.cpu.reg_write(UC_X86_REG_EBX, case['bx']); o.cpu.reg_write(UC_X86_REG_ECX, case['cx']); o.cpu.reg_write(UC_X86_REG_EDX, case['dx'])
        o.run(o.CODE + (0x9727 if case['fn'] == 'create' else 0x96cd))
        fl = reg(UC_X86_REG_EFLAGS)
        out = (bytes(o.cpu.mem_read(o.POOL + BASE, 66 * 0x22)) + bytes(o.cpu.mem_read(0x19e0, 4)) + bytes(o.cpu.mem_read(0x19c3, 4)) + struct.pack('<IIIII', *[rel(reg(q)) & 0xffffffff for q in (UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_EDI)]) + bytes([(fl >> 6) & 1, fl & 1]) + b''.join(log))
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'pool': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
