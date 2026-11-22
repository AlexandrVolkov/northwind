#!/usr/bin/env python3
"""Verify the tower's attack (FS:9D = LE:1 CA7C, with 9688 the direction of the shot) with Unicorn: the enemy search D895 and the effect creator 9727
are recorded stubs; the check covers the reload counters, the shot direction and the arguments of the effect call."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_combat import CombatOracle, STRIDE
from forestry_source import source_constants


class TowerOracle(CombatOracle):
    RANGES = {'tower': ([(0xca7c, 0xcaee), (0x9688, 0x96cd)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=20000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('tower did not return')


def cases():
    rng = random.Random(0xca7c)
    out = []
    for n in range(4000):
        out.append({'condition': rng.choice([14, 15, 13, 100, 127, 128, 200, 0, 1, 110]), 'tick': rng.randrange(256), 'reload': rng.choice([0, 1, 5, 9, 10, 11, 255, rng.randrange(256)]),
                    'x': rng.randrange(64, 4000), 'y': rng.randrange(64, 4000), 'army': rng.choice([2, 2, 1]), 'found': rng.choice([True, True, False]),
                    'dx': rng.choice([0, 0, 1, -1, 5, -5, 40, -40, 64, -200, 300, rng.randrange(-400, 400)]), 'dy': rng.choice([0, 0, 1, -1, 5, -5, 40, -40, 64, -200, 300, rng.randrange(-400, 400)]),
                    'layer': rng.choice([0, 1, 2])})
        if out[-1]['dx'] == 0 and out[-1]['dy'] == 0: out[-1]['dy'] = 3   # the same point gives direction FFh and a read beyond the record
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = TowerOracle(code, report['fs_assignments'], constants)
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    o.cpu.mem_write(0x19d7, objects[1]['payload'][0x19d7:0x19e0])
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    template = o.DAT + 2 + 13 * STRIDE
    log = []; state = {}
    def search():
        r = o.cpu.reg_read; log.append(b'D' + struct.pack('<B', r(UC_X86_REG_EAX) & 0xff))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x200)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (o.cpu.reg_read(UC_X86_REG_EFLAGS) & ~0x40) | (0 if state['found'] else 0x40)); o.ret()
    def effect():
        r = o.cpu.reg_read; esp = r(UC_X86_REG_ESP)
        args = struct.unpack('<III', bytes(o.cpu.mem_read(esp + 4, 12)))
        log.append(b'E' + struct.pack('<BBBII', r(UC_X86_REG_EBX) & 0xff, (r(UC_X86_REG_EBX) >> 8) & 0xff, r(UC_X86_REG_EAX) & 0xff, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX)) + struct.pack('<III', args[0] - o.POOL, *args[1:]))
        o.ret()
    o.stubs = {0xd895: search, 0x9727: effect}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state['found'] = case['found']; o.prepare(32, 32); o.mode = 'tower'
        o.cpu.mem_write(0x1c49, struct.pack('<I', template))
        tower = bytearray(80); tower[0] = case['army']; struct.pack_into('<II', tower, 4, case['x'], case['y']); tower[0x19] = case['condition']
        tower[0x23] = case['tick']; tower[0x24] = case['reload']; tower[0x16] = 0x55
        victim = bytearray(80); struct.pack_into('<II', victim, 4, (case['x'] + case['dx']) & 0xffffffff, (case['y'] + case['dy']) & 0xffffffff); victim[0x0d] = case['layer']
        o.cpu.mem_write(o.POOL + 0x100, bytes(tower)); o.cpu.mem_write(o.POOL + 0x200, bytes(victim))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100)
        o.run(o.CODE + 0xca7c)
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x30)) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']}, 'cases': all_cases,
            'results': {'tower': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
