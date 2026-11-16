#!/usr/bin/env python3
"""Compare an explicit translation with original x86 building-frame instructions.

Runs only EXE code offsets C8E0..C9FD. The effect callback at 9727 is skipped;
no original game, DOS calls, file writes or complete update loop is emulated.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_ESI, UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EFLAGS
from analyze_executable import analyze


def translated_step(record, condition, previous, frame):
    """Restricted path: nonnegative condition, state=0, flag=0, odd tick.

    Preserve the original 8/16/32-bit arithmetic and branch guards. This is not
    a universal HP-to-frame lookup, a construction rule, or a game simulation.
    """
    a, b, maximum = record[0x4a], record[0x4b], record[0x45]
    low = struct.unpack_from('<H', record, 0x48)[0] + a
    high = low + b
    if condition == previous:
        return frame, condition
    quotient = condition * ((a+1) & 255) // ((maximum+1) & 255)
    target = high + (((a+1) & 255) - quotient) - 2
    if condition < previous:
        if quotient == 0:
            condition = 255
        if frame != (target & 65535) and (target >= high or frame < low or frame >= high):
            frame = target & 65535
    elif frame != (target & 65535):
        delta = (quotient-a) & 255
        signed_delta = delta-256 if delta & 128 else delta
        repair_target = ((signed_delta & 0xffffffff) & 0xffff0000) | ((signed_delta+low) & 65535)
        if repair_target < low or frame < low or frame >= high:
            frame = repair_target & 65535
    return frame, condition


class OriginalStep:
    CODE = 0x10000
    OBJECT = 0x400000
    TEMPLATE = 0x500000
    STACK = 0x600000

    def __init__(self, code, record):
        if struct.unpack_from('<I', record, 0x40)[0] & 0x80:
            raise ValueError('Direction-dependent frame path is outside this experiment')
        self.cpu = Uc(UC_ARCH_X86, UC_MODE_32)
        self.cpu.mem_map(0, 0x10000)
        self.cpu.mem_map(self.CODE, (len(code)+4095)//4096*4096)
        self.cpu.mem_write(self.CODE, code)
        for address in [self.OBJECT, self.TEMPLATE, self.STACK]:
            self.cpu.mem_map(address, 4096)
        self.cpu.mem_write(self.TEMPLATE, record)
        self.cpu.mem_write(0x1c49, struct.pack('<I', self.TEMPLATE))
        self.cpu.mem_write(0x19ea, b'\x01')  # Gate off the even-tick animation advance.
        self.effects_skipped = 0
        self.cpu.hook_add(UC_HOOK_CODE, self.on_instruction)

    def on_instruction(self, cpu, address, size, user_data):
        if address == self.CODE + 0x9727:
            # Effect creation is outside this frame-selection experiment.
            stack = cpu.reg_read(UC_X86_REG_ESP)
            target = struct.unpack('<I', cpu.mem_read(stack, 4))[0]
            cpu.reg_write(UC_X86_REG_ESP, stack+4)
            cpu.reg_write(UC_X86_REG_EIP, target)
            self.effects_skipped += 1
        elif not self.CODE+0xc8e0 <= address < self.CODE+0xc9fd:
            raise ValueError(f'Unexpected control-flow escape: {address:#x}')

    def run(self, condition, previous, frame):
        record = bytearray(64)
        struct.pack_into('<H', record, 1, frame)
        record[0x19], record[0x1a] = condition, previous
        self.cpu.mem_write(self.OBJECT, bytes(record))
        self.cpu.reg_write(UC_X86_REG_ESI, self.OBJECT)
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK+4092)
        self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(self.CODE+0xc8e0, self.CODE+0xc9fd, count=500)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.CODE+0xc9fd:
            raise ValueError('Original instruction block did not reach its boundary')
        result = bytes(self.cpu.mem_read(self.OBJECT, 64))
        return struct.unpack_from('<H', result, 1)[0], result[0x19]


def verify(game):
    analysis, code = analyze(game)
    files = {p.name.upper(): p for p in game.iterdir() if p.is_file()}
    dat = files['ARMY__02.DAT'].read_bytes()
    record = dat[2+2*171:2+3*171]  # Linked and named castle template, not a guessed SPR match.
    base = struct.unpack_from('<H', record, 0x48)[0]
    maximum = record[0x45]
    if (base, maximum, record[0x4a], record[0x4b]) != (876, 127, 8, 1):
        raise ValueError('Castle parameters differ from this experiment profile')
    original = OriginalStep(code, record)
    count = 0
    for previous in [0, maximum]:
        for condition in range(maximum+1):
            for frame in range(base, base+17):
                expected = original.run(condition, previous, frame)
                actual = translated_step(record, condition, previous, frame)
                if actual != expected:
                    raise ValueError(f'Mismatch: {(condition, previous, frame)} -> translated {actual}, x86 {expected}')
                count += 1
    examples = []
    for previous, frame, conditions in [(maximum, 884, [127, 113, 99, 85, 71, 56, 42, 28, 14, 0]),
                                         (0, 888, [14, 28, 56, 85, 113, 127])]:
        for condition in conditions:
            result = original.run(condition, previous, frame)
            examples.append({'condition_before': condition, 'previous_condition': previous,
                             'frame_before': frame, 'frame_after': result[0], 'condition_after': result[1]})
    return {'format_version': 1, 'source_sha256': {name: analysis['source_sha256'][name]
             for name in ['NORWIND.EXE', 'ARMY__02.DAT', 'ARMY__02.WRK']},
            'code_range': [0xc8e0, 0xc9fd], 'template': {'file': 'ARMY__02.DAT', 'index': 2, 'file_offset': 344},
            'case_count': count, 'mismatch_count': 0, 'examples': examples,
            'restrictions': ['Original instructions executed in Unicorn 2.1.4, 32-bit x86.',
                             'Object state +0x14=0, flags +0x0f=0, odd tick; condition 0..127, previous 0 or 127.',
                             'Only frame +1 and condition +0x19 compared; effect creation at 0x9727 skipped.',
                             'Does not verify complete repair/construction, damage, timing, effects, or death processing.']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    report = verify(args.game)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(f"Original x86 comparison: {report['case_count']} cases, {report['mismatch_count']} mismatches")


if __name__ == '__main__':
    main()
