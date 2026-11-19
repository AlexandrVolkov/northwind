#!/usr/bin/env python3
"""Execute the original message/diplomacy kernel under Unicorn on generated relation states.

Sending EDD5 (with EE69), first contact F250, war declaration F000/F057 (with EEFA, 21459, 23A6E,
1EAD8) and third-party reactions EF3F including its deferred re-entry. Handlers of other message
types are stubbed at their entries and recorded. Cases are stored in the fixture for C# replay.
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle
from forestry_source import source_constants

PARTICIPANTS = 8
TYPES = 30
KERNEL = {0xf250, 0xf000, 0xf057, 0xe65a}


class DiplomacyOracle(CombatOracle):
    def guard(self, cpu, address, size, user_data):
        # EFEC stores the object-relative return address EFF9 (CS-relative in the original flat model).
        if address == 0xeff9: cpu.reg_write(UC_X86_REG_EIP, self.CODE + 0xeff9); return
        CombatOracle.guard(self, cpu, address, size, user_data)

    RANGES = {'send': ([(0xedd5, 0xee38), (0xee69, 0xee92), (0xf250, 0xf28b), (0xf000, 0xf072), (0xeefa, 0xeffe),
                        (0x21459, 0x21490), (0x23a6e, 0x23a95), (0x1ead8, 0x1eaed), (0xe65a, 0xe65b)], [])}


def cases():
    rng = random.Random(2026)
    out = []
    for _ in range(1500):
        count = rng.choice([2, 3, 4, 6, 8])
        relations = [[0 if a == b else rng.choice([0, 0, 1, 1, 2, 3, 4, 5]) for b in range(count)] for a in range(count)]
        out.append({'count': count, 'relations': relations, 'hostile': [rng.choice([0, 0, 1 << rng.randrange(count)]) for _ in range(count)],
                    'dead': [rng.choice([0, 0, 0, 1]) for _ in range(count)], 'reputation': [rng.choice([0, 100, 2500, 9000]) for _ in range(count)],
                    'queued': [[[rng.choice([3, 5, 7, 20]), rng.randrange(count)] for _ in range(rng.choice([0, 0, 1, 3, 8]))] for _ in range(count)],
                    'type': rng.choice([3, 3, 5, 5, 16, 20]), 'sender': rng.randrange(count), 'other': rng.randrange(count),
                    'param': rng.randrange(1 << 20), 'time': rng.choice([0, 1000, 0xffffff00])})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = DiplomacyOracle(code, report['fs_assignments'], constants)
    le3 = Path(__file__).resolve().parents[1] / 'build/assembly/LE_3.bin'
    data = le3.read_bytes()
    table = bytearray(data[0xefb:0xefb + 0x13 * TYPES])
    handlers = sorted({struct.unpack_from('<I', table, 0x13 * k + 8)[0] for k in range(TYPES)})
    for k in range(TYPES): struct.pack_into('<I', table, 0x13 * k + 8, o.CODE + struct.unpack_from('<I', table, 0x13 * k + 8)[0])
    reactions = data[0xddb:0xddb + 72]
    calls = []

    def stub(address):
        def run():
            calls.append(['%X' % address, o.cpu.reg_read(UC_X86_REG_EBX) & 0xffff, bytes(o.cpu.mem_read(o.cpu.reg_read(UC_X86_REG_EDI), 14)).hex()]); o.ret()
        return run
    o.stubs = {h: stub(h) for h in handlers if h not in KERNEL}
    # LE:3 operands (fixups: EE05 -> 3:0EFB, F04C -> 3:0DDB, EF3F/EF4C/EFD0/EFE2 -> 3:0DD7) move to OBJ3 so they do not
    # overlap the LE:2 participant records in the flat emulator memory.
    OBJ3 = 0x8000
    for at, value in [(0xee05, 0xefb), (0xf04c, 0xddb), (0xef3f, 0xdd7), (0xef4c, 0xdd7), (0xefd0, 0xdd7), (0xefe2, 0xdd7)]:
        window = bytes(o.cpu.mem_read(o.CODE + at, 10)); k = window.index(struct.pack('<I', value))
        o.cpu.mem_write(o.CODE + at + k, struct.pack('<I', OBJ3 + value))
    o.mode = 'send'; all_cases = cases(); digest = hashlib.sha256()
    for case in all_cases:
        calls.clear(); n = case['count']
        o.cpu.mem_write(0x97a, bytes(0xf5 * 8)); o.cpu.mem_write(OBJ3 + 0xefb, bytes(table)); o.cpu.mem_write(OBJ3 + 0xddb, reactions)
        o.cpu.mem_write(0x6c6, bytes([n])); o.cpu.mem_write(OBJ3 + 0xdd7, bytes(4)); o.cpu.mem_write(0x1ca9, struct.pack('<I', case['time']))
        o.cpu.mem_write(o.PARTICIPANTS, bytes(0x1a35 * 8))
        for p in range(n):
            base = 0x97a + 0xf5 * p
            o.cpu.mem_write(base + 0x12, struct.pack('<I', case['hostile'][p])); o.cpu.mem_write(base + 0xb9, bytes(case['relations'][p]))
            queue = bytearray(b'\xff' + bytes(13)) * 8
            for slot, (t, other) in enumerate(case['queued'][p]): queue[slot * 14] = t; queue[slot * 14 + 9] = other
            o.cpu.mem_write(base + 0x34, bytes(queue))
            stats = o.PARTICIPANTS + 0x1a35 * p
            o.cpu.mem_write(stats + 5, bytes([case['dead'][p]])); o.cpu.mem_write(stats + 0x73, struct.pack('<I', case['reputation'][p]))
        o.cpu.reg_write(UC_X86_REG_EAX, case['type'] | (case['other'] << 8)); o.cpu.reg_write(UC_X86_REG_EBX, case['sender'] | (case['other'] << 8))
        o.cpu.reg_write(UC_X86_REG_EBP, case['param']); o.run(o.CODE + 0xedd5)
        state = b''.join(bytes(o.cpu.mem_read(0x97a + 0xf5 * p + 0x12, 4)) + bytes(o.cpu.mem_read(0x97a + 0xf5 * p + 0xb9, 16)) +
                         bytes(o.cpu.mem_read(0x97a + 0xf5 * p + 0x34, 112)) + bytes(o.cpu.mem_read(o.PARTICIPANTS + 0x1a35 * p + 0x73, 4)) for p in range(n))
        digest.update(state + json.dumps([o.cpu.reg_read(UC_X86_REG_EAX) & 255, calls]).encode())
    return {'format_version': 1, 'source_sha256': {'NORWIND.EXE': report['source_sha256']['NORWIND.EXE']},
            'type_table_sha256': hashlib.sha256(data[0xefb:0xefb + 0x13 * TYPES]).hexdigest(),
            'cases': all_cases, 'results': {'send': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
