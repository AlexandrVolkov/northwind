#!/usr/bin/env python3
"""Verify the use and the drop of items (LE:1 124FD use at a point or an object, 12546 / 12588 drop one / all items of a participant on a cell) and the AH = 3 handlers
of the lightnings 31..33 (12256, 12296, 122D6), the disease 19 (120AD) and the phantom jug 35 (12316) with Unicorn. The object creation A758, the placement check 10AD2,
the object set-up 43E5 and the effect creation 9727 are recorded stubs; 68A0 / 68BD / 124D8 are original code."""
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

USABLE = [19, 31, 32, 33, 35]


class UseOracle(CombatOracle):
    RANGES = {'use': ([(0x124fd, 0x12588 + 0x46), (0x124d8, 0x124fd), (0x68a0, 0x68d8), (0x12256, 0x12316 + 0x6d), (0x120ad, 0x120e2)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=50000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('use did not return')


def cases():
    rng = random.Random(0x124fd)
    out = []
    for n in range(1500):
        kind = rng.choice(['use', 'use', 'drop', 'drop_all'])
        case = {'kind': kind, 'owner': rng.choice([0, 1, 2]), 'inventory': [rng.choice([0, 0, 1, 6, 7, 12, 14, 19, 31, 32, 33, 35, 9, 20]) for _ in range(20)],
                'cell': [rng.randrange(2, 60), rng.randrange(2, 60)], 'classes': [rng.choice([0, 0, 4, 9, 11, 0x0c]) for _ in range(36)], 'phantom': rng.choice([0, 1, 2, 9, 5]),
                'army_class': [rng.choice([3, 3, 1, 0]) for _ in range(4)], 'template_flags': [rng.choice([0, 1, 2, 3]) for _ in range(8)],
                'target': {'army': rng.choice([0, 1, 2, 3]), 'template': rng.choice([0x1b, 0x1b, 3, 0, 5]), 'layer': rng.choice([0, 1, 2, 3]), 'x': rng.randrange(64, 4000), 'y': rng.randrange(64, 4000)},
                'placement_zf': rng.choice([0, 1]), 'create_zf': rng.choice([0, 1])}
        if kind == 'use':
            case['item'] = rng.choice(USABLE)
            if rng.random() < 0.85: case['inventory'][rng.randrange(20)] = case['item']
        if kind == 'drop': case['item'] = rng.choice([1, 6, 7, 9, 12, 14, 20, 21, 4, 5])
        out.append(case)
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = UseOracle(code, report['fs_assignments'], constants)
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    table = bytearray(objects[2]['payload'][0x4191:0x4191 + 0x19 * 36])
    for k in range(36):
        h = struct.unpack_from('<I', table, 0x19 * k + 4)[0]
        if h: struct.pack_into('<I', table, 0x19 * k + 4, o.CODE + h)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def create():   # A758: al army, ah owner, bl template, bh layer, ecx x, edx y; the stack argument is the object count
        log.append(b'C' + struct.pack('<BBBBII', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255, reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x800)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if state['create_zf'] else 0)); o.ret()
    def placement():   # 10AD2
        log.append(b'K' + struct.pack('<BBBII', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255, reg(UC_X86_REG_EBX) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x800)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0 if state['placement_zf'] else 0x40)); o.ret()
    def setup():
        log.append(b'I' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.ret()
    def effect():
        log.append(b'E' + struct.pack('<BBBII', reg(UC_X86_REG_EAX) & 255, reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX))
                   + bytes(o.cpu.mem_read(reg(UC_X86_REG_ESP) + 4, 4))); o.ret()
    o.stubs = {0xa758: create, 0x10ad2: placement, 0x43e5: setup, 0x9727: effect}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state.update(placement_zf=case['placement_zf'], create_zf=case['create_zf']); o.prepare(32, 32); o.mode = 'use'
        o.cpu.mem_write(0x6ba, struct.pack('<I', o.PARTICIPANTS)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL))
        t = bytearray(table)
        for k in range(36): t[0x19 * k + 0x0c] = case['classes'][k]
        t[0x19 * 35 + 0x0e] = case['phantom']
        o.cpu.mem_write(0x4191, bytes(t))
        for army in range(4):
            o.cpu.mem_write(0x6ca + 0x56 * army + 4, struct.pack('<I', o.DAT + army * 0x1000)); o.cpu.mem_write(0x6ca + 0x56 * army + 8, bytes([case['army_class'][army]]))
            for index in range(8):
                raw = bytearray(0xab); raw[0x77] = case['template_flags'][index]; o.cpu.mem_write(o.DAT + army * 0x1000 + index * 0xab, bytes(raw))
        for owner in range(3):
            part = bytearray(0xf5); part[0xcd:0xcd + 20] = bytes(case['inventory'] if owner == case['owner'] else [0] * 20); o.cpu.mem_write(0x97a + 0xf5 * owner, bytes(part))
        g = case['target']; target = bytearray(0x40); target[0] = g['army']; target[0x0e] = g['template']; target[0x0d] = g['layer']; struct.pack_into('<II', target, 4, g['x'], g['y'])
        o.cpu.mem_write(o.POOL + 0x400, bytes(target))
        cx, cy = case['cell']
        if case['kind'] == 'use':
            o.cpu.reg_write(UC_X86_REG_EAX, (case['owner'] << 8) | case['item']); o.cpu.reg_write(UC_X86_REG_ECX, (cy << 8) | cx); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x400)
            o.cpu.reg_write(UC_X86_REG_ECX, (cy << 8) | cx); o.run(o.CODE + 0x124fd)
        elif case['kind'] == 'drop':
            o.cpu.reg_write(UC_X86_REG_EAX, (case['owner'] << 8) | case['item']); o.cpu.reg_write(UC_X86_REG_ECX, (cy << 8) | cx); o.run(o.CODE + 0x12546)
        else:
            o.cpu.reg_write(UC_X86_REG_EAX, case['owner'] << 8); o.cpu.reg_write(UC_X86_REG_ECX, (cy << 8) | cx); o.run(o.CODE + 0x12588)
        out = b''.join(bytes(o.cpu.mem_read(0x97a + 0xf5 * k + 0xcd, 20)) for k in range(3)) + b''.join(bytes(o.cpu.mem_read(0x4191 + 0x19 * k + 0x0e, 5)) for k in (19, 31, 32, 33, 35)) \
            + bytes(o.cpu.mem_read(o.POOL + 0x400, 0x40)) + b''.join(log)
        if __import__('os').environ.get('DEBUG_CASE') == str(all_cases.index(case)): print(out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases,
            'results': {'use': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
