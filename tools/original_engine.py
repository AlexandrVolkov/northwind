#!/usr/bin/env python3
"""The original unit logic of NORWIND.EXE run under Unicorn on a snapshot of the live game (tools/reference_run.py snapshot, 72 MB of emulated memory).

Layout of the live game: the code object is at linear 1EF000h, the data segment at 17F000h (the live code is relocated: absolute operands are linear addresses 17F000h + offset, so the snapshot is used as it is), pointers are linear, the FS table (FS:xx = code
offset + 1EF000h) is at 28A180h. Flat segments, FS with that base. Stack and GDT sit in the free top of the memory.
"""
import struct
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32
from unicorn.x86_const import *

CODE, DS, FS, SIZE = 0x1EF000, 0x17F000, 0x28A180, 72 << 20
GDT, STACK, STOP = SIZE - 0x2000, SIZE - 0x10000, SIZE - 0x4000


class Engine:
    def __init__(self, path):
        data = open(path, 'rb').read()
        self.cpu = cpu = Uc(UC_ARCH_X86, UC_MODE_32)
        cpu.mem_map(0, SIZE); cpu.mem_write(0, data)
        cpu.mem_write(GDT + 8, struct.pack('<HHBBBB', 0xfff, FS & 65535, (FS >> 16) & 255, 0x93, 0x40, FS >> 24))
        cpu.mem_write(GDT + 16, struct.pack('<HHBBBB', 65535, 0, 0, 0x93, 0xcf, 0))
        cpu.mem_write(GDT + 24, struct.pack('<HHBBBB', 65535, 0, 0, 0x9b, 0xcf, 0))
        cpu.reg_write(UC_X86_REG_GDTR, (0, GDT, 31, 0))
        for r in (UC_X86_REG_DS, UC_X86_REG_ES, UC_X86_REG_SS): cpu.reg_write(r, 0x10)
        cpu.reg_write(UC_X86_REG_CS, 0x18); cpu.reg_write(UC_X86_REG_FS, 8)
        self.pool = struct.unpack_from('<I', data, DS + 0x6aa)[0]

    def u32(self, a): return struct.unpack('<I', bytes(self.cpu.mem_read(a, 4)))[0]
    def put(self, a, b): self.cpu.mem_write(a, b)
    def dsw(self, off, b): self.cpu.mem_write(DS + off, b)         # a variable of the data segment
    def dsr(self, off, n): return bytes(self.cpu.mem_read(DS + off, n))

    def call(self, offset, count=3000000, **regs):
        cpu = self.cpu; esp = STACK - 0x100
        cpu.mem_write(esp, struct.pack('<I', STOP)); cpu.reg_write(UC_X86_REG_ESP, esp); cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        for k, v in regs.items(): cpu.reg_write(globals()['UC_X86_REG_' + k.upper()], v)
        cpu.emu_start(CODE + offset, STOP, count=count)
