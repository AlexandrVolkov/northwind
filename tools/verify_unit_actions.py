#!/usr/bin/env python3
"""Verify the unit action handlers of LE:1 (8227..9210: the E1 dispatcher 8227, the action handlers 8273, 82C3, 833F, 839F, 83D9, 843E, 84AA ..., the helpers 8E3C, 8E56, 8E81, 8EC7, 8F70, ...)
with Unicorn, one function at a time. The callees that are not part of the checked function are recorded stubs: their registers are logged and their results (EAX, EBX, ECX, EDX, EDI, ZF) come from the
case. Template lookups (68BD) give EDX = the template buffer. Output: the record, the object and the globals after the run, the registers and the flags (ZF, CF, SF) and the log of the calls."""
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

# name: (entry, end, the callee entries that are stubbed)
FUNCS = {
    'f8e3c': (0x8e3c, 0x8e56, []),
    'f8e56': (0x8e56, 0x8e81, [0x68bd]),
    'f8e81': (0x8e81, 0x8ec7, [0x8d89, 0x8abf, 0x8af4, 0xd895, 0x8bea, 0x8b5e, 0x8c7c]),
    'f8ec7': (0x8ec7, 0x8f70, [0x8d89, 0x8b5e, 0x8cbb, 0x8b37, 0x8af4, 0x8bea, 0x8c7c, 0x8abf]),
    'f8f70': (0x8f70, 0x902f, [0x8a7f, 0x8d89, 0x8c98, 0x8db0, 0x8ca1, 0x9b86, 0x8bea]),
    'f8227': (0x8227, 0x8270, [0x30, 0x40, 0x50, 0x60, 0x70, 0x80]),
    'f8273': (0x8273, 0x82c3, [0x8d89, 0x8ca1, 0x8c98, 0x8e81]),
    'f82c3': (0x82c3, 0x833f, [0x8d89, 0x8af4, 0x8b37, 0x8d3e, 0x8e3c, 0x902f, 0x8b4c, 0x8b0f, 0x8ec7, 0x8c98]),
    'f833f': (0x833f, 0x839f, [0x82c3, 0x8d89, 0x219c3, 0xd7b1, 0xda45]),
    'f839f': (0x839f, 0x83d9, [0x8d89, 0x8e56, 0x8ec7]),
    'f83d9': (0x83d9, 0x843e, [0x839f, 0x8d89, 0x219c3, 0xd7b1, 0xda45, 0x8c98]),
    'f843e': (0x843e, 0x84aa, [0x8abf, 0x8d89, 0x8cf3, 0x8e56, 0x8a7f, 0x8ec7, 0x8f70]),
    'f877f': (0x877f, 0x878d, [0xc0f6]),
    'f878d': (0x878d, 0x881b, [0x8d89, 0x8c98, 0x8d0b]),
    'f881b': (0x881b, 0x8917, [0x8d89, 0x8c98, 0x8d0b, 0x43b0, 0x219c3, 0x9e41, 0x9e55, 0x10ad2]),
    'f8917': (0x8917, 0x89f1, [0x9064, 0xa758, 0x8c98, 0xc4cd, 0xcaee, 0xd1ba]),
    'f89f1': (0x89f1, 0x8a54, [0x1eca0, 0x22e46, 0xbe2e]),
    'f8a54': (0x8a54, 0x8a7f, [0x12588, 0x4473]),
    'f8a7f': (0x8a7f, 0x8a9f, [0xd182]),
    'f8a9f': (0x8a9f, 0x8abf, [0x68bd]),
    'f8abf': (0x8abf, 0x8af4, [0x68bd]),
    'f8af4': (0x8af4, 0x8b0f, []),
    'f8b0f': (0x8b0f, 0x8b37, [0xa38a, 0x8120]),
    'f8b37': (0x8b37, 0x8b4c, [0x8b0f]),
    'f8b4c': (0x8b4c, 0x8b5e, [0xb5ab]),
}
POOLREC, OBJ1, OBJ2, TMPL = 0x800, 0x900, 0xa00, 0xc00


class ActionOracle(CombatOracle):
    RANGES = {}

    RET = None

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP if self.RET is None else self.RET))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=4000)


def prng(seed, n):
    """The same linear congruential generator is used by tests/OriginalUnitActionChecks.cs."""
    x = seed & 0xffffffff; out = bytearray()
    for _ in range(n):
        x = (x * 1664525 + 1013904223) & 0xffffffff; out.append(x >> 24)
    return bytes(out)


def cases(names):
    rng = random.Random(0x8e3c)
    out = []
    for n in range(2500):
        out.append({'fn': rng.choice(names), 'r': rng.randrange(1 << 32), 'o1': rng.randrange(1 << 32), 'o2': rng.randrange(1 << 32), 'tm': rng.randrange(1 << 32), 'f1c': rng.choice([0, 0x900, 0xa00]), 'f14': rng.choice([0, 1, 2, 3, 4, 5, 0x40, 0x41, 0x4a, 0x2d, (rng.randrange(256) & ~7) | rng.randrange(6)]), 'f19': rng.choice([0, 1, 5, 0x80, 0xff, rng.randrange(256)]), 'pt': rng.randrange(1 << 32), 'f3': rng.randrange(8), 'f15': rng.choice([0, 1, 2, 3, 0x10, 0x11, rng.randrange(256)]), 'f31': rng.choice([0, 1, rng.randrange(256)]),
                    'f30': rng.choice([0, 0x10, 0x91, 0xf3, rng.randrange(256)]), 'f17': rng.choice([0, 2, 0x40, 0x42, rng.randrange(1 << 16)]), 'g1c9f': rng.choice([0, 0x40, 0x80, rng.randrange(256)]), 'g512': rng.choice([0, 0, 1, rng.randrange(1 << 20)]),
                    'g51a': rng.randrange(1 << 20), 'g51e': rng.choice([0, 0x400900]), 'g522': rng.choice([0, 3, rng.randrange(256)]),
                    'regs': [rng.choice([0, 1, 2, 3, rng.randrange(1 << 32)]) for _ in range(5)],
                    'calls': [[rng.choice([0, 1, 1]), rng.choice([0, 1, 2, 3, rng.randrange(256)]) | (rng.choice([0, 1, 2, 3, 4]) << 8), rng.randrange(1 << 20) if rng.random() < 0.5 else rng.choice([0, 1, 2]), rng.randrange(1 << 20) if rng.random() < 0.5 else rng.choice([0, 1, 2]), rng.randrange(1 << 20), rng.choice([0, 0x400900, 0x400a00])] for _ in range(8)]})
    return out


def generate(game, names):
    constants = source_constants(game); report, code = analyze(game)
    o = ActionOracle(code, report['fs_assignments'], constants)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def rel(v): return v - o.POOL if o.POOL <= v < o.POOL + 0x2000 else v
    def stub(addr, kind):
        def f():
            log.append(b'C' + struct.pack('<HIIIIII', addr & 0xffff, reg(UC_X86_REG_EAX), reg(UC_X86_REG_EBX), reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX), rel(reg(UC_X86_REG_EDI)), rel(reg(UC_X86_REG_ESI))))
            if addr == 0x68bd: o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + TMPL); o.ret(); return   # the template lookup keeps everything but EDX
            zf, ax, bx, cx, dx, di = state['calls'].pop(0) if state['calls'] else (1, 0, 0, 0, 0, 0)
            o.cpu.reg_write(UC_X86_REG_EAX, ax); o.cpu.reg_write(UC_X86_REG_EBX, bx); o.cpu.reg_write(UC_X86_REG_ECX, cx)
            o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + TMPL if addr == 0xc4cd else dx); o.cpu.reg_write(UC_X86_REG_EDI, di)
            o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if zf else 0)); o.ret()
        return f
    digest = hashlib.sha256(); all_cases = cases(names)
    for case in all_cases:
        log.clear(); state['calls'] = [list(c) for c in case['calls']]
        entry, end, callees = FUNCS[case['fn']]
        o.prepare(32, 32); o.mode = 'actions'
        o.RANGES['actions'] = ([(entry, end)], [])
        o.stubs = {a: stub(a, None) for a in callees}
        o.cpu.mem_write(o.POOL, bytes(0x2000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x1c49, struct.pack('<I', o.POOL + TMPL))
        r = bytearray(prng(case['r'], 0x50)); r[3] = case['f3']; r[0x14] = case['f14']; r[0x19] = case['f19']; r[0x15] = case['f15']; r[0x31] = case['f31']; r[0x30] = case['f30']; struct.pack_into('<H', r, 0x17, case['f17']); struct.pack_into('<I', r, 0x1c, case['f1c'])
        o.cpu.mem_write(o.POOL + POOLREC, bytes(r)); o.cpu.mem_write(o.POOL + OBJ1, prng(case['o1'], 0x50)); o.cpu.mem_write(o.POOL + OBJ2, prng(case['o2'], 0x50)); o.cpu.mem_write(o.POOL + TMPL, prng(case['tm'], 0x100))
        o.cpu.mem_write(0x97a + 0xf5 * r[3], prng(case['pt'], 0xf5)); o.cpu.mem_write(0x1ccb, bytes(0x16 * 256 + 4))
        o.cpu.mem_write(0x1c9f, bytes([case['g1c9f']])); o.cpu.mem_write(0x512, struct.pack('<I', case['g512'])); o.cpu.mem_write(0x51a, struct.pack('<I', case['g51a'])); o.cpu.mem_write(0x51e, struct.pack('<I', case['g51e'])); o.cpu.mem_write(0x522, bytes([case['g522']]))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + POOLREC)
        o.RET = None; o.cpu.mem_write(0x50e, bytes(4))
        if case['fn'] == 'f8227':
            table = o.STACK + 0x7e8; o.RET = table
            o.cpu.mem_write(table, b''.join(struct.pack('<i', (o.CODE + a) - table) for a in callees)); o.cpu.mem_write(0x50e, bytes(4))
        for q, v in zip((UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_EDI), case['regs']): o.cpu.reg_write(q, v)
        o.run(o.CODE + entry)
        fl = reg(UC_X86_REG_EFLAGS)
        out = (bytes(o.cpu.mem_read(o.POOL + POOLREC, 0x50)) + bytes(o.cpu.mem_read(o.POOL + OBJ1, 0x50)) + bytes(o.cpu.mem_read(o.POOL + OBJ2, 0x50)) + bytes(o.cpu.mem_read(0x1c9f, 1)) + bytes(o.cpu.mem_read(0x512, 4)) + bytes(o.cpu.mem_read(0x50e, 4)) + bytes(o.cpu.mem_read(0x1ccb + 0x16 * r[3], 1))
               + struct.pack('<IIIII', *[rel(reg(q)) for q in (UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_EDI)]) + bytes([(fl >> 6) & 1, fl & 1]) + b''.join(log))
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'actions': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--funcs', default=','.join(FUNCS))
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game, args.funcs.split(','))) + '\n', encoding='utf-8')
