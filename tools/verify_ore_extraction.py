#!/usr/bin/env python3
"""Execute the original ore extraction chain under Unicorn and store hashed results.

Covered native blocks (LE:1 offsets, ARMY__02.WRK offsets):
  grant    123D3 -> WRK 7F5/851..8E8 -> 9ED AH=0      cargo codes from the mine
  extract  B47F..B502 with C4CD/C591, 123D3, B502/B3F7, CE9D and the RNG table
  credit   CC7B castle service -> 1243A -> 11EB0 / 11ECB owner stock and statistics
  carrier  86B6..877F ore-carrier controller; 8F70, C439 and 8B0F are stubbed at their
           entries because 8F70 and C439 are verified separately and 8B0F is a bridge
  validate WRK 593..5B3 (mine AH=5) and 13B..157/1B8 (castle AH=5)
  return   WRK A9B..AB3 empty ore carrier back to worker template 10
Only parameters and SHA-256 digests are written; no original bytes leave the game folder.
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


class OreOracle(OccupancyOracle):
    MAP, SEG = 0xe00000, 0xf00000

    def __init__(self, code, assignments, constants):
        super().__init__(code, assignments)
        self.cpu.mem_map(self.MAP, 0x40000); self.cpu.mem_map(self.SEG, 0x10000)
        self.cpu.mem_write(0x297e, bytes(constants['random_values']))
        self.cpu.mem_write(0x1906, b'\xff'); self.cpu.mem_write(0x190a, b'\xff')  # dirty cache outside view
        self.stubs = {}; self.calls = []

    RANGES = {
        'grant': ([(0x123d3, 0x12406), (0xae3a, 0xae57), (0x9e41, 0x9e55), (0x6885, 0x68d8)],
                  [(0x7f5, 0x80e), (0x851, 0x874), (0x8a7, 0x8aa), (0x8ce, 0x938), (0x9ed, 0x9fc), (0xa09, 0xa27)]),
        'extract': ([(0xb47f, 0xb5ab), (0xb3f7, 0xb47f), (0xc4cd, 0xc50c), (0xc58e, 0xc5d7), (0x6885, 0x68d8),
                     (0x123d3, 0x12406), (0xae3a, 0xae57), (0x9e41, 0x9e55), (0xce9d, 0xcf07)],
                    [(0x7f5, 0x80e), (0x851, 0x874), (0x8a7, 0x8aa), (0x8ea, 0x8fe), (0x9ed, 0x9fc), (0xa09, 0xa27)]),
        'credit': ([(0xcc7b, 0xccc4), (0xc4cd, 0xc50c), (0xc58e, 0xc5d7), (0x6885, 0x68d8), (0x1243a, 0x124a5),
                    (0x11eb0, 0x11ee6), (0x1ead8, 0x1eaed)], []),
        'carrier': ([(0x86b6, 0x877f), (0x8d89, 0x8db0)], []),
        'validate': ([(0x6885, 0x68d8)], [(0x524, 0x541), (0x593, 0x5b3), (0xfe, 0x117), (0x13b, 0x157), (0x1b8, 0x1bb)]),
        'return': ([(0xae3a, 0xae57), (0x9e41, 0x9e55), (0x6885, 0x68d8)], [(0xa9b, 0xab3), (0xa6c, 0xa6f), (0x7f5, 0x7f9), (0x815, 0x833)]),
    }

    def guard(self, cpu, address, size, user_data):
        stub = self.stubs.get(address - self.CODE)
        if stub is not None:
            stub(); return
        code, wrk = self.RANGES[self.mode]
        if any(self.CODE + a <= address < self.CODE + b for a, b in code): return
        if any(self.WRK + a <= address < self.WRK + b for a, b in wrk): return
        raise ValueError(f'Ore escape {self.mode}: {address:#x}')

    def ret(self, **registers):
        esp = self.cpu.reg_read(UC_X86_REG_ESP)
        target = struct.unpack('<I', bytes(self.cpu.mem_read(esp, 4)))[0]
        for name, value in registers.items():
            self.cpu.reg_write(globals()['UC_X86_REG_' + name.upper()], value)
        self.cpu.reg_write(UC_X86_REG_ESP, esp + 4); self.cpu.reg_write(UC_X86_REG_EIP, target)

    def zf(self): return (self.cpu.reg_read(UC_X86_REG_EFLAGS) >> 6) & 1

    def run(self, address):
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=200000)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError(f'{self.mode} did not return')

    def terrain(self, width, height, cells, properties):
        self.cpu.mem_write(0x18ee, struct.pack('<I', self.MAP)); self.cpu.mem_write(0x18fe, struct.pack('<I', self.SEG))
        self.cpu.mem_write(0x1902, struct.pack('<I', len(properties) // 8))
        self.cpu.mem_write(self.MAP, struct.pack(f'<{len(cells)}I', *cells)); self.cpu.mem_write(self.SEG, properties)


def pattern(size, a, b):
    return bytearray((i * a + b) & 255 for i in range(size))


def worker(template=10, owner=0, condition=40, cargo=0, flags=0, heading=0):
    raw = pattern(54, 7, 5); raw[0] = 2; raw[3] = owner; raw[0x0d] = 1; raw[0x0e] = template
    raw[0x19] = condition; raw[0x25] = heading; raw[0x31] = cargo; struct.pack_into('<H', raw, 0x17, flags)
    return raw


def building(size, template, owner, x, y, flags):
    raw = pattern(size, 11, 3); raw[0] = 2; raw[3] = owner; raw[0x0d] = 1; raw[0x0e] = template
    struct.pack_into('<II', raw, 4, x, y); struct.pack_into('<H', raw, 0x17, flags); struct.pack_into('<I', raw, 0x10, size)
    return raw


def anchor(cell, footprint): return cell * 32 + ((footprint & 1) << 4)


def generate(game, project, trace=None):
    constants = source_constants(game); report, code = analyze(game)
    o = OreOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); wrk = (game / 'ARMY__02.WRK').read_bytes()
    o.cpu.mem_write(o.DAT, dat); o.cpu.mem_write(o.WRK, wrk)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2)); o.cpu.mem_write(0x6ca + 2 * 0x56 + 9, struct.pack('<I', o.WRK))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    source = (game / 'MAP_1.MAP').read_bytes(); width, height = struct.unpack_from('<II', source, 17)
    cells = list(struct.unpack_from(f'<{width * height}I', source, 25))
    bundled = json.loads((project / 'assets/imported/map.json').read_text(encoding='utf-8'))
    if bundled['cells'] != cells: raise ValueError('Packaged cells differ from original MAP_1.MAP')
    properties = bytes.fromhex(bundled['segment_properties_hex'])
    fixture = {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT', 'ARMY__02.WRK']},
               'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in [0, 2, 6, 8, 10, 11]}, 'results': {}}
    fixture['source_sha256']['MAP_1.MAP'] = hashlib.sha256(source).hexdigest()

    class Traced:
        def __init__(self, name): self.inner = hashlib.sha256(); self.name = name; self.parts = []
        def update(self, data):
            self.inner.update(data)
            if trace: self.parts.append(data.hex())
        def flush(self):
            if trace and self.parts: trace.write(self.name + ' ' + ''.join(self.parts) + '\n'); self.parts = []
        def hexdigest(self): self.flush(); return self.inner.hexdigest()

    def save(name, digest, count):
        fixture['results'][name] = {'case_count': count, 'sha256': digest.hexdigest()}; print(name, count, flush=True)

    # Grant: every cargo code into an empty or loaded worker.
    spec = {'cargo': list(range(256)), 'existing': [0, 1, 3], 'conditions': [0, 1, 127, 128, 255],
            'flags': [0, 131072, 4294967295], 'headings': [0, 7, 63, 255], 'random_indices': [0, 255]}
    fixture['grant'] = spec; digest = Traced('grant'); count = 0; o.mode = 'grant'
    for cargo, existing, condition, flags, heading, index in product(*(spec[k] for k in ['cargo', 'existing', 'conditions', 'flags', 'headings', 'random_indices'])):
        o.cpu.mem_write(o.DAT, dat); o.cpu.mem_write(o.DAT + 2 + 10 * STRIDE + 0x40, struct.pack('<I', flags))
        raw = worker(10, 0, condition, existing, 0, heading); o.cpu.mem_write(o.POOL + 1, bytes(raw))
        o.cpu.mem_write(0x19eb, bytes([index])); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EAX, cargo)
        o.run(o.CODE + 0x123d3)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 54)) + bytes(o.cpu.mem_read(0x19eb, 1)) + bytes([o.zf()])); digest.flush(); count += 1
    save('grant', digest, count); o.cpu.mem_write(o.DAT, dat)

    # Extract: full B47F, real map and terrain catalog.
    o.prepare(width, height); o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT + 2 + 6 * STRIDE))
    mine_t = template(6)
    ore = [(i % width, i // width) for i, c in enumerate(cells) if (c >> 16) & 255 in (1, 2)]
    spec = {'positions': [list(ore[0]), list(ore[len(ore) // 3]), list(ore[2 * len(ore) // 3]), list(ore[-1]), [5, 5]],
            'stocks': [0, 1, 100, 2047, 2048, 2148, 4096, 4294967295], 'mine_flags': [0, 64], 'ticks': [0, 1, 16],
            'visitors': [[-1, 0, 0], [0, 0, 40], [0, 1, 40], [0, 0, 0], [0, 1, 0], [1, 0, 40], [1, 1, 40], [1, 0, 0], [1, 1, 0]],
            'counters': [0, 1, 2], 'conditions': [0, 1], 'types': [1, 2], 'random_indices': [0, 200], 'window': 7}
    fixture['extract'] = spec; digest = Traced('extract'); count = 0; o.mode = 'extract'
    for (cx, cy), stock, mflags, tick, (vowner, vcargo, vcond), counter, condition, kind, index in product(*(spec[k] for k in [
            'positions', 'stocks', 'mine_flags', 'ticks', 'visitors', 'counters', 'conditions', 'types', 'random_indices'])):
        o.terrain(width, height, cells, properties); o.cpu.mem_write(0x19eb, bytes([index])); o.cpu.mem_write(0x19ea, bytes([tick]))
        x, y = anchor(cx, mine_t[3]), anchor(cy, mine_t[4])
        mine = building(40, 6, 0, x, y, mflags); mine[0x15] = counter; mine[0x19] = condition; mine[0x23] = kind
        struct.pack_into('<I', mine, 0x24, stock); o.cpu.mem_write(o.POOL + 1, bytes(mine))
        o.cpu.mem_write(o.GRID, bytes(width * height * 12))
        visitor = worker(10, max(vowner, 0), vcond, vcargo); o.cpu.mem_write(o.POOL + 0x100, bytes(visitor))
        if vowner >= 0:
            ex = ((x + struct.unpack('b', mine_t[0x53:0x54])[0]) & 0xffffffff) >> 5
            ey = ((y + struct.unpack('b', mine_t[0x54:0x55])[0]) & 0xffffffff) >> 5
            o.cpu.mem_write(o.GRID + (width * height + ey * width + ex) * 4, struct.pack('<I', 0x100))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.run(o.CODE + 0xb47f)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 40)) + bytes(o.cpu.mem_read(o.POOL + 0x100, 54)) + bytes(o.cpu.mem_read(0x19eb, 1)))
        h = spec['window'] // 2
        for yy in range(cy - h, cy + h + 1):
            for xx in range(cx - h, cx + h + 1):
                if 0 <= xx < width and 0 <= yy < height: digest.update(bytes(o.cpu.mem_read(o.MAP + (yy * width + xx) * 4, 4)))
        digest.flush(); count += 1
    save('extract', digest, count)

    # Castle receive and owner crediting.
    castle_t = template(2); o.prepare(16, 16); o.cpu.mem_write(0x1972, b'\xff')
    for k, target in [(1, 0x11eb0), (2, 0x11ecb)]: o.cpu.mem_write(0x4191 + 25 * k + 4, struct.pack('<I', o.CODE + target))
    spec = {'castle_flags': [0, 64, 65535], 'visitor_flags': [0, 64, 65535], 'templates': [0, 8, 10], 'cargo': [0, 1, 2],
            'owners': [[0, 0], [3, 0], [3, 3]], 'ticks': [0, 1, 16], 'present': [0, 1],
            'stocks': [[0, 0, 0, 0], [4294967196, 7, 4294967295, 1]]}
    fixture['credit'] = spec; digest = Traced('credit'); count = 0; o.mode = 'credit'
    for cflags, vflags, vt, cargo, (cowner, vowner), tick, present, stocks in product(*(spec[k] for k in [
            'castle_flags', 'visitor_flags', 'templates', 'cargo', 'owners', 'ticks', 'present', 'stocks'])):
        x, y = anchor(6, castle_t[3]), anchor(6, castle_t[4])
        castle = building(49, 2, cowner, x, y, cflags); o.cpu.mem_write(o.POOL + 1, bytes(castle))
        visitor = worker(vt, vowner, 40, cargo, vflags); o.cpu.mem_write(o.POOL + 0x100, bytes(visitor))
        o.cpu.mem_write(o.GRID, bytes(16 * 16 * 12)); o.cpu.mem_write(0x19ea, bytes([tick]))
        if present:
            ex = (x + struct.unpack('b', castle_t[0x53:0x54])[0]) >> 5; ey = (y + struct.unpack('b', castle_t[0x54:0x55])[0]) >> 5
            o.cpu.mem_write(o.GRID + (256 + ey * 16 + ex) * 4, struct.pack('<I', 0x100))
        owner = 0x97a + cowner * 0xf5; stats = o.PARTICIPANTS + cowner * 0x1a35
        o.cpu.mem_write(owner + 0x18, struct.pack('<4I', *stocks)); o.cpu.mem_write(stats + 0x1a08, struct.pack('<2I', stocks[0], stocks[2]))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.run(o.CODE + 0xcc7b)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 49)) + bytes(o.cpu.mem_read(o.POOL + 0x100, 54))
                      + bytes(o.cpu.mem_read(owner + 0x18, 16)) + bytes(o.cpu.mem_read(stats + 0x1a08, 8))); digest.flush(); count += 1
    save('credit', digest, count)

    # Ore-carrier controller with recorded collaborator calls.
    o.prepare(16, 16)
    spec = {'phases': [0, 1, 2, 3, 4, 0x13], 'cargo': [0, 1], 'targets': [0, 0x200], 'saved': [0, 0x200, 0x280, 0x300],
            'timers': [0, 1, 254, 255], 'approach': ['pending', 'done', 'failed'], 'receiver': [0, 0x380], 'entry_flags': [0, 64]}
    fixture['carrier'] = spec; digest = Traced('carrier'); count = 0; o.mode = 'carrier'
    outcome = {}

    def approach():
        o.calls.append(('8F70', struct.unpack('<I', bytes(o.cpu.mem_read(o.POOL + 1 + 0x1c, 4)))[0]))
        kind = outcome['approach']; eflags = o.cpu.reg_read(UC_X86_REG_EFLAGS) & ~0x40
        if kind != 'pending': eflags |= 0x40
        o.ret(ebx=0 if kind == 'done' else 1, eflags=eflags, eax=0x5a)

    def search():
        o.calls.append(('C439', o.cpu.reg_read(UC_X86_REG_EAX), o.cpu.reg_read(UC_X86_REG_EBX)))
        found = outcome['receiver']; eflags = o.cpu.reg_read(UC_X86_REG_EFLAGS) & ~0x40
        if not found: eflags |= 0x40
        o.ret(edi=o.POOL + found if found else 0, eflags=eflags)

    def aside():
        o.calls.append(('8B0F',)); o.ret()
    o.stubs = {0x8f70: approach, 0xc439: search, 0x8b0f: aside}
    o.cpu.mem_write(o.POOL + 0x200, bytes(building(40, 6, 0, 64, 64, 0)))                        # live own mine
    other = building(40, 6, 3, 96, 64, 0); o.cpu.mem_write(o.POOL + 0x280, bytes(other))         # foreign mine
    dead = building(40, 6, 0, 64, 96, 0); dead[0x19] = 0; o.cpu.mem_write(o.POOL + 0x300, bytes(dead))
    for phase, cargo, target, saved, timer, outcome['approach'], outcome['receiver'], entry in product(*(spec[k] for k in [
            'phases', 'cargo', 'targets', 'saved', 'timers', 'approach', 'receiver', 'entry_flags'])):
        raw = worker(8 if cargo else 10, 0, 40, cargo); raw[0x15] = phase; raw[0x30] = timer
        struct.pack_into('<I', raw, 0x1c, target); struct.pack_into('<I', raw, 0x32, saved)
        mine = building(40, 6, 0, 64, 64, 0); o.cpu.mem_write(o.POOL + 0x200, bytes(mine))
        o.cpu.mem_write(o.POOL + 1, bytes(raw)); o.calls = []
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EBX, entry); o.run(o.CODE + 0x86b6)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 54)) + bytes([o.zf()]) + json.dumps(o.calls).encode()); digest.flush(); count += 1
    o.stubs = {}; save('carrier', digest, count)

    # AH=5 receiver validators.
    spec = {'receivers': [6, 2], 'flags': [0, 0x300, 0x1000, 0x20000, 0x20300, 0x200, 4294967295], 'owners': [[0, 0], [0, 3]]}
    fixture['validate'] = spec; digest = Traced('validate'); count = 0; o.mode = 'validate'
    for receiver, flags, (rowner, vowner) in product(*(spec[k] for k in ['receivers', 'flags', 'owners'])):
        o.cpu.mem_write(o.DAT, dat); o.cpu.mem_write(o.DAT + 2 + 10 * STRIDE + 0x40, struct.pack('<I', flags))
        o.cpu.mem_write(o.POOL + 1, bytes(building(40, receiver, rowner, 64, 64, 0)))
        o.cpu.mem_write(o.POOL + 0x100, bytes(worker(10, vowner)))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EDX, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EAX, 0x500)
        entry = struct.unpack_from('<I', template(receiver), 0x3b)[0]; o.run(o.WRK + entry)
        digest.update(bytes([o.zf()])); digest.flush(); count += 1
    o.cpu.mem_write(o.DAT, dat); save('validate', digest, count)

    # Empty ore carrier returns to template 10 without the timber state reset.
    spec = {'headings': list(range(256)), 'random_indices': [0, 1, 127, 255]}
    fixture['return'] = spec; digest = Traced('return'); count = 0; o.mode = 'return'
    for heading, index in product(spec['headings'], spec['random_indices']):
        raw = worker(8, 0, 40, 0, 0, heading); o.cpu.mem_write(o.POOL + 1, bytes(raw)); o.cpu.mem_write(0x19eb, bytes([index]))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.run(o.WRK + 0xa9b)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 54)) + bytes(o.cpu.mem_read(0x19eb, 1))); digest.flush(); count += 1
    save('return', digest, count)
    return fixture


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--trace', type=Path, help='optional local per-case dump for locating mismatches')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    trace = args.trace.open('w') if args.trace else None
    args.fixture.write_text(json.dumps(generate(args.game, root, trace), indent=2) + '\n', encoding='utf-8')
