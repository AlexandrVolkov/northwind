#!/usr/bin/env python3
"""Verify the object allocator FS:45 = LE:1 A758..A870 (the first-fit search in the chain of the objects of the owner, the carving of the free space, the clearing and filling of the record, the call of the
initialization script of the template with AH = 0) with Unicorn. The call of the script ([19C3]) is a recorded stub."""
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

R = 0x4000          # the first object (pool offset)
TT = 0x1000         # the template tables of the armies (0x400 each)
STUB = 0x30         # the code offset of the stub of the script


class AllocOracle(CombatOracle):
    RANGES = {'alloc': ([(0xa758, 0xa879)], [])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=400000)


def cases():
    rng = random.Random(0xa758)
    out = []
    for n in range(800):
        fail = rng.random() < 0.08
        tsize = [[rng.choice([0x40, 0x44, 0x50, 0x60]) for _ in range(4)] for _ in range(3)]
        count = rng.randrange(0, 6)
        objs = [{'army': rng.randrange(3), 'tmpl': rng.randrange(4), 'slack': rng.choice([0, 0, 8, 0x20, 0x40, 0x80, rng.randrange(0x100)])} for _ in range(count)]
        out.append({'army': rng.randrange(3), 'owner': rng.randrange(2), 'tmpl': rng.randrange(4), 'layer': rng.randrange(256), 'x': rng.randrange(1 << 30), 'y': rng.randrange(1 << 30), 'args': [rng.randrange(1 << 30) for _ in range(3)],
                    'tsize': tsize, 'tscript': [[rng.randrange(0x40, 0x100) for _ in range(4)] for _ in range(3)], 'thp': [[rng.randrange(256) for _ in range(4)] for _ in range(3)], 'tframe': [[rng.randrange(1 << 16) for _ in range(4)] for _ in range(3)],
                    'objs': objs, 'hint': rng.randrange(count + 1), 'fail': fail, 'ax': rng.randrange(1 << 32), 'cx': rng.randrange(1 << 30), 'dx': rng.randrange(1 << 30)})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = AllocOracle(code, report['fs_assignments'], constants)
    log = []
    def reg(r): return o.cpu.reg_read(r)
    def script():
        log.append(b'I' + struct.pack('<IIIII', reg(UC_X86_REG_EAX), reg(UC_X86_REG_EBX), reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX), reg(UC_X86_REG_EDI) - o.POOL)); o.ret()
    o.stubs = {STUB: script}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'alloc'
        o.cpu.mem_write(o.POOL, bytes(0x20000)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL))
        for a in range(3):
            tb = bytearray(0x56); struct.pack_into('<I', tb, 4, o.POOL + TT + 0x400 * a)
            struct.pack_into('<I', tb, 9, o.CODE + STUB - case['tscript'][a][case['tmpl']] if a == case['army'] else o.CODE + STUB - 0x40)
            o.cpu.mem_write(0x6ca + 0x56 * a, bytes(tb))
            tt = bytearray(0x400)
            for t in range(4):
                e = bytearray(0xab); e[0] = case['tsize'][a][t]; struct.pack_into('<I', e, 0x3b, case['tscript'][a][t]); e[0x45] = case['thp'][a][t]; struct.pack_into('<H', e, 0x48, case['tframe'][a][t]); tt[0xab * t:0xab * (t + 1)] = e
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
        o.cpu.mem_write(o.STACK + 0xf04, struct.pack('<III', *case['args']))
        o.cpu.reg_write(UC_X86_REG_EAX, case['army'] | (case['owner'] << 8) | (case['ax'] & 0xffff0000)); o.cpu.reg_write(UC_X86_REG_EBX, case['tmpl'] | (case['layer'] << 8))
        o.cpu.reg_write(UC_X86_REG_ECX, case['cx']); o.cpu.reg_write(UC_X86_REG_EDX, case['dx'])
        for q in (UC_X86_REG_ESI, UC_X86_REG_EDI): o.cpu.reg_write(q, 0x1234)
        o.run(o.CODE + 0xa758)
        dump = bytes(o.cpu.mem_read(o.POOL + R, 0x700)) + bytes(o.cpu.mem_read(part + 5, 13)) + bytes(o.cpu.mem_read(0x19c3, 4)) + bytes(o.cpu.mem_read(0x1f44, 1)) + bytes(o.cpu.mem_read(0x1f2f, 2))
        out = dump + struct.pack('<IBB', (reg(UC_X86_REG_EDI) - o.POOL) & 0xffffffff, reg(UC_X86_REG_EAX) & 255, o.zf()) + b''.join(log)
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'alloc': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
