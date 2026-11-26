#!/usr/bin/env python3
"""Execute the original melee hit chain and unit vitals under Unicorn; store hashed results.

  hit      LE:1 BFC9 (FS 119): damage = attack - DAT[44] or a 1/(2-d) chance of 1 point
  lookup   LE:1 982B (FS A1): object under an effect point, footprint containment, DAT 8000h
  strike   ARMY__00.WRK 4F3 effect type 0 AH=0: attack from experience, 982B, BFC9, sound, visual
  vitals   LE:1 90E9..9210 unit prefix: death dispatch, regeneration, bleeding, rest, flee 8E0E,
           wound voice, experience growth and terrain death
Sound (A416/A47E) and death (95D3) are stubbed at their entries and their arguments recorded.
"""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_occupancy import OccupancyOracle
from forestry_source import source_constants

STRIDE = 171


class CombatOracle(OccupancyOracle):
    MAP, SEG = 0xe00000, 0xf00000

    def __init__(self, code, assignments, constants):
        super().__init__(code, assignments)
        self.cpu.mem_map(self.MAP, 0x40000); self.cpu.mem_map(self.SEG, 0x10000)
        self.cpu.mem_write(0x297e, bytes(constants['random_values']))
        self.stubs = {}; self.calls = []; self.boundary = None

    RANGES = {
        'hit': ([(0xbfc9, 0xc010), (0x68bd, 0x68d8), (0x6885, 0x68a0), (0x9e41, 0x9e55)], []),
        'lookup': ([(0x982b, 0x98cc)], []),
        'strike': ([(0x982b, 0x98cc), (0xbfc9, 0xc010), (0x68bd, 0x68d8), (0x6885, 0x68a0), (0x9e41, 0x9e55)],
                   [(0x4f3, 0x54a), (0x6fe, 0x71f), (0x735, 0x74c)]),
        'cellhit': ([(0x97c6, 0x982b), (0xbfc9, 0xc010), (0x68bd, 0x68d8), (0x6885, 0x68a0), (0x9e41, 0x9e55)], [(0x6fe, 0x71f), (0x735, 0x74c), (0x762, 0x7fe)]),
        'vitals': ([(0x90e9, 0x9210), (0x8e0e, 0x8e3c), (0xd12c, 0xd157), (0x9e41, 0x9e69)], []),
    }

    def guard(self, cpu, address, size, user_data):
        if self.boundary is not None and address == self.CODE + self.boundary:
            cpu.emu_stop(); self.stopped = True; return
        stub = self.stubs.get(address - self.CODE)
        if stub is not None: stub(); return
        code, wrk = self.RANGES[self.mode]
        if any(self.CODE + a <= address < self.CODE + b for a, b in code): return
        if any(self.WRK + a <= address < self.WRK + b for a, b in wrk): return
        raise ValueError(f'Combat escape {self.mode}: {address:#x}')

    def ret(self, **registers):
        esp = self.cpu.reg_read(UC_X86_REG_ESP)
        target = struct.unpack('<I', bytes(self.cpu.mem_read(esp, 4)))[0]
        for name, value in registers.items(): self.cpu.reg_write(globals()['UC_X86_REG_' + name.upper()], value)
        self.cpu.reg_write(UC_X86_REG_ESP, esp + 4); self.cpu.reg_write(UC_X86_REG_EIP, target)

    def zf(self): return (self.cpu.reg_read(UC_X86_REG_EFLAGS) >> 6) & 1

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=100000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError(f'{self.mode} did not return')

    def terrain(self, width, height, classes):
        """Map whose cell (x,y) uses tile index y*width+x; descriptor low 3 bits = class."""
        self.cpu.mem_write(0x18de, struct.pack('<II', width, height))
        self.cpu.mem_write(0x18ee, struct.pack('<I', self.MAP)); self.cpu.mem_write(0x18fe, struct.pack('<I', self.SEG))
        self.cpu.mem_write(self.MAP, struct.pack(f'<{width * height}I', *range(width * height)))
        self.cpu.mem_write(self.SEG, b''.join(struct.pack('<II', c, 0) for c in classes))


def pattern(size, a, b): return bytearray((i * a + b) & 255 for i in range(size))


def unit(template=0, owner=0, hp=10, flags=0, x=80, y=80, layer=1):
    raw = pattern(54, 13, 1); raw[0] = 2; raw[3] = owner; raw[0x0d] = layer; raw[0x0e] = template
    raw[0x19] = hp & 255; struct.pack_into('<H', raw, 0x17, flags); struct.pack_into('<II', raw, 4, x, y)
    return raw


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = CombatOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); wrk0 = (game / 'ARMY__00.WRK').read_bytes()
    o.cpu.mem_write(o.DAT, dat); o.cpu.mem_write(o.WRK, wrk0)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    fixture = {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT', 'ARMY__00.WRK']},
               'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in [0, 18, 20]}, 'results': {}}

    def save(name, digest, count):
        fixture['results'][name] = {'case_count': count, 'sha256': digest.hexdigest()}; print(name, count, flush=True)

    def set_template(index, **fields):
        base = o.DAT + 2 + index * STRIDE; o.cpu.mem_write(base, template(index))
        for offset, value in fields.items():
            o.cpu.mem_write(base + int(offset[1:], 16), value if isinstance(value, bytes) else bytes([value & 255]))

    # BFC9 hit formula.
    spec = {'attacks': list(range(16)), 'kinds': [0, 1, 3], 'defences': [0, 1, 2, 3, 4, 127, 255],
            'health': [128, 255, 0, 1, 2, 5, 10, 127], 'flags': [0, 65535], 'random_indices': list(range(0, 256, 8))}
    fixture['hit'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'hit'
    for attack, kind, defence, hp, flags, index in product(*(spec[k] for k in ['attacks', 'kinds', 'defences', 'health', 'flags', 'random_indices'])):
        set_template(0, o44=defence); raw = unit(0, 0, hp, flags); o.cpu.mem_write(o.POOL + 1, bytes(raw))
        o.cpu.mem_write(0x19eb, bytes([index])); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1)
        o.cpu.reg_write(UC_X86_REG_EAX, attack | (kind << 8)); o.run(o.CODE + 0xbfc9)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 54)) + bytes(o.cpu.mem_read(0x19eb, 1)) + bytes([o.zf()])); count += 1
    set_template(0); save('hit', digest, count)

    # 982B lookup on an 8x8 map, target footprints from DAT[3]/[4].
    o.prepare(8, 8); o.terrain(8, 8, [i % 8 for i in range(64)])
    spec = {'points': [[x, y] for x in (64, 79, 80, 95, 96, 112, 127, 128) for y in (64, 80, 96, 111, 112, 128)],
            'footprints': [[1, 1], [2, 1], [3, 2]], 'flags': [0, 0x8000], 'layers': [0, 1, 2, 5], 'present': [0, 1]}
    fixture['lookup'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'lookup'
    for (px, py), (fw, fh), flags, layer, present in product(*(spec[k] for k in ['points', 'footprints', 'flags', 'layers', 'present'])):
        set_template(0, o03=fw, o04=fh, o40=struct.pack('<I', flags))
        o.cpu.mem_write(o.GRID, bytes(8 * 8 * 12)); target = unit(0, 0, 10, 0, 96, 96, 1); o.cpu.mem_write(o.POOL + 0x100, bytes(target))
        if present: o.cpu.mem_write(o.GRID + ((layer & 3) * 64 + (py >> 5) * 8 + (px >> 5)) * 4, struct.pack('<I', 0x100))
        effect = pattern(0x22, 5, 9); effect[5] = layer; struct.pack_into('<I', effect, 7, px); struct.pack_into('<I', effect, 0xc, py)
        o.cpu.mem_write(o.POOL + 1, bytes(effect)); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.run(o.CODE + 0x982b)
        found = o.cpu.reg_read(UC_X86_REG_EDI); hit = o.cpu.reg_read(UC_X86_REG_EAX) & 255
        digest.update(struct.pack('<IBB', found - o.POOL if present and found >= o.POOL else 0, hit, o.zf())); count += 1
    set_template(0); save('lookup', digest, count)

    # Strike effect, type 0, AH=0.
    sounds = []
    o.stubs = {0xa47e: lambda: (sounds.append(o.cpu.reg_read(UC_X86_REG_EAX)), o.ret())}
    spec = {'experience': [0, 62, 63, 64, 127, 128, 190, 191, 255], 'defences': [0, 2, 3, 7], 'health': [0, 1, 6, 10],
            'flags': [0, 0x10, 0x8000], 'present': [0, 1], 'random_indices': [0, 1, 77, 255]}
    fixture['strike'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'strike'
    for experience, defence, hp, flags, present, index in product(*(spec[k] for k in ['experience', 'defences', 'health', 'flags', 'present', 'random_indices'])):
        set_template(0, o03=1, o04=1, o44=defence, o40=struct.pack('<I', flags)); sounds.clear()
        o.cpu.mem_write(o.GRID, bytes(8 * 8 * 12)); target = unit(0, 3, hp, 0, 96, 96, 1); o.cpu.mem_write(o.POOL + 0x100, bytes(target))
        if present: o.cpu.mem_write(o.GRID + (64 + 3 * 8 + 3) * 4, struct.pack('<I', 0x100))
        attacker = unit(0, 0, 10, 0, 64, 96, 1); attacker[0x16] = experience; o.cpu.mem_write(o.POOL + 0x200, bytes(attacker))
        effect = pattern(0x22, 5, 9); effect[0] = 0; effect[1] = 0; effect[5] = 1
        struct.pack_into('<II', effect, 7, 100, 0); struct.pack_into('<I', effect, 0xc, 100)
        o.cpu.mem_write(o.POOL + 1, bytes(effect)); o.cpu.mem_write(0x19eb, bytes([index]))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EBX, o.POOL + 0x200); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.WRK + 0x4f3)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + bytes(o.cpu.mem_read(o.POOL + 0x100, 54))
                      + bytes(o.cpu.mem_read(0x19eb, 1)) + json.dumps(sounds).encode()); count += 1
    o.stubs = {}; set_template(0); save('strike', digest, count)

    # Cell hit, type 6, WRK 762: the object in the cell of the point, damage from the attacker's experience.
    sounds = []
    o.stubs = {0xa47e: lambda: (sounds.append(o.cpu.reg_read(UC_X86_REG_EAX)), o.ret())}
    o.prepare(8, 8); o.terrain(8, 8, [i % 8 for i in range(64)])
    spec = {'experience': [0, 63, 64, 190, 191], 'kinds': [0, 1, 3, 5], 'defences': [0, 5, 6, 7, 20], 'health': [0, 1, 6, 10, 128],
            'flags': [0, 0x10], 'present': [0, 1], 'cells': [0, 1, 3, 7], 'random_indices': [0, 77]}
    fixture['cellhit'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'cellhit'
    for experience, kind, defence, hp, flags, present, cx, index in product(*(spec[k] for k in ['experience', 'kinds', 'defences', 'health', 'flags', 'present', 'cells', 'random_indices'])):
        set_template(0, o03=1, o04=1, o44=defence, o40=struct.pack('<I', flags)); sounds.clear()
        o.cpu.mem_write(o.GRID, bytes(8 * 8 * 12)); target = unit(0, 3, hp, 0, cx * 32 + 16, 112, 1); o.cpu.mem_write(o.POOL + 0x100, bytes(target))
        if present: o.cpu.mem_write(o.GRID + (64 + 3 * 8 + cx) * 4, struct.pack('<I', 0x100))
        attacker = unit(0, 0, 10, 0, 64, 96, 1); attacker[0x16] = experience; attacker[0x14] = kind | 0x10; o.cpu.mem_write(o.POOL + 0x200, bytes(attacker))
        effect = pattern(0x22, 5, 9); effect[0] = 6; effect[1] = 0; effect[5] = 1
        o.cpu.mem_write(o.POOL + 1, bytes(effect)); o.cpu.mem_write(0x19eb, bytes([index]))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EBX, o.POOL + 0x200); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.cpu.reg_write(UC_X86_REG_ECX, cx * 32 + 16); o.cpu.reg_write(UC_X86_REG_EDX, 112)
        o.run(o.WRK + 0x762)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 0x22)) + bytes(o.cpu.mem_read(o.POOL + 0x100, 54))
                      + bytes(o.cpu.mem_read(0x19eb, 1)) + json.dumps(sounds).encode()); count += 1
    o.stubs = {}; set_template(0); save('cellhit', digest, count)

    # 90E9 vitals up to the animation core at 9210.
    calls = []
    o.stubs = {0xa416: lambda: (calls.append(['A416', o.cpu.reg_read(UC_X86_REG_EAX)]), o.ret()),
               0x95d3: lambda: (calls.append(['95D3']), o.ret(eax=0xff))}
    o.boundary = 0x9210; o.prepare(8, 8); o.terrain(8, 8, [i % 8 for i in range(64)])
    o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT + 2))
    spec = {'health': [255, 0, 1, 2, 3, 9, 10], 'maximum': [10, 50], 'experience': [0, 31, 32, 64, 127, 200],
            'mechanical': [0, 1], 'states': [0, 0x34], 'ticks': [0, 1], 'random_indices': [0, 31, 200], 'actions': [0, 3],
            'flags': [0, 0x1a], 'cells': [[2, 3, 0], [3, 3, 0], [3, 3, 1]], 'layers': [0, 1]}
    fixture['vitals'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'vitals'
    allowed = bytes([1, 1, 1, 0, 1, 1, 1, 1])
    for hp, maximum, experience, mechanical, state, tick, index, action, flags, (cx, cy, bridge), layer in product(*(spec[k] for k in [
            'health', 'maximum', 'experience', 'mechanical', 'states', 'ticks', 'random_indices', 'actions', 'flags', 'cells', 'layers'])):
        set_template(0, o40=struct.pack('<I', 0x1800 | (0x10 if mechanical else 0)), o45=12, o58=allowed,
                     oa3=struct.pack('<I', 0x1234), oa7=struct.pack('<I', 0x5678)); calls.clear()
        raw = unit(0, 0, hp, flags, cx * 32 + 16, cy * 32 + 16, layer); raw[0x1a] = maximum; raw[0x16] = experience
        raw[0x2d] = state; raw[0x14] = (raw[0x14] & 0xf8) | action
        o.cpu.mem_write(o.GRID, bytes(8 * 8 * 12))
        if bridge: o.cpu.mem_write(o.GRID + (((layer - 1) & 3) * 64 + cy * 8 + cx) * 4, struct.pack('<I', 0x300))
        o.cpu.mem_write(o.POOL + 1, bytes(raw)); o.cpu.mem_write(0x19ea, bytes([tick])); o.cpu.mem_write(0x19eb, bytes([index]))
        o.cpu.mem_write(0x19ec, bytes([(index * 3) & 255])); o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.run(o.CODE + 0x90e9)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 54)) + bytes(o.cpu.mem_read(0x19eb, 2)) + json.dumps(calls).encode()); count += 1
    o.stubs = {}; o.boundary = None; save('vitals', digest, count)
    return fixture


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game), indent=2) + '\n', encoding='utf-8')
