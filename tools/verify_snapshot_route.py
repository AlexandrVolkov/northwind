#!/usr/bin/env python3
"""Runs the original local contour search (LE:1 73F5, route storage input) with Unicorn on a snapshot of the live original (tools/reference_run.py snapshot).

The snapshot is the emulated linear memory. Pointers are linear (the pool, the map, the planes); the variables of the data segment are addressed absolutely by the code, so a copy of the data segment
(linear 17F000h) is placed at address 0 as well. Prints the directions the original gives for a unit and a goal cell.
usage: verify_snapshot_route.py SNAPSHOT --unit REF --goal X Y [--code-base 0x1b7153]
"""
import argparse
import struct
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
from unicorn.x86_const import *

DS = 0x17F000
STACK = 0x1F00000
STOP = 0x1FF0000


def load(path, code_base):
    cpu = Uc(UC_ARCH_X86, UC_MODE_32)
    data = open(path, 'rb').read()
    cpu.mem_map(0, len(data)); cpu.mem_write(0, data)
    cpu.mem_write(0, data[DS:DS + 0x20000])          # absolute data operands
    return cpu, data


def search(cpu, data, code_base, unit, goal, entry=0x73f5):
    pool = struct.unpack_from('<I', data, DS + 0x6aa)[0]
    cpu.mem_write(0x525, bytes(goal)); cpu.mem_write(0x1c61, bytes([200]))
    cpu.mem_write(0x2730, struct.pack('<I', 0xffffffff))
    esp = STACK - 0x100
    cpu.mem_write(esp, struct.pack('<I', STOP)); cpu.reg_write(UC_X86_REG_ESP, esp)
    cpu.reg_write(UC_X86_REG_ESI, pool + unit); cpu.reg_write(UC_X86_REG_EFLAGS, 2)
    cpu.emu_start(code_base + entry, STOP, count=3000000)
    result = struct.unpack('<I', bytes(cpu.mem_read(0x2730, 4)))[0]
    dirs = []
    if result != 0xffffffff:
        for k in range(200):
            w = bytes(cpu.mem_read(result + 2 * k, 2))
            dirs.append(w[0] & 0xf)
            if w[0] & 0x88: break
    return result, dirs


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('snapshot'); ap.add_argument('--unit', type=int, required=True); ap.add_argument('--goal', type=int, nargs=2, required=True)
    ap.add_argument('--code-base', type=lambda x: int(x, 0), default=0x1b7153)
    a = ap.parse_args()
    cpu, data = load(a.snapshot, a.code_base)
    print(search(cpu, data, a.code_base, a.unit, a.goal))
