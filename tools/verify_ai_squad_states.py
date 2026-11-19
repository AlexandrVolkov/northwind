#!/usr/bin/env python3
"""Execute the AI squad states LE:1 1F078 (table LE:3 F083, states 0..8) and their helpers under Unicorn.

Helpers covered: 1FBB1 retreat (6996 direction with the DS:287E arctangent table, DS:19C7 steps), 1FC28/1FC6B march to a
cell (step 4 request), 1FCED fight modes, 1FF74/1FFB7 strength comparisons, 21CA0 enemy census (D7B1), 1EBEF random
explored cell, 20071 messages (table LE:3 F02E), 1EB5C requests, squad registry calls. Stubbed at their entries and
recorded: A4C2 (message display), 20001 (stuck-member rescue), worker states 9 (1F476) and 10 (1F653).
Request lists +1AB/+34F are compared without bytes +3 and +5..+0B (register leftovers at several call sites).
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_combat import CombatOracle, unit, STRIDE
from forestry_source import source_constants

TEMPLATES = [0, 1, 2, 3, 4, 6, 9, 10, 13, 18, 20, 24]
STATS, LIST, SQUAD = 0x1a35, 0x9df, 0x52
STATES = [0x1f31e, 0x1f3f5, 0x1f1df, 0x1f0d5, 0x1f8cf, 0x1fad9, 0x1f72c, 0x1f7ca, 0x1fa01, 0x1f476, 0x1f653]
EXPLORED = 0x7000
RANGES = [(0x1f078, 0x1f476), (0x1f72c, 0x1fbb1), (0x1fbb1, 0x20095), (0x200ab, 0x200b5), (0x20143, 0x2024d), (0x20275, 0x2029f),
          (0x20408, 0x205fb), (0x219c3, 0x21a00), (0x21ca0, 0x21d84), (0xd7b1, 0xd85e), (0x1eaed, 0x1eb68), (0x1ead8, 0x1eaed),
          (0x1ecf8, 0x1ed26), (0x1edfd, 0x1ee02), (0x1ef21, 0x1ef26), (0x1efb8, 0x1efcc), (0x1eff9, 0x1f078), (0x6996, 0x69fe), (0x9e41, 0x9e69),
          (0x215f1, 0x21660), (0xc331, 0xc344), (0x6877, 0x68d8), (0x1ebef, 0x1ec3f)]


class StateOracle(CombatOracle):
    RANGES = {'states': (RANGES, [])}


def cases():
    rng = random.Random(1078)
    out = []
    for n in range(4000):
        p = rng.randrange(4); fight = n % 2 == 1
        ids = rng.sample(range(1, 256), rng.randint(1, 4))
        x0, y0 = rng.randrange(24), rng.randrange(24); w, h = rng.randrange(2, 9), rng.randrange(2, 9)
        def inside(): return min(31, y0 + rng.randrange(h + 1)) * 32 + min(31, x0 + rng.randrange(w + 1))
        def cell(): return inside() if fight and rng.random() < 0.8 else rng.randrange(1024)
        def rect():
            a, b = (x0, y0) if fight and rng.random() < 0.5 else (rng.randrange(32), rng.randrange(32))
            return [a, b, min(31, a + rng.randrange(1, 14)), min(31, b + rng.randrange(1, 14))]
        enemies = [[rng.choice([4, 6, 13, 16, 0, 18, 3, 20, 26]), rng.choice([q for q in range(8) if q != p]), rng.choice([0xff, 1 << p, rng.randrange(256)]),
                    rng.choice([60, 100, 127, 5, 0]), cell(), rng.choice([1, 1, 2, 0])] for _ in range(rng.randint(0, 8))]
        own = [[rng.choice([0, 1, 17, 18, 27, 13]), rng.choice(ids + [0]), rng.randrange(256), rng.choice([0, 0, 1, 2, 0x50]), 0,
                rng.randrange(65536), cell(), rng.randrange(256), 0, rng.randrange(3)] for _ in range(rng.randint(0, 8))]
        squads = []
        for ident in ids:
            state = rng.choice([0, 0, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 8, 8, 9, 10])
            stage = rng.choice([0, 1, 2, 3, 4]) if state != 6 else rng.choice([0, 1, 2, 3, 4, 5, 6, 0x80, 0x81, 0x82, 3])
            mode = rng.choice([0, 1, 2, 2, 3]); sub = rng.choice([0, 1, 2, 3]) if mode == 1 else rng.choice([0, 1, 2, 3, 4])
            squads.append({'id': ident, 'state': state, 'stage': stage, 'sub': sub, 'mode': mode,
                           'timer': rng.choice([0, 0x10, 0x20, 0x40, 0x400, 0x3ff, 1, 7, rng.randrange(1 << 16)]),
                           'step': rng.choice([0, 2, 3, 4, 5]), 'next': rng.choice([0xff, 0xff, 2, 3, 4]), 'count': rng.choice([0, 1, 2, 5, 11, 40]),
                           'strength': rng.choice([0, 20, 100, 1000]), 'flags41': rng.choice([0, 2]), 'planes': rng.choice([2, 3, 6, 7]),
                           'centre': [min(31, x0 + w // 2), min(31, y0 + h // 2)] if fight else [rng.randrange(32), rng.randrange(32)],
                           'target': rect(), 'home': rect(), 'step_area': rect(), 'priority': rng.choice([0, 0x20, 0x40, 0x80, 0xa0]),
                           'saved': rng.choice([0, 2, 3, 4]), 'object': rng.choice([0, 0, 1, 2, 3]), 'join': rng.choice(ids + [0, 77]),
                           'point': [rng.randrange(36), rng.randrange(36)]})
        lists = [[rng.choice([0xff, 0xff, 0xff, 2, 3, 0x0a]), rng.choice(ids + [0])] for _ in range(40)]
        out.append({'participant': p, 'salt': rng.randrange(256), 'own': own, 'enemies': enemies, 'squads': squads, 'lists': lists,
                    'hostile': [rng.choice([0xff, 0xff, rng.randrange(256)]) for _ in range(8)], 'ticks': rng.randint(1, 3),
                    'block': {'0a': rng.choice([0, 5, 100]), '0e': rng.choice([16, 32, 64]), '77': rng.choice([0, 1, 2]), '2a': rng.choice([0, 1, 5, 20]),
                              '29': rng.choice([0, 1, 2, 3, 4])},
                    'global': rng.choice([0, 0xff]), 'random': [rng.randrange(256), rng.randrange(256)], 'explored_seed': rng.randrange(1 << 16),
                    'stale_y': rng.randrange(256)})
    return out


def explored(seed):
    return bytes(((c * 167 + seed) ^ (c >> 3) ^ (seed >> 8)) & 255 for c in range(1024))


def build_list(case):
    raw = bytearray([len(case['squads'])])
    for k, s in enumerate(case['squads']):
        r = bytearray((i * 7 + k * 31 + case['salt']) & 255 for i in range(SQUAD))
        r[0] = case['participant']; r[1] = s['id']; r[2] = s['state']; r[4] = s['priority']; r[5] = s['stage']; r[6] = s['sub']; r[7] = s['mode']
        r[0x0d:0x0f] = bytes(s['point']); struct.pack_into('<I', r, 0x0f, 0 if s['object'] == 0 else 0x100 + 0x40 * (s['object'] - 1))
        if s['state'] == 7: r[0x13] = s['join']
        else: r[0x13:0x17] = bytes(s['target'])
        r[0x17:0x1b] = bytes(s['home']); struct.pack_into('<I', r, 0x1b, s['timer']); r[0x1f] = s['saved']
        r[0x20:0x22] = bytes(s['centre']); struct.pack_into('<I', r, 0x22, s['count']); struct.pack_into('<I', r, 0x36, s['strength'])
        r[0x3a] = s['planes']; r[0x41] = s['flags41']; r[0x4a] = s['step']; r[0x4b] = s['next']; r[0x4d:0x51] = bytes(s['step_area'])
        raw += r
    return raw


def mask_lists(block):
    out = bytearray()
    for base in (0x1ab, 0x34f):
        for k in range(20):
            r = bytearray(block[base + k * 0x15:base + (k + 1) * 0x15]); r[3] = 0; r[5:0x0c] = bytes(7); out += r
    return bytes(out)


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    o = StateOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    arctangent = objects[1]['payload'][0x287e:0x297e]; directions = objects[1]['payload'][0x19c7:0x19d7]
    messages = objects[2]['payload'][0xf02e:0xf06a]  # 5 rows: AI character byte +29 is 1..4
    o.cpu.mem_write(0x287e, arctangent); o.cpu.mem_write(0x19c7, directions); o.cpu.mem_write(0xf02e, messages)
    o.cpu.mem_write(0xf083, b''.join(struct.pack('<I', o.CODE + a) for a in STATES))
    o.cpu.mem_write(0x18da, struct.pack('<I', EXPLORED))
    log = []
    def stub(name, registers):
        def run():
            log.append(bytes([name]) + b''.join(struct.pack('<I', o.cpu.reg_read(globals()['UC_X86_REG_' + r])) for r in registers)); o.ret()
        return run
    o.stubs[0xa4c2] = stub(1, ['EAX'])
    def rescue():
        log.append(bytes([2, bytes(o.cpu.mem_read(o.cpu.reg_read(UC_X86_REG_EDI) + 1, 1))[0]])); o.ret()
    o.stubs[0x20001] = rescue
    def worker():
        log.append(bytes([3, bytes(o.cpu.mem_read(o.cpu.reg_read(UC_X86_REG_EDI) + 1, 1))[0]])); o.ret()
    o.stubs[0x1f476] = worker; o.stubs[0x1f653] = worker
    all_cases = cases(); digest = hashlib.sha256()
    for case in all_cases:
        o.prepare(32, 32); p = case['participant']; stats = o.PARTICIPANTS + STATS * p
        block = bytearray(STATS); block[0] = p
        b = case['block']; struct.pack_into('<I', block, 0x0a, b['0a']); struct.pack_into('<I', block, 0x0e, b['0e']); block[0x77] = b['77']
        block[0x2a] = b['2a']; block[0x29] = b['29']
        for k, (mode, first) in enumerate(case['lists']):
            r = bytearray((i * 5 + k) & 255 for i in range(0x15)); r[0] = mode; r[1] = first
            block[(0x1ab if k < 20 else 0x34f) + (k % 20) * 0x15:][:0x15] = r
            at = (0x1ab if k < 20 else 0x34f) + (k % 20) * 0x15; block[at:at + 0x15] = r
        lst = build_list(case); block[LIST:LIST + len(lst)] = lst
        o.cpu.mem_write(stats, bytes(block)); o.cpu.mem_write(0xed20, struct.pack('<I', stats))
        o.cpu.mem_write(0x19e9, bytes([case['global']])); o.cpu.mem_write(0x19eb, bytes(case['random']))
        o.cpu.mem_write(EXPLORED, explored(case['explored_seed'])); o.cpu.mem_write(0xf0e5, bytes([case['stale_y']]))
        for q in range(8):
            o.cpu.mem_write(0x97a + 0xf5 * q + 9, struct.pack('<I', 0x100) + bytes(1) + struct.pack('<I', 0x100) + struct.pack('<I', case['hostile'][q]))
        size = 32 * 32 * 4; at = 0x100
        enemy_refs = []
        for t, owner, f20, hp, c, plane in case['enemies']:
            rec = unit(t, owner, hp, 0, (c & 31) * 32 + 17, (c >> 5) * 32 + 3, 1) + bytes(10)
            rec[0x20] = f20; struct.pack_into('<I', rec, 0x10, 0x40)
            o.cpu.mem_write(o.POOL + at, bytes(rec)); o.cpu.mem_write(o.GRID + plane * size + c * 4, struct.pack('<I', at)); at += 0x40
        while at < 0x100 + 0x40 * 3:
            o.cpu.mem_write(o.POOL + at, bytes(unit(0, 7, 0, 0, 40, 40, 1)) + bytes(10)); at += 0x40   # dead placeholders for +0F
        start = at
        for t, sq, f0f, f14, f15, f17, c, f20, target, plane in case['own']:
            rec = unit(t, p, 10, f17, (c & 31) * 32 + 5, (c >> 5) * 32 + 9, 1) + bytes(10)
            rec[0x1b] = sq; rec[0x0f] = f0f; rec[0x14] = f14; rec[0x20] = f20; struct.pack_into('<I', rec, 0x10, 0x40)
            o.cpu.mem_write(o.POOL + at, bytes(rec)); at += 0x40
        participant = 0x97a + 0xf5 * p
        o.cpu.mem_write(participant + 0xe, struct.pack('<I', start)); o.cpu.mem_write(participant + 9, struct.pack('<I', at))
        o.cpu.mem_write(o.POOL + at, bytes(0x400))
        log.clear(); o.mode = 'states'
        for _ in range(case['ticks']):
            o.cpu.reg_write(UC_X86_REG_ESI, stats); o.run(o.CODE + 0x1f078)
        final = bytes(o.cpu.mem_read(stats, STATS)); count = final[LIST]
        out = final[LIST:LIST + 1 + SQUAD * count] + final[0x17c:0x1ab] + mask_lists(final) + bytes([final[0x19e8]]) + \
            bytes(o.cpu.mem_read(o.POOL + start, at - start)) + b''.join(log) + bytes(o.cpu.mem_read(0x19eb, 2))
        digest.update(out); case["sha256_prefix"] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES},
            'arctangent_hex': arctangent.hex(), 'messages_hex': messages.hex(), 'cases': all_cases,
            'results': {'states': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
