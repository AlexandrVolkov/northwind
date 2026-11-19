#!/usr/bin/env python3
"""Execute the AI upkeep step LE:1 226F1..227C6 under Unicorn on generated settlement chains.

With +1A14 bit 2 every 128 steps (counter + 63h) living objects whose DAT[40] & 4800Ch = 8 lose one point (+19);
otherwise with bit 1 every 64 steps damaged buildings (DAT[45] - hp >= 5, record +0F bit 2) get state 48h in +14.
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from analyze_executable import analyze
from verify_combat import CombatOracle, unit, STRIDE
from forestry_source import source_constants

STATS = 0x1a35


class UpkeepOracle(CombatOracle):
    RANGES = {'upkeep': ([(0x226f1, 0x227c7), (0x68bd, 0x68d8), (0x6885, 0x68a0)], [])}


def cases(templates, full):
    rng = random.Random(226)
    out = []
    for _ in range(1500):
        objs = []
        for _ in range(rng.randint(0, 12)):
            t = rng.randrange(templates)
            hp = rng.choice([0, 1, 20, 127, 128, 200, (full[t] - rng.randint(3, 6)) & 255, (full[t] - rng.randint(3, 6)) & 255])
            objs.append([t, hp, rng.choice([0, 2, 3, 0x82]), rng.choice([0, 0x80, 0x81, 0x4f]), rng.choice([0, 0, 2048])])
        turn = rng.choice([(k * 64 - 0x63) & 0xffffffff for k in [1, 2, 3, 4]] + [rng.randrange(1 << 32)])
        out.append({'turn': turn, 'flags': rng.choice([0, 1, 2, 3]), 'objects': objs})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = UpkeepOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    count = struct.unpack_from('<H', dat, 0)[0] // STRIDE   # EXE 3593: u16 table byte count
    all_cases = cases(count, [dat[2 + i * STRIDE + 0x45] for i in range(count)]); digest = hashlib.sha256()
    o.prepare(32, 32)
    for case in all_cases:
        o.cpu.mem_write(0x97a, bytes(0xf5)); stats = o.PARTICIPANTS
        o.cpu.mem_write(stats, bytes(STATS)); o.cpu.mem_write(stats + 1, struct.pack('<I', case['turn'])); o.cpu.mem_write(stats + 0x1a14, bytes([case['flags']]))
        at = 0x100
        for t, hp, f, state, stock in case['objects']:
            rec = unit(t, 0, hp, 0, 48, 48, 1); rec[0x0f] = f; rec[0x14] = state; struct.pack_into('<I', rec, 0x24, stock)
            struct.pack_into('<I', rec, 0x10, 0x40); o.cpu.mem_write(o.POOL + at, bytes(rec)); at += 0x40
        o.cpu.mem_write(0x97a + 0xe, struct.pack('<I', 0x100)); o.cpu.mem_write(0x97a + 9, struct.pack('<I', at))
        o.mode = 'upkeep'; o.boundary = None; o.cpu.reg_write(__import__('unicorn').x86_const.UC_X86_REG_ESI, stats); o.run(o.CODE + 0x226f1)
        digest.update(bytes(o.cpu.mem_read(stats, 8)) + bytes(o.cpu.mem_read(o.POOL + 0x100, at - 0x100)))
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': hashlib.sha256(dat[2:2 + count * STRIDE]).hexdigest(), 'template_count': count, 'cases': all_cases,
            'results': {'upkeep': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
