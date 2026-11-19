#!/usr/bin/env python3
"""Compare the actual 6DEF column gauge pixels and 63CB health color callback with C#."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_EDI, UC_X86_REG_ESI, UC_X86_REG_EIP
from analyze_executable import analyze
from verify_occupancy import OccupancyOracle


class BarOracle(OccupancyOracle):
    def guard(self, cpu, address, size, user_data):
        allowed = [(0x63cb, 0x63f7)] if self.mode == 'health' else [(0x6def, 0x6eb2), (0x3eea, 0x3f32)]
        if not any(self.CODE + lo <= address < self.CODE + hi for lo, hi in allowed):
            raise ValueError(f'Unexpected status-bar control-flow escape: {address:#x}')

    def run(self, start):
        self.start(self.CODE + start)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('Status-bar operation did not return')


def generate(game):
    report, code = analyze(game); o = BarOracle(code, report['fs_assignments'])
    o.cpu.mem_write(0x68e, struct.pack('<I', o.GRID))
    o.cpu.mem_write(0x64c, struct.pack('<I', o.GRID + 0x60000))
    o.cpu.mem_write(0x1969, struct.pack('<I', o.POOL + 1))
    fixture = dict(format_version=1, source_sha256=report['source_sha256']['NORWIND.EXE'],
                   dimensions=[[9, 50], [5, 7], [16, 16]], maxima=[0, 1, 5, 10, 70, 127])
    digest = hashlib.sha256(); count = 0
    for width, height in fixture['dimensions']:
        for maximum in fixture['maxima']:
            for current in range(maximum + 1):
                o.mode = 'health'; raw = bytearray(30); raw[0x14] = current ^ 255; raw[0x15] = maximum
                o.cpu.mem_write(o.DAT, bytes(raw)); o.cpu.mem_write(o.POOL + 1 + 0x19, bytes([current]))
                o.cpu.reg_write(UC_X86_REG_ESI, o.DAT); o.run(0x63cb)
                health_color = bytes(o.cpu.mem_read(o.DAT + 0x16, 1))[0]; digest.update(bytes([health_color]))
                for color in [health_color, 0x94]:
                    o.mode = 'draw'
                    o.cpu.mem_write(o.GRID, bytes([0xa5]) * (640 * 64))
                    for register, value in [(UC_X86_REG_EAX, 4), (UC_X86_REG_EBX, 4), (UC_X86_REG_ECX, (maximum << 8) | current),
                                            (UC_X86_REG_EDX, (height << 8) | width), (UC_X86_REG_EDI, color)]:
                        o.cpu.reg_write(register, value)
                    o.run(0x6def)
                    digest.update(b''.join(bytes(o.cpu.mem_read(o.GRID + (4+y)*640 + 4, width)) for y in range(height)))
                    count += 1
    fixture.update(case_count=count, sha256=digest.hexdigest())
    return fixture


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__); p.add_argument('--game', type=Path, required=True); p.add_argument('--fixture', type=Path, required=True)
    args = p.parse_args(); result = generate(args.game)
    args.fixture.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8'); print(f'Original status bars: {result["case_count"]} images')
