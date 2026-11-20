#!/usr/bin/env python3
"""Execute the catapult stone (ARMY__00.WRK effect type 8, 85A) and its explosion (type 10, 986) under Unicorn.

  create  WRK 85A AH=0: owner, aim scatter 9A1B (accuracy 1), velocity 996F (speed 9), sound A47E
  step    WRK 85A AH=1: flight D6E6; on landing BD0F hits the cell (attack 0Ah, planes 0 and 1, deduplicated objects),
          CFAC (crater) is stubbed and recorded, then WRK 986 AH=0 turns the effect into an explosion (sound 7)
  smoke   WRK 986 AH=1 (9AE): drift by the vector FS:[0C] -> DS:1C8E, frames, free
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


class CatapultOracle(CombatOracle):
    RANGES = {
        'create': ([(0x9a1b, 0x9a8e), (0x996f, 0x9a1b), (0x9e41, 0x9e69)], [(0x85a, 0x88e)]),
        'step': ([(0xd6e6, 0xd740), (0xbd0f, 0xbe2e), (0x219c3, 0x21a00)], [(0x85a, 0x8da), (0x986, 0x9ae)]),
        'smoke': ([], [(0x986, 0x9d5)]),
        'bolt_create': ([(0x9a1b, 0x9a8e), (0x996f, 0x9a1b), (0xa5b8, 0xa5f4), (0x9e41, 0x9e69)], [(0x12ec, 0x1319)]),
        'bolt_step': ([(0xd6e6, 0xd740), (0xbd0f, 0xbe2e), (0x219c3, 0x21a00), (0x9e41, 0x9e69)], [(0x12ec, 0x13a3), (0xa44, 0xa6c), (0xad5, 0xaeb)]),
        'big': ([], [(0xa44, 0xa6c), (0xad5, 0xaeb)]),
    }


def effect_record(x, y, layer):
    raw = pattern(0x22, 5, 9); raw[0] = 8; raw[1] = 0; raw[5] = layer; raw[0x11] = 0
    struct.pack_into('<I', raw, 7, x); struct.pack_into('<I', raw, 0xc, y)
    return raw


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = CatapultOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat); o.cpu.mem_write(o.WRK, (game / 'ARMY__00.WRK').read_bytes())
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    fixture = {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT', 'ARMY__00.WRK']},
               'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in [0, 2, 16, 26]}, 'results': {}}
    sounds = []; craters = []

    def save(name, digest, count):
        fixture['results'][name] = {'case_count': count, 'sha256': digest.hexdigest()}; print(name, count, flush=True)

    o.prepare(W, H); o.terrain(W, H, [0] * (W * H))
    o.cpu.mem_write(0x18e6, struct.pack('<II', W * 32, H * 32))
    o.cpu.mem_write(o.FS, struct.pack('<II', H * 32, W * 32))  # FS:[0] height, FS:[4] width in pixels
    o.cpu.mem_write(o.FS + 0xc, struct.pack('<I', 0x1c8e))    # FS:[0C] -> the drift vector
    o.stubs = {0xa47e: lambda: (sounds.append(o.cpu.reg_read(UC_X86_REG_EAX)), o.ret()),
               0xcfac: lambda: (craters.append([o.cpu.reg_read(UC_X86_REG_EDX) & 0xffff, o.cpu.reg_read(UC_X86_REG_EAX) & 0xffff,
                                                o.cpu.reg_read(UC_X86_REG_ECX) & 0xffff]), o.ret())}

    spec = {'origins': [[48, 48], [250, 260], [500, 16]], 'targets': [[48, 48], [80, 48], [48, 400], [300, 301], [16, 500], [511, 511], [0, 0]],
            'experience': [0, 16, 127], 'random_indices': [0, 1, 99, 255], 'lift': [0]}   # 9727 creates effects with +11 = 0
    fixture['create'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'create'
    for (ax, ay), (tx, ty), experience, index, lift in product(*(spec[k] for k in ['origins', 'targets', 'experience', 'random_indices', 'lift'])):
        sounds.clear(); shooter = unit(16, 3, 10, 0, ax, ay, 1); shooter[0x16] = experience; o.cpu.mem_write(o.POOL + 0x200, bytes(shooter))
        effect = effect_record(ax + 8, ay - 8, 1); effect[0x11] = lift; o.cpu.mem_write(o.POOL + 1, bytes(effect))
        o.cpu.mem_write(0x19eb, bytes([index, (index * 7) & 255]))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EBX, o.POOL + 0x200)
        o.cpu.reg_write(UC_X86_REG_ECX, tx); o.cpu.reg_write(UC_X86_REG_EDX, ty); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.WRK + 0x85a)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + bytes(o.cpu.mem_read(0x19eb, 2)) + json.dumps(sounds).encode()); count += 1
    save('create', digest, count)

    # Step: the stone at (x, y) with remaining distance and velocity; objects around the landing cell in planes 0..2.
    spec = {'positions': [[200, 200], [5, 7], [500, 500], [16 * 32 - 3, 40]], 'remaining': [-3, 0, 1, 40, 2000],
            'velocities': [[0x900, 0], [-0x900, 0x300], [0x40, -0x900], [0x7fffff00, 0x100]], 'arc': [[0, 0x400], [0x7ff0, 0x20], [0x10, 0xff00], [0x200, 0x8000]],
            'objects': [[], [[1, 10, 0]], [[1, 10, 4], [0, 3, 0]], [[1, 0, 0], [1, 200, 0]], [[2, 127, 9], [1, 8, 3]]],
            'shared': [0, 1]}
    fixture['step'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'step'
    for (x, y), remaining, (vx, vy), (height, rise), objects, shared in product(*(spec[k] for k in [
            'positions', 'remaining', 'velocities', 'arc', 'objects', 'shared'])):
        sounds.clear(); craters.clear()
        effect = effect_record(x, y, 1); struct.pack_into('<i', effect, 0x12, remaining); struct.pack_into('<ii', effect, 0x18, vx, vy)
        struct.pack_into('<H', effect, 0x10, height); struct.pack_into('<H', effect, 0x20, rise)
        o.cpu.mem_write(o.POOL + 1, bytes(effect)); o.cpu.mem_write(o.GRID, bytes(W * H * 12))
        cell = min(H - 1, y >> 5) * W + min(W - 1, x >> 5); at = 0x100; refs = []
        for plane, hp, defence in objects:
            base = o.DAT + 2 + 0 * STRIDE   # template 0 with the defence of the case
            rec = unit(0, 1, hp, 0x1f, x, y, 1); rec[0x0e] = 0 if defence == 0 else 2
            o.cpu.mem_write(o.POOL + at, bytes(rec)); o.cpu.mem_write(o.GRID + (plane * W * H + cell) * 4, struct.pack('<I', at)); refs.append(at)
            if shared: o.cpu.mem_write(o.GRID + (((plane + 1) % 2) * W * H + cell) * 4, struct.pack('<I', at))
            at += 0x40
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EAX, 0x100)
        o.run(o.WRK + 0x85a)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + b''.join(bytes(o.cpu.mem_read(o.POOL + r, 0x36)) for r in refs)
                      + json.dumps([sounds, craters]).encode()); count += 1
    save('step', digest, count)

    spec = {'drift': [[0, 0], [1000, -1000], [-37, 5]], 'frames': [[1, 1], [2, 1], [1, 18], [2, 18], [1, 2]]}
    fixture['smoke'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'smoke'
    for (dx, dy), (timer, left) in product(*(spec[k] for k in ['drift', 'frames'])):
        effect = effect_record(300, 200, 1); effect[0] = 10; effect[0x16] = timer; effect[0x17] = left
        o.cpu.mem_write(o.POOL + 1, bytes(effect)); o.cpu.mem_write(0x1c8e, struct.pack('<ii', dx, dy))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EAX, 0x100)
        o.run(o.WRK + 0x986)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22))); count += 1
    save('smoke', digest, count)

    # Ballista bolt (type 1Dh, 12EC) and the big explosion (type 0Ch, A44/AD5).
    spec = {'origins': [[48, 48], [250, 260], [500, 16]], 'targets': [[48, 48], [300, 301], [511, 511], [0, 0]],
            'experience': [0, 127], 'random_indices': [0, 1, 99, 255]}
    fixture['bolt_create'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'bolt_create'
    for (ax, ay), (tx, ty), experience, index in product(*(spec[k] for k in ['origins', 'targets', 'experience', 'random_indices'])):
        shooter = unit(26, 3, 10, 0, ax, ay, 1); shooter[0x16] = experience; o.cpu.mem_write(o.POOL + 0x200, bytes(shooter))
        effect = effect_record(ax + 8, ay - 8, 1); o.cpu.mem_write(o.POOL + 1, bytes(effect))
        o.cpu.mem_write(0x19eb, bytes([index, (index * 7) & 255]))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EBX, o.POOL + 0x200)
        o.cpu.reg_write(UC_X86_REG_ECX, tx); o.cpu.reg_write(UC_X86_REG_EDX, ty); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.WRK + 0x12ec)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + bytes(o.cpu.mem_read(0x19eb, 2))); count += 1
    save('bolt_create', digest, count)

    spawned = []; refuse = [False]
    def spawn():
        r = o.cpu.reg_read; esp = r(UC_X86_REG_ESP)
        shooter, tx, ty = struct.unpack('<III', bytes(o.cpu.mem_read(esp + 4, 12)))
        spawned.append([r(UC_X86_REG_EAX) & 255, r(UC_X86_REG_EBX) & 0xffff, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX), shooter, tx, ty])
        o.cpu.mem_write(o.POOL + 0x3000, bytes(0x22)); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x3000)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (o.cpu.reg_read(UC_X86_REG_EFLAGS) & ~0x40) | (0 if refuse[0] else 0x40)); o.ret()
    o.stubs[0x9727] = spawn
    spec = {'positions': [[200, 200], [5, 7], [500, 500], [16 * 32 - 3, 40]], 'remaining': [-3, 0, 40],
            'objects': [[], [[1, 10, 0]], [[1, 10, 4], [0, 3, 0]]], 'random_indices': [0, 1, 128, 200, 255], 'refuse': [0, 1]}
    fixture['bolt_step'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'bolt_step'
    for (x, y), remaining, objects, index, refused in product(*(spec[k] for k in ['positions', 'remaining', 'objects', 'random_indices', 'refuse'])):
        sounds.clear(); spawned.clear(); refuse[0] = bool(refused)
        effect = effect_record(x, y, 1); effect[0] = 0x1d; effect[2] = 3; struct.pack_into('<i', effect, 0x12, remaining)
        struct.pack_into('<ii', effect, 0x18, 0x900, -0x300); struct.pack_into('<HH', effect, 0x10, 0, 0); struct.pack_into('<H', effect, 0x20, 0x200)
        o.cpu.mem_write(o.POOL + 1, bytes(effect)); o.cpu.mem_write(o.GRID, bytes(W * H * 12)); o.cpu.mem_write(o.POOL + 0x3000, bytes(0x22))
        cell = min(H - 1, y >> 5) * W + min(W - 1, x >> 5); at = 0x100; refs = []
        for plane, hp, defence in objects:
            rec = unit(0, 1, hp, 0x1f, x, y, 1); rec[0x0e] = 0 if defence == 0 else 2
            o.cpu.mem_write(o.POOL + at, bytes(rec)); o.cpu.mem_write(o.GRID + (plane * W * H + cell) * 4, struct.pack('<I', at)); refs.append(at); at += 0x40
        o.cpu.mem_write(0x19eb, bytes([index, (index * 3 + 1) & 255]))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EAX, 0x100)
        o.run(o.WRK + 0x12ec)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + b''.join(bytes(o.cpu.mem_read(o.POOL + r, 0x36)) for r in refs)
                      + bytes(o.cpu.mem_read(o.POOL + 0x3000 + 2, 1)) + bytes(o.cpu.mem_read(0x19eb, 2)) + json.dumps([sounds, spawned]).encode()); count += 1
    save('bolt_step', digest, count)

    spec = {'frames': [[1, 1], [2, 1], [1, 18], [2, 18]], 'ah': [0, 1]}
    fixture['big'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'big'
    for (timer, left), ah in product(*(spec[k] for k in ['frames', 'ah'])):
        sounds.clear(); effect = effect_record(300, 200, 1); effect[0] = 0xc; effect[0x16] = timer; effect[0x17] = left
        o.cpu.mem_write(o.POOL + 1, bytes(effect))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EAX, ah << 8)
        o.run(o.WRK + 0xa44)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + json.dumps(sounds).encode()); count += 1
    save('big', digest, count)
    return fixture


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game), indent=2) + '\n', encoding='utf-8')
