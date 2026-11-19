#!/usr/bin/env python3
"""Run native exploration, fog corner selection and minimap pixels, without exporting original bytes.

813E uses the real already-ported 6885 resolver. 3EEA only records a dirty rectangle;
it is stubbed to RET for the minimap. All map and pixel writers execute unchanged.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_texts import TextsOracle


class VisibilityOracle(TextsOracle):
    RECORD = TextsOracle.BANK
    TEMPLATE = RECORD + 0x1000
    MAP = RECORD + 0x2000
    GRID = RECORD + 0x20000
    TILES = RECORD + 0x80000
    COLORS = RECORD + 0xa0000
    OBJECTS = RECORD + 0xb0000

    def __init__(self, code):
        super().__init__(code)
        # Bookkeeping only; preserves the actual geometry registers.
        self.cpu.mem_write(self.CODE + 0x3eea, b'\xc3')

    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if self.mode == 'corners' and at == 0xa0ce:
            self.goal = 'revealed'; cpu.emu_stop(); return
        allowed = {
            'reveal': [(0x813e, 0x81d9), (0x6885, 0x68a0)],
            'all': [(0x121c0, 0x121e2)],
            'corners': [(0xa000, 0xa05b)],
            'sprite': [(0xa063, 0xa075)],
            'minimap': [(0xa1d1, 0xa320), (0x3eea, 0x3eeb)],
        }[self.mode]
        if not any(lo <= at < hi for lo, hi in allowed):
            raise ValueError(f'Unexpected visibility control flow {at:X} ({self.mode})')

    def put32(self, at, *values):
        self.cpu.mem_write(at, struct.pack('<' + 'I' * len(values), *values))

    def kernel(self, mode, start, stop):
        self.mode = mode; self.goal = None
        self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(self.CODE + start, self.CODE + stop, count=1000)
        if not self.goal and self.cpu.reg_read(UC_X86_REG_EIP) != self.CODE + stop:
            raise ValueError('Visibility kernel hit instruction limit')


def reveal_cases():
    cases = []
    for width, height in [(16, 16), (31, 23), (64, 48), (160, 160)]:
        for x, y in [(0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1), (width // 2, height // 2), (1, 2)]:
            for radius in [0, 1, 3, 8]:
                for experience in [0, 39, 40, 127, 128, 255]:
                    for owner in [0, 1, 7, 8, 15, 32, 255]:
                        cases.append([width, height, x, y, radius, experience, owner])
    return cases


def minimap_inputs(width, height, salt):
    n = width * height
    return dict(
        unexplored=bytes((i * 37 + salt * 19) & 255 for i in range(n)),
        tiles=[(i * 17 + salt * 3) % 256 for i in range(n)],
        terrain=bytes((i * 3 + 29) & 255 for i in range(256)),
        plane0=[1 + i % 23 for i in range(n)],
        plane1=[1 + (i * 3) % 23 if i % 5 == salt % 5 else 0 for i in range(n)],
        plane2=[1 + i % 23 if i % 7 == salt % 7 else 0 for i in range(n)],
        owners=bytes((i * 5 + salt) & 255 for i in range(24)),
        owner_colors=bytes((i * 11 + salt * 7) & 255 for i in range(16)))


def format_fixture(fixture):
    """Keep each numerical test vector on one line so future reviews need fewer tokens."""
    pretty = json.dumps(fixture, indent=2)
    return re.sub(r'\[\n(?:[ \t]+-?\d+,?\n)+[ \t]*\]',
                  lambda match: json.dumps(json.loads(match.group())), pretty) + '\n'


def generate(game):
    report, code = analyze(game)
    o = VisibilityOracle(code); c = o.cpu
    fixture = dict(format_version=1, source_sha256=report['source_sha256']['NORWIND.EXE'])
    digest = hashlib.sha256(); cases = reveal_cases()
    for width, height, x, y, radius, experience, owner in cases:
        n = width * height
        o.put32(0x18de, width, height); o.put32(0x18da, o.MAP)
        o.put32(0x6ce, o.TEMPLATE)
        record = bytearray(64); record[3] = owner; record[0x16] = experience
        struct.pack_into('<II', record, 4, x * 32 + 16, y * 32 + 31)
        template = bytearray(171); template[0x47] = radius
        # These fields should have no effect on the reveal rectangle.
        template[3:5] = bytes([4, 7]); struct.pack_into('<I', template, 0x40, 8)
        c.mem_write(o.RECORD, bytes(record)); c.mem_write(o.TEMPLATE, bytes(template))
        initial = bytes((i * 37 + owner * 19) & 255 for i in range(n))
        c.mem_write(o.MAP - 16, b'\xa5' * 16 + initial + b'\x5a' * 16)
        c.reg_write(UC_X86_REG_ESI, o.RECORD); o.run('reveal', 0x813e)
        digest.update(bytes(c.mem_read(o.MAP, n)))
        if bytes(c.mem_read(o.MAP - 16, 16)) != b'\xa5' * 16 or bytes(c.mem_read(o.MAP + n, 16)) != b'\x5a' * 16:
            raise ValueError('Reveal wrote outside the exploration map')
    fixture['reveal'] = dict(cases=cases, case_count=len(cases), sha256=digest.hexdigest())
    print(f'Native reveal: {len(cases)} cases', flush=True)
    digest = hashlib.sha256()
    for owner in range(256):
        c.mem_write(o.MAP, bytes(range(256))); o.put32(0x18de, 16, 16); o.put32(0x18da, o.MAP)
        c.reg_write(UC_X86_REG_EDX, owner); o.run('all', 0x121c0)
        digest.update(bytes(c.mem_read(o.MAP, 256)))
    fixture['scenario_all'] = dict(owner_values=256, sha256=digest.hexdigest())
    digest = hashlib.sha256(); count = 0
    offsets = [-17, -16, -15, -1, 1, 15, 16, 17]
    centre = o.MAP + 32
    for participant in range(16):
        bit = (1 << (participant & 31)) & 255
        for noise in [0, 255]:
            for pattern in range(512):
                values = [((noise & (255 ^ bit)) | (bit if pattern & (1 << i) else 0)) for i in range(9)]
                c.mem_write(centre, bytes([values[8]]))
                for offset, value in zip(offsets, values): c.mem_write(centre + offset, bytes([value]))
                c.mem_write(0x1f42, bytes([bit])); c.reg_write(UC_X86_REG_ESI, centre); c.reg_write(UC_X86_REG_EDI, 0x33c)
                o.kernel('corners', 0xa000, 0xa05b)
                mask = 0 if o.goal else c.reg_read(UC_X86_REG_EAX) & 255
                digest.update(bytes([mask])); count += 1
    fixture['corners'] = dict(participants=16, noise=[0, 255], patterns=512, case_count=count, sha256=digest.hexdigest())
    digest = hashlib.sha256()
    for mask in range(1, 15):
        for parity in [0, 1]:
            c.reg_write(UC_X86_REG_EAX, mask); c.mem_write(0x1f41, bytes([parity]))
            o.kernel('sprite', 0xa063, 0xa075)
            digest.update(struct.pack('<I', c.reg_read(UC_X86_REG_EAX)))
    fixture['sprites'] = dict(sha256=digest.hexdigest(), case_count=28)
    print(f'Native fog corners: {count} cases', flush=True)
    mini_cases = []
    for width, height in [(16, 16), (31, 23), (64, 48), (160, 160)]:
        for participant in [0, 1, 7, 8, 15]:
            for salt in [0, 3, 15]:
                mini_cases.append([width, height, participant, salt, (salt * 5) & 255])
    digest = hashlib.sha256()
    for width, height, participant, salt, terrain_owner in mini_cases:
        inp = minimap_inputs(width, height, salt); n = width * height
        o.put32(0x18de, width, height); o.put32(0x18da, o.MAP); o.put32(0x18ce, o.GRID)
        o.put32(0x18ee, o.TILES); o.put32(0x18fa, o.COLORS); o.put32(0x6aa, o.OBJECTS)
        c.mem_write(0x1c50, inp['owner_colors'])
        # Only eight owner colours precede 1C58/1C5C (minimap position).
        # Native owner&15 can read those bytes for out-of-range owners; keep the alias in the test.
        o.put32(0x68e, o.FRAME); o.put32(0x1c58, 0, 0); o.put32(0x1906, 1, 1)
        c.mem_write(0x1c4e, b'\x01'); c.mem_write(0x1c60, b'\x01')
        c.mem_write(0x1972, bytes([participant])); c.mem_write(0x1971, bytes([terrain_owner]))
        c.mem_write(o.MAP, inp['unexplored'])
        for plane in range(3):
            # Native reference = offset; space each synthetic record by 64 bytes.
            c.mem_write(o.GRID + plane * n * 4, struct.pack('<' + 'I' * n, *(r * 64 for r in inp[f'plane{plane}'])))
        for ref, owner in enumerate(inp['owners']): c.mem_write(o.OBJECTS + ref * 64 + 3, bytes([owner]))
        c.mem_write(o.TILES, b''.join(struct.pack('<HH', i, 0) for i in inp['tiles']))
        c.mem_write(o.COLORS, inp['terrain']); c.mem_write(o.FRAME, b'\xa5' * (640 * 480))
        o.run('minimap', 0xa1d1)
        digest.update(bytes(c.mem_read(o.FRAME, 640 * 480)))
    fixture['minimap'] = dict(cases=mini_cases, case_count=len(mini_cases), sha256=digest.hexdigest(), viewport=[1, 1, 14, 14])
    print(f'Native minimap: {len(mini_cases)} complete framebuffers', flush=True)
    return fixture


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(format_fixture(generate(args.game)), encoding='utf-8')
