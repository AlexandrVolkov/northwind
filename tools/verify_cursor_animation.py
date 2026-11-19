#!/usr/bin/env python3
"""Execute the original LE:1 3DB5 cursor animation step under bounded x86 control flow."""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EIP
from analyze_executable import analyze
from verify_occupancy import OccupancyOracle


class CursorOracle(OccupancyOracle):
    def guard(self, cpu, address, size, user_data):
        if not self.CODE + 0x3db5 <= address < self.CODE + 0x3df2:
            raise ValueError(f'Unexpected cursor control-flow escape: {address:#x}')


def generate(game):
    report, code = analyze(game)
    if code[0x2515:0x251f].hex() != 'c7054619000000000e00':
        raise ValueError('Default cursor range no longer matches the source profile')
    oracle = CursorOracle(code, report['fs_assignments'])
    result = dict(format_version=1, source_sha256=report['source_sha256']['NORWIND.EXE'],
                  frames=[0, 13, 14, 15, 65535, 65536, 4294967295], previous=[0, 1, 127, 252, 255],
                  timers=list(range(256)), ranges=[[0, 14], [15, 24], [0, 29], [55, 64]])
    digest = hashlib.sha256(); count = 0
    for frame, previous, timer, (first, last) in product(result['frames'], result['previous'], result['timers'], result['ranges']):
        oracle.cpu.mem_write(0x193e, struct.pack('<I', frame))
        oracle.cpu.mem_write(0x1946, struct.pack('<HHB', first, last, previous))
        oracle.cpu.mem_write(0x19e4, bytes([timer]))
        oracle.start(oracle.CODE + 0x3db5)
        if oracle.cpu.reg_read(UC_X86_REG_EIP) != oracle.STOP:
            raise ValueError('Cursor step did not return')
        digest.update(bytes(oracle.cpu.mem_read(0x193e, 4)) + bytes(oracle.cpu.mem_read(0x194a, 1))); count += 1
    result.update(case_count=count, sha256=digest.hexdigest())
    return result


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--game', type=Path, required=True); p.add_argument('--fixture', type=Path, required=True)
    args = p.parse_args(); result = generate(args.game)
    args.fixture.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'Original cursor: {result["case_count"]} steps')
