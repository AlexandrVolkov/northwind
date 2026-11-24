#!/usr/bin/env python3
"""Verify the hand-over of an object to another owner, LE:1 D1BA..D2D1 (the template flags decide the notification 9A8E, the first-fit block in the chain of the new owner, the copy of the record with REP MOVSB, the flags
of the old and the new record, the call 43B0 with the new record), with Unicorn. 9A8E and 43B0 are recorded stubs; 6885 (the template of the record) runs as the original code."""
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
from verify_object_alloc import R, TT

SRC = 0x3c00        # the record that is handed over (pool offset)


class TransferOracle(CombatOracle):
    RANGES = {'transfer': ([(0xd1ba, 0xd2d1), (0x6877, 0x68d8)], [])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=400000)


def cases():
    rng = random.Random(0xd1ba)
    out = []
    for n in range(800):
        fail = rng.random() < 0.08
        tsize = [[rng.choice([0x40, 0x44, 0x50, 0x60]) for _ in range(4)] for _ in range(3)]
        count = rng.randrange(0, 6)
        objs = [{'army': rng.randrange(3), 'tmpl': rng.randrange(4), 'slack': rng.choice([0, 0, 8, 0x20, 0x40, 0x80, rng.randrange(0x100)])} for _ in range(count)]
        out.append({'owner': rng.randrange(2), 'sarmy': rng.randrange(3), 'stmpl': rng.randrange(4), 'seed': rng.randrange(1 << 32), 'g273f': rng.randrange(256), 'ax': rng.randrange(1 << 32),
                    'tsize': tsize, 'tflags': [[rng.choice([0, 8, 0x100000, 0x40000, 0x400000, rng.randrange(1 << 32)]) for _ in range(4)] for _ in range(3)],
                    'tb34': [[rng.randrange(256) for _ in range(2)] for _ in range(3)], 'objs': objs, 'hint': rng.randrange(count + 1), 'fail': fail})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = TransferOracle(code, report['fs_assignments'], constants)
    log = []
    def reg(r): return o.cpu.reg_read(r)
    def stub(addr):
        def f():
            log.append(b'C' + struct.pack('<HI', addr & 0xffff, (reg(UC_X86_REG_EAX) - (o.POOL if o.POOL <= reg(UC_X86_REG_EAX) < o.POOL + 0x20000 else 0)) & 0xffffffff) + struct.pack('<I', reg(UC_X86_REG_ESI) - o.POOL)); o.ret()
        return f
    o.stubs = {0x9a8e: stub(0x9a8e), 0x43b0: stub(0x43b0)}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'transfer'
        o.cpu.mem_write(o.POOL, bytes(0x20000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL))
        for a in range(3):
            tb = bytearray(0x56); struct.pack_into('<I', tb, 4, o.POOL + TT + 0x400 * a)
            o.cpu.mem_write(0x6ca + 0x56 * a, bytes(tb))
            tt = bytearray(0x400)
            for t in range(4):
                e = bytearray(0xab); e[0] = case['tsize'][a][t]; struct.pack_into('<I', e, 0x40, case['tflags'][a][t]); e[3] = case['tb34'][a][0] if a < 3 else 0; e[4] = t; tt[0xab * t:0xab * (t + 1)] = e
            o.cpu.mem_write(o.POOL + TT + 0x400 * a, bytes(tt))
        pos = R; starts = []
        objs = case['objs'] if not case['fail'] else [{'army': 0, 'tmpl': 0, 'slack': 0}] * 1100
        for ob in objs:
            sz = case['tsize'][ob['army']][ob['tmpl']] + ob['slack']
            starts.append(pos)
            blk = bytearray([0xaa]) * sz; blk[0] = ob['army']; blk[0xe] = ob['tmpl']; struct.pack_into('<I', blk, 0x10, sz)
            o.cpu.mem_write(o.POOL + pos, bytes(blk)); pos += sz
        starts.append(pos); hint = starts[min(case['hint'], len(starts) - 1)] if not case['fail'] else R
        part = 0x97a + 0xf5 * case['owner']
        o.cpu.mem_write(part + 5, struct.pack('<I', o.POOL + R)); o.cpu.mem_write(part + 9, struct.pack('<I', pos)); o.cpu.mem_write(part + 0xe, struct.pack('<I', hint))
        x = case['seed']; src = bytearray()
        for _ in range(0x70): x = (x * 1664525 + 1013904223) & 0xffffffff; src.append(x >> 24)
        src[0] = case['sarmy']; src[0xe] = case['stmpl']; struct.pack_into('<I', src, 0x10, 0x70)
        o.cpu.mem_write(o.POOL + SRC, bytes(src))
        o.cpu.mem_write(0x273f, bytes([case['g273f'], 0x55]))
        o.cpu.reg_write(UC_X86_REG_EAX, case['owner'] | (case['ax'] & 0xffffff00)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + SRC)
        for q in (UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_EDI): o.cpu.reg_write(q, 0x1234)
        o.run(o.CODE + 0xd1ba)
        dump = bytes(o.cpu.mem_read(o.POOL + SRC, 0x70)) + bytes(o.cpu.mem_read(o.POOL + R, 0x700)) + bytes(o.cpu.mem_read(part + 5, 13)) + bytes(o.cpu.mem_read(0x273f, 2))
        out = dump + struct.pack('<BB', reg(UC_X86_REG_EAX) & 255, o.zf()) + b''.join(log)
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'transfer': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
