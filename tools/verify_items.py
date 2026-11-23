#!/usr/bin/env python3
"""Verify the item machinery of LE:1 with Unicorn: the per-update loop 12383 over the participants' inventories (+CD, 20 slots), the pickup 1243A, the search 124D8 and the
handlers of the item table (LE:3 +4191, 25-byte entries): 11EB0 / 11ECB (cargo kinds 1, 2), 11EE6 (gold), 11EFF (manuscripts), 11F5B (staffs, stones), 11F64 (curse of Rufin),
11FC8 (of Onis), 12022 (of Irvil), 1217E / 121E5 (stones 6, 14). The object address service 6885, the sounds and the message are recorded stubs; the random streams (9E41, 9E55,
9E69), the flight request 8E0E and the statistics address 1EAD8 run as original code."""
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

HANDLED = [1, 2, 4, 5, 6, 7, 9, 10, 11, 13, 14, 15, 17, 18, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 34]
STUBBED = {0x5593: 'M', 0xa4a9: 'S'}


class ItemOracle(CombatOracle):
    RANGES = {'items': ([(0x12383, 0x123d3), (0x1243a, 0x124a5), (0x124d8, 0x124fd), (0x11eb0, 0x1205c), (0x1217e, 0x12198), (0x121e5, 0x121ff),
                         (0x1ead8, 0x1eaed), (0x8e0e, 0x8e3c), (0x9e41, 0x9e7d)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=200000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('items did not return')


def cases():
    rng = random.Random(0x12383)
    out = []
    for n in range(900):
        kind = rng.choice(['pickup', 'pickup', 'loop', 'loop', 'loop'])
        inventory = [rng.choice([0, 0, 0] + HANDLED) for _ in range(20)]
        case = {'kind': kind, 'inventory': inventory, 'owner': rng.choice([0, 1, 2]), 'player': rng.choice([0, 1, 2]), 'flag': rng.choice([1, 1, 1, 0]),
                'count': rng.choice([1, 2, 3]), 'tick': rng.choice([0, 0, 0, 1, 255, rng.randrange(256)]), 'global_1ec5': rng.randrange(256),
                'gold': rng.randrange(1 << 20), 'wood': rng.randrange(1 << 20), 'stats': rng.randrange(1 << 16), 'timer': rng.choice([0, 1, 2, 10000, rng.randrange(1 << 16)]),
                'random_start': [rng.randrange(256) for _ in range(3)], 'map': [rng.choice([32, 40, 64]), rng.choice([32, 48])]}
        if kind == 'pickup': case['item'] = rng.choice(HANDLED + [0, 0])
        case['objects'] = [{'army': rng.choice([0, 1, 2]), 'flags': rng.choice([0, 8, 8, 0x20, 0, 0x2008]), 'level': rng.randrange(256), 'state': rng.randrange(256), 'extra': rng.randrange(256)}
                           for _ in range(rng.choice([0, 1, 3, 6]))]
        case['updates'] = rng.choice([1, 1, 2])
        out.append(case)
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = ItemOracle(code, report['fs_assignments'], constants)
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    table = objects[2]['payload'][0x4191:0x4191 + 0x19 * 36]
    table = bytearray(table)
    for k in range(36):   # the loader's relocation: the handler pointers (+4) are offsets in LE:1
        h = struct.unpack_from('<I', table, 0x19 * k + 4)[0]
        if h: struct.pack_into('<I', table, 0x19 * k + 4, o.CODE + h)
    table = bytes(table)
    o.cpu.mem_write(0x4191, table)
    log = []; state = {}
    def stub_flags():
        r = o.cpu.reg_read; esi = r(UC_X86_REG_ESI)
        flags = struct.unpack('<I', bytes(o.cpu.mem_read(esi + 0x1c, 4)))[0]   # the test object's flags stand in for the template of 6885
        o.cpu.mem_write(o.POOL + 0x1000, struct.pack('<I', 0) * 0x20); o.cpu.mem_write(o.POOL + 0x1000 + 0x40, struct.pack('<I', flags))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x1000); log.append(b'T' + struct.pack('<I', esi - o.POOL)); o.ret()
    def message():
        log.append(b'M'); o.ret()
    def sound():
        log.append(b'S' + struct.pack('<B', o.cpu.reg_read(UC_X86_REG_EAX) & 255)); o.ret()
    o.stubs = {0x6885: stub_flags, 0x5593: message, 0xa4a9: sound}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(*case['map']); o.mode = 'items'
        o.cpu.mem_write(o.POOL, bytes(0x1000))
        o.cpu.mem_write(0x4191, table)
        o.cpu.mem_write(0x1c4e, bytes([case['flag']])); o.cpu.mem_write(0x6c6, bytes([case['count']])); o.cpu.mem_write(0x1972, bytes([case['player']]))
        o.cpu.mem_write(0x19e9, bytes([case['tick']])); o.cpu.mem_write(0x19ea, bytes([case['tick'] ^ 0x5a])); o.cpu.mem_write(0x1ec5, bytes([case['global_1ec5']]))
        o.cpu.mem_write(0x19eb, bytes(case['random_start']))
        o.cpu.mem_write(0x6ba, struct.pack('<I', o.PARTICIPANTS))
        # the participants: inventory +CD, wood +18, gold +20, first object +0E, end +09 (offsets into the object pool)
        for owner in range(3):
            base = 0x97a + 0xf5 * owner
            part = bytearray(0xf5)
            inv = case['inventory'] if owner == case['owner'] else [(i * 7 + owner) % 3 and case['inventory'][i] for i in range(20)]
            part[0xcd:0xcd + 20] = bytes(inv)
            struct.pack_into('<I', part, 0x18, case['wood']); struct.pack_into('<I', part, 0x20, case['gold'])
            count = len(case['objects']); struct.pack_into('<I', part, 0x0e, 0x100); struct.pack_into('<I', part, 0x09, 0x100 + 0x40 * count)
            o.cpu.mem_write(base, bytes(part))
        for i, obj in enumerate(case['objects']):
            raw = bytearray(0x40); raw[0] = obj['army']; raw[3] = case['owner']; raw[0x16] = obj['level']; raw[0x2d] = obj['state']; raw[0x17] = obj['extra']
            struct.pack_into('<I', raw, 0x10, 0x40); struct.pack_into('<I', raw, 0x1c, obj['flags'])
            o.cpu.mem_write(o.POOL + 0x100 + 0x40 * i, bytes(raw))
        for k in range(3):
            for off in (0x1a08, 0x1a0c): o.cpu.mem_write(o.PARTICIPANTS + 0x1a35 * k + off, struct.pack('<I', case['stats']))
        entry = struct.pack('<I', case['timer'])
        for code_ in range(36): o.cpu.mem_write(0x4191 + 0x19 * code_ + 0x15, entry)
        regs = {}
        for _ in range(case['updates']):
            if case['kind'] == 'loop':
                o.cpu.reg_write(UC_X86_REG_EAX, 0); o.run(o.CODE + 0x12383)
            else:
                o.cpu.reg_write(UC_X86_REG_EAX, (case['owner'] << 8) | case['item']); o.cpu.reg_write(UC_X86_REG_EBX, 0x12345600 | case['item'])
                o.run(o.CODE + 0x1243a); regs['eax'] = o.cpu.reg_read(UC_X86_REG_EAX) & 0xff; regs['zf'] = (o.cpu.reg_read(UC_X86_REG_EFLAGS) >> 6) & 1
            o.cpu.mem_write(0x19e9, bytes([(case['tick'] + 1) & 255]))
        out = b''.join(bytes(o.cpu.mem_read(0x97a + 0xf5 * k, 0xf5)) for k in range(3)) + b''.join(bytes(o.cpu.mem_read(0x4191 + 0x19 * k + 0x15, 4)) for k in range(36)) \
            + bytes(o.cpu.mem_read(o.POOL + 0x100, 0x40 * len(case['objects']))) \
            + b''.join(bytes(o.cpu.mem_read(o.PARTICIPANTS + 0x1a35 * k + off, 4)) for k in range(3) for off in (0x1a08, 0x1a0c)) \
            + bytes(o.cpu.mem_read(0x19eb, 3)) + json.dumps(regs, sort_keys=True).encode() + b''.join(log)
        if __import__('os').environ.get('DEBUG_CASE') == str(all_cases.index(case)): print(out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'random_values': constants['random_values'], 'cases': all_cases,
            'results': {'items': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
