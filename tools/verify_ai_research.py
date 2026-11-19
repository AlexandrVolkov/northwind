#!/usr/bin/env python3
"""Execute the AI development step LE:1 23C11..23DC1 (with 1ECA0, 22E46, 223B4, A673, 1EB5C, 9E41) under Unicorn.

Every 256 steps (counter + 85h) a settlement with +53 starts its laboratory (DAT class 808h: +22 = +23 = 5), orders one
(mode 5 in +34F) when the tree allows, or plans to open it (mode 2 in +83B). List +83B is then resolved through the
development tree: mode 2 picks a random parent of the wanted item, mode 1 of kind 1 orders that building.
Register leftovers are masked: +34F bytes +4..+7, +0A, +0B and +83B bytes +4..+7.
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, unit, STRIDE
from forestry_source import source_constants
from tech_source import decode

STATS, ORDERS, REQUESTS = 0x1a35, 0x34f, 0x83b


class ResearchOracle(CombatOracle):
    RANGES = {'research': ([(0x23c11, 0x23dc2), (0x1eca0, 0x1ecf8), (0x22e46, 0x22e85), (0x223b4, 0x22435), (0xa673, 0xa6ae),
                            (0x1eaed, 0x1eb68), (0x9e41, 0x9e55), (0x68bd, 0x68d8), (0x6885, 0x68bd)], [])}


def cases(records):
    rng = random.Random(23)
    out = []
    for _ in range(2000):
        mutations = [[rng.random() < 0.3, rng.choice([0, 0, 1, 2, 3]) if rng.random() < 0.5 else None] for _ in records]
        requests = []
        for _ in range(rng.choice([0, 1, 3, 20])):
            mode = rng.choice([1, 2, 2, 2, 3, 0xff])
            r = rng.choice(records)
            kind, ident = (r['kind'] & rng.choice([0x7f, 0xff]), r['id']) if rng.random() < 0.85 else (rng.choice([0, 1, 0x80]), rng.randrange(30))
            requests.append([mode, kind, ident, rng.randrange(1 << 32)])
        orders = [[5, rng.choice([0x6a, 0x61, 3])] for _ in range(rng.choice([0, 0, 1, 20]))]
        objs = [[rng.choice([12, 12, 2, 3, 0]), rng.choice([0, 1, 9, 10, 127]), rng.choice([0, 0x80])] for _ in range(rng.choice([0, 0, 1, 3]))]
        out.append({'me': rng.randrange(8), 'turn': rng.choice([(0x100 - 0x85) & 0xffffffff, (0x200 - 0x85), rng.randrange(1 << 32)]),
                    'flags': rng.choice([0, 1, 1, 3]), 'b53': rng.choice([0, 1, 1]), 'random': rng.randrange(256),
                    'mutations': [[int(f), u] for f, u in mutations], 'requests': requests, 'orders': orders, 'objects': objs})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = ResearchOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2)); o.cpu.mem_write(0x6ca + 2 * 0x56 + 0x55, bytes([struct.unpack_from('<H', dat)[0] // STRIDE]))
    raw = (game / 'ARMY__02.TRE').read_bytes(); records, end = decode(raw)
    o.prepare(32, 32)
    all_cases = cases(records); digest = hashlib.sha256(); stats_seen = {}
    for case in all_cases:
        me = case['me']; tree = o.WRK + 0x400 * me
        data = bytearray(raw)
        for r, (flip, unmet) in zip(records, case['mutations']):
            if flip: data[r['offset']] ^= 0x80
            if unmet is not None: data[r['offset'] + 2] = unmet
        o.cpu.mem_write(tree, bytes(data)); o.cpu.mem_write(0x6ca + 2 * 0x56 + 0x15 + 4 * me, struct.pack('<I', tree))
        o.cpu.mem_write(0x97a, bytes(0xf5 * 8))
        for p in range(8): o.cpu.mem_write(0x97a + 0xf5 * p, b'\x02')
        b = bytearray(STATS); b[0] = me; struct.pack_into('<I', b, 1, case['turn']); b[0x1a34] = case['flags']; b[0x53] = case['b53']
        for k in range(20): b[ORDERS + k * 0x15] = 0xff; b[REQUESTS + k * 0x15] = 0xff
        for k, (mode, first) in enumerate(case['orders']): b[ORDERS + k * 0x15] = mode; b[ORDERS + k * 0x15 + 1] = first
        for k, (mode, kind, ident, timer) in enumerate(case['requests']):
            at = REQUESTS + k * 0x15; b[at] = mode; b[at + 8] = kind; b[at + 9] = ident; b[at + 0xa] = 2; struct.pack_into('<I', b, at + 0x10, timer)
        stats = o.PARTICIPANTS + STATS * me; o.cpu.mem_write(stats, bytes(b))
        at = 0x100
        for t, hp, state in case['objects']:
            rec = unit(t, me, hp, 0, 48, 48, 1); rec[0x14] = state; struct.pack_into('<I', rec, 0x10, 0x40); o.cpu.mem_write(o.POOL + at, bytes(rec).ljust(0x40, b'\0')); at += 0x40
        base = 0x97a + 0xf5 * me
        o.cpu.mem_write(base + 0xe, struct.pack('<I', 0x100)); o.cpu.mem_write(base + 9, struct.pack('<I', at))
        o.cpu.mem_write(0x19eb, bytes([case['random']]))
        o.cpu.mem_write(0xed20, struct.pack('<I', stats)); o.cpu.mem_write(0xed2c, bytes([me])); o.cpu.mem_write(0xed2d, struct.pack('<I', base))
        o.cpu.reg_write(UC_X86_REG_ESI, stats)
        o.mode = 'research'; o.boundary = None; o.run(o.CODE + 0x23c11)
        out = bytearray(o.cpu.mem_read(stats, STATS))
        for k in range(20):
            a = ORDERS + k * 0x15; out[a + 4:a + 8] = bytes(4); out[a + 0xa:a + 0xc] = bytes(2)
            a = REQUESTS + k * 0x15; out[a + 4:a + 8] = bytes(4)
        digest.update(bytes(out) + bytes(o.cpu.mem_read(tree, end + 2)) + bytes(o.cpu.mem_read(o.POOL + 0x100, at - 0x100)) + bytes(o.cpu.mem_read(0x19eb, 1)))
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'tree_sha256': hashlib.sha256(raw[:end + 2]).hexdigest(), 'cases': all_cases,
            'results': {'research': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
