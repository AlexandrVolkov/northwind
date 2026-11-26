#!/usr/bin/env python3
"""Execute the native planner 72FE (explicit parameters, up to four unbounded contour searches) and hash its results."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_route_search import SearchOracle, make_cases, DELTAS


class PlanOracle(SearchOracle):
    def guard(self, cpu, address, size, user_data):
        if address == self.CODE + 0x7d4c:
            x, y = cpu.mem_read(0x274e, 2); d = cpu.reg_read(UC_X86_REG_EAX) & 255
            dx, dy = DELTAS[d]; self.trace.extend([(x + dx) & 255, (y + dy) & 255, d])
        if not any(self.CODE + a <= address < self.CODE + b for a, b in [(0x72fe, 0x7a03), (0x7d4c, 0x7fa7), (0x6877, 0x68a0)]):
            raise ValueError(f'Unexpected planner execution: {address:#x}')

    def plan(self, c):
        w, h = c['size']; fw, fh = c['footprint']; x, y = c['start']; gx, gy = c['target']
        self.prepare(w, h); self.cpu.mem_write(0x1f00, bytes(0x900))
        dat = bytearray(171); dat[3:5] = bytes([fw, fh]); dat[0x58] = 8; dat[0x59] = 0
        self.cpu.mem_write(self.DAT, bytes(dat)); self.cpu.mem_write(0x6ce + 2 * 0x56, struct.pack('<I', self.DAT))
        self.cpu.mem_write(0x98c, bytes(4))
        cells = bytes.fromhex(c['blocked']); self.cpu.mem_write(self.MAP, b''.join(struct.pack('<I', v) for v in cells))
        self.cpu.mem_write(self.SEG, bytes(8) + bytes([1]) + bytes(7))
        self.cpu.mem_write(0x18ee, struct.pack('<I', self.MAP)); self.cpu.mem_write(0x18fe, struct.pack('<I', self.SEG))
        self.cpu.mem_write(0x2798, bytes(4)); self.cpu.mem_write(0x1f41, bytes([0]))
        half = fw >> 1
        self.trace = bytearray(); self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.reg_write(UC_X86_REG_EAX, 0); self.cpu.reg_write(UC_X86_REG_EBX, 1 | (fw << 8))
        self.cpu.reg_write(UC_X86_REG_ECX, (x + half) | ((y + half) << 8)); self.cpu.reg_write(UC_X86_REG_EDX, (gx + half) | ((gy + half) << 8))
        self.cpu.reg_write(UC_X86_REG_EDI, self.DAT + 0x58); self.cpu.reg_write(UC_X86_REG_ESI, self.POOL + 1)
        self.cpu.emu_start(self.CODE + 0x72fe, self.STOP, count=60000000)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError(f'Native planner did not return: {c["name"]}')
        found = (self.cpu.reg_read(UC_X86_REG_EAX) & 255) == 0xff
        pointer = struct.unpack('<I', self.cpu.mem_read(0x2730, 4))[0]
        end = bytes(self.cpu.mem_read(0x274e, 2)); left = self.cpu.mem_read(0x1f41, 1)[0]; route = bytearray()
        if pointer != 0xffffffff:
            for i in range(2100):
                v = self.cpu.mem_read(pointer + i, 1)[0]
                if v & 8: break
                route.append(v)
            else: raise ValueError('Unterminated native path')
        return bytes([found]) + struct.pack('<H', len(route)) + bytes(route) + end + bytes([left]) + struct.pack('<I', len(self.trace) // 3) + self.trace, found


def generate(game):
    report, code = analyze(game); o = PlanOracle(code, report['fs_assignments'])
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    o.cpu.mem_write(0x19c7, objects[1]['payload'][0x19c7:0x19e0])
    cases = [c for c in make_cases() if c['footprint'][0] == c['footprint'][1] and c['footprint'][0] <= 2]
    digest = hashlib.sha256(); found_count = 0
    for i, c in enumerate(cases):
        packed, found = o.plan(c); digest.update(packed); c['sha256'] = hashlib.sha256(packed).hexdigest(); found_count += found
        if i % 32 == 0: print(i, c['name'], found, flush=True)
    return dict(format_version=1, source_sha256=report['source_sha256']['NORWIND.EXE'], cases=cases, result=dict(case_count=len(cases), found_count=found_count, sha256=digest.hexdigest()))


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__); p.add_argument('--game', type=Path, required=True); p.add_argument('--fixture', type=Path, required=True); a = p.parse_args()
    result = generate(a.game); a.fixture.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8'); print(result['result'])
