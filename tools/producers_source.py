#!/usr/bin/env python3
"""Export what each ARMY__02 template can make (its WRK answer to AH=6, LE:1 D19C) into data/original_producers.json.

D19C jumps into the template's WRK callback with AH=6; a producer returns EDX = its table (seven-byte header, then
four-byte rows template, frame u16, interval, FFh; the interval paces production, docs/recruitment-production.md). 22E85 (AI order executor) lists the producers of a template from
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


def extract(game, army=2):
    constants = source_constants(game); report, code = analyze(game)
    o = ProducerOracle(code, report['fs_assignments'], constants)
    dat = (game / f'ARMY__0{army}.DAT').read_bytes(); wrk = (game / f'ARMY__0{army}.WRK').read_bytes()
    o.cpu.mem_write(o.DAT, dat); o.cpu.mem_write(o.WRK, wrk)
    o.cpu.mem_write(0x6ca + army * 0x56 + 4, struct.pack('<I', o.DAT + 2)); o.cpu.mem_write(0x6ca + army * 0x56 + 9, struct.pack('<I', o.WRK))
    count = struct.unpack_from('<H', dat)[0] // STRIDE
    o.mode = 'ah6'; producers = {}
    for t in range(count):
        o.cpu.reg_write(UC_X86_REG_EAX, 0x600); o.cpu.reg_write(UC_X86_REG_EBX, t | army << 8); o.cpu.reg_write(UC_X86_REG_EDX, 0)
        o.run(o.CODE + 0xd19c)
        edx = o.cpu.reg_read(UC_X86_REG_EDX)
        if edx == 0: continue
        rows, intervals, at = [], [], edx + 7
        while bytes(o.cpu.mem_read(at, 1))[0] != 0xff:
            row = bytes(o.cpu.mem_read(at, 4)); rows.append(row[0]); intervals.append(row[3]); at += 4
        producers[str(t)] = {'wrk_table_offset': edx - o.WRK, 'makes': rows, 'intervals': intervals}
    return {'format_version': 1, 'source_sha256': {'NORWIND.EXE': report['source_sha256']['NORWIND.EXE'],
            f'ARMY__0{army}.WRK': hashlib.sha256(wrk).hexdigest(), f'ARMY__0{army}.DAT': hashlib.sha256(dat).hexdigest()},
            'army': f'ARMY__0{army}', 'producers': producers}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'data/original_producers.json')
    args = parser.parse_args()
    main = extract(args.game)
    horde = extract(args.game, 1)
    main['armies'] = {'1': {'army': horde['army'], 'source_sha256': horde['source_sha256'], 'producers': horde['producers']}}   # the horde of slot 4 (ARMY__01)
    args.output.write_text(json.dumps(main, indent=1) + '\n', encoding='utf-8')
