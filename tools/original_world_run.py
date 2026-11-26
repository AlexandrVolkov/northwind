#!/usr/bin/env python3
"""The whole object update loop (LE:1 42BB: settlements, objects, AI) of the live original under Unicorn on a snapshot: original_world_run.py SNAPSHOT TICKS [REPORT_EVERY].
Prints the objects that moved. A tick sets [1C4E] = 1 and [1C4F] = 1 so the loop body runs once (the game runs it every second timer tick)."""
import struct
import sys
import time
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import original_engine as E


def units(e):
    out = []
    for ow in range(8):
        part = 0x97a + 0xf5 * ow
        first = struct.unpack('<I', e.dsr(part + 5, 4))[0]; end = e.pool + struct.unpack('<I', e.dsr(part + 9, 4))[0]
        cur = first
        while 0 < cur < end:
            r = bytes(e.cpu.mem_read(cur, 0x36)); size = struct.unpack_from('<I', r, 0x10)[0]
            out.append((ow, r[0], r[0xe], struct.unpack_from('<II', r, 4), r[0x19], r[0x2d], cur - e.pool))
            if size == 0: break
            cur += size
    return out


if __name__ == '__main__':
    e = E.Engine(sys.argv[1]); ticks = int(sys.argv[2]); every = int(sys.argv[3]) if len(sys.argv) > 3 else 500
    e.dsw(0x1c4e, bytes([1])); first = {u[6]: u for u in units(e)}; start = time.time()
    for t in range(1, ticks + 1):
        e.dsw(0x1c4f, bytes([1]))
        try: e.call(0x42bb, count=50000000)
        except Exception as ex: print('stop at tick', t, ex, hex(e.cpu.reg_read(E.UC_X86_REG_EIP) - E.CODE)); break
        if t % every == 0:
            now = units(e); moved = [(first[u[6]][3], u[3], u[0], u[1], u[2]) for u in now if u[6] in first and (u[0], u[1]) != (7, 4) and first[u[6]][3] != u[3]]
            print(f'tick {t} ({time.time() - start:.0f}s): {len(now)} objects, {len(moved)} moved:', moved[:6])
