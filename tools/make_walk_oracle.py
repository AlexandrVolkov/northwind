#!/usr/bin/env python3
"""Per-tick trajectories of the original unit script (militia, open ground, one unit) for several headings and goals: tests/fixtures/walk-oracle.json.
usage: make_walk_oracle.py SNAPSHOT OUT.json [real]   (real: keep the terrain of the map, start (21,29), far and blocked goals)"""
import json
import struct
import sys
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import original_engine as E

real = len(sys.argv) > 3
cases = []
start = (21, 29) if real else (50, 50)
for heading in ((0, 3) if real else (0, 3, 5)):
    for goal in (((37, 35), (60, 60), (21, 90), (40, 29), (12, 40)) if real else ((60, 53), (50, 60), (40, 45), (58, 50))):
        e = E.Engine(sys.argv[1]); rec = e.pool + 204
        mapp = struct.unpack('<I', e.dsr(0x18ee, 4))[0]; tile = struct.unpack('<I', bytes(e.cpu.mem_read(mapp + 4 * (30 * 128 + 30), 4)))[0]
        if not real: e.cpu.mem_write(mapp, struct.pack('<I', tile) * (128 * 128))
        for k in range(3): e.cpu.mem_write(struct.unpack('<I', e.dsr(0x18ce + 4 * k, 4))[0], bytes(128 * 128 * 4))
        tbl = struct.unpack('<I', e.dsr(0x6ce + 0x56 * 2, 4))[0]
        e.cpu.mem_write(rec + 4, struct.pack('<II', start[0] * 32 + 16, start[1] * 32 + 16)); e.cpu.mem_write(rec + 0x22, bytes(goal)); e.cpu.mem_write(rec + 0x2d, bytes([0x22]))
        e.cpu.mem_write(rec + 0x14, bytes([1])); e.cpu.mem_write(rec + 0x15, bytes([0])); e.cpu.mem_write(rec + 0x25, bytes([heading]))
        e.dsw(0x1c49, struct.pack('<I', tbl)); e.dsw(0x1c61, bytes([200]))
        out = []
        for t in range(400 if real else 130):
            e.call(0xd182, esi=rec, eax=0x100); out.append(struct.unpack('<II', bytes(e.cpu.mem_read(rec + 4, 8))))
        cases.append({'heading': heading, 'goal': list(goal), 'start': list(start), 'positions': out})
json.dump({'note': 'Original unit script (D182, AH=1) on a snapshot of the live game: militia of mission 1, all cells ground, empty occupancy, start (50,50); positions after each tick.', 'cases': cases}, open(sys.argv[2], 'w'))
print(len(cases), 'cases')
