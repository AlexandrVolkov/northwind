#!/usr/bin/env python3
"""Execute the unit corpse (ARMY__02.WRK effect type 1, 161D) and the blood splash (ARMY__00.WRK effect type 3, 6C7)
under Unicorn. Real: 982B (object under the point), 9E41. Stubbed and recorded: 9727 (the splash)."""
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


class CorpseOracle(CombatOracle):
    RANGES = {'corpse': ([(0x982b, 0x98cc), (0x9e41, 0x9e55)], [(0x161d, 0x16dd)]), 'blood': ([], [(0x6c7, 0x6fe)])}


def effect_record(x, y, kind):
    raw = pattern(0x22, 5, 9); raw[0] = kind; raw[1] = 2; raw[5] = 0
    struct.pack_into('<I', raw, 7, x); struct.pack_into('<I', raw, 0xc, y)
    return raw


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = CorpseOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    fixture = {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT', 'ARMY__02.WRK', 'ARMY__00.WRK']},
               'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in [0, 7]}, 'results': {}}
    spawns = []
    def spawn():
        r = o.cpu.reg_read
        spawns.append([r(UC_X86_REG_EAX) & 255, r(UC_X86_REG_EBX) & 0xffff, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX)]); o.ret()
    o.stubs = {0x9727: spawn}
    o.prepare(W, H); o.cpu.mem_write(0x18e6, struct.pack('<II', W * 32, H * 32))

    def save(name, digest, count):
        fixture['results'][name] = {'case_count': count, 'sha256': digest.hexdigest()}; print(name, count, flush=True)

    o.cpu.mem_write(o.WRK, (game / 'ARMY__02.WRK').read_bytes())
    spec = {'terrain': [0, 2, 1], 'support': [None, 0, 7], 'flags': [0, 0x08, 0x10, 0x18, 0x1f, 0x80], 'headings': [0, 3, 7, 0xff],
            'random_indices': [0, 1, 99, 255], 'positions': [[200, 200], [3, 4]]}
    fixture['corpse'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'corpse'
    for terrain, support, flags, heading, index, (x, y) in product(*(spec[k] for k in ['terrain', 'support', 'flags', 'headings', 'random_indices', 'positions'])):
        spawns.clear()
        o.terrain(W, H, [terrain] * (W * H)); o.prepare(W, H); o.cpu.mem_write(0x18e6, struct.pack('<II', W * 32, H * 32))
        o.cpu.mem_write(o.GRID, bytes(W * H * 12))
        if support is not None:
            rec = unit(support, 1, 50, 0, x, y, 0); o.cpu.mem_write(o.POOL + 0x100, bytes(rec))
            o.cpu.mem_write(o.GRID + ((y >> 5) * W + (x >> 5)) * 4, struct.pack('<I', 0x100))
        dead = unit(0, 3, 0xf1, flags, x, y, 1); dead[0x25] = heading; struct.pack_into('<H', dead, 1, 0x1234)
        o.cpu.mem_write(o.POOL + 0x200, bytes(dead))
        effect = effect_record(x, y, 1); o.cpu.mem_write(o.POOL + 1, bytes(effect))
        o.cpu.mem_write(0x19eb, bytes([index]))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EBX, o.POOL + 0x200); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.WRK + 0x161d)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + bytes(o.cpu.mem_read(0x19eb, 1)) + json.dumps(spawns).encode()); count += 1
    for timer, stage, variant in product([1, 2, 600], [1, 2, 4], [0, 3, 7]):
        effect = effect_record(200, 200, 1); struct.pack_into('<I', effect, 0x18, timer); effect[0x17] = stage; effect[6] = variant
        o.cpu.mem_write(o.POOL + 1, bytes(effect)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EAX, 0x100)
        o.run(o.WRK + 0x161d)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22))); count += 1
    save('corpse', digest, count)

    o.cpu.mem_write(o.WRK, (game / 'ARMY__00.WRK').read_bytes())
    digest = hashlib.sha256(); count = 0; o.mode = 'blood'
    for ah, timer, stage in product([0, 1], [1, 2, 5, 0xdc], [1, 6, 7, 11]):
        effect = effect_record(200, 200, 3); effect[0x16] = timer; effect[0x17] = stage
        o.cpu.mem_write(o.POOL + 1, bytes(effect))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EAX, ah << 8)
        o.run(o.WRK + 0x6c7)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22))); count += 1
    save('blood', digest, count)
    return fixture


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game), indent=2) + '\n', encoding='utf-8')
