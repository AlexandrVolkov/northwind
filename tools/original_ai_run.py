#!/usr/bin/env python3
"""The AI of the live original: the object loop 42BB and the AI loop 1E7EA per tick on a snapshot, war between owner 3 (AI) and owner 0 (the player).
usage: original_ai_run.py SNAPSHOT TICKS [EVERY]; prints the soldiers (army 2, templates 0..2) of owner 3 and how far they are from their castle."""
import struct
import sys
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import original_engine as E
import original_world_run as W

e = E.Engine(sys.argv[1]); ticks = int(sys.argv[2]); every = int(sys.argv[3]) if len(sys.argv) > 3 else 5000
part = lambda o: 0x97a + 0xf5 * o
e.dsw(part(3) + 0x12, struct.pack('<I', 1)); e.dsw(part(0) + 0x12, struct.pack('<I', 8))
e.dsw(part(3) + 0xb9, bytes([2])); e.dsw(part(0) + 0xb9 + 3, bytes([2])); e.dsw(0x1c4e, bytes([1]))
# the AI of owner 3: active (+1A27 bit 0) and one campaign plan (mode 1, stage 0, target = the player) in the plan list +697 (entries of 15h bytes, mode FFh = free)
block = struct.unpack('<I', e.dsr(0x6ba, 4))[0] + 0x1a35 * 3
e.put(block + 0x1a27, bytes([int(e.cpu.mem_read(block + 0x1a27, 1)[0]) | 1]))   # the blocks are at linear addresses
e.put(block + 0x697, bytes([1, 0, 0, 0, 0, 0, 0, 0]))
e.dsw(part(3) + 0x18, struct.pack('<IIII', 20000, 20000, 20000, 20000))   # resources of the AI participant
e.put(block + 0x41, bytes(4))   # the threshold of the own force (+41): the AI does not fall back to a truce
castle = next(u[3] for u in W.units(e) if u[0] == 3 and u[1] == 2 and u[2] == 2)
for t in range(1, ticks + 1):
    e.dsw(0x1c4f, bytes([1]))
    try: e.call(0x42bb, count=50000000); e.call(0x1e7ea, count=50000000)
    except Exception as ex: print('stop', t, ex, hex(e.cpu.reg_read(E.UC_X86_REG_EIP) - E.CODE)); break
    if t % every == 0:
        soldiers = [u for u in W.units(e) if u[0] == 3 and u[1] == 2 and u[2] in (0, 1) and u[4] > 0]
        far = [(u[3][0] >> 5, u[3][1] >> 5) for u in soldiers if abs(u[3][0] - castle[0]) + abs(u[3][1] - castle[1]) > 640]
        print(f'tick {t}: {len(soldiers)} soldiers of owner 3, {len(far)} far from the castle', far[:8])
