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
    'fc439': (0xc439, 0xc4cd, []),
    'f84aa': (0x84aa, 0x8659, [0x8d89, 0x8c98, 0x8ca1, 0xc331, 0x8c47, 0x8af4, 0x8c89, 0xcfac, 0x123d3, 0xd101, 0x8b0f, 0xc35f]),
}
POOLREC, OBJ1, OBJ2, TMPL = 0x800, 0x900, 0xa00, 0xc00
DS_WINDOWS = [(0x19c0, 0x40), (0x1c30, 0x40), (0x4160, 0x10), (0x9080, 0x10), (0x1978, 0x48)]   # filled with pseudo-random bytes (the globals of the functions that read them)


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
        out.append({'fn': rng.choice(names + ['fb319'] * 4 + ['fc0f6'] * 5 + ['fcb77'] * 4 + ['fcccb'] * 4), 'r': rng.randrange(1 << 32), 'o1': rng.randrange(1 << 32), 'o2': rng.randrange(1 << 32), 'tm': rng.randrange(1 << 32), 'f1c': rng.choice([0, 0x900, 0xa00]), 'f14': rng.choice([0, 1, 2, 3, 4, 5, 0x40, 0x41, 0x4a, 0x2d, (rng.randrange(256) & ~7) | rng.randrange(6)]), 'f19': rng.choice([0, 1, 5, 0x80, 0xff, rng.randrange(256)]), 'pt': rng.randrange(1 << 32), 'f3': rng.randrange(8), 'nch': rng.randrange(0, 5), 'st': rng.randrange(0, 5), 'bm': rng.choice([8, 0x1000, 0x20000, 0xffffffff, 0x10, rng.randrange(1 << 32)]), 'sel': rng.randrange(4), 'g273': rng.randrange(1, 5), 'g274': rng.randrange(1, 5), 'ds': rng.randrange(1 << 32), 'tab': rng.randrange(1 << 32), 'fs13d': rng.randrange(256), 'px': rng.randrange(1024), 'py': rng.randrange(1024), 'cur': rng.choice([0xffffffff, rng.randrange(0x3ff), rng.randrange(0x3ff), 0]), 'g525': rng.randrange(256), 'g526': rng.randrange(256), 'g1c61': rng.choice([0, 1, 5, rng.randrange(256)]), 'g2730': rng.choice([0xffffffff, 0, rng.randrange(1 << 32)]), 'rt': rng.randrange(1 << 32), 'map': rng.randrange(1 << 32), 'f15': rng.choice([0, 1, 2, 3, 0x10, 0x11, rng.randrange(256)]), 'f31': rng.choice([0, 1, rng.randrange(256)]),
                    'f30': rng.choice([0, 0x10, 0x91, 0xf3, rng.randrange(256)]), 'f17': rng.choice([0, 2, 0x40, 0x42, rng.randrange(1 << 16)]), 'g1c9f': rng.choice([0, 0x40, 0x80, rng.randrange(256)]), 'g512': rng.choice([0, 0, 1, rng.randrange(1 << 20)]),
                    'g51a': rng.randrange(1 << 20), 'g51e': rng.choice([0, 0x400900]), 'g522': rng.choice([0, 3, rng.randrange(256)]),
                    'regs': [rng.choice([0, 1, 2, 3, rng.randrange(1 << 32)]) for _ in range(5)],
                    'calls': [[rng.choice([0, 1, 1]), rng.choice([0, 1, 2, 3, rng.randrange(256)]) | (rng.choice([0, 1, 2, 3, 4]) << 8), rng.randrange(1 << 20) if rng.random() < 0.4 else rng.choice([0, 1, 2, 3, 0xff]), rng.randrange(1 << 20) if rng.random() < 0.5 else rng.choice([0, 1, 2]), rng.randrange(1 << 20), rng.choice([0, 0x400900, 0x400a00])] for _ in range(8)]})
    for n, c in enumerate(out):
        if c['fn'] == 'fcccb' and n % 2 == 0: c['z19'] = 1; c['f15'] = 1; c['f22'] = (n // 2) % 5; c['f23'] = (n // 3) % 4; c['f24'] = (n // 5) % 40
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
        o.RANGES['actions'] = ([(entry, end), (0x6877, 0x68d8)], [])
        o.cpu.mem_write(o.POOL, bytes(0x2000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x1c49, struct.pack('<I', o.POOL + TMPL))
        r = bytearray(prng(case['r'], 0x50)); r[0] %= 3; r[0xe] = 0; r[3] = case['f3']; struct.pack_into('<II', r, 4, case['px'], case['py']); struct.pack_into('<I', r, 0x28, case['cur']); r[0x14] = case['f14']; r[0x19] = case['f19']; r[0x15] = case['f15']; r[0x31] = case['f31']; r[0x30] = case['f30']
        if 'f22' in case: r[0x22] = case['f22']
        if 'f23' in case: r[0x23] = case['f23']; struct.pack_into('<I', r, 0x24, case['f24'])
        struct.pack_into('<H', r, 0x17, case['f17']); struct.pack_into('<I', r, 0x1c, case['f1c'])
        o.cpu.mem_write(o.POOL + POOLREC, bytes(r)); ob1, ob2 = bytearray(constrain(prng(case['o1'], 0x50))), bytearray(constrain(prng(case['o2'], 0x50)))
        if case['fn'] == 'fcb77': ob1[3] %= 3; ob2[3] %= 3
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
        dsb = prng(case['ds'], 0xe8)
        for k, (wa, wn) in enumerate(DS_WINDOWS): o.cpu.mem_write(wa, dsb[sum(n for _, n in DS_WINDOWS[:k]):sum(n for _, n in DS_WINDOWS[:k + 1])])
        o.cpu.mem_write(o.FS + 0x13d, bytes([case['fs13d']])); o.cpu.mem_write(0x1c49, struct.pack('<I', o.POOL + TMPL))
        o.cpu.mem_write(0x273f, bytes([case['g273'], case['g274']]))
        o.cpu.mem_write(0x525, bytes([case['g525'], case['g526']])); o.cpu.mem_write(0x1c61, bytes([case['g1c61']])); o.cpu.mem_write(0x2730, struct.pack('<I', case['g2730'])); o.cpu.mem_write(0x6b6, struct.pack('<I', o.POOL + 0x1000)); o.cpu.mem_write(o.POOL + 0x1000, prng(case['rt'], 0x800))
        o.cpu.mem_write(o.POOL + 0xa000, prng(case['map'] + 9, 8 * 0x1a35)); o.cpu.mem_write(0x6ba, struct.pack('<I', o.POOL + 0xa000))
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
        if case['fn'] in ('fb9eb', 'fba2e', 'fb5d8'): o.cpu.reg_write(UC_X86_REG_EAX, (case['regs'][0] & 0xffffff00) | (case['regs'][0] & 7))
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
        if case['fn'] == 'fc439':
            tf = struct.unpack('<I', prng(case['tm'], 0x100)[0x40:0x44])[0]; o.cpu.reg_write(UC_X86_REG_EBX, case['bm']); o.cpu.reg_write(UC_X86_REG_EAX, (tf & case['bm']) if case['sel'] else case['regs'][0])
        if case['fn'] == 'fd157': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ2)
        if case['fn'] == 'fc4cd': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ1)
        if case['fn'] == 'f68bd': o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + OBJ2)
        o.cpu.mem_write(0x1f0a, bytes(0x3b))
        if case['fn'] == 'fcccb': o.cpu.mem_write(0x1f70, prng(case['ds'] + 5, 0x1090))
        if case.get('z19'): o.cpu.mem_write(0x19ea, b'\x00')
        o.run(o.CODE + entry)
        fl = reg(UC_X86_REG_EFLAGS)
        out = (bytes(o.cpu.mem_read(o.POOL + POOLREC, 0x80)) + bytes(o.cpu.mem_read(o.POOL + OBJ1, 0x50)) + bytes(o.cpu.mem_read(o.POOL + OBJ2, 0x50)) + bytes(o.cpu.mem_read(0x1c9f, 1)) + bytes(o.cpu.mem_read(0x512, 4)) + bytes(o.cpu.mem_read(0x50e, 4)) + bytes(o.cpu.mem_read(0x1ccb + 0x16 * r[3], 1)) + bytes(o.cpu.mem_read(0x525, 2)) + bytes(o.cpu.mem_read(0x1c61, 1)) + b''.join(bytes(o.cpu.mem_read(a, n)) for a, n in DS_WINDOWS) + bytes(o.cpu.mem_read(o.FS + 0x13d, 1)) + b''.join(bytes(o.cpu.mem_read(o.POOL + 0xa000 + k * 0x1a35 + 0x170, 0x40)) for k in range(3)) + b''.join(bytes(o.cpu.mem_read(o.POOL + 0xa000 + k * 0x1a35 + 0x70, 0x10)) for k in range(8)) + bytes(o.cpu.mem_read(0x1ca2, 0x20)) + bytes(o.cpu.mem_read(0x1f0a, 0x3b))
               + struct.pack('<IIIII', *[rel(reg(q)) for q in (UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_EDI)]) + bytes([(fl >> 6) & 1, fl & 1]) + bytes(o.cpu.mem_read(0x97a + 0xf5 * case['f3'], 0xf5)) + b''.join(log))
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
