#!/usr/bin/env python3
"""Execute the AI order executor LE:1 227C7 (list +34F, modes 1..10) under Unicorn, with 22E85 (producers through the real
WRK AH=6 callbacks), 22FE4 site checks, 23201 and the water-crossing scan. AF07 (building permission) is stubbed at its
entry: it allows a site unless (x ^ y ^ template ^ seed) & 3 == 0, and every call is recorded.

Shares the world of tools/verify_ai_military.py.

Covered with it: 216DF/21DD4 and 21660/21696/2029F/2035A recruitment, 214C3/21555 target choice, 1EC6E/218A8 sight
strength, 215F1/2160B, 22E46/223B4/A673 random open template (ARMY__02.TRE), 1EB5C requests and the squad registry.
Request bytes the call sites fill from register leftovers are not compared: +7 of every +1AB record, +5..+6 of all
records but mode 8, +8..+0B of mode-6 records and +4..+7 of every +34F record. A case whose original faults (DIV in mode 8) is recorded as a fault.
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn import UcError
from unicorn.x86_const import *
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_combat import CombatOracle, unit, STRIDE
from forestry_source import source_constants
from tech_source import decode

STATS, LIST, SQUAD, MIL, ORD = 0x1a35, 0x9df, 0x52, 0x1ab, 0x34f
UNITS = [0, 1, 17, 18, 27, 13]
BUILDINGS = [2, 3, 4, 9, 12, 23, 24, 6, 10]
RANGES = [(0x227c7, 0x22e46), (0x22e85, 0x230ed), (0x23201, 0x2323a), (0xd7b1, 0xd85e), (0xd19c, 0xd1ba), (0xa879, 0xa8a0), (0x20dd0, 0x21459), (0x214c3, 0x21660), (0x21660, 0x21780), (0x21dd4, 0x21e99), (0x1ec6e, 0x1eca0), (0x218a8, 0x219c3),
          (0x219c3, 0x21a00), (0x22e46, 0x22e85), (0x223b4, 0x22435), (0xa673, 0xa6ae), (0x68a0, 0x68bd), (0x6877, 0x68d8),
          (0x9e41, 0x9e69), (0xc331, 0xc344), (0x1eaed, 0x1eb68), (0x200ab, 0x2029f), (0x2029f, 0x202d0), (0x2035a, 0x20408),
          (0x20095, 0x200ab), (0x1f306, 0x1f31e), (0x1f464, 0x1f476), (0x1f8c0, 0x1f8cf), (0x1f9ec, 0x1fa01), (0x1fb9c, 0x1fbb1)]


class MilitaryOracle(CombatOracle):
    RANGES = {'military': (RANGES, [(0, 0x10000)])}


def terrain_class(case, cell):
    x, y = cell % 32, cell // 32; orient, at, width, seed = case['terrain']
    if width and at <= (x if orient == 0 else y) < at + width: return 2
    return 1 if (x * 7 + y * 13 + seed) % 41 == 0 else (3 if (x + y + seed) % 5 == 0 else 0)


def cases():
    rng = random.Random(2277)
    out = []
    for n in range(2000):
        p = rng.randrange(4); count = 6; others = [q for q in range(count) if q != p]
        ids = rng.sample(range(1, 256), rng.randint(0, 6))
        def cell(): return [rng.randrange(32), rng.randrange(32)]
        blocks = {q: {'strength': rng.choice([0, 100, 500, 2000, 9000]), 'home': cell(), 'land': [rng.randrange(20), rng.randrange(20)],
                      '45': rng.choice([0, 50, 300]), '13': rng.choice([0, 10, 60]), '0e': rng.choice([16, 32, 64]), '52': rng.choice([0, 1, 3]),
                      '1a01': rng.choice([0, 0xc0, 0xff]), '2a': rng.choice([0, 1, 5, 20]), 'flags': rng.choice([1, 1, 1, 0])} for q in range(count)}
        relations = {q: [rng.choice([0, 1, 2, 3]) for _ in range(count)] for q in range(count)}
        objects = []
        for _ in range(rng.randint(0, 14)):
            objects.append({'owner': p, 't': rng.choice(UNITS + [2, 2, 3, 3, 10, 15, 4, 9]), 'cell': cell(), 'squad': rng.choice([0, 0, 0, 5]),
                            'hp': rng.choice([10, 60, 127, 0]), 'kind': 0, 'stock': 0, 'flags': rng.choice([0xff, 4, 0, 0x24]), 'cmd': rng.choice([0, 1, 5])})
        for _ in range(rng.randint(0, 14)):
            t = rng.choice(BUILDINGS + UNITS)
            objects.append({'owner': rng.choice(others), 't': t, 'cell': cell(), 'squad': 0, 'hp': rng.choice([10, 60, 127, 0]),
                            'kind': rng.choice([1, 2]), 'stock': rng.choice([0, 500])})
        squads = [[i, rng.choice([0, 2, 4, 5, 7]), rng.choice([0, 1, 2]), rng.choice([0, 0x40, 0x80, 0xc8, 0xff]), cell(), rng.choice([0, 30, 200])] for i in ids]
        reqs = []
        for _ in range(rng.randint(1, 5)):
            mode = rng.choice([1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 8, 8, 10, 10, 0, 9])
            r = {'mode': mode, 'first': rng.choice([0x15, 0x17, 0x11, rng.randrange(256)]), 'stage': rng.choice([0, 0, 1, 2]), 'prio': rng.choice([0x20, 0x80, 0xa0]),
                 'b4': rng.randrange(32), 'b5': rng.randrange(32), 'b6': rng.randrange(32), 'b7': rng.randrange(32),
                 'param': rng.choice([0, 20, 60, 200, 1000]), 'squad': rng.choice(ids + [0]) if ids else 0,
                 'age': rng.choice([0, 0x100, 0x400, 0x800, 0x1000, 7, 8, 1001, 1500]), 'tries': rng.choice([0, 1, 5, 10])}
            x0, y0 = rng.randrange(2, 24), rng.randrange(2, 24)
            r['b4'], r['b5'], r['b6'], r['b7'] = x0, y0, min(29, x0 + rng.randrange(1, 8)), min(29, y0 + rng.randrange(1, 8))
            r['prio'] = rng.choice([0x40, 0x80, 0x80])
            if mode == 1: r['b6'] = rng.choice([0x00, 0x08, 0x18, 0x08]); r['param'] = rng.choice([0, 15, 40, 300])
            if mode == 2: r['b4'] = rng.choice([0, 1, 10, 17, 3, 5]); r['b5'] = 2; r['param'] = rng.choice([0, 1, 3])
            if mode in (3, 4, 5, 8): r['param'] = rng.choice([3, 4, 9, 12, 23]) | 2 << 8
            if mode == 7: r['param'] = 0
            reqs.append(r)
        out.append({'participant': p, 'count': count, 'blocks': {str(k): v for k, v in blocks.items()}, 'relations': {str(k): v for k, v in relations.items()},
                    'hostile': [rng.choice([0xff ^ (1 << q), 0, rng.randrange(256)]) for q in range(count)], 'objects': objects, 'squads': squads,
                    'requests': reqs, 'random': [rng.randrange(256), rng.randrange(256)], 'seed': rng.randrange(256),
                    'resources': [rng.choice([0, 100, 5000]) for _ in range(4)], 'enabled': rng.choice([1, 1, 1, 0]),
                    'terrain': [rng.randrange(2), rng.randrange(4, 26), rng.choice([0, 1, 3]), rng.randrange(6)],
                    'class': rng.choice([[0x808, 0x800], [0x808, 0x800], [0xa08, 0x800]])})
        if n % 3 == 0:   # campaign ready to march, and plunder with mines to find
            c = out[-1]; mines = [q for q in others]
            for q in mines:
                for _ in range(2):
                    c['objects'].append({'owner': q, 't': 6, 'cell': cell(), 'squad': 0, 'hp': 127, 'kind': rng.choice([1, 2]), 'stock': 500})
                c['relations'][str(p)][q] = rng.choice([1, 2, 1, 0])
            c['blocks'][str(p)]['1a01'] = 0xff; c['blocks'][str(p)]['flags'] = 1
            if ids:
                army = rng.choice(ids)
                c['requests'][:2] = [{'mode': 7, 'first': rng.choice([3, 4]), 'stage': 1, 'prio': 0x80, 'b4': rng.choice(others), 'b5': 0, 'b6': 0, 'b7': 0,
                                      'param': 0, 'squad': 0, 'age': rng.choice([1, 0x800]), 'tries': 0},
                                     {'mode': 8, 'first': 0x17, 'stage': rng.choice([1, 1, 0]), 'prio': 0xa0, 'b4': 5, 'b5': 5, 'b6': 0xe8, 'b7': 0,
                                      'param': 100, 'squad': rng.choice([army, army, 0]), 'age': 3, 'tries': 0}]
            c['requests'].append({'mode': 10, 'first': 9, 'stage': 1, 'prio': 0, 'b4': 0, 'b5': 0, 'b6': 0, 'b7': 0, 'param': rng.choice([1, 2]),
                                  'squad': 0, 'age': 0x200, 'tries': 0})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    _, objs = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    o = MilitaryOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    raw = (game / 'ARMY__02.TRE').read_bytes(); records, end = decode(raw)
    classes = objs[2]['payload'][0xf0e6:0xf0eb]; o.cpu.mem_write(0xf0e6, classes)
    all_cases = cases(); digest = hashlib.sha256(); faults = 0
    wrk = (game / 'ARMY__02.WRK').read_bytes()
    global STRUCT_COUNT; STRUCT_COUNT = struct.unpack_from('<H', dat)[0] // STRIDE
    fort = objs[2]['payload'][0xf0f0:0xf0f8 + 0x80]
    log = []; seed = [0]
    def af07():
        eax, ebx, ecx, edx = (o.cpu.reg_read(r) for r in (UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX))
        allowed = ((ecx ^ edx ^ (eax & 0xff) ^ seed[0]) & 3) != 0
        log.append(struct.pack('<BHII', eax & 0xff, eax & 0xffff, ecx, edx))
        flags = o.cpu.reg_read(UC_X86_REG_EFLAGS) & ~0x40
        o.cpu.reg_write(UC_X86_REG_EFLAGS, flags | (0x40 if allowed else 0)); o.ret()
    o.stubs[0xaf07] = af07
    for case in all_cases:
        o.prepare(32, 32); p = case['participant']; count = case['count']
        o.cpu.mem_write(0x6c6, bytes([count])); o.cpu.mem_write(0x6c8, bytes([count]))
        o.terrain(32, 32, [terrain_class(case, c) for c in range(1024)]); o.prepare(32, 32)
        o.cpu.mem_write(0x97a, bytes(0xf5 * 8))
        for q in range(count):
            tree = o.PARTICIPANTS + 0x18000 + 0x400 * q; o.cpu.mem_write(tree, raw[:end + 2])
            o.cpu.mem_write(0x6ca + 2 * 0x56 + 0x15 + 4 * q, struct.pack('<I', tree))
        size = 32 * 32 * 4; refs = []; at = 0x100; chains = {q: [at, at] for q in range(8)}
        for q in range(count):
            chains[q][0] = at
            for ob in case['objects']:
                if ob['owner'] != q: continue
                x, y = ob['cell']
                rec = unit(ob['t'], q, ob['hp'], 0, x * 32 + 9, y * 32 + 13, 1) + bytes(10)
                rec[0x1b] = ob['squad']; rec[0x23] = ob['kind']; rec[0x0f] = ob.get('flags', 0); rec[0x14] = ob.get('cmd', 5); struct.pack_into('<I', rec, 0x24, ob['stock']); struct.pack_into('<I', rec, 0x10, 0x40)
                if ob['owner'] == p: rec[0x22] = 0; rec[0x29:0x31] = bytes(8)
                o.cpu.mem_write(o.POOL + at, bytes(rec)); o.cpu.mem_write(o.GRID + size + (y * 32 + x) * 4, struct.pack('<I', at))
                ob['_ref'] = at; at += 0x40
            chains[q][1] = at
        o.cpu.mem_write(o.POOL + at, bytes(0x400))
        for q in range(8):
            base = 0x97a + 0xf5 * q; o.cpu.mem_write(base, b'\x02')
            o.cpu.mem_write(base + 9, struct.pack('<I', chains[q][1]) + bytes(1) + struct.pack('<I', chains[q][0]))
            if q < count:
                o.cpu.mem_write(base + 0x12, struct.pack('<I', case['hostile'][q])); o.cpu.mem_write(base + 0xb9, bytes(case['relations'][str(q)]))
                o.cpu.mem_write(base + 0x18, struct.pack('<4I', *case['resources']))
        blocks = {}
        for q in range(count):
            b = bytearray(STATS); v = case['blocks'][str(q)]; b[0] = q
            struct.pack_into('<I', b, 0x19e9, v['strength']); b[0x61], b[0x62] = v['home']; lx, ly = v['land']
            b[0x63:0x67] = bytes([lx, ly, lx + 8, ly + 8]); struct.pack_into('<I', b, 0x45, v['45']); struct.pack_into('<I', b, 0x13, v['13'])
            struct.pack_into('<I', b, 0x0e, v['0e']); b[0x52] = v['52']; b[0x1a01] = v['1a01']; b[0x2a] = v['2a']; b[0x19e4] = v['flags']
            b[0x1a15] = case['enabled']; b[0x5c] = v['52']
            for k in range(20): b[MIL + k * 0x15] = 0xff; b[ORD + k * 0x15] = 0xff
            blocks[q] = b
        b = blocks[p]
        lst = bytearray([len(case['squads'])])
        for k, (ident, state, stage, prio, centre, strength) in enumerate(case['squads']):
            s = bytearray((i * 3 + k * 17) & 255 for i in range(SQUAD)); s[0] = p; s[1] = ident; s[2] = state; s[5] = stage; s[4] = prio
            s[0x20], s[0x21] = centre; struct.pack_into('<I', s, 0x36, strength); lst += s
        b[LIST:LIST + len(lst)] = lst
        ids = bytearray(256)
        for ident, *_ in case['squads']: ids[ident] = 1
        b[0x7b:0x17b] = ids
        for k, r in enumerate(case['requests']):
            a = ORD + k * 0x15; rr = bytearray((i * 11 + k) & 255 for i in range(0x15))
            rr[0] = r['mode']; rr[1] = r['first']; rr[2] = r['stage']; rr[3] = r['prio']; rr[4:8] = bytes([r['b4'], r['b5'], r['b6'], r['b7']])
            param = r['param']
            struct.pack_into('<I', rr, 8, param); rr[0x0c] = r['squad']; struct.pack_into('<I', rr, 0x10, r['age']); rr[0x14] = r['tries']
            rr[0x14] = r['tries']
            b[a:a + 0x15] = rr
        for q in range(count): o.cpu.mem_write(o.PARTICIPANTS + STATS * q, bytes(blocks[q]))
        stats = o.PARTICIPANTS + STATS * p
        o.cpu.mem_write(0x6ba, struct.pack('<I', o.PARTICIPANTS)); o.cpu.mem_write(0xed20, struct.pack('<I', stats))
        o.cpu.mem_write(0xed2c, bytes([p])); o.cpu.mem_write(0xed2d, struct.pack('<I', 0x97a + 0xf5 * p))
        o.cpu.mem_write(0xed39, struct.pack('<II', *case['class'])); o.cpu.mem_write(0x19eb, bytes(case['random']))
        o.cpu.mem_write(0x6ca + 2 * 0x56 + 9, struct.pack('<I', o.WRK)); o.cpu.mem_write(o.WRK, wrk)
        o.cpu.mem_write(0x6ca + 2 * 0x56 + 0x55, bytes([STRUCT_COUNT]))
        o.cpu.mem_write(0xf0f0, fort); seed[0] = case['seed']; log.clear()
        o.mode = 'military'; fault = 0
        try:
            o.cpu.reg_write(UC_X86_REG_ESI, stats); o.run(o.CODE + 0x227c7)
        except UcError:
            fault = 1; faults += 1
        final = bytearray(o.cpu.mem_read(stats, STATS))
        for k in range(20):
            a = MIL + k * 0x15; final[a + 7] = 0
            if final[a] != 8: final[a + 5:a + 7] = bytes(2)
            if final[a] == 6: final[a + 8:a + 0x0c] = bytes(4)
            a = ORD + k * 0x15; final[a + 4:a + 8] = bytes(4)
        n = final[LIST]
        out = bytes([fault]) if fault else bytes([fault]) + bytes(final[0x7b:0x17b]) + bytes(final[LIST:LIST + 1 + SQUAD * n]) + bytes(final[MIL:MIL + 0x15 * 20]) + \
            bytes(final[ORD:ORD + 0x15 * 20]) + bytes(o.cpu.mem_read(o.POOL + 0x100, at - 0x100)) + bytes(o.cpu.mem_read(0x19eb, 2)) + bytes(o.cpu.mem_read(0xed39, 8)) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    print('faults', faults)
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'tree_sha256': hashlib.sha256(raw[:end + 2]).hexdigest(), 'gather_classes_hex': classes.hex(), 'cases': all_cases,
            'fortification_hex': fort.hex(), 'results': {'orders': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
