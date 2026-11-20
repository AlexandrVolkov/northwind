#!/usr/bin/env python3
"""Execute original LE:1 10799 through its call to 103EC and capture the transfer arguments."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct

from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EIP, UC_X86_REG_ESI, UC_X86_REG_ESP
from analyze_executable import INNER_MZ, analyze, read_le_objects
from verify_occupancy import OccupancyOracle


class ArrivalOracle(OccupancyOracle):
    def guard(self, cpu, address, size, user_data):
        if address == self.CODE + 0x103ec:
            self.result = (cpu.reg_read(UC_X86_REG_ECX) & 0xffff,
                           cpu.reg_read(UC_X86_REG_EAX) & 0xffffffff,
                           cpu.reg_read(UC_X86_REG_EBX) & 0xffffffff)
            sp = cpu.reg_read(UC_X86_REG_ESP)
            cpu.reg_write(UC_X86_REG_ESP, sp + 4)
            cpu.reg_write(UC_X86_REG_EIP, struct.unpack('<I', cpu.mem_read(sp, 4))[0])
            return
        if not self.CODE + 0x10799 <= address < self.CODE + 0x107e9:
            raise ValueError(f'Unexpected campaign arrival control flow: {address:#x}')


def generate(game: Path):
    report, code = analyze(game)
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    le2 = objects[1]['payload']
    oracle = ArrivalOracle(code, report['fs_assignments'])
    oracle.cpu.mem_write(0x19c7, le2[0x19c7:0x19d7])
    rng = random.Random(171009)
    cases = []
    digest = hashlib.sha256()
    samples = [(0, 0), (16, 48), (120, 4064), (4032, 127), (4064, 4064)]
    samples += [(rng.randrange(4096), rng.randrange(4096)) for _ in range(15)]
    for direction in range(8):
        for x, y in samples:
            current = (rng.randrange(1, 3), 1)  # stay inside the world; bounds are checked separately
            oracle.cpu.mem_write(0x1cc7, bytes(current))
            oracle.cpu.mem_write(oracle.POOL + 4, struct.pack('<II', x, y))
            oracle.cpu.reg_write(UC_X86_REG_EAX, direction)
            oracle.cpu.reg_write(UC_X86_REG_ESI, oracle.POOL)
            oracle.result = None
            oracle.start(oracle.CODE + 0x10799)
            if oracle.result is None or oracle.cpu.reg_read(UC_X86_REG_EIP) != oracle.STOP:
                raise ValueError('Original arrival did not call transfer and return')
            area, arrival_x, arrival_y = oracle.result
            digest.update(struct.pack('<IIII', area & 255, (area >> 8) & 255, arrival_x, arrival_y))
            cases.append(dict(direction=direction, current=list(current), previous=[x, y]))
    return dict(format_version=1, source_sha256={'NORWIND.EXE': report['source_sha256']['NORWIND.EXE'],
                                                'LE:2:19C7': hashlib.sha256(le2[0x19c7:0x19d7]).hexdigest()},
                stubbed_call='LE:1 103EC; captured ECX/EAX/EBX at entry', case_count=len(cases),
                result_sha256=digest.hexdigest(), cases=cases)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', required=True, type=Path)
    parser.add_argument('--fixture', required=True, type=Path)
    args = parser.parse_args()
    result = generate(args.game)
    args.fixture.parent.mkdir(parents=True, exist_ok=True)
    args.fixture.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Original campaign arrival: {result["case_count"]} x86 cases')
