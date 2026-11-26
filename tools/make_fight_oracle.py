#!/usr/bin/env python3
"""Per-tick positions, states and health of two hostile militiamen (owner 0 and owner 3) on open ground under the original unit script (D182, AH = 1): tests/fixtures/fight-oracle.json.
usage: make_fight_oracle.py SNAPSHOT OUT.json"""
import json
import struct
import sys
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import original_engine as E
import original_world_run as W

cases = []
for gap in (3, 5, 8):
    e = E.Engine(sys.argv[1]); us = W.units(e)
    A = [u for u in us if u[0] == 0 and u[1] == 2 and u[2] == 0][0]; B = [u for u in us if u[0] == 3 and u[1] == 2 and u[2] == 0][0]
    rA, rB = e.pool + A[6], e.pool + B[6]
    mapp = struct.unpack('<I', e.dsr(0x18ee, 4))[0]; tile = struct.unpack('<I', bytes(e.cpu.mem_read(mapp + 4 * (30 * 128 + 30), 4)))[0]
    e.cpu.mem_write(mapp, struct.pack('<I', tile) * (128 * 128))
    for k in range(3): e.cpu.mem_write(struct.unpack('<I', e.dsr(0x18ce + 4 * k, 4))[0], bytes(128 * 128 * 4))
    part = lambda o: 0x97a + 0xf5 * o
    e.dsw(part(3) + 0x12, struct.pack('<I', 1)); e.dsw(part(0) + 0x12, struct.pack('<I', 8))
    pl = struct.unpack('<I', e.dsr(0x18ce + 4, 4))[0]
    for r, ref, (x, y) in ((rA, A[6], (50, 50)), (rB, B[6], (50 + gap, 50))):
        for off, val in ((4, struct.pack('<II', x * 32 + 16, y * 32 + 16)), (0x2d, b'\0'), (0x14, b'\0'), (0x15, b'\0'), (0x1c, bytes(4)), (0x2e, b'\0'), (0x25, b'\x02' if r == rA else b'\x06'), (0x1b, b'\0'), (0x22, b'\0\0'), (0x26, b'\0'), (0x2f, b'\0'), (0x30, b'\0\0\0\0\0')): e.cpu.mem_write(r + off, val)
        e.cpu.mem_write(pl + 4 * (y * 128 + x), struct.pack('<I', ref))
    tbl = struct.unpack('<I', e.dsr(0x6ce + 0x56 * 2, 4))[0]
    log = []
    for t in range(120):
        for r in (rA, rB):
            e.dsw(0x1c49, struct.pack('<I', tbl)); e.call(0xd182, esi=r, eax=0x100)
        ra, rb = bytes(e.cpu.mem_read(rA, 0x36)), bytes(e.cpu.mem_read(rB, 0x36))
        log.append([list(struct.unpack_from('<II', ra, 4)), ra[0x19], ra[0x2d], list(struct.unpack_from('<II', rb, 4)), rb[0x19], rb[0x2d]])
    cases.append({'gap': gap, 'ticks': log})
json.dump({'note': 'Original unit script on a snapshot: hostile militiamen A (owner 0, cell (50,50)) and B (owner 3) on open ground; per tick A position, health, state (+2D), B position, health, state.', 'cases': cases}, open(sys.argv[2], 'w'))
