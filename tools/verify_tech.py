#!/usr/bin/env python3
"""Execute the development-tree routines of LE:1 under Unicorn on mutated per-settlement copies of ARMY__0x.TRE.

A673 (template open), A6AE with A6F9 (complete an item, lower the conditions of its children), A716 (open research items),
223B4 (templates of a DAT class, optionally only open ones; army 2 DAT). Sequences of operations run on one copy.
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

MASKS = [(0x28, 8), (0xf08, 0x808), (0xf08, 0x208), (0xf08, 0x408), (0, 0), (0x808, 0x800)]


class TechOracle(CombatOracle):
    RANGES = {'tech': ([(0xa673, 0xa6ae), (0xa6ae, 0xa716), (0xa716, 0xa758), (0x223b4, 0x22435), (0x6885, 0x68a0)], [])}


def cases(trees):
    rng = random.Random(1999)
    out = []
    for _ in range(1500):
        army = rng.choice([0, 1, 2, 2, 2]); records, _ = trees[army]
        mutations = [[rng.random() < 0.3, rng.choice([0, 0, 1, 2, 3, 0x80, 0x81, 0xff]) if rng.random() < 0.4 else None] for _ in records]
        ops = []
        for _ in range(rng.randint(1, 6)):
            kind = rng.choice(['available', 'complete', 'complete', 'researchable', 'buildable'])
            if kind == 'available': ops.append(['available', rng.randrange(30)])
            elif kind == 'complete':
                r = rng.choice(records); ops.append(['complete', r['kind'] & rng.choice([0x7f, 0xff]), r['id'] if rng.random() < 0.9 else rng.randrange(40)])
            elif kind == 'researchable': ops.append(['researchable'])
            elif army == 2: ops.append(['buildable', rng.choice([0, 1]), *rng.choice(MASKS)])
        out.append({'army': army, 'owner': rng.randrange(8), 'mutations': [[int(f), u] for f, u in mutations], 'ops': ops})
    return out


def mutate(data, records, mutations):
    data = bytearray(data)
    for r, (flip, unmet) in zip(records, mutations):
        if flip: data[r['offset']] ^= 0x80
        if unmet is not None: data[r['offset'] + 2] = unmet
    return data


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = TechOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2)); o.cpu.mem_write(0x6ca + 2 * 0x56 + 0x55, bytes([struct.unpack_from('<H', dat)[0] // STRIDE]))
    raw = [(game / f'ARMY__0{k}.TRE').read_bytes() for k in range(3)]
    trees = [decode(d) for d in raw]
    all_cases = cases(trees); digest = hashlib.sha256(); outputs = {'available': 0, 'complete': 0, 'researchable': 0, 'buildable': 0}
    for case in all_cases:
        army, owner = case['army'], case['owner']; records, end = trees[army]
        tree = o.WRK + 0x400 * owner
        o.cpu.mem_write(tree, bytes(mutate(raw[army], records, case['mutations'])))
        o.cpu.mem_write(0x6ca + army * 0x56 + 0x15 + 4 * owner, struct.pack('<I', tree))
        result = []
        for op in case['ops']:
            o.mode = 'tech'; o.boundary = None; outputs[op[0]] += 1
            if op[0] == 'available':
                o.cpu.reg_write(UC_X86_REG_EAX, op[1] | (army << 8)); o.cpu.reg_write(UC_X86_REG_EBX, owner); o.run(o.CODE + 0xa673)
                result.append(o.zf())
            elif op[0] == 'complete':
                o.cpu.mem_write(o.POOL + 0x100, bytes(unit(0, owner, 10, 0, 16, 16, 1)[:3]) + bytes([owner]))
                o.cpu.mem_write(o.POOL + 0x100, bytes([army])); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100)
                o.cpu.reg_write(UC_X86_REG_EAX, op[2] | (op[1] << 8)); o.run(o.CODE + 0xa6ae)
            elif op[0] == 'researchable':
                o.cpu.mem_write(0x1f45, b'\x55' * 0x100)
                o.cpu.reg_write(UC_X86_REG_EAX, owner | (army << 8)); o.run(o.CODE + 0xa716)
                n = o.cpu.reg_read(UC_X86_REG_EAX) & 255; result.append([n, bytes(o.cpu.mem_read(0x1f45, 16 * n)).hex()])
            else:
                o.cpu.mem_write(0x1f45, b'\x55' * 0x40)
                o.cpu.reg_write(UC_X86_REG_EAX, op[1] | (army << 8)); o.cpu.reg_write(UC_X86_REG_EBX, owner)
                o.cpu.reg_write(UC_X86_REG_ECX, op[2]); o.cpu.reg_write(UC_X86_REG_EDX, op[3]); o.run(o.CODE + 0x223b4)
                n = o.cpu.reg_read(UC_X86_REG_EAX) & 255; result.append(list(o.cpu.mem_read(0x1f45, n + 1)))
        digest.update(bytes(o.cpu.mem_read(tree, end + 2)) + json.dumps(result).encode())
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'tree_sha256': [hashlib.sha256(d[:t[1] + 2]).hexdigest() for d, t in zip(raw, trees)], 'operations': outputs, 'cases': all_cases,
            'results': {'tech': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
