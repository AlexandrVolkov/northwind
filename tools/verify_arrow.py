#!/usr/bin/env python3
"""Execute the original arrow effect (ARMY__00.WRK effect type 2) under Unicorn; store hashed results.

  create  WRK 593..5D9 (AH=0): owner, aim scatter 9A1B, velocity 996F, direction frame A5B8, sound A47E
  step    WRK 5DA..6C6 (AH=1): flight with 24.8 positions, early hit on foreign obstacles, arrival hit
          with attack 4 - DAT[44], then the visual effects 6FE/735 (AH=0) or the miss handlers 7FE/AEB
Sound (A47E) is stubbed and recorded; 7FE and AEB are recorded as exits.
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


class ArrowOracle(CombatOracle):
    RANGES = {
        'create': ([(0x9a1b, 0x9a8e), (0x996f, 0x9a1b), (0xa5b8, 0xa5f4), (0x9e41, 0x9e69)], [(0x593, 0x5da)]),
        'step': ([(0x982b, 0x98cc), (0x9e41, 0x9e55)], [(0x5d9, 0x6c7), (0x6fe, 0x71f), (0x735, 0x74c)]),
    }


def effect_record(x, y, layer):
    raw = pattern(0x22, 5, 9); raw[0] = 2; raw[1] = 0; raw[5] = layer; raw[0x11] = 0
    struct.pack_into('<I', raw, 7, x); struct.pack_into('<I', raw, 0xc, y)
    return raw


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = ArrowOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat); o.cpu.mem_write(o.WRK, (game / 'ARMY__00.WRK').read_bytes())
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    fixture = {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT', 'ARMY__00.WRK']},
               'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in [0, 2]}, 'results': {}}
    sounds = []; exits = []

    def save(name, digest, count):
        fixture['results'][name] = {'case_count': count, 'sha256': digest.hexdigest()}; print(name, count, flush=True)

    o.prepare(16, 16); o.terrain(16, 16, [0] * 256)
    o.cpu.mem_write(0x18e6, struct.pack('<II', 512, 512))
    o.cpu.mem_write(o.FS, struct.pack('<II', 512, 512))  # FS:[0] height, FS:[4] width in pixels
    o.stubs = {0xa47e: lambda: (sounds.append(o.cpu.reg_read(UC_X86_REG_EAX)), o.ret())}

    spec = {'origins': [[48, 48], [250, 260], [500, 16]], 'targets': [[48, 48], [80, 48], [48, 400], [300, 301], [16, 500], [511, 511], [0, 0], [250, 20]],
            'experience': [0, 15, 16, 127, 255], 'random_indices': [0, 1, 99, 255]}
    fixture['create'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'create'
    for (ax, ay), (tx, ty), experience, index in product(*(spec[k] for k in ['origins', 'targets', 'experience', 'random_indices'])):
        sounds.clear(); attacker = unit(1, 0, 10, 0, ax, ay, 1); attacker[0x16] = experience; o.cpu.mem_write(o.POOL + 0x200, bytes(attacker))
        effect = effect_record(ax + 8, ay - 8, 1); o.cpu.mem_write(o.POOL + 1, bytes(effect))
        o.cpu.mem_write(0x19eb, bytes([index, (index * 7) & 255]))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EBX, o.POOL + 0x200)
        o.cpu.reg_write(UC_X86_REG_ECX, tx); o.cpu.reg_write(UC_X86_REG_EDX, ty); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.WRK + 0x593)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + bytes(o.cpu.mem_read(0x19eb, 2)) + json.dumps(sounds).encode()); count += 1
    save('create', digest, count)

    def leave(name):
        def stub():
            exits.append([name, o.cpu.reg_read(UC_X86_REG_EBX) & 255]); o.ret(); o.cpu.emu_stop(); o.stopped = True
        return stub
    # 7FE/AEB are jumped to, not called: stop there and record the argument in BL.
    o.boundary = None
    spec = {'remaining': [0, 5, 16, 17, 1000], 'velocities': [[0x0f00, 0], [-0x0f00, 0x0300], [0x40, -0x0f00], [0x0a00, 0x0a00]],
            'present': [0, 1], 'flags': [0, 8, 0x10, 0x18], 'owners': [0, 3], 'health': [0, 1, 10], 'defences': [0, 3, 4, 7],
            'random_indices': [0, 1]}
    fixture['step'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'step'
    for remaining, (vx, vy), present, flags, owner, hp, defence, index in product(*(spec[k] for k in [
            'remaining', 'velocities', 'present', 'flags', 'owners', 'health', 'defences', 'random_indices'])):
        sounds.clear(); exits.clear()
        base = o.DAT + 2 + 2 * STRIDE; o.cpu.mem_write(base, template(2))
        o.cpu.mem_write(base + 3, bytes([1, 1])); o.cpu.mem_write(base + 0x40, struct.pack('<I', flags)); o.cpu.mem_write(base + 0x44, bytes([defence]))
        effect = effect_record(200, 200, 1); effect[2] = 0; effect[0x17] = 0x0a
        struct.pack_into('<I', effect, 0x12, remaining); struct.pack_into('<ii', effect, 0x18, vx, vy)
        o.cpu.mem_write(o.POOL + 1, bytes(effect)); o.cpu.mem_write(o.GRID, bytes(16 * 16 * 12))
        target = unit(2, owner, hp, 0, 208, 208, 1); o.cpu.mem_write(o.POOL + 0x100, bytes(target))
        if present: o.cpu.mem_write(o.GRID + (256 + 6 * 16 + 6) * 4, struct.pack('<I', 0x100))
        o.cpu.mem_write(0x19eb, bytes([index]))
        o.stubs = {0xa47e: lambda: (sounds.append(o.cpu.reg_read(UC_X86_REG_EAX)), o.ret())}
        o.RANGES['step'][1].extend([])
        o.boundary = None; o.exit_points = {0x7fe: '7FE', 0xaeb: 'AEB'}
        o.stopped = False
        o.cpu.mem_write(o.STACK + 0xf00, struct.pack('<I', o.STOP))
        o.cpu.reg_write(UC_X86_REG_ESP, o.STACK + 0xf00); o.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EAX, 0x100)
        o.wrk_exit = None
        o.cpu.emu_start(o.WRK + 0x5da, o.STOP, count=100000)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + bytes(o.cpu.mem_read(o.POOL + 0x100, 54)) + bytes(o.cpu.mem_read(0x19eb, 1))
                      + json.dumps([sounds, o.wrk_exit]).encode()); count += 1
    save('step', digest, count)
    return fixture


def guard(self, cpu, address, size, user_data):
    if self.mode == 'step' and address in (self.WRK + 0x7fe, self.WRK + 0xaeb):
        self.wrk_exit = ['%X' % (address - self.WRK), cpu.reg_read(UC_X86_REG_EBX) & 255]
        cpu.reg_write(UC_X86_REG_EIP, self.STOP); cpu.emu_stop(); return
    CombatOracle.guard(self, cpu, address, size, user_data)


ArrowOracle.guard = guard

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game), indent=2) + '\n', encoding='utf-8')
