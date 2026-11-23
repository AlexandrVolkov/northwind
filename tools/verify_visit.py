#!/usr/bin/env python3
"""Verify the visit of an object (LE:1 902F: the action «посетить» of a unit, with 9B86 the footprint gap, 8C1E / 8C98 / 8C7C the movement requests, 960B the turn towards the
target and 12406 the visitor service) with Unicorn. The WRK AH=8 call of the target and the pickup 1243A are recorded stubs (both are verified separately: container.json, items.json)."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_combat import CombatOracle
from forestry_source import source_constants

WRK_STUB = 0x2ff00


class VisitOracle(CombatOracle):
    RANGES = {'visit': ([(0x902f, 0x9064), (0x9b86, 0x9bf9), (0x8c1e, 0x8c47), (0x8c7c, 0x8c89), (0x8c98, 0x8ca1), (0x960b, 0x9688), (0x12406, 0x1243a), (0x68bd, 0x68d8)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=20000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('visit did not return')


TABLE = [7, 6, 5, 0, 0xff, 4, 1, 2, 3]


def direction(fx, fy, tx, ty):
    cl = ch = 2; dx = (tx - fx) & 0xffffffff; dy = (ty - fy) & 0xffffffff
    if dx == 0: cl -= 1
    elif tx < fx: dx = (-dx) & 0xffffffff; cl -= 2
    if dy == 0: ch -= 1
    elif ty < fy: dy = (-dy) & 0xffffffff; ch -= 2
    if dx >= dy:
        if (dy * 2) & 0xffffffff < dx: ch = 1
    elif (dx * 2) & 0xffffffff < dy: cl = 1
    return TABLE[3 * cl + ch]


def cases():
    rng = random.Random(0x902f)
    out = []
    for n in range(1500):
        cx, cy = rng.randrange(200, 3000), rng.randrange(200, 3000)
        close = rng.choice([True, True, True, False])
        out.append({'visitor': {'army': rng.choice([0, 1]), 'template': rng.randrange(4), 'x': cx, 'y': cy, 'state': rng.choice([0, 0, 0, 0x10, 0x01, 0x20, 0x34, rng.randrange(256)]),
                                'facing': rng.randrange(256), 'owner': rng.choice([0, 1, 2]), 'frame': rng.randrange(65536)},
                    'target': {'army': rng.choice([0, 1]), 'template': rng.randrange(4), 'condition': rng.choice([100, 50, 1, 0, 0x80, 0xff]),
                               'dx': rng.choice([0, 16, -16, 40, -40, 120, -300, rng.randrange(-60, 60)] if close else [rng.randrange(-900, 900)]),
                               'dy': rng.choice([0, 16, -16, 40, -40, 120, -300, rng.randrange(-60, 60)] if close else [rng.randrange(-900, 900)])},
                    'templates': [[{'w': rng.choice([1, 2, 3]), 'h': rng.choice([1, 2, 3]), 'flags': rng.choice([0, 0x20000, 0x20000, 0x20000 | 8, rng.randrange(1 << 20)]),
                                    'base': rng.randrange(2000), 'frames': rng.choice([1, 4, 8, 12])} for _ in range(4)] for _ in range(2)],
                    'wrk': {'al': rng.choice([0, 1, 1, 2, 6, 0x22, 3]), 'zf': rng.choice([0, 1, 1])}})
        # a visitor that is already facing the target gets through 8C1E: the case generator keeps the facing random, so half of the cases set the facing to the answer of 9688 later
    for case in out:   # most visitors already face the target: the later stages of 902F are reached
        v, t = case['visitor'], case['target']
        d = direction(v['x'], v['y'], (v['x'] + t['dx']) & 0xffffffff, (v['y'] + t['dy']) & 0xffffffff)
        if d != 0xff and rng.random() < 0.7: v['facing'] = (v['facing'] & 0xf8) | (d & 7)
        if rng.random() < 0.7: v['state'] = 0
        if rng.random() < 0.6: t['condition'] = 100
        if rng.random() < 0.6: t['dx'] = rng.randrange(-8, 9) * 4; t['dy'] = rng.randrange(-8, 9) * 4
        d = direction(v['x'], v['y'], (v['x'] + t['dx']) & 0xffffffff, (v['y'] + t['dy']) & 0xffffffff)
        if d != 0xff and rng.random() < 0.7: v['facing'] = (v['facing'] & 0xf8) | (d & 7)
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = VisitOracle(code, report['fs_assignments'], constants)
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    o.cpu.mem_write(0x19d7, objects[1]['payload'][0x19d7:0x19e0])
    o.cpu.mem_write(o.CODE + WRK_STUB, b'\xc3' * 16)   # the stub address of the WRK call: a bare ret
    log = []; state = {}
    def wrk():
        r = o.cpu.reg_read; log.append(b'W' + struct.pack('<BII', (r(UC_X86_REG_EAX) >> 8) & 255, r(UC_X86_REG_ESI) - o.POOL, r(UC_X86_REG_EDI) - o.POOL))
        flags = o.cpu.reg_read(UC_X86_REG_EFLAGS) & ~0x40
        o.cpu.reg_write(UC_X86_REG_EFLAGS, flags | (0x40 if state['zf'] else 0)); o.cpu.reg_write(UC_X86_REG_EAX, state['al']); o.ret()
    def pickup():
        r = o.cpu.reg_read; log.append(b'P' + struct.pack('<BB', r(UC_X86_REG_EAX) & 255, (r(UC_X86_REG_EAX) >> 8) & 255)); o.ret()
    o.stubs = {WRK_STUB: wrk, 0x1243a: pickup}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'visit'; state.update(case['wrk'])
        for army in range(2):
            o.cpu.mem_write(0x6ca + 0x56 * army + 4, struct.pack('<I', o.DAT + army * 0x400))
            o.cpu.mem_write(0x6ca + 0x56 * army + 9, struct.pack('<I', o.CODE + WRK_STUB))
            for index, t in enumerate(case['templates'][army]):
                raw = bytearray(0xab); raw[3] = t['w']; raw[4] = t['h']; struct.pack_into('<I', raw, 0x3b, 0); struct.pack_into('<I', raw, 0x40, t['flags'])
                struct.pack_into('<H', raw, 0x48, t['base']); raw[0x4a] = t['frames']
                o.cpu.mem_write(o.DAT + army * 0x400 + index * 0xab, bytes(raw))
        v, t = case['visitor'], case['target']
        visitor = bytearray(0x40); visitor[0] = v['army']; visitor[0x0e] = v['template']; struct.pack_into('<II', visitor, 4, v['x'], v['y']); visitor[0x2d] = v['state']
        visitor[0x25] = v['facing']; visitor[3] = v['owner']; struct.pack_into('<H', visitor, 1, v['frame'])
        target = bytearray(0x40); target[0] = t['army']; target[0x0e] = t['template']; target[0x19] = t['condition']
        struct.pack_into('<II', target, 4, (v['x'] + t['dx']) & 0xffffffff, (v['y'] + t['dy']) & 0xffffffff)
        o.cpu.mem_write(o.POOL + 0x100, bytes(visitor)); o.cpu.mem_write(o.POOL + 0x200, bytes(target))
        o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT + v['army'] * 0x400 + v['template'] * 0xab))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x200)
        o.run(o.CODE + 0x902f)
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x40)) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'direction_table': list(objects[1]['payload'][0x19d7:0x19e0]),
            'cases': all_cases, 'results': {'visit': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
