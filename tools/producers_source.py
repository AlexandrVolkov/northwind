#!/usr/bin/env python3
"""Export what each ARMY__02 template can make (its WRK answer to AH=6, LE:1 D19C) into data/original_producers.json.

D19C jumps into the template's WRK callback with AH=6; a producer returns EDX = its table (seven-byte header, then
four-byte rows template, frame u16, interval, FFh). 22E85 (AI order executor) lists the producers of a template from
these rows, duplicates included. The callbacks are executed under Unicorn from the original NORWIND.EXE and WRK."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, STRIDE
from forestry_source import source_constants


class ProducerOracle(CombatOracle):
    RANGES = {'ah6': ([(0xd19c, 0xd1ba), (0x68a0, 0x68bd), (0xa879, 0xa8a0)], [(0, 0x10000)])}


def extract(game):
    constants = source_constants(game); report, code = analyze(game)
    o = ProducerOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); wrk = (game / 'ARMY__02.WRK').read_bytes()
    o.cpu.mem_write(o.DAT, dat); o.cpu.mem_write(o.WRK, wrk)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2)); o.cpu.mem_write(0x6ca + 2 * 0x56 + 9, struct.pack('<I', o.WRK))
    count = struct.unpack_from('<H', dat)[0] // STRIDE
    o.mode = 'ah6'; producers = {}
    for t in range(count):
        o.cpu.reg_write(UC_X86_REG_EAX, 0x600); o.cpu.reg_write(UC_X86_REG_EBX, t | 2 << 8); o.cpu.reg_write(UC_X86_REG_EDX, 0)
        o.run(o.CODE + 0xd19c)
        edx = o.cpu.reg_read(UC_X86_REG_EDX)
        if edx == 0: continue
        rows, at = [], edx + 7
        while bytes(o.cpu.mem_read(at, 1))[0] != 0xff: rows.append(bytes(o.cpu.mem_read(at, 1))[0]); at += 4
        producers[str(t)] = {'wrk_table_offset': edx - o.WRK, 'makes': rows}
    return {'format_version': 1, 'source_sha256': {'NORWIND.EXE': report['source_sha256']['NORWIND.EXE'],
            'ARMY__02.WRK': hashlib.sha256(wrk).hexdigest(), 'ARMY__02.DAT': hashlib.sha256(dat).hexdigest()},
            'army': 'ARMY__02', 'producers': producers}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'data/original_producers.json')
    args = parser.parse_args()
    args.output.write_text(json.dumps(extract(args.game), indent=1) + '\n', encoding='utf-8')
