#!/usr/bin/env python3
"""Execute squad step 4, march in formation (LE:1 1EF26), under Unicorn.

Covered: 103AF formation choice (LE:3 table 3D45), 1037E/1030B scaling, 6996 direction, 10262 rotation (DS:2A7E half sine
table), B942 command reset, 10075/100FA/101B4 placement of members along the formation segments. A4C2 (message) is
stubbed and recorded. The LE:3 table, its records and the variables 3D20..3D44 live at their flat addresses; LE:2 tables
(287E arctangent, 2A7E sine) as well.
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_combat import CombatOracle, unit, STRIDE
from forestry_source import source_constants

TEMPLATES = [0, 1, 17, 18, 27, 10]
SQUAD = 0x52


class FormationOracle(CombatOracle):
    RANGES = {'formation': ([(0x1ef26, 0x1efb8), (0x10075, 0x103ec), (0xb942, 0xb977), (0x6996, 0x69fe)], [])}


def cases():
    rng = random.Random(1926)
    out = []
    for n in range(3000):
        ident = rng.randrange(1, 256)
        def cell(): return [rng.randrange(32), rng.randrange(32)]
        members = []
        for _ in range(rng.choice([0, 1, 3, 6, 12, 25])):
            members.append([rng.choice(TEMPLATES), rng.choice([ident, ident, ident, 0, rng.randrange(256)]),
                            rng.choice([0, 1, 0x41, 0x48, 0x63]), rng.randrange(1024), rng.randrange(32), rng.randrange(32)])
        count = rng.choice([len([m for m in members if m[1] == ident]), rng.choice([1, 2, 9, 30, 100, 600])])
        out.append({'participant': rng.randrange(4), 'id': ident, 'kind': rng.choice([1, 1, 2, 3, 0, 4]), 'goal': cell(),
                    'from': cell() if rng.random() < 0.8 else [rng.randrange(256), rng.randrange(256)],
                    'centre': cell(), 'count': count, 'stage': rng.choice([0, 0, 0, 1, 2]), 'timer': rng.choice([0, 0x100, 5, 0x200, 0x1ff]),
                    'moving': rng.choice([0, 1, 3]), 'members': members, 'salt': rng.randrange(256)})
        if rng.random() < 0.15: out[-1]['goal'] = rng.choice([[0, 0], [31, 31], [0, 31], [31, 0], [1, 30]])
    return out


def squad(case):
    s = bytearray((i * 13 + case['salt']) & 255 for i in range(SQUAD))
    s[0] = case['participant']; s[1] = case['id']; s[8] = case['kind']; s[9], s[0x0a] = case['goal']; s[0x0b], s[0x0c] = case['from']
    struct.pack_into('<I', s, 0x1b, case['timer']); s[0x20], s[0x21] = case['centre']; struct.pack_into('<I', s, 0x22, case['count'])
    s[0x43] = case['moving']; s[0x4c] = case['stage']
    return s


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    o = FormationOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    le2, le3 = objects[1]['payload'], objects[2]['payload']
    o.cpu.mem_write(0x287e, le2[0x287e:0x297e]); o.cpu.mem_write(0x2a7e, le2[0x2a7e:0x2abe])
    table = le3[0x3d45:0x3e40]
    log = []
    def message():
        log.append(b'M' + struct.pack('<I', o.cpu.reg_read(UC_X86_REG_EAX))); o.ret()
    o.stubs[0xa4c2] = message
    all_cases = cases(); digest = hashlib.sha256(); SQ = 0x3f000
    for case in all_cases:
        o.prepare(32, 32); p = case['participant']; log.clear()
        o.cpu.mem_write(0x3d20, bytes(0x25) + table)   # variables, then the table and its records
        at = 0x100
        for t, sq, f14, f17, x, y in case['members']:
            rec = unit(t, p, 10, f17, x * 32 + 7, y * 32 + 19, 1) + bytes(10)
            rec[0x1b] = sq; rec[0x14] = f14; struct.pack_into('<I', rec, 0x10, 0x40)
            o.cpu.mem_write(o.POOL + at, bytes(rec)); at += 0x40
        o.cpu.mem_write(o.POOL + at, bytes(0x40))
        participant = 0x97a + 0xf5 * p
        o.cpu.mem_write(participant + 0xe, struct.pack('<I', 0x100)); o.cpu.mem_write(participant + 9, struct.pack('<I', at))
        o.cpu.mem_write(o.POOL + SQ, bytes(squad(case)))
        o.mode = 'formation'; o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + SQ); o.run(o.CODE + 0x1ef26)
        al = o.cpu.reg_read(UC_X86_REG_EAX) & 0xff
        out = bytes(o.cpu.mem_read(o.POOL + SQ, SQUAD)) + bytes(o.cpu.mem_read(o.POOL + 0x100, at - 0x100)) + b''.join(log) + bytes([al != 0])
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'formation': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
