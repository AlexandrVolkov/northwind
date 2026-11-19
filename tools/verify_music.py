#!/usr/bin/env python3
"""Execute menu/ambience playback dispatch and repeat termination under Unicorn."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_ESP, UC_X86_REG_EIP
from analyze_executable import analyze
from music_source import generate as scene_sources
from verify_occupancy import OccupancyOracle


class MusicOracle(OccupancyOracle):
    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if at == 0x1416b and self.mode in ('menu', 'ambient'):
            if cpu.reg_read(UC_X86_REG_ESI) != self.DAT:
                raise ValueError('Dispatch uses a different audio bank')
            self.calls.append((cpu.reg_read(UC_X86_REG_EAX), cpu.reg_read(UC_X86_REG_EBX)))
            sp = cpu.reg_read(UC_X86_REG_ESP)
            cpu.reg_write(UC_X86_REG_EIP, struct.unpack('<I', cpu.mem_read(sp, 4))[0])
            cpu.reg_write(UC_X86_REG_ESP, sp + 4)
            return
        allowed = {'menu': [(0x1cbf7, 0x1cc1b)], 'ambient': [(0x148dd, 0x1495c)],
                   'repeat': [(0x14449, 0x1446a)]}[self.mode]
        if not any(lo <= at < hi for lo, hi in allowed):
            raise ValueError(f'Unexpected music control-flow escape: {address:#x} ({self.mode})')

    def run(self, mode, at):
        self.mode = mode
        self.calls = []
        self.start(self.CODE + at)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP:
            raise ValueError('Music operation did not return')


def generate(game):
    report, code = analyze(game)
    catalog = scene_sources(game)
    o = MusicOracle(code, report['fs_assignments'])
    o.cpu.mem_write(0xa0a3, struct.pack('<I', o.DAT))
    o.cpu.mem_write(0x6bf9, struct.pack('<I', o.DAT))
    previous_values = [0xffffffff, 0, 1, 2, 3, 4, 5]
    digest = hashlib.sha256()
    for previous in previous_values:
        for requested in range(6):
            o.cpu.mem_write(0x9fc9, struct.pack('<I', previous))
            o.cpu.reg_write(UC_X86_REG_EAX, requested)
            o.run('menu', 0x1cbf7)
            expected = [] if previous == requested else [(requested, 1)]
            if o.calls != expected:
                raise ValueError('Menu profile differs from native dispatch')
            digest.update(bytes(o.cpu.mem_read(0x9fc9, 4)) + bytes([len(o.calls)]))
            for call in o.calls: digest.update(struct.pack('<II', *call))
    menus = dict(previous=previous_values, requested=list(range(6)), case_count=len(previous_values) * 6, sha256=digest.hexdigest())
    o.run('ambient', 0x148dd)
    if o.calls != [(profile['sound'], profile['mixer_mode']) for profile in catalog['ambient_channels']]:
        raise ValueError('Ambient source metadata differs from native dispatch')
    ambient = dict(case_count=len(o.calls), sha256=hashlib.sha256(b''.join(struct.pack('<II', *call) for call in o.calls)).hexdigest())
    repeats = [0, 1, 2, 3, 255]
    digest = hashlib.sha256()
    for repeat in repeats:
        raw = bytearray((i * 13 + 7) % 256 for i in range(32))
        raw[0] = 1; raw[1] = repeat
        struct.pack_into('<IIII', raw, 2, 100, 0xabcdef, 0, 0xabcdef + 100)
        o.cpu.mem_write(o.DAT, bytes(raw))
        o.cpu.reg_write(UC_X86_REG_EDI, o.DAT)
        o.run('repeat', 0x14449)
        digest.update(bytes(o.cpu.mem_read(o.DAT, 32)))
    return dict(format_version=1, source_sha256=report['source_sha256']['NORWIND.EXE'],
                menu=menus, ambient_start=ambient,
                repeat_end=dict(repeats=repeats, case_count=len(repeats), sha256=digest.hexdigest()))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    result = generate(args.game)
    args.fixture.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print('Native music: 42 menu transitions, 6 ambient starts, 5 repeat endings')
