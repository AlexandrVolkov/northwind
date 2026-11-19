#!/usr/bin/env python3
"""Execute the original death path under Unicorn; store hashed results.

  dispatch  LE:1 95D3: special death 12D08 (DAT flag 100000h), death voice DAT[A7], then A4DF
  death     LE:1 A4DF..A5B7: first step (snap 9A8E, clear occupancy 43B0, death frames by hit kind),
            frame countdown, corpse object DAT[53 + kind] through 9727 unless on water, removal 98DB
Collaborators are stubbed at their entries and recorded with their arguments.
"""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, unit, STRIDE
from forestry_source import source_constants


class DeathOracle(CombatOracle):
    RANGES = {
        'dispatch': ([(0x95d3, 0x960b)], []),
        'death': ([(0xa4df, 0xa5b8), (0x6885, 0x68a0), (0xd12c, 0xd157)], []),
    }


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = DeathOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    fixture = {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
               'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in [0, 1, 18, 20]}, 'results': {}}
    calls = []

    def save(name, digest, count):
        fixture['results'][name] = {'case_count': count, 'sha256': digest.hexdigest()}; print(name, count, flush=True)

    def rec(name, *registers):
        def stub():
            calls.append([name] + [o.cpu.reg_read(globals()['UC_X86_REG_' + r.upper()]) for r in registers]); o.ret(eax=o.cpu.reg_read(UC_X86_REG_EAX))
        return stub

    o.prepare(8, 8); o.terrain(8, 8, [i % 8 for i in range(64)])
    o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT + 2))
    o.stubs = {0x12d08: rec('12D08'), 0xa416: rec('A416', 'eax'), 0xa4df: rec('A4DF')}
    spec = {'flags': [0, 0x100000], 'health': [0, 1, 0xf4, 0xf3, 0x80], 'marks': [0, 0x80]}
    fixture['dispatch'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'dispatch'
    for flags, hp, marks in product(*(spec[k] for k in ['flags', 'health', 'marks'])):
        base = o.DAT + 2; o.cpu.mem_write(base, template(0)); o.cpu.mem_write(base + 0x40, struct.pack('<I', 0x1800 | flags))
        o.cpu.mem_write(base + 0xa7, struct.pack('<I', 0x5678)); calls.clear()
        raw = unit(0, 0, hp, marks); o.cpu.mem_write(o.POOL + 1, bytes(raw)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.run(o.CODE + 0x95d3)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 54)) + bytes([o.cpu.reg_read(UC_X86_REG_EAX) & 255]) + json.dumps(calls).encode()); count += 1
    o.cpu.mem_write(o.DAT + 2, template(0)); save('dispatch', digest, count)

    def corpse():
        r = lambda n: o.cpu.reg_read(globals()['UC_X86_REG_' + n])
        calls.append(['9727', r('EAX') & 255, r('EBX') & 0xffff, r('ECX'), r('EDX')]); o.ret()
    o.stubs = {0x9a8e: rec('9A8E'), 0x43b0: rec('43B0', 'eax'), 0x9727: corpse, 0x98db: rec('98DB')}
    spec = {'templates': [0, 1, 18, 20], 'health': [0, 1, 0xfc, 0xf4, 0xf3, 0xf1, 0xf0, 0xfb, 0xfa, 0xef], 'marks': [0, 0x08, 0x10, 0x18, 0x80],
            'counters': [0, 1, 2], 'parity': [0, 1], 'headings': [0, 3, 7, 255], 'cells': [[0, 0], [1, 0], [2, 0], [5, 3]],
            'corpses': [[0xff, 0xff, 0xff, 0xff], [4, 5, 6, 7]], 'kinds': [[3, 0, 2, 0], [0, 0, 0, 0]]}
    fixture['death'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'death'
    for index, hp, marks, counter, parity, heading, (cx, cy), corpses, kinds in product(*(spec[k] for k in [
            'templates', 'health', 'marks', 'counters', 'parity', 'headings', 'cells', 'corpses', 'kinds'])):
        base = o.DAT + 2 + index * STRIDE; o.cpu.mem_write(base, template(index))
        o.cpu.mem_write(base + 0x4f, bytes(kinds)); o.cpu.mem_write(base + 0x53, bytes(corpses)); calls.clear()
        raw = unit(index, 0, hp, marks, cx * 32 + 16, cy * 32 + 16, 1); raw[0x15] = counter; raw[0x2e] = parity; raw[0x25] = heading
        o.cpu.mem_write(o.POOL + 1, bytes(raw)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.run(o.CODE + 0xa4df)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 54)) + json.dumps(calls).encode()); count += 1
        o.cpu.mem_write(base, template(index))
    save('death', digest, count)
    return fixture


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game), indent=2) + '\n', encoding='utf-8')
