#!/usr/bin/env python3
"""Execute the AI worker squad states 9 (LE:1 1F476) and 10 (1F653) through the state loop 1F078 under Unicorn.

Helpers covered: 206D5 random idle member, 20730 member by activity, 224D1 nearest timber cell of the squad area,
2258F random grove (fog [18DA], 2095D squads covering a cell, 2268E terrain class in an area), C439 receiver search,
205E7 enemy near the centre and the 1EB5C requests. Only squads in states 9 and 10 are generated.
Compared: the squad list, the members and other own objects, lists +1AB (without +8..+0B, EBP leftover) and +4F3
(without +4..+7, C439 leftovers), the random indices.
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import unit, STRIDE
from verify_ai_squad_states import StateOracle, RANGES as STATE_RANGES, STATES, STATS, LIST, SQUAD, EXPLORED, explored
from forestry_source import source_constants

TEMPLATES = [0, 2, 4, 6, 8, 10, 11, 13]
RANGES = STATE_RANGES + [(0x1f476, 0x1f72c), (0x206d5, 0x20771), (0x224d1, 0x22613), (0x2268e, 0x226f1), (0x2095d, 0x2098b),
                         (0xc439, 0xc4cd), (0x1eb40, 0x1eb5c), (0x1fe97, 0x1fece), (0x1f004, 0x1f011)]


class WorkerOracle(StateOracle):
    RANGES = {'states': (RANGES, [])}


def prop(seed, c):
    """Terrain descriptor dword of tile c (the map uses tile index = cell): class in bits 0..2, sometimes bits 8..11."""
    h = (c * 2654435761 + seed * 97) & 0xffffffff
    return [1, 1, 1, 0, 2, 3, 1, 5][(h >> 29) & 7] | (((h >> 8) & 0xf00) if (h & 0x1f) < 6 else 0)


def cases():
    rng = random.Random(1476)
    out = []
    for n in range(3000):
        p = rng.randrange(4)
        ids = rng.sample(range(1, 256), rng.randint(1, 3))
        def rect():
            a, b = rng.randrange(30), rng.randrange(30)
            return [a, b, min(31, a + rng.randrange(0, 12)), min(31, b + rng.randrange(0, 12))]
        enemies = [[rng.choice([0, 3, 4, 18]), rng.choice([q for q in range(8) if q != p]), rng.choice([0xff, 1 << p]),
                    rng.choice([60, 100, 0]), rng.randrange(1024), rng.choice([1, 1, 2])] for _ in range(rng.choice([0, 0, 0, 1, 3]))]
        own = []
        for _ in range(rng.randint(0, 10)):
            t = rng.choice([10, 10, 10, 8, 11, 0, 2, 4, 4, 6, 6])
            own.append([t, rng.choice(ids + [0]) if t in (0, 8, 10, 11) else 0, rng.choice([0x3f, 0x13, 0x08, 0, 0x37]),
                        rng.choice([0, 0, 1, 1, 2, 3, 4, 0x58, 0x60, 0x41]), rng.choice([0, 0, 1]), rng.randrange(1024),
                        rng.choice([0, 1, 5, 10, 15, 20, 21, 60, 127, 200]), rng.choice([0, 0, 500, 3])])
        squads = []
        for ident in ids:
            state = rng.choice([9, 9, 10])
            squads.append({'id': ident, 'state': state, 'stage': rng.choice([0, 1, 2, 2, 3]), 'priority': rng.choice([0, 0x40, 0x80]),
                           'mine': rng.choice([-1, -1, -2] + list(range(len(own)))),
                           'target': rect(), 'timer': rng.choice([0, 0, 0, 0x20, 0x40, 1, 0x1f, rng.randrange(1 << 16)]),
                           'saved': rng.choice([0, 1, 2, 5, 20]), 'count': rng.choice([0, 1, 2, 3, 5, 8, 40]),
                           'planes': rng.choice([2, 6]), 'centre': [rng.randrange(32), rng.randrange(32)],
                           'step': rng.choice([0, 2, 3]), 'next': rng.choice([0xff, 0x82, 2, 0])})
        lists = [[rng.choice([0xff, 0xff, 0xff, 1, 4]), rng.choice(ids + [0])] for _ in range(40)]
        out.append({'participant': p, 'salt': rng.randrange(256), 'own': own, 'enemies': enemies, 'squads': squads, 'lists': lists,
                    'hostile': [rng.choice([0xff, 0]) for _ in range(8)], 'ticks': rng.randint(1, 3),
                    'random': [rng.randrange(256), rng.randrange(256)], 'explored_seed': rng.randrange(1 << 16),
                    'terrain_seed': rng.randrange(1 << 16)})
    return out


def own_offset(case, index): return 0x100 + 0x40 * max(3, len(case['enemies'])) + 0x40 * index


def build_list(case):
    raw = bytearray([len(case['squads'])])
    for k, s in enumerate(case['squads']):
        r = bytearray((i * 7 + k * 31 + case['salt']) & 255 for i in range(SQUAD))
        r[0] = case['participant']; r[1] = s['id']; r[2] = s['state']; r[4] = s['priority']; r[5] = s['stage']
        mine = 0 if s['mine'] == -1 else 0x100 if s['mine'] == -2 else own_offset(case, s['mine'])
        struct.pack_into('<I', r, 0x0f, mine); r[0x13:0x17] = bytes(s['target']); struct.pack_into('<I', r, 0x1b, s['timer'])
        r[0x1f] = s['saved']; r[0x20:0x22] = bytes(s['centre']); struct.pack_into('<I', r, 0x22, s['count'])
        r[0x3a] = s['planes']; r[0x4a] = s['step']; r[0x4b] = s['next']
        raw += r
    return raw


def masked_lists(block):
    out = bytearray()
    for base, lo, hi in ((0x1ab, 8, 0x0c), (0x4f3, 4, 8)):
        for k in range(20):
            r = bytearray(block[base + k * 0x15:base + (k + 1) * 0x15]); r[lo:hi] = bytes(hi - lo); out += r
    return bytes(out)


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = WorkerOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    o.cpu.mem_write(0xf083, b''.join(struct.pack('<I', o.CODE + a) for a in STATES))
    o.cpu.mem_write(0x18da, struct.pack('<I', EXPLORED))
    all_cases = cases(); digest = hashlib.sha256(); counts = {}
    for case in all_cases:
        o.terrain(32, 32, [prop(case['terrain_seed'], c) for c in range(1024)]); o.prepare(32, 32)
        p = case['participant']; stats = o.PARTICIPANTS + STATS * p
        block = bytearray(STATS); block[0] = p
        for k, (mode, first) in enumerate(case['lists']):
            r = bytearray((i * 5 + k) & 255 for i in range(0x15)); r[0] = mode; r[1] = first
            at = (0x1ab if k < 20 else 0x4f3) + (k % 20) * 0x15; block[at:at + 0x15] = r
        lst = build_list(case); block[LIST:LIST + len(lst)] = lst
        o.cpu.mem_write(stats, bytes(block)); o.cpu.mem_write(0xed20, struct.pack('<I', stats))
        o.cpu.mem_write(0x19eb, bytes(case['random'])); o.cpu.mem_write(EXPLORED, explored(case['explored_seed']))
        for q in range(8):
            o.cpu.mem_write(0x97a + 0xf5 * q + 9, struct.pack('<I', 0x100) + bytes(1) + struct.pack('<I', 0x100) + struct.pack('<I', case['hostile'][q]))
        size = 32 * 32 * 4; at = 0x100
        for t, owner, f20, hp, c, plane in case['enemies']:
            rec = unit(t, owner, hp, 0, (c & 31) * 32 + 17, (c >> 5) * 32 + 3, 1) + bytes(10)
            rec[0x20] = f20; struct.pack_into('<I', rec, 0x10, 0x40)
            o.cpu.mem_write(o.POOL + at, bytes(rec)); o.cpu.mem_write(o.GRID + plane * size + c * 4, struct.pack('<I', at)); at += 0x40
        while at < 0x100 + 0x40 * 3:
            o.cpu.mem_write(o.POOL + at, bytes(unit(0, 7, 0, 0, 40, 40, 1)) + bytes(10)); at += 0x40
        start = at
        for t, sq, f0f, f14, f15, c, hp, stock in case['own']:
            rec = unit(t, p, hp, 0, (c & 31) * 32 + 5, (c >> 5) * 32 + 9, 1) + bytes(10)
            rec[0x1b] = sq; rec[0x0f] = f0f; rec[0x14] = f14; rec[0x15] = f15; struct.pack_into('<I', rec, 0x24, stock)
            struct.pack_into('<I', rec, 0x10, 0x40)
            o.cpu.mem_write(o.POOL + at, bytes(rec)); at += 0x40
        participant = 0x97a + 0xf5 * p
        o.cpu.mem_write(participant + 0xe, struct.pack('<I', start)); o.cpu.mem_write(participant + 9, struct.pack('<I', at))
        o.cpu.mem_write(o.POOL + at, bytes(0x400))
        o.mode = 'states'
        for _ in range(case['ticks']):
            o.cpu.reg_write(UC_X86_REG_ESI, stats); o.run(o.CODE + 0x1f078)
        final = bytes(o.cpu.mem_read(stats, STATS)); count = final[LIST]
        out = final[LIST:LIST + 1 + SQUAD * count] + masked_lists(final) + bytes(o.cpu.mem_read(o.POOL + start, at - start)) + \
            bytes(o.cpu.mem_read(0x19eb, 2))
        for s in case['squads']: counts[(s['state'], s['stage'])] = counts.get((s['state'], s['stage']), 0) + 1
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    print('squads by (state, stage):', sorted(counts.items()))
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'workers': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
