#!/usr/bin/env python3
"""Execute original hourglass AL=1 callback; stub only downstream 1CC1B."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EDI
from analyze_executable import INNER_MZ, analyze, read_le_objects
from verify_texts import TextsOracle


class HourglassOracle(TextsOracle):
    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if not (0x18ED4 <= at < 0x18EF5 or 0x19185 <= at < 0x1923B or at == 0x1CC1B):
            raise ValueError(f'Unexpected hourglass escape: {at:X}')


def cases():
    for phase in range(4):
        for tick, last in ((0, 0), (4, 0), (5, 0), (10, 0), (0, 254), (255, 250)):
            for offset in (0, 1, 48, 49, 50, 51):
                yield phase, tick, last, offset


def generate(game):
    _, code = analyze(game)
    oracle = HourglassOracle(code); cpu = oracle.cpu
    cpu.mem_write(oracle.CODE + 0x1CC1B, b'\xc3')
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    record = objects[2]['payload'][0xAF38:0xAF38 + 45]
    base = struct.unpack_from('<I', record, 0x24)[0]
    widget = oracle.FRAME + 0x2000
    digest = hashlib.sha256(); count = 0
    for phase, tick, last, offset in cases():
        cpu.mem_write(widget, record)
        cpu.mem_write(widget + 0x13, bytes([last]))
        cpu.mem_write(widget + 0x28, struct.pack('<I', base + offset))
        cpu.mem_write(0xA0AA, bytes([phase])); cpu.mem_write(0x19E4, bytes([tick]))
        cpu.mem_write(0xA0A7, b'\x00'); cpu.mem_write(0x1C35, b'\x34\x12')
        cpu.mem_write(0x193A, struct.pack('<I', 0x12345678))
        cpu.reg_write(UC_X86_REG_EDI, widget); cpu.reg_write(UC_X86_REG_EAX, 1)
        oracle.run('hourglass', 0x19185)
        out_last = cpu.mem_read(widget + 0x13, 1)[0]
        out_frame = struct.unpack('<I', cpu.mem_read(widget + 0x28, 4))[0]
        launch = cpu.mem_read(0xA0A7, 1)[0]
        key = struct.unpack('<H', cpu.mem_read(0x1C35, 2))[0]
        button = struct.unpack('<I', cpu.mem_read(0x193A, 4))[0]
        digest.update(struct.pack('<BIBHI', out_last, out_frame, launch, key, button)); count += 1
    return dict(format_version=1, source_sha256=hashlib.sha256((game/'NORWIND.EXE').read_bytes()).hexdigest(),
                cases=count, output_sha256=digest.hexdigest(), stubbed_calls=['1CC1B (RET after state mutations)'])


if __name__ == '__main__':
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--game',type=Path,required=True)
    ap.add_argument('--fixture',type=Path,default=Path('tests/fixtures/menu-hourglass.json'))
    a=ap.parse_args(); r=generate(a.game)
    a.fixture.write_text(json.dumps(r,indent=2)+'\n'); print(r)
