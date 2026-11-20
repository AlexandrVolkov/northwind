#!/usr/bin/env python3
"""Execute original LE:1 18ED4/18EF5 frame-step helpers with Unicorn."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import (UC_X86_REG_EAX, UC_X86_REG_ECX, UC_X86_REG_EDX,
                              UC_X86_REG_EDI, UC_X86_REG_EFLAGS)
from analyze_executable import analyze
from verify_texts import TextsOracle


class MenuAnimationOracle(TextsOracle):
    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if not 0x18ED4 <= at < 0x18F16:
            raise ValueError(f'Unexpected frame-step escape: {at:X}')


def cases():
    for direction in ('forward', 'backward'):
        for tick in (0, 1, 2, 3, 4, 5, 127, 254, 255):
            for last in (0, 1, 2, 3, 4, 127, 253, 254, 255):
                for frame, limit, reset in ((60, 90, 60), (89, 90, 60), (90, 90, 60),
                                            (122, 173, 122), (172, 173, 122), (173, 173, 122),
                                            (0, 0, 0), (255, 255, 204), (2**32-1, 1, 0)):
                    for delay in (0, 3, 5):
                        yield dict(direction=direction, tick=tick, last=last,
                                   frame=frame, limit=limit, reset=reset, delay=delay)


def generate(game):
    _, code = analyze(game)
    oracle = MenuAnimationOracle(code)
    cpu = oracle.cpu
    widget = oracle.FRAME + 0x1000
    digest = hashlib.sha256()
    count = 0
    for case in cases():
        cpu.mem_write(0x19E4, bytes([case['tick']]))
        cpu.mem_write(widget + 0x13, bytes([case['last']]))
        cpu.mem_write(widget + 0x28, struct.pack('<I', case['frame']))
        cpu.reg_write(UC_X86_REG_EDI, widget)
        cpu.reg_write(UC_X86_REG_EAX, case['limit'])
        cpu.reg_write(UC_X86_REG_ECX, case['reset'])
        cpu.reg_write(UC_X86_REG_EDX, case['delay'])
        oracle.run('frame', 0x18ED4 if case['direction'] == 'forward' else 0x18EF5)
        last = cpu.mem_read(widget + 0x13, 1)[0]
        frame = struct.unpack('<I', cpu.mem_read(widget + 0x28, 4))[0]
        carry = cpu.reg_read(UC_X86_REG_EFLAGS) & 1
        digest.update(struct.pack('<BIB', last, frame, carry))
        count += 1
    return dict(format_version=1, source_sha256=hashlib.sha256((game / 'NORWIND.EXE').read_bytes()).hexdigest(),
                case_count=count, output_sha256=digest.hexdigest())


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--game', type=Path, required=True)
    ap.add_argument('--fixture', type=Path, default=Path('tests/fixtures/menu-animation.json'))
    args = ap.parse_args()
    result = generate(args.game)
    args.fixture.write_text(json.dumps(result, indent=2) + '\n')
    print(f'Native frame steps: {result["case_count"]} cases, {result["output_sha256"]}')
