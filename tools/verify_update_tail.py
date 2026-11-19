#!/usr/bin/env python3
"""Execute the per-object tail of the object update loop LE:1 434D..4384 under Unicorn.

After every AH=1 update 42BB copies the condition OBE[19] to OBE[1A], forgets a target that is
removed (+17 & 200h) or dead (i8 +19 < 0), and every eighth global tick clears OBE[17] bits 0,1,2,5,9.
"""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, unit
from forestry_source import source_constants


class TailOracle(CombatOracle):
    RANGES = {'tail': ([(0x434d, 0x4384)], [])}


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = TailOracle(code, report['fs_assignments'], constants)
    o.prepare(8, 8); o.mode = 'tail'; o.boundary = 0x4384
    spec = {'health': [0, 1, 10, 127, 128, 255], 'previous': [0, 10], 'targets': [0, 1], 'target_flags': [0, 0x200, 0xfdff, 0xffff],
            'target_health': [0, 1, 127, 128, 255], 'flags': [0, 0x0227, 0xffff], 'ticks': [0, 1, 7, 8]}
    digest = hashlib.sha256(); count = 0
    for hp, previous, has_target, tflags, thp, flags, tick in product(*spec.values()):
        raw = unit(0, 0, hp, flags); raw[0x1a] = previous; struct.pack_into('<I', raw, 0x1c, 0x100 if has_target else 0)
        o.cpu.mem_write(o.POOL + 1, bytes(raw)); o.cpu.mem_write(o.POOL + 0x100, bytes(unit(0, 3, thp, tflags)))
        o.cpu.mem_write(0x19ea, bytes([tick])); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.run(o.CODE + 0x434d)
        if not o.stopped: raise ValueError('Tail did not reach 4384')
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 54))); count += 1
    return {'format_version': 1, 'source_sha256': {'NORWIND.EXE': report['source_sha256']['NORWIND.EXE']}, 'tail': spec,
            'results': {'tail': {'case_count': count, 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game), indent=2) + '\n', encoding='utf-8')
