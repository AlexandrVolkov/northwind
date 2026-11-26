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
    'f8bc7': (0x8bc7, 0x8bea, [0x9b86]),
    'f8bea': (0x8bea, 0x8c1e, [0x8c98, 0x960b]),
    'f8c47': (0x8c47, 0x8c7c, [0x8c98, 0x960b]),
    'f8c89': (0x8c89, 0x8c98, []),
    'f8cf3': (0x8cf3, 0x8d0b, [0x8bc7, 0x8cbb]),
    'f7fa7': (0x7fa7, 0x8120, [0x8120, 0x43b0, 0x73f5, 0x7c63, 0xbb26, 0x43b7, 0x802c]),
    'f902f': (0x902f, 0x9064, [0x9b86, 0x8c1e, 0x12406, 0x8c7c, 0x1243a]),
    'f81d9': (0x81d9, 0x8227, [0x813e, 0x6885, 0x43e5]),
    'fa10b': (0xa10b, 0xa1d1, []),
    'f9a8e': (0x9a8e, 0x9b0d, [0x4442, 0x43b7, 0x43b0, 0x8120]),
    'fda45': (0xda45, 0xda7f, []),
    'fd740': (0xd740, 0xd77a, [0x982b]),
    'fd77a': (0xd77a, 0xd7b1, [0x9e41, 0x9e55, 0x996f]),
    'f8659': (0x8659, 0x86b6, [0x8d89, 0xc439, 0x8f70]),
    'fbb8b': (0xbb8b, 0xbc15, [0x6885, 0xbadf]),
    'f6877': (0x6877, 0x68a0, []),
    'f6885': (0x6885, 0x68a0, []),
    'f68a0': (0x68a0, 0x68bd, []),
    'f68bd': (0x68bd, 0x68d8, []),
    'fc4cd': (0xc4cd, 0xc5d7, [0x68bd]),
    'fcaee': (0xcaee, 0xcb21, [0x43e5]),
    'fd12c': (0xd12c, 0xd157, []),
    'fd157': (0xd157, 0xd182, []),
    'fd182': (0xd182, 0xd19c, [0x30]),
    'fd19c': (0xd19c, 0xd1ba, [0x30]),
    'fc331': (0xc331, 0xc344, []),
    'fc344': (0xc344, 0xc35f, []),
    'fc35f': (0xc35f, 0xc439, []),
    'fc0f6': (0xc0f6, 0xc331, [0x8ca1, 0x9653, 0x43b0, 0xd363, 0xa758]),
    'fb9eb': (0xb9eb, 0xba2e, []),
    'fba2e': (0xba2e, 0xba60, []),
    'fbc15': (0xbc15, 0xbc2e, [0x43b0, 0xbb8e, 0x98db]),
    'fb5ab': (0xb5ab, 0xb5d8, [0x9e41]),
    'fb5d8': (0xb5d8, 0xb616, []),
    'fb616': (0xb616, 0xb6f1, []),
    'fbe2e': (0xbe2e, 0xbe6f, []),
    'fbe6f': (0xbe6f, 0xbe9d, [0x9e41, 0x9727]),
    'fcb21': (0xcb21, 0xcb77, [0xac63, 0xae3a, 0x43e5]),
    'fcb77': (0xcb77, 0xcc7b, [0xc4cd, 0xcaee, 0xcb21, 0xd1ba, 0x1eaed]),
    'fcc7b': (0xcc7b, 0xccc4, [0xc4cd, 0x1243a]),
    'fccc4': (0xccc4, 0xcccb, []),
    'fcccb': (0xcccb, 0xcd72, [0xa716, 0x9e41, 0xa6ae]),
    'fb319': (0xb319, 0xb394, [0xd1ba]),
    'fb6f1': (0xb6f1, 0xb765, [0xba2e, 0x6ec1]),
    'f6eb2': (0x6eb2, 0x6ec1, []),
    'f6ec1': (0x6ec1, 0x6f18, []),
    'fd2d1': (0xd2d1, 0xd363, []),
    'fs14b': (0x11dde, 0x11eb0, [0x21a00, 0xd7b1, 0xc344, 0xd157]),
    'fs187': (0x10ad2, 0x10b75, [0x9e41, 0x9e55, 0xac63]),
    'fs16b': (0x9064, 0x90e9, [0x9e41, 0xac63]),
    'fs18b': (0x11cc9, 0x11d2e, []),
    'f7a03': (0x7a03, 0x7a36, [0x6996]),
    'fae3a': (0xae3a, 0xae57, []),
    'fc439': (0xc439, 0xc4cd, []),
    'f4409': (0x4409, 0x4442, []),
    'fdb47': (0xdb47, 0xdbae, []),
    'fdbae': (0xdbae, 0xdc91, []),
    'fdc91': (0xdc91, 0xde0c, [0xa416, 0xd2d1, 0x43b0]),
    'fde0c': (0xde0c, 0xdec8, []),
    'f7012': (0x7012, 0x704f, []),
    'f704f': (0x704f, 0x7069, []),
    'f7069': (0x7069, 0x70b0, []),
    'f9bf9': (0x9bf9, 0x9c57, [0x9e7d, 0xc06e, 0xc0a4]),
    'f9c57': (0x9c57, 0x9d87, [0xdee4, 0xaab3, 0xc7ab, 0xfec6, 0x151e3, 0x9d87, 0x68d8, 0x10005, 0x10032]),
    'fa320': (0xa320, 0xa38a, []),
    'fa38a': (0xa38a, 0xa416, [0x43b0, 0x9e41, 0x7a36]),
    'fa416': (0xa416, 0xa444, [0x1416b]),
    'fa444': (0xa444, 0xa47e, [0x9e41, 0x1416b]),
    'fa47e': (0xa47e, 0xa4a9, [0x1416b]),
    'fa4a9': (0xa4a9, 0xa4c2, [0x1416b]),
    'f84aa': (0x84aa, 0x8659, [0x8d89, 0x8c98, 0x8ca1, 0xc331, 0x8c47, 0x8af4, 0x8c89, 0xcfac, 0x123d3, 0xd101, 0x8b0f, 0xc35f]),
}
POOLREC, OBJ1, OBJ2, TMPL = 0x800, 0x900, 0xa00, 0xc00
DS_WINDOWS = [(0x19c0, 0x40), (0x1c30, 0x50), (0x4160, 0x10), (0x9080, 0x10), (0x1978, 0x48), (0x1c80, 0x50), (0x1ec0, 0x10), (0x1960, 0x18), (0x9090, 0x10), (0x1904, 0x10), (0x33c, 0x100), (0x640, 0xc)]   # filled with pseudo-random bytes (the globals of the functions that read them)


class ActionOracle(CombatOracle):
    RANGES = {}

    RET = None

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP if self.RET is None else self.RET))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=200000)


def prng(seed, n):
    """The same linear congruential generator is used by tests/OriginalUnitActionChecks.cs."""
    x = seed & 0xffffffff; out = bytearray()
    for _ in range(n):
        x = (x * 1664525 + 1013904223) & 0xffffffff; out.append(x >> 24)
    return bytes(out)


def constrain(b):
    """The army (+0) of an object is 0..2 and its template (+0E) 0: the lookups 6885 / 68BD then stay inside the one template buffer."""
    b = bytearray(b); b[0] %= 3; b[0xe] = 0
    for k in (4, 8): b[k + 1] &= 3; b[k + 2] = 0; b[k + 3] = 0
    return b


def cases(names):
    rng = random.Random(0x8e3c)
    out = []
    for n in range(2500):
        out.append({'fn': rng.choice(names + ['fd2d1'] * 4 + ['fb319'] * 4 + ['fc0f6'] * 5 + ['fcb77'] * 4 + ['fcccb'] * 4 + ['fdb47'] * 5 + ['fdbae'] * 8 + ['fdc91'] * 14 + ['fde0c'] * 8 + ['f4409'] * 2 + ['fa320'] * 6 + ['fa38a'] * 8 + ['fa416'] * 4 + ['fa444'] * 8 + ['fa47e'] * 3 + ['fa4a9'] * 3 + ['f9bf9'] * 3 + ['f9c57'] * 14 + ['f7012'] * 6 + ['f704f'] * 4 + ['f7069'] * 8), 'r': rng.randrange(1 << 32), 'o1': rng.randrange(1 << 32), 'o2': rng.randrange(1 << 32), 'tm': rng.randrange(1 << 32), 'f1c': rng.choice([0, 0x900, 0xa00]), 'f14': rng.choice([0, 1, 2, 3, 4, 5, 0x40, 0x41, 0x4a, 0x2d, (rng.randrange(256) & ~7) | rng.randrange(6)]), 'f19': rng.choice([0, 1, 5, 0x80, 0xff, rng.randrange(256)]), 'pt': rng.randrange(1 << 32), 'f3': rng.randrange(8), 'nch': rng.randrange(0, 5), 'st': rng.randrange(0, 5), 'bm': rng.choice([8, 0x1000, 0x20000, 0xffffffff, 0x10, rng.randrange(1 << 32)]), 'sel': rng.randrange(4), 'g273': rng.randrange(1, 5), 'g274': rng.randrange(1, 5), 'ds': rng.randrange(1 << 32), 'tab': rng.randrange(1 << 32), 'fs13d': rng.randrange(256), 'px': rng.randrange(1024), 'py': rng.randrange(1024), 'cur': rng.choice([0xffffffff, rng.randrange(0x3ff), rng.randrange(0x3ff), 0]), 'g525': rng.randrange(256), 'g526': rng.randrange(256), 'g1c61': rng.choice([0, 1, 5, rng.randrange(256)]), 'g2730': rng.choice([0xffffffff, 0, rng.randrange(1 << 32)]), 'rt': rng.randrange(1 << 32), 'map': rng.randrange(1 << 32), 'f15': rng.choice([0, 1, 2, 3, 0x10, 0x11, rng.randrange(256)]), 'f31': rng.choice([0, 1, rng.randrange(256)]),
                    'f30': rng.choice([0, 0x10, 0x91, 0xf3, rng.randrange(256)]), 'f17': rng.choice([0, 2, 0x40, 0x42, rng.randrange(1 << 16)]), 'g1c9f': rng.choice([0, 0x40, 0x80, rng.randrange(256)]), 'g512': rng.choice([0, 0, 1, rng.randrange(1 << 20)]),
                    'g51a': rng.randrange(1 << 20), 'g51e': rng.choice([0, 0x400900]), 'g522': rng.choice([0, 3, rng.randrange(256)]),
                    'regs': [rng.choice([0, 1, 2, 3, rng.randrange(1 << 32)]) for _ in range(5)],
                    'calls': [[rng.choice([0, 1, 1]), rng.choice([0, 1, 2, 3, rng.randrange(256)]) | (rng.choice([0, 1, 2, 3, 4]) << 8), rng.randrange(1 << 20) if rng.random() < 0.4 else rng.choice([0, 1, 2, 3, 0xff]), rng.randrange(1 << 20) if rng.random() < 0.5 else rng.choice([0, 1, 2]), rng.randrange(1 << 20), rng.choice([0, 0x400900, 0x400a00])] for _ in range(8)]})
    for n, c in enumerate(out):
        if c['fn'] == 'fcccb' and n % 2 == 0: c['z19'] = 1; c['f15'] = 1; c['f22'] = (n // 2) % 5; c['f23'] = (n // 3) % 4; c['f24'] = (n // 5) % 40
        if c['fn'] == 'fs187': c['regs'][2] = n * 37 % 0x400; c['regs'][3] = n * 53 % 0x400
        if c['fn'] in ('fdb47', 'fdbae', 'fdc91', 'f4409'): c['px'] = 160 + n * 7 % 340; c['py'] = 160 + n * 13 % 340
        if c['fn'] == 'fdc91':
            c['f22'] = [0, 1, 3, 0xfc, 0x63, 0x64, 0xff, 4, 0x20, 0x2f][n % 10]; c['f15'] = [0, 0, 1, 1, 1, 2, 0x12, 0x22, 0x32, 3, 0x13, 0x41][(n // 10) % 12]; c['sel'] = (n // 120) % 4
        if c['fn'] == 'fde0c':
            r2, r3 = c['regs'][2], c['regs'][3]; c['regs'][1] = 1 + (n % 2) + ((1 + (n // 2) % 2) << 8); c['regs'][2] = 32 * (3 + n % 12) + 16; c['regs'][3] = 32 * (3 + (n // 3) % 12) + 16; c['sel'] = (n // 7) % 4
        if c['fn'] in ('f7012', 'f704f', 'f7069'):
            toks = bytearray()
            for _ in range(rng.randrange(0, 18)):
                kind = rng.randrange(8)
                if kind == 0: toks += b'\x0d'
                elif kind == 1: toks += b'\x7e' + bytes([rng.randrange(0x20, 0xe0)])
                elif kind == 2 and c['fn'] == 'f7069': toks += b'%n' + bytes([0x31 + rng.randrange(4)])
                else: toks += bytes([rng.choice([rng.randrange(0x20, 0x7d), rng.randrange(0xb0, 0xff)])])
            c['txt'] = (bytes(toks) + b'\xff').hex(); c['font'] = bytes(rng.randrange(256) for _ in range(0x200 + 32)).hex()
            c['regs'][0] = (c['regs'][0] & ~0xff) | (c['regs'][0] % 8); c['regs'][1] = (c['regs'][1] & ~0xff) | (c['regs'][1] % 8); c['regs'][2] = (c['regs'][2] & ~0xff) | (c['regs'][2] % 8); c['regs'][4] = 0x400000 + 0x2800
        if c['fn'] in ('f9bf9', 'f9c57'):
            c['w35'] = [0x44, 0x58, 0xf, 0x3b, 0x3c, 0x3d, 2, 3, 5, 9, 10, rng.randrange(1 << 16), 0x44, 0x3c][n % 14]; c['d1cad'] = [1, 0x400, 0x401, 0xffffffff, 0, 1, 1, rng.randrange(1 << 32)][(n // 14) % 8]
            c['b1c4e'] = [1, 1, 1, 0, 7][(n // 5) % 5]; c['d1ca9'] = rng.choice([0, 0x1000, 0xfff, 0x2000, rng.randrange(1 << 32)]) & 0xffffffff; c['b19e9'] = rng.choice([0, 1, 0xff]); c['b9090'] = rng.choice([0, 1, 1]); c['b9091'] = rng.randrange(256)
            ids = rng.sample(range(1, 9), rng.randrange(2, 6)); ids += [5] if max(ids) <= 3 else []; c['lst'] = bytes(ids + [0xff]).hex(); c['b1972'] = rng.choice(ids); c['d1cb1'] = rng.randrange(1 << 32); c['ff'] = rng.randrange(1 << 8)
            if n % 3 == 0: c['calls'][0][1] = 0x1d001f
        if c['fn'] == 'fa320': c['regs'][0] = rng.choice([0, 3, 7, 8, 20, 25, 31, 32, 40, rng.randrange(1 << 32)]); c['regs'][1] = rng.choice([0, 3, 7, 8, 20, 25, 31, 32, 40, rng.randrange(1 << 32)]); c['sel'] = n % 3
        if c['fn'] == 'fa38a':
            for call in c['calls']: call[1] = (call[1] & ~0xff) | (call[1] % 8); call[3] = rng.choice([0, 0x402000, 0x402000])
            c['sel'] = n % 3
        if c['fn'] in ('fa416', 'fa444', 'fa4a9'): c['regs'][0] = rng.choice([0, 1, 5, rng.randrange(1 << 32)]); c['regs'][1] = rng.choice([0, 0xff, 0x1, 0x80, rng.randrange(1 << 32)])
        if c['fn'] == 'fd2d1': c['px'] = 160 + n * 7 % 340; c['py'] = 160 + n * 13 % 340
        if c['fn'] == 'fb319': c['px'] = n % 40; c['py'] = (n // 3) % 40
        if c['fn'] == 'fcb77': c['f22'] = [0x31, 0x31, 0x3b, 0, 0x40, 0x31][n % 6]; c['f19'] = rng.choice([1, 5, 0x80])
        if c['fn'] == 'fc0f6': c['f15'] = [2, 2, 3, 3, 3, 1, 0, 0xa][n % 8]; c['sel'] = (n // 8) % 4
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
            if addr == 0x6885: o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + TMPL); o.ret(); return   # the template lookup keeps everything but EDI
            if addr == 0x68bd: o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + TMPL); o.ret(); return   # the template lookup keeps everything but EDX
            zf, ax, bx, cx, dx, di = state['calls'].pop(0) if state['calls'] else (1, 0, 0, 0, 0, 0)
            o.cpu.reg_write(UC_X86_REG_EAX, ax); o.cpu.reg_write(UC_X86_REG_EBX, bx); o.cpu.reg_write(UC_X86_REG_ECX, cx)
            o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + TMPL if addr in (0xc4cd, 0x982b) else dx); o.cpu.reg_write(UC_X86_REG_EDI, di)
            if addr == 0xa758 or addr == 0xac63 or (addr == 0xc4cd and state['fn'] in ('fcb77', 'fcc7b')): o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + (OBJ1 if di & 1 else OBJ2))
            if addr == 0xd1ba and state['fn'] == 'fcb77': o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + OBJ2)
            if addr == 0xbadf: o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + 0x1900 + 4 * ((ax >> 8) & 0xff))
            if addr == 0xbb26: o.cpu.reg_write(UC_X86_REG_ECX, o.POOL + (OBJ1 if cx & 1 else OBJ2))
            o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if zf else 0)); o.ret()
        return f
    digest = hashlib.sha256(); all_cases = cases(names)
    for case in all_cases:
        if os.environ.get('ONLY_CASE') and str(all_cases.index(case)) != os.environ['ONLY_CASE']: continue
        log.clear(); state['calls'] = [list(c) for c in case['calls']]; state['fn'] = case['fn']
        entry, end, callees = FUNCS[case['fn']]
        o.prepare(32, 32); o.mode = 'actions'
        o.RANGES['actions'] = ([(entry, end)], [])
        o.stubs = {a: stub(a, None) for a in callees if a not in (0x6885, 0x68bd)}
        o.RANGES['actions'] = ([(entry, end), (0x6877, 0x68d8), (0x4409, 0x4442), (0xdb47, 0xdc91), (0xa416, 0xa444)], [])
        o.cpu.mem_write(o.POOL, bytes(0x3000)); o.cpu.mem_write(o.FS + 0x17f, bytes(4)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x1c49, struct.pack('<I', o.POOL + TMPL))
        r = bytearray(prng(case['r'], 0x50)); r[0] %= 3; r[0xe] = 0; r[3] = case['f3']; struct.pack_into('<II', r, 4, case['px'], case['py']); struct.pack_into('<I', r, 0x28, case['cur']); r[0x14] = case['f14']; r[0x19] = case['f19']; r[0x15] = case['f15']; r[0x31] = case['f31']; r[0x30] = case['f30']
        if 'f22' in case: r[0x22] = case['f22']
        if 'f23' in case: r[0x23] = case['f23']; struct.pack_into('<I', r, 0x24, case['f24'])
        struct.pack_into('<H', r, 0x17, case['f17']); struct.pack_into('<I', r, 0x1c, case['f1c'])
        o.cpu.mem_write(o.POOL + POOLREC, bytes(r)); ob1, ob2 = bytearray(constrain(prng(case['o1'], 0x50))), bytearray(constrain(prng(case['o2'], 0x50)))
        if case['fn'] == 'fcb77': ob1[3] %= 3; ob2[3] %= 3
        if case['fn'] == 'fs14b' and case['sel'] != 0:
            for ob in (ob1, ob2): ob[0x20] = 0xff; ob[0x19] = 0x7f; ob[3] %= 8
        o.cpu.mem_write(o.POOL + OBJ1, bytes(ob1)); o.cpu.mem_write(o.POOL + OBJ2, bytes(ob2)); o.cpu.mem_write(o.POOL + TMPL, prng(case['tm'], 0x100))
        for k in range(4):
            pl = prng(case['map'] + k, 1024); o.cpu.mem_write(o.POOL + 0x4000 + 0x1000 * k, b''.join(struct.pack('<I', (0, OBJ1, OBJ2)[b % 3]) for b in pl)); o.cpu.mem_write(0x18ce + 4 * k, struct.pack('<I', o.POOL + 0x4000 + 0x1000 * k))
        if case['fn'] in ('fd182', 'fd19c'): o.cpu.mem_write(o.POOL + TMPL + 0x3b, struct.pack('<I', o.CODE + 0x30))
        for ar in range(3):
            tb = bytearray(0x56); struct.pack_into('<I', tb, 4, o.POOL + TMPL); o.cpu.mem_write(0x6ca + 0x56 * ar, bytes(tb))
        n = max(case['nch'], 1) if case['fn'] == 'fb6f1' else case['nch']; st = min(case['st'], n)
        for k in range(n):
            ob = bytearray(constrain(prng(case['o1'] + 7 + k, 0x50))); struct.pack_into('<I', ob, 0x10, 0x60)
            if ob[0x1c] & 1: ob[0x1b] = case['regs'][1] & 0xff
            if case['fn'] == 'fb319': ob[0x19] = 0x7f; struct.pack_into('<II', ob, 4, 900 + ob[0x1d] % 120, 850 + ob[0x1e] % 100)
            if case['fn'] == 'fb5d8' and ob[0x1c] & 2: ob[0x1b] = 1 + ob[0x1d] % (n + 1)
            o.cpu.mem_write(o.POOL + 0x8000 + 0x60 * k, bytes(ob))
        for ow in range(8):
            pb = bytearray(prng(case['pt'] + ow, 0xf5)); struct.pack_into('<I', pb, 9, 0x8000 + 0x60 * n); struct.pack_into('<I', pb, 0xe, 0x8000 + 0x60 * st); o.cpu.mem_write(0x97a + 0xf5 * ow, bytes(pb))
        lst = [o.POOL + OBJ1 if (case['pt'] >> k) & 1 else o.POOL + OBJ2 for k in range(case['pt'] % 7)] + [0]
        o.cpu.mem_write(0x1f45, struct.pack('<%dI' % len(lst), *lst)); o.cpu.mem_write(0x1ca2, bytes(0x20))
        if case['fn'] == 'fd77a': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ1)
        tab = bytearray(); tv = prng(case['tab'], 256)
        for i in range(256): tab += bytes([tv[i] & 0x7f, (i * 167 + (case['tab'] & 0xff)) & 0xff])
        o.cpu.mem_write(o.STACK + 0x900, bytes(tab) + b'\xff\xff'); o.cpu.mem_write(o.POOL + 0x1900, struct.pack('<4I', o.POOL + OBJ1, o.POOL + OBJ2, o.POOL + OBJ1, o.POOL + OBJ2))
        o.cpu.mem_write(0x1ccb, bytes(0x16 * 256 + 4)); o.cpu.mem_write(0x1f45, struct.pack('<%dI' % len(lst), *lst))
        dsb = prng(case['ds'], 0x29c)
        for k, (wa, wn) in enumerate(DS_WINDOWS): o.cpu.mem_write(wa, dsb[sum(n for _, n in DS_WINDOWS[:k]):sum(n for _, n in DS_WINDOWS[:k + 1])])
        o.cpu.mem_write(o.FS + 0x13d, bytes([case['fs13d']])); o.cpu.mem_write(0x1c49, struct.pack('<I', o.POOL + TMPL))
        o.cpu.mem_write(0x273f, bytes([case['g273'], case['g274']]))
        o.cpu.mem_write(0x525, bytes([case['g525'], case['g526']])); o.cpu.mem_write(0x1c61, bytes([case['g1c61']])); o.cpu.mem_write(0x2730, struct.pack('<I', case['g2730'])); o.cpu.mem_write(0x6b6, struct.pack('<I', o.POOL + 0x1000)); o.cpu.mem_write(o.POOL + 0x1000, prng(case['rt'], 0x800))
        o.cpu.mem_write(o.POOL + 0xa000, prng(case['map'] + 9, 8 * 0x1a35)); o.cpu.mem_write(0x6ba, struct.pack('<I', o.POOL + 0xa000))
        o.cpu.mem_write(0x18e6, struct.pack('<II', 1024, 1024))
        o.terrain(32, 32, [b % 8 for b in prng(case['map'], 1024)])
        o.cpu.mem_write(0x1c9f, bytes([case['g1c9f']])); o.cpu.mem_write(0x512, struct.pack('<I', case['g512'])); o.cpu.mem_write(0x51a, struct.pack('<I', case['g51a'])); o.cpu.mem_write(0x51e, struct.pack('<I', case['g51e'])); o.cpu.mem_write(0x522, bytes([case['g522']]))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + POOLREC); o.cpu.reg_write(UC_X86_REG_EBP, o.POOL + TMPL)
        o.RET = None; o.cpu.mem_write(0x50e, bytes(4))
        if case['fn'] == 'f8227':
            table = o.STACK + 0x7e8; o.RET = table
            o.cpu.mem_write(table, b''.join(struct.pack('<i', (o.CODE + a) - table) for a in callees)); o.cpu.mem_write(0x50e, bytes(4))
        for q, v in zip((UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_EDI), case['regs']): o.cpu.reg_write(q, v)
        if case['fn'] == 'fd77a': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ1)
        if case['fn'] == 'fbb8b': o.cpu.reg_write(UC_X86_REG_EDX, 0x100)
        if case['fn'] == 'f68a0': o.cpu.reg_write(UC_X86_REG_EAX, (case['regs'][0] & 0xffff00ff) | ((((case['regs'][0] >> 8) & 0xff) % 3) << 8))
        if case['fn'] == 'fd19c': o.cpu.reg_write(UC_X86_REG_EBX, ((((case['regs'][1] >> 8) & 0xff) % 3) << 8))
        if case['fn'] == 'fc35f': o.cpu.reg_write(UC_X86_REG_EDX, case['regs'][3] & 3)
        if case['fn'] == 'fc344': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ1)
        if case['fn'] in ('fc0f6', 'fbe2e'):
            if case['fn'] == 'fc0f6': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + TMPL)
            o.cpu.reg_write(UC_X86_REG_EAX, case['regs'][0] & 0xffffff00)
            tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100)); pa = 0x97a + 0xf5 * case['f3']; pb = bytearray(o.cpu.mem_read(pa, 0xf5))
            for k in (0x7c, 0x80, 0x84, 0x88):
                if case['sel'] & 1: struct.pack_into('<I', tb, k, struct.unpack_from('<I', tb, k)[0] & 0xff)
            for k in (0x18, 0x1c, 0x20, 0x24):
                if case['sel'] & 2: struct.pack_into('<I', pb, k, struct.unpack_from('<I', pb, k)[0] | 0x10000)
            o.cpu.mem_write(o.POOL + TMPL, bytes(tb)); o.cpu.mem_write(pa, bytes(pb))
        if case['fn'] == 'fb616':
            r2, r3 = case['regs'][2], case['regs'][3]; b1 = prng(case['o1'], 0x50)[3]; b2 = prng(case['o2'], 0x50)[3]
            o.cpu.reg_write(UC_X86_REG_ECX, (r2 & 0xffff0000) | (r2 & 31) | (((r2 >> 8) & 31) << 8)); o.cpu.reg_write(UC_X86_REG_EDX, (r3 & 0xffff0000) | (r3 & 31) | (((r3 >> 8) & 31) << 8))
            o.cpu.reg_write(UC_X86_REG_EAX, (case['regs'][0] & 0xffffff00) | (b1 if case['sel'] & 1 else b2 if case['sel'] & 2 else case['regs'][0] & 0xff))
        if case['fn'] in ('fb9eb', 'fba2e', 'fb5d8', 'fda45'): o.cpu.reg_write(UC_X86_REG_EAX, (case['regs'][0] & 0xffffff00) | (case['regs'][0] & 7))
        if case['fn'] == 'fb319' and case['sel'] != 0:
            tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100)); tb[0x46] = 5; tb[0x4e] = 0x10; tb[0x40] |= 8; o.cpu.mem_write(o.POOL + TMPL, bytes(tb))
        if case['fn'] == 'fb6f1':
            o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x8000); o.cpu.reg_write(UC_X86_REG_EBP, o.POOL + 0x8000 + 0x60 * n)
            tb = prng(case['tm'], 0x100)
            for k in range(6):
                if (case['sel'] >> (k % 2)) & 1: o.cpu.mem_write(0x1978 + 0xc * k + 7, bytes([tb[0x12 + 8 * k]]))
            o.cpu.mem_write(0x1c87, bytes([case['regs'][1] & 0xff]))
        if case['fn'] == 'f6ec1':
            o.cpu.mem_write(0x1969, struct.pack('<I', o.POOL + OBJ1)); tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100))
            for k in range(6): struct.pack_into('<I', tb, 0x0d + 8 * k, struct.unpack_from('<I', tb, 0x0d + 8 * k)[0] % 0x30)
            o.cpu.mem_write(o.POOL + TMPL, bytes(tb))
        if case['fn'] == 'fd2d1':
            sel, r1, r2, r3 = case['sel'], case['regs'][1], case['regs'][2], case['regs'][3]
            o.cpu.reg_write(UC_X86_REG_EAX, (0x900, 0xa00, 0, 0x900)[sel]); o.cpu.reg_write(UC_X86_REG_EBX, (0xa00, 0x900, r3, 0)[(sel + r2) & 3])
        if case['fn'] in ('f7012', 'f704f', 'f7069'):
            txt = bytes.fromhex(case['txt']); font = bytearray(bytes.fromhex(case['font']))
            for i in range(256): struct.pack_into('<H', font, 2 * i, 0x200 + (i % 8) * 4)
            o.cpu.mem_write(o.POOL + 0x2200, txt); o.cpu.mem_write(o.POOL + 0x2400, bytes(font)); o.cpu.mem_write(0x19ee, struct.pack('<I', o.POOL + 0x2400))
            o.cpu.mem_write(o.POOL + 0x2800, bytes(0x100)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x2200)
            for ow in range(8):
                pa = 0x97a + 0xf5 * ow; pbb = bytearray(o.cpu.mem_read(pa, 0xf5)); pbb[0xa4 + (ow * 3 + case['nch']) % 30] = 0xff; o.cpu.mem_write(pa, bytes(pbb))
        if case['fn'] in ('f9bf9', 'f9c57'):
            def wd(a, v, n): o.cpu.mem_write(a, int(v).to_bytes(n, 'little'))
            wd(0x1c35, case['w35'], 2); wd(0x1cad, case['d1cad'], 4); wd(0x1c4e, case['b1c4e'], 1); wd(0x1ca9, case['d1ca9'], 4); wd(0x19e9, case['b19e9'], 1); wd(0x9090, case['b9090'], 1); wd(0x9091, case['b9091'], 1)
            lst = bytes.fromhex(case['lst']); o.cpu.mem_write(0x1cb4, b'\x00' + lst); wd(0x1972, case['b1972'], 1); wd(0x1cb1, case['d1cb1'], 4)
            o.cpu.mem_write(0x1ccb, bytes(b if b != 0xff else 0xfe for b in prng(case['ds'] + 40, 0x16 * 9)) if case['ff'] % 4 else bytes([0xff] * (0x16 * 4)) + bytes(0x16 * 5)); o.cpu.mem_write(o.FS + 0x17f, bytes(4))
        if case['fn'] == 'fa38a':
            o.cpu.mem_write(o.POOL + 0x2000, struct.pack('<I', (0, 0x900, 0xa00)[case['sel']]))
        if case['fn'] == 'fa47e':
            rr = bytearray(o.cpu.mem_read(o.POOL + POOLREC, 0x50)); rr[1] %= 3; o.cpu.mem_write(o.POOL + POOLREC, bytes(rr))
        if case['fn'] == 'fdc91':
            tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100)); tb[0x45] = 1 + tb[0x45] % 200; tb[0x4a] %= tb[0x45] + 1; tb[0x51] %= 3; tb[0x52] %= 3; tb[0x4c] %= 0x30; o.cpu.mem_write(o.POOL + TMPL, bytes(tb))
            rr = bytearray(o.cpu.mem_read(o.POOL + POOLREC, 0x50))
            if case['sel'] & 1: rr[0x1a] = rr[0x19]
            if case['sel'] & 2: rr[0x19] = 1 + rr[0x19] % 100; rr[0x1a] = 0
            o.cpu.mem_write(o.POOL + POOLREC, bytes(rr))
        if case['fn'] == 'fde0c':
            seg = bytearray(prng(case['map'] + 3, 8192))
            if case['sel'] & 2:
                for t in range(1024): seg[t * 8 + 2] = seg[t * 8 + 3] = (seg[t * 8 + 2] & 15) * 17
            o.cpu.mem_write(o.SEG, bytes(seg))
            if case['sel'] & 1:   # sparse planes: a cell in sixteen holds an object
                for k in range(4): o.cpu.mem_write(o.POOL + 0x4000 + 0x1000 * k, b''.join(struct.pack('<I', OBJ1 if b % 16 == 0 else 0) for b in prng(case['map'] + k, 1024)))
        if case['fn'] == 'fs14b' and case['sel'] != 0:
            tb = bytearray(o.cpu.mem_read(o.POOL + TMPL, 0x100)); tb[0x46] = 5; tb[0x74] = 0xf; tb[0x40] &= ~0x20 & 0xff; tb[0x41] &= 0x7f; o.cpu.mem_write(o.POOL + TMPL, bytes(tb))
        if case['fn'] in ('fs187', 'fs16b'): o.cpu.reg_write(UC_X86_REG_EAX, (case['regs'][0] & 0xffff0000) | ((((case['regs'][0] >> 8) & 0xff) % 3) << 8))
        o.cpu.mem_write(0x6c6, bytes(1)); o.cpu.mem_write(0x6c9, bytes(1)); o.cpu.mem_write(0x3e87, bytes(4))
        if case['fn'] == 'f7a03':
            q = prng(case['ds'] + 13, 8); o.cpu.mem_write(0x274e, bytes([q[0], q[1]])); o.cpu.mem_write(0x2791, bytes([q[2], q[3]]))
        if case['fn'] == 'fs18b':
            a1, a2 = prng(case['ds'] + 11, 0x3000), prng(case['ds'] + 12, 0x3000)
            blk = bytes(((a1[k] | a2[k]) if k % 3 == 0 else (a1[k] & a2[k])) & (0 if k % 0x84 in (0x59, 0x17) else 0xff) for k in range(0x3000))
            o.cpu.mem_write(0x5000, blk); o.cpu.mem_write(0x3e87, struct.pack('<I', 0x5000)); o.cpu.mem_write(0x6c9, bytes([1 + case['sel'] % 4])); o.cpu.mem_write(0x6c6, bytes([1 + case['g273'] % 4]))
        if case['fn'] == 'fc439':
            tf = struct.unpack('<I', prng(case['tm'], 0x100)[0x40:0x44])[0]; o.cpu.reg_write(UC_X86_REG_EBX, case['bm']); o.cpu.reg_write(UC_X86_REG_EAX, (tf & case['bm']) if case['sel'] else case['regs'][0])
        if case['fn'] == 'fd157': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ2)
        if case['fn'] == 'fc4cd': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ1)
        if case['fn'] == 'f68bd': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ2)
        o.cpu.mem_write(0x1f0a, bytes(0x3b))
        if case['fn'] == 'fcccb': o.cpu.mem_write(0x1f70, prng(case['ds'] + 5, 0x1090))
        if case.get('z19'): o.cpu.mem_write(0x19ea, b'\x00')
        try: o.run(o.CODE + entry)
        except Exception: print('FAIL', all_cases.index(case), case['fn'], hex(o.cpu.reg_read(UC_X86_REG_EIP) - o.CODE), [hex(o.cpu.reg_read(q)) for q in (UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_EDI, UC_X86_REG_ESI)]); raise
        fl = reg(UC_X86_REG_EFLAGS)
        out = (bytes(o.cpu.mem_read(o.POOL + POOLREC, 0x80)) + bytes(o.cpu.mem_read(o.POOL + OBJ1, 0x50)) + bytes(o.cpu.mem_read(o.POOL + OBJ2, 0x50)) + bytes(o.cpu.mem_read(0x1c9f, 1)) + bytes(o.cpu.mem_read(0x512, 4)) + bytes(o.cpu.mem_read(0x50e, 4)) + bytes(o.cpu.mem_read(0x1ccb + 0x16 * r[3], 1)) + bytes(o.cpu.mem_read(0x525, 2)) + bytes(o.cpu.mem_read(0x1c61, 1)) + b''.join(bytes(o.cpu.mem_read(a, n)) for a, n in DS_WINDOWS) + bytes(o.cpu.mem_read(o.FS + 0x13d, 1)) + b''.join(bytes(o.cpu.mem_read(o.POOL + 0xa000 + k * 0x1a35 + 0x170, 0x40)) for k in range(3)) + b''.join(bytes(o.cpu.mem_read(o.POOL + 0xa000 + k * 0x1a35 + 0x70, 0x10)) for k in range(8)) + bytes(o.cpu.mem_read(0x1ca2, 0x20)) + bytes(o.cpu.mem_read(0x1f0a, 0x7b)) + bytes(o.cpu.mem_read(o.FS + 0x17f, 4)) + bytes(o.cpu.mem_read(o.POOL + 0x2800, 0x100))
               + struct.pack('<IIIII', *[rel(reg(q)) for q in (UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_EDI)]) + bytes([(fl >> 6) & 1, fl & 1]) + bytes(o.cpu.mem_read(0x97a + 0xf5 * case['f3'], 0xf5)) + (b''.join(bytes(o.cpu.mem_read(o.POOL + 0x4000 + 0x1000 * k, 0x1000)) for k in range(4)) if case['fn'] == 'fd2d1' else b'') + b''.join(log))
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
