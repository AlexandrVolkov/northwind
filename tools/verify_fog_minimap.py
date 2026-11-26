#!/usr/bin/env python3
"""Run original LE:1 A1D1..A320 minimap drawing on deterministic synthetic worlds.

The only stub is 3EEA, which records a dirty rectangle. The minimap's
map/object/terrain readers and framebuffer writes execute unchanged.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EIP
from analyze_executable import analyze
from verify_texts import TextsOracle


class MinimapOracle(TextsOracle):
    RECORD = TextsOracle.BANK
    MAP = RECORD + 0x2000
    GRID = RECORD + 0x20000
    TILES = RECORD + 0x80000
    COLORS = RECORD + 0xa0000
    OBJECTS = RECORD + 0xb0000

    def __init__(self, code):
        super().__init__(code)
        self.cpu.mem_write(self.CODE + 0x3eea, b'\xc3')

    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if not (0xa1d1 <= at < 0xa320 or at == 0x3eea):
            raise ValueError(f'Unexpected minimap control flow {at:X}')

    def put32(self, at, *values):
        self.cpu.mem_write(at, struct.pack('<' + 'I' * len(values), *values))

    def run_minimap(self):
        self.run('minimap', 0xa1d1)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP:
            raise ValueError('Minimap did not return')


def inputs(width, height, salt):
    n = width * height
    return dict(unexplored=bytes((i * 37 + salt * 19) & 255 for i in range(n)),
        tiles=[(i * 17 + salt * 3) % 256 for i in range(n)],
        terrain=bytes((i * 3 + 29) & 255 for i in range(256)),
        plane0=[1 + i % 23 for i in range(n)],
        plane1=[1 + (i * 3) % 23 if i % 5 == salt % 5 else 0 for i in range(n)],
        plane2=[1 + i % 23 if i % 7 == salt % 7 else 0 for i in range(n)],
        owners=bytes((i * 5 + salt) & 255 for i in range(24)),
        owner_colors=bytes((i * 11 + salt * 7) & 255 for i in range(16)))


def generate(game):
    report, code = analyze(game)
    oracle = MinimapOracle(code); cpu = oracle.cpu
    cases = [[w,h,p,s,(s*5)&255]
             for w,h in [(16,16),(31,23),(64,48),(160,160)]
             for p in [0,1,7,8,15] for s in [0,3,15]]
    digest = hashlib.sha256()
    for width,height,participant,salt,terrain_owner in cases:
        values = inputs(width,height,salt); n = width*height
        oracle.put32(0x18de,width,height)
        for at,val in [(0x18da,oracle.MAP),(0x18ce,oracle.GRID),
                       (0x18ee,oracle.TILES),(0x18fa,oracle.COLORS),
                       (0x6aa,oracle.OBJECTS),(0x68e,oracle.FRAME)]:
            oracle.put32(at,val)
        cpu.mem_write(0x1c50,values['owner_colors'])
        oracle.put32(0x1c58,0,0); oracle.put32(0x1906,1,1)
        cpu.mem_write(0x1c4e,b'\x01');cpu.mem_write(0x1c60,b'\x01')
        cpu.mem_write(0x1972,bytes([participant]));cpu.mem_write(0x1971,bytes([terrain_owner]))
        cpu.mem_write(oracle.MAP,values['unexplored'])
        for plane in range(3):
            refs=values[f'plane{plane}']
            cpu.mem_write(oracle.GRID+plane*n*4,struct.pack('<'+'I'*n,*(r*64 for r in refs)))
        for ref,owner in enumerate(values['owners']):
            cpu.mem_write(oracle.OBJECTS+ref*64+3,bytes([owner]))
        cpu.mem_write(oracle.TILES,b''.join(struct.pack('<HH',i,0) for i in values['tiles']))
        cpu.mem_write(oracle.COLORS,values['terrain'])
        cpu.mem_write(oracle.FRAME,b'\xa5'*(640*480))
        oracle.run_minimap()
        digest.update(bytes(cpu.mem_read(oracle.FRAME,640*480)))
    return dict(format_version=1,source_sha256=report['source_sha256']['NORWIND.EXE'],
                cases=cases,case_count=len(cases),framebuffer_sha256=digest.hexdigest(),
                stubbed_call='3EEA dirty rectangle')


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game',type=Path,required=True)
    parser.add_argument('--fixture',type=Path,required=True)
    args=parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game),indent=2)+'\n')
