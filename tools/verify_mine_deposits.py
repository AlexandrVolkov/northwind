#!/usr/bin/env python3
"""Compare bounded original LE:1 B3F7 ore counting with packaged MAP_1.MAP cells."""
import argparse
import hashlib
import json
from pathlib import Path
import struct

from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_ECX, UC_X86_REG_EDX
from verify_occupancy import OccupancyOracle


class MineOracle(OccupancyOracle):
    def guard(self, cpu, address, size, user_data):
        if not self.CODE + 0xb3f7 <= address < self.CODE + 0xb47f:
            raise ValueError(f'Unexpected mine-counter escape: {address:#x}')

    def count(self, x, y, kind):
        self.cpu.reg_write(UC_X86_REG_EAX, 0x100 | kind)
        self.cpu.reg_write(UC_X86_REG_ECX, x * 32)
        self.cpu.reg_write(UC_X86_REG_EDX, y * 32)
        self.start(self.CODE + 0xb3f7)
        return self.cpu.reg_read(UC_X86_REG_EAX)


def generate(game, project):
    source = (game / 'MAP_1.MAP').read_bytes()
    width, height = struct.unpack_from('<II', source, 17)
    if source[:4] != b'MAP2' or (width, height) != (128, 128):
        raise ValueError('Unexpected source map')
    cells = struct.unpack_from(f'<{width * height}I', source, 25)
    bundled = json.loads((project / 'assets/imported/map.json').read_text())
    if bundled['cells'] != list(cells):
        raise ValueError('Packaged cells differ from original MAP_1.MAP')
    code = (game / 'NORWIND.EXE').read_bytes()
    from analyze_executable import analyze
    report, le_code = analyze(game)
    oracle = MineOracle(le_code, report['fs_assignments'])
    oracle.cpu.mem_write(0x18de, struct.pack('<II', width, height))
    oracle.cpu.mem_write(0x18ee, struct.pack('<I', oracle.GRID))
    oracle.cpu.mem_write(oracle.GRID, struct.pack(f'<{len(cells)}I', *cells))
    positions = {(0, 0), (127, 0), (0, 127), (127, 127), (1, 1), (126, 126), (64, 64)}
    ore = [(i % width, i // width) for i, cell in enumerate(cells) if (cell >> 16) & 255 in (1, 2)]
    positions.update(ore[::max(1, len(ore) // 60)])
    positions.update(((i * 37) % width, (i * 53) % height) for i in range(40))
    cases = []
    for x, y in sorted(positions):
        first, second = oracle.count(x, y, 1), oracle.count(x, y, 2)
        cases.append({'x': x, 'y': y, 'first': first, 'second': second,
                      'type': 2 if second > first else 1, 'remaining': max(first, second) << 11})
    return {'format_version': 1, 'source_sha256': {'NORWIND.EXE': hashlib.sha256(code).hexdigest(),
             'MAP_1.MAP': hashlib.sha256(source).hexdigest()}, 'cases': cases}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    args.fixture.write_text(json.dumps(generate(args.game, root), indent=2) + '\n')
    print(f'Verified {len(json.loads(args.fixture.read_text())["cases"])} mine positions against original x86')
