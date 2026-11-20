#!/usr/bin/env python3
"""Execute original idle animations of bush, candles, owl (19063/190ED).

The owl's phase-completion call to 192EA is stopped at entry with RET;
all mutations before that call, including the new phase, are native.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EDI
from analyze_executable import INNER_MZ, analyze, read_le_objects
from verify_texts import TextsOracle


class WidgetOracle(TextsOracle):
    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if not (0x18ED4 <= at < 0x18EF5 or 0x19063 <= at < 0x19185 or at == 0x192EA):
            raise ValueError(f'Unexpected menu widget escape: {at:X}')


def cases():
    for index in (3, 4, 5):
        for phase in range(4):
            for tick, last in ((0, 0), (2, 0), (3, 0), (5, 0), (0, 254), (255, 250)):
                for frame_offset in (0, 1, 29, 30, 50, 51):
                    yield index, phase, tick, last, frame_offset


def generate(game):
    _, code = analyze(game)
    oracle = WidgetOracle(code); cpu = oracle.cpu
    cpu.mem_write(oracle.CODE + 0x192EA, b'\xc3')
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    data = objects[2]['payload']
    source = {3: 0xAE4F, 4: 0xAE7C, 5: 0xAEA9}
    widget = oracle.FRAME + 0x2000
    digest = hashlib.sha256(); count = 0
    for index, phase, tick, last, frame_offset in cases():
        record = data[source[index]:source[index] + 45]
        base = struct.unpack_from('<I', record, 0x24)[0]
        cpu.mem_write(widget, record)
        cpu.mem_write(widget + 0x13, bytes([last]))
        cpu.mem_write(widget + 0x28, struct.pack('<I', base + frame_offset))
        cpu.mem_write(0xA0AA, bytes([phase])); cpu.mem_write(0x19E4, bytes([tick]))
        cpu.reg_write(UC_X86_REG_EDI, widget); cpu.reg_write(UC_X86_REG_EAX, 1)
        oracle.run('widget', 0x190ED if index == 4 else 0x19063)
        out_last = cpu.mem_read(widget + 0x13, 1)[0]
        out_frame = struct.unpack('<I', cpu.mem_read(widget + 0x28, 4))[0]
        out_phase = cpu.mem_read(0xA0AA, 1)[0]
        digest.update(struct.pack('<BIB', out_last, out_frame, out_phase)); count += 1
    return dict(format_version=1, source_sha256=hashlib.sha256((game/'NORWIND.EXE').read_bytes()).hexdigest(),
                cases=count, output_sha256=digest.hexdigest(), stubbed_calls=['192EA (RET only after owl changes phase)'])


if __name__ == '__main__':
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--game',type=Path,required=True)
    ap.add_argument('--fixture',type=Path,default=Path('tests/fixtures/menu-widgets.json'))
    a=ap.parse_args(); result=generate(a.game)
    a.fixture.write_text(json.dumps(result,indent=2)+'\n')
    print(result)
