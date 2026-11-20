#!/usr/bin/env python3
"""Execute the fire shot (ARMY__00.WRK effect type 11h, BAB) and its smoke trail (type 12h, C8A) under Unicorn.

  create  WRK BAB AH=0, with a shooter (owner, scatter 9A1B accuracy 2) or without (ballista spawn): velocity 996F
          speed 15, direction A5B8, sound A47E
  step    WRK BED: smoke spawn 9727 (stubbed, recorded), 982B before the flight (foreign building: ignite on 9E41 bit 0),
          flight D6E6, 982B after it: hit with attack 4 - DAT[44], deflect when 9E41 * DAT[44] >= 256; forest ignites.
          The continuations CE3 (fire) and FA4 (deflection) are recorded as exits.
  trail   WRK C8A AH=0 and AH=1 (rise, drift by FS:[0C] -> DS:1C8E, frames)
"""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, unit, pattern, STRIDE
from forestry_source import source_constants

W, H = 16, 16


class FireOracle(CombatOracle):
    RANGES = {
        'create': ([(0x9a1b, 0x9a8e), (0x996f, 0x9a1b), (0xa5b8, 0xa5f4), (0x9e41, 0x9e69)], [(0xbab, 0xbed)]),
        'step': ([(0xd6e6, 0xd740), (0x982b, 0x98cc), (0x9e41, 0x9e55)], [(0xbab, 0xc8a)]),
        'trail': ([], [(0xc8a, 0xce3)]),
    }

    def guard(self, cpu, address, size, user_data):
        if self.mode == 'step' and address in (self.WRK + 0xce3, self.WRK + 0xfa4):
            self.exit = ['%X' % (address - self.WRK), (cpu.reg_read(UC_X86_REG_EAX) >> 8) & 255]
            cpu.reg_write(UC_X86_REG_EIP, self.STOP); cpu.emu_stop(); return
        CombatOracle.guard(self, cpu, address, size, user_data)


def effect_record(x, y, layer, kind=0x11):
    raw = pattern(0x22, 5, 9); raw[0] = kind; raw[1] = 0; raw[5] = layer; raw[0x11] = 0
    struct.pack_into('<I', raw, 7, x); struct.pack_into('<I', raw, 0xc, y)
    return raw


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = FireOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat); o.cpu.mem_write(o.WRK, (game / 'ARMY__00.WRK').read_bytes())
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    fixture = {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT', 'ARMY__00.WRK']},
               'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in [0, 2, 3, 17, 18]}, 'results': {}}
    sounds = []; spawns = []

    def save(name, digest, count):
        fixture['results'][name] = {'case_count': count, 'sha256': digest.hexdigest()}; print(name, count, flush=True)

    o.prepare(W, H)
    o.cpu.mem_write(0x18e6, struct.pack('<II', W * 32, H * 32))
    o.cpu.mem_write(o.FS, struct.pack('<II', H * 32, W * 32))
    o.cpu.mem_write(o.FS + 0xc, struct.pack('<I', 0x1c8e))
    def spawn():
        r = o.cpu.reg_read
        spawns.append([r(UC_X86_REG_EAX) & 255, r(UC_X86_REG_EBX) & 0xffff, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX)]); o.ret()
    o.stubs = {0xa47e: lambda: (sounds.append(o.cpu.reg_read(UC_X86_REG_EAX)), o.ret()), 0x9727: spawn}

    spec = {'origins': [[48, 48], [250, 260], [500, 16]], 'targets': [[48, 48], [80, 48], [48, 400], [300, 301], [511, 511], [0, 0]],
            'experience': [0, 16, 127], 'random_indices': [0, 1, 99, 255], 'shooter': [0, 1]}
    fixture['create'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'create'
    o.terrain(W, H, [0] * (W * H))
    for (ax, ay), (tx, ty), experience, index, has in product(*(spec[k] for k in ['origins', 'targets', 'experience', 'random_indices', 'shooter'])):
        sounds.clear(); shooter = unit(17, 3, 10, 0, ax, ay, 1); shooter[0x16] = experience; o.cpu.mem_write(o.POOL + 0x200, bytes(shooter))
        effect = effect_record(ax + 8, ay - 8, 1); o.cpu.mem_write(o.POOL + 1, bytes(effect))
        o.cpu.mem_write(0x19eb, bytes([index, (index * 7) & 255]))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EBX, o.POOL + 0x200 if has else 0)
        o.cpu.reg_write(UC_X86_REG_ECX, tx); o.cpu.reg_write(UC_X86_REG_EDX, ty); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.WRK + 0xbab)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + bytes(o.cpu.mem_read(0x19eb, 2)) + json.dumps(sounds).encode()); count += 1
    save('create', digest, count)

    # Objects: [template, owner, hp]; the shot's owner is 3. Terrain: class of every cell (1 = forest).
    spec = {'positions': [[200, 200], [5, 7], [16 * 32 - 3, 40]], 'remaining': [-1, 0, 30],
            'velocities': [[0xf00, 0], [-0xf00, 0x300], [0, 0]], 'objects': [None, [0, 3, 10], [0, 1, 10], [0, 1, 0], [2, 1, 100], [2, 3, 100], [3, 1, 60], [18, 1, 10]],
            'forest': [0, 1], 'random_indices': [0, 1, 77, 200]}
    fixture['step'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'step'
    for (x, y), remaining, (vx, vy), obj, forest, index in product(*(spec[k] for k in ['positions', 'remaining', 'velocities', 'objects', 'forest', 'random_indices'])):
        sounds.clear(); spawns.clear(); o.exit = None
        o.terrain(W, H, [forest] * (W * H)); o.prepare(W, H)
        o.cpu.mem_write(0x18e6, struct.pack('<II', W * 32, H * 32))
        effect = effect_record(x, y, 1); effect[2] = 3; struct.pack_into('<i', effect, 0x12, remaining); struct.pack_into('<ii', effect, 0x18, vx, vy)
        o.cpu.mem_write(o.POOL + 1, bytes(effect)); o.cpu.mem_write(o.GRID, bytes(W * H * 12))
        if obj:
            t, owner, hp = obj
            rec = unit(t, owner, hp, 0x1f, x, y, 1); o.cpu.mem_write(o.POOL + 0x100, bytes(rec))
            o.cpu.mem_write(o.GRID + (W * H + (min(H - 1, y >> 5)) * W + min(W - 1, x >> 5)) * 4, struct.pack('<I', 0x100))
        o.cpu.mem_write(0x19eb, bytes([index]))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EAX, 0x100)
        o.run(o.WRK + 0xbab)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + (bytes(o.cpu.mem_read(o.POOL + 0x100, 0x36)) if obj else b'') + bytes(o.cpu.mem_read(0x19eb, 1))
                      + json.dumps([sounds, spawns, o.exit]).encode()); count += 1
    save('step', digest, count)

    spec = {'drift': [[0, 0], [1000, -1000]], 'states': [[0, 0x1f4, 3, 11], [0x100, 0x20, 1, 1], [0xff00, 0x40, 1, 5], [5, 0, 2, 3]]}
    fixture['trail'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'trail'
    for (dx, dy), (height, rise, timer, left) in product(*(spec[k] for k in ['drift', 'states'])):
        for ah in (0, 1):
            effect = effect_record(300, 200, 1, 0x12); struct.pack_into('<HH', effect, 0x10, height, 0); struct.pack_into('<H', effect, 0x20, rise)
            effect[0x16] = timer; effect[0x17] = left
            o.cpu.mem_write(o.POOL + 1, bytes(effect)); o.cpu.mem_write(0x1c8e, struct.pack('<ii', dx, dy))
            o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EAX, ah << 8)
            o.run(o.WRK + 0xc8a)
            digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22))); count += 1
    save('trail', digest, count)
    return fixture


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game), indent=2) + '\n', encoding='utf-8')
