#!/usr/bin/env python3
"""Verify the walk step of units (LE:1 70B0..72FD, state 2 of the motion machine) with Unicorn. Services are recorded stubs: 7FA7 (next route step: AL heading, AH code), 9653 (turn
one sector, ZF), 9B0D (occupancy transfer at the step midpoint), 9727 (effect), 813E (fog reveal), 8120 (release the route), D895 (search for an enemy)."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle
from forestry_source import source_constants


class WalkOracle(CombatOracle):
    RANGES = {'walk': ([(0x70b0, 0x72fe)], [])}

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=2000)


def cases():
    rng = random.Random(0x70b0)
    out = []
    for n in range(1500):
        w = rng.choice([8, 16, 32]); h = rng.choice([8, 16, 32])
        out.append({'al': rng.randrange(8), 'bl': rng.choice([0, 0x40, 0x80, 0xc0, rng.randrange(256)]), 'f24': rng.choice([0, 1, 2, 3, 4, 5, 8, rng.randrange(256)]), 'f25': rng.randrange(256),
                    'f26': rng.randrange(256), 'f27': rng.choice([0, 1, 5, 0x20, rng.randrange(256)]), 'f2c': rng.choice([0, 1, 5, 0x10, 0x11, 0x20, rng.randrange(256)]), 'f16': rng.randrange(256),
                    'f0d': rng.randrange(256), 'x': rng.randrange(0, w * 32), 'y': rng.randrange(0, h * 32), 'f28': rng.randrange(1 << 32),
                    'dir': rng.randrange(8), 'code': rng.choice([0, 1, 2, 3, 3, 3, 4]),
                    'turn_zf': [rng.choice([0, 1]) for _ in range(3)],
                    'width': w, 'tab18ce': rng.randrange(256), 'tabs': rng.randrange(1 << 30),
                    'template': {'n4a': rng.choice([1, 4, 8, 13]), 'n57': rng.choice([0, 1, 2, 4]), 'na2': rng.choice([0xff, 0x82, 0x88]), 'base': rng.randrange(3000), 'n58': [rng.randrange(256) for _ in range(8)]}})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = WalkOracle(code, report['fs_assignments'], constants)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def route():   # 7FA7
        log.append(b'F'); o.cpu.reg_write(UC_X86_REG_EAX, state['dir'] | (state['code'] << 8)); o.ret()
    def turn():
        log.append(b'T' + struct.pack('<B', (reg(UC_X86_REG_EAX) >> 8) & 255)); z = state['turn_zf'].pop(0) if state['turn_zf'] else 0
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if z else 0)); o.ret()
    def occupancy():
        log.append(b'B' + struct.pack('<I', reg(UC_X86_REG_ECX))); o.ret()
    def effect():
        log.append(b'E' + struct.pack('<BBBII', reg(UC_X86_REG_EAX) & 255, reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX))); o.ret()
    def fog(): log.append(b'V'); o.ret()
    def release(): log.append(b'R'); o.ret()
    def search(): log.append(b'D' + struct.pack('<B', reg(UC_X86_REG_EAX) & 255)); o.ret()
    o.stubs = {0x7fa7: route, 0x9653: turn, 0x9b0d: occupancy, 0x9727: effect, 0x813e: fog, 0x8120: release, 0xd895: search}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state.update(case); state['turn_zf'] = list(case['turn_zf']); o.prepare(32, 32); o.mode = 'walk'
        t = case['template']; raw = bytearray(0xab); raw[0x4a] = t['n4a']; raw[0x57] = t['n57']; raw[0xa2] = t['na2']; struct.pack_into('<H', raw, 0x48, t['base']); raw[0x58:0x60] = bytes(t['n58'])
        o.cpu.mem_write(o.DAT, bytes(raw)); o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT))
        rng = random.Random(case['tabs']); m = o.MAP
        o.cpu.mem_write(m, bytes(rng.randrange(256) for _ in range(0x2000)))
        o.cpu.mem_write(m + 0x8000, b''.join(struct.pack('<H', rng.randrange(0x400)) for _ in range(0x1000)))
        o.cpu.mem_write(m + 0x10000, bytes(rng.randrange(256) for _ in range(0x2000 * 8 + 8)))
        o.cpu.mem_write(0x18ce, struct.pack('<I', m)); o.cpu.mem_write(0x18ee, struct.pack('<I', m + 0x8000)); o.cpu.mem_write(0x18fe, struct.pack('<I', m + 0x10000)); o.cpu.mem_write(0x18de, bytes([case['width']]))
        o.cpu.mem_write(0x19c7, bytes(rng.choice([0, 1, 0xff]) for _ in range(16)))
        idx = (case['x'] >> 5) + (((case['y'] >> 5) * case['width']) & 0xffff)
        case['occ'] = struct.unpack('<I', bytes(o.cpu.mem_read(m + 4 * idx, 4)))[0]
        wi = struct.unpack('<H', bytes(o.cpu.mem_read(m + 0x8000 + 4 * idx, 2)))[0]
        case['tt'] = bytes(o.cpu.mem_read(m + 0x10000 + wi * 8, 1))[0]
        case['signs'] = list(bytes(o.cpu.mem_read(0x19c7, 16)))
        o.cpu.mem_write(o.POOL, bytes(0x1000))
        r = bytearray(0x40); r[0x24] = case['f24']; r[0x25] = case['f25']; r[0x26] = case['f26']; r[0x27] = case['f27']; r[0x2c] = case['f2c']; r[0x16] = case['f16']; r[0x0d] = case['f0d']
        struct.pack_into('<II', r, 4, case['x'], case['y']); struct.pack_into('<I', r, 0x28, case['f28'])
        o.cpu.mem_write(o.POOL + 0x800, bytes(r)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x800); o.cpu.reg_write(UC_X86_REG_EBX, case['bl']); o.cpu.reg_write(UC_X86_REG_EAX, case['al'] | ((case['al'] ^ 5) << 8))
        o.run(o.CODE + 0x70b0)
        out = bytes(o.cpu.mem_read(o.POOL + 0x800, 0x40)) + bytes(o.cpu.mem_read(0x525, 2)) + bytes(o.cpu.mem_read(0x1c9f, 1)) + bytes([reg(UC_X86_REG_EBX) & 255]) + b''.join(log)
        import os
        if os.environ.get('DEBUG_CASE') == str(all_cases.index(case)): print('PYTHON', out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases, 'results': {'walk': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
