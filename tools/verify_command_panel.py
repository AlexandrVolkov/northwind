#!/usr/bin/env python3
"""Execute LE:1 6EC1..6F17 to verify six-slot command visibility from real DAT/OBE records."""
import argparse
import hashlib
import json
from pathlib import Path
import struct

from unicorn.x86_const import UC_X86_REG_EIP
from unicorn.x86_const import UC_X86_REG_ESI
from itertools import product
from analyze_executable import analyze
from verify_occupancy import OccupancyOracle


class PanelOracle(OccupancyOracle):
    def guard(self, cpu, address, size, user_data):
        if self.mode == 'group' and address == self.CODE + 0xb71c:
            cpu.emu_stop(); return
        if not any(self.CODE + lo <= address < self.CODE + hi for lo, hi in [(0x6ec1, 0x6f18), (0x6885, 0x68a0), (0xb72d, 0xb764)]):
            raise ValueError(f'Unexpected panel control-flow escape: {address:#x}')


def generate(game):
    report, code = analyze(game)
    oracle = PanelOracle(code, report['fs_assignments'])
    dat = (game / 'ARMY__02.DAT').read_bytes()
    oracle.cpu.mem_write(oracle.DAT, dat)
    oracle.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', oracle.DAT + 2))
    oracle.cpu.mem_write(0x1969, struct.pack('<I', oracle.POOL + 1))
    def record(template, flags):
        raw = bytearray(54); raw[0] = 2; raw[14] = template; raw[15] = flags
        oracle.cpu.mem_write(oracle.POOL + 1, bytes(raw))

    def mask():
        return sum(1 << i for i in range(6) if struct.unpack('<I', oracle.cpu.mem_read(0x1978 + 12*i + 2, 4))[0])

    cases = []
    for template in [0, 2, 3, 10, 20]:
        for flags in range(256):
            record(template, flags)
            oracle.cpu.mem_write(0x1978, bytes([0xa5]) * 72)
            oracle.start(oracle.CODE + 0x6ec1)
            if oracle.cpu.reg_read(UC_X86_REG_EIP) != oracle.STOP:
                raise ValueError('Original command panel failed to return')
            cases.append([template, flags, mask()])
    groups = []
    # Mixed source templates, all group code kinds, empty/partial/full WorkFlags, and high bits.
    for first, second, a, b in product([0, 2, 10, 20], [0, 2, 10, 20], [0, 1, 3, 7, 15, 31, 63, 64, 128, 255], [0, 1, 3, 7, 15, 31, 63, 64, 128, 255]):
        oracle.mode = ''
        record(first, a); oracle.start(oracle.CODE + 0x6ec1)
        oracle.mode = 'group'
        record(second, b); oracle.cpu.reg_write(UC_X86_REG_ESI, oracle.POOL + 1)
        oracle.start(oracle.CODE + 0xb72d)
        if oracle.cpu.reg_read(UC_X86_REG_EIP) != oracle.CODE + 0xb71c:
            raise ValueError('Group panel failed to reach its boundary')
        groups.append({'members': [[first, a], [second, b]], 'mask': mask()})
    return {'format_version': 1, 'source_sha256': {n: hashlib.sha256((game / n).read_bytes()).hexdigest()
            for n in ['NORWIND.EXE', 'ARMY__02.DAT']}, 'cases': cases, 'groups': groups}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    result = generate(args.game)
    args.fixture.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'Original command panel: {len(result["cases"])} single, {len(result["groups"])} group cases')
