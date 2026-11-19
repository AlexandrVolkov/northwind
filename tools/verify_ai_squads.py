#!/usr/bin/env python3
"""Execute the AI squad registry, squad census and group orders of LE:1 under Unicorn.

  registry  2008B..20275, 2098B: squad number pool (+7B), create 200B5 (evicts the lowest +4 when 50 squads exist),
            add 2010B, remove 20143, weakest 20189, disband 201B8, transfer 201F6, find 20232/2024D/2098B, strength 20275
  census    205FB: members (+1B), strength DAT[76], kinds DAT[41] & 0Fh, command counts, flags DAT[74], centre cell
  orders    B765, B7B9, B851, B8A2, B8EE, B942 (command +14 and targets of squad members), B977 (squad bounds)
Squad records keep pattern bytes outside the fields set by the generator, so every byte the code touches is compared.
"""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, unit, STRIDE
from forestry_source import source_constants

TEMPLATES = [0, 1, 2, 3, 4, 6, 9, 10, 13, 18, 20, 24]
STATS, LIST, IDS, SQUAD = 0x1a35, 0x9df, 0x7b, 0x52
REGISTRY = [(0x20095, 0x20275 + 0x2a), (0x2098b, 0x209b0)]
ORDERS = [(0xb765, 0xb9eb)]


class SquadOracle(CombatOracle):
    RANGES = {'registry': (REGISTRY, []), 'census': ([(0x205fb, 0x206d5)], []), 'orders': (ORDERS, [])}


def squad_bytes(k, salt):
    return bytearray((i * 7 + k * 31 + salt) & 255 for i in range(SQUAD))


def build_list(case):
    raw = bytearray([len(case['squads'])])
    for k, (ident, state, priority, target, rect, home, strength) in enumerate(case['squads']):
        s = squad_bytes(k, case['salt']); s[0] = case['participant']; s[1] = ident; s[2] = state; s[4] = priority
        struct.pack_into('<I', s, 0x0f, target); s[0x13:0x17] = bytes(rect); s[0x17:0x1b] = bytes(home); struct.pack_into('<I', s, 0x36, strength)
        raw += s
    return raw + bytes(1 + 50 * SQUAD - len(raw))


def cases():
    rng = random.Random(2009)
    out = []
    ops = ['census'] * 3 + ['create'] * 3 + ['disband', 'transfer', 'find', 'find_area', 'strength_area', 'find_state', 'allocate'] + \
          ['order', 'order_formation', 'order_parameter', 'command', 'order_point', 'clear', 'bounds'] * 2
    for _ in range(2500):
        op = rng.choice(ops); p = rng.randrange(8)
        count = rng.choice([0, 1, 2, 3, 5, 8, 49, 50])
        idents = rng.sample(range(256), count) if rng.random() < 0.8 else [rng.randrange(256) for _ in range(count)]
        objs = []; refs = [0x100 + 0x40 * i for i in range(12)]
        for i in range(rng.randint(0, 12)):
            sq = rng.choice(idents + [0, rng.randrange(256)]) if idents else rng.choice([0, 1, 7])
            objs.append([rng.choice(TEMPLATES), sq, rng.randrange(256), rng.randrange(256), rng.randrange(65536),
                         rng.randrange(32 * 32), rng.randrange(32 * 32)])
        def rect():
            a, b = rng.randrange(32), rng.randrange(32)
            return [a, b, min(31, a + rng.randrange(12)), min(31, b + rng.randrange(12))]
        squads = [[ident, rng.choice([0, 2, 3, 4, 8, 9, 10, rng.randrange(256)]), rng.choice([0, 1, 5, 0x80, 0xff, rng.randrange(256)]),
                   rng.choice(refs + [0, rng.randrange(1 << 32)]), rect(), rect(), rng.randrange(1 << 16)] for ident in idents]
        used = rng.sample(range(256), rng.choice([0, 3, 40, 255, 256]))
        pick = rng.randrange(count) if count else 0
        out.append({'op': op, 'participant': p, 'salt': rng.randrange(256), 'objects': objs, 'squads': squads, 'used': used, 'pick': pick,
                    'command': rng.choice([0x40, 0x48, 0x50, 0x58, 0x60, 0x08, 0x88, rng.randrange(256)]),
                    'squad': rng.choice(idents + [0, rng.randrange(256)]) if idents else rng.randrange(256),
                    'point': [rng.randrange(40), rng.randrange(40)], 'value': rng.randrange(1 << 32), 'state': rng.choice([0, 2, 3, 4, 8, 9]),
                    'target': rng.choice(refs + [0, 0x40]), 'limits': [rng.choice([0x400, 0x3ff, 0xffffffff]), rng.choice([0x400, 0x7ff, 0xffffffff])]})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = SquadOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    o.prepare(32, 32)
    stats = o.PARTICIPANTS; all_cases = cases(); digests = {}; counts = {}
    for case in all_cases:
        p = case['participant']; participant = 0x97a + 0xf5 * p
        o.cpu.mem_write(stats, bytes(STATS)); o.cpu.mem_write(stats, bytes([p]))
        ids = bytearray(256)
        for u in case['used']: ids[u] = 1
        o.cpu.mem_write(stats + IDS, bytes(ids)); o.cpu.mem_write(stats + LIST, bytes(build_list(case)))
        o.cpu.mem_write(0xed20, struct.pack('<I', stats))
        o.cpu.mem_write(0x18e6, struct.pack('<II', *case['limits']))
        at = 0x100
        for t, sq, f0f, f14, f17, cx, cy in case['objects']:
            rec = unit(t, p, 10, f17, (cx & 31) * 32 + (cx >> 5), (cy & 31) * 32 + (cy >> 5), 1)
            rec[0x1b] = sq; rec[0x0f] = f0f; rec[0x14] = f14; struct.pack_into('<I', rec, 0x10, 0x40)
            o.cpu.mem_write(o.POOL + at, bytes(rec) + bytes(0x40 - len(rec))); at += 0x40
        for q in range(8): o.cpu.mem_write(0x97a + 0xf5 * q + 9, struct.pack('<I', 0x100) + bytes(1) + struct.pack('<I', 0x100))
        o.cpu.mem_write(participant + 0xe, struct.pack('<I', 0x100)); o.cpu.mem_write(participant + 9, struct.pack('<I', at))
        objects = lambda: bytes(o.cpu.mem_read(o.POOL + 0x100, at - 0x100))
        squads = lambda: bytes(o.cpu.mem_read(stats + IDS, 256)) + bytes(o.cpu.mem_read(stats + LIST, 1 + 50 * SQUAD))
        reg = lambda r: o.cpu.reg_read(globals()['UC_X86_REG_' + r.upper()])
        record = stats + LIST + 1 + SQUAD * case['pick']
        px, py = case['point']; point = px | (py << 8)
        op = case['op']; out = b''
        def call(mode, address, **registers):
            o.mode = mode
            for name, value in registers.items(): o.cpu.reg_write(globals()['UC_X86_REG_' + name.upper()], value)
            o.run(o.CODE + address)
        if op == 'census':
            call('census', 0x205fb, edi=record, esi=0x1234); out = bytes(o.cpu.mem_read(record, SQUAD))
        elif op == 'create':
            call('registry', 0x200b5); zf = o.zf()
            out = bytes([zf]) + (struct.pack('<I', reg('edi') - stats) if zf else b'') + squads() + objects()
        elif op in ('disband', 'transfer'):
            ident = bytes(o.cpu.mem_read(record + 1, 1))[0] if case['squads'] else case['squad']
            regs = dict(eax=p | (ident << 8), edi=stats + LIST)
            if op == 'transfer': regs['edx'] = case['value'] & 0xff
            call('registry', 0x201b8 if op == 'disband' else 0x201f6, **regs); out = squads() + objects()
        elif op == 'find':
            call('registry', 0x20232, edi=stats + LIST, ebx=case['squad']); zf = o.zf()
            out = bytes([zf]) + (struct.pack('<I', reg('edi') - stats) if not zf else b'')
        elif op == 'find_area':
            call('registry', 0x2024d, edi=stats + LIST, ecx=point); zf = o.zf()
            out = bytes([zf]) + (struct.pack('<I', reg('edi') - stats) if not zf else b'')
        elif op == 'strength_area':
            call('registry', 0x20275, edi=stats + LIST, ecx=point); out = struct.pack('<I', reg('eax'))
        elif op == 'find_state':
            call('registry', 0x2098b, edi=stats + LIST, eax=case['state'], edx=o.POOL + case['target']); zf = o.zf()
            out = bytes([zf]) + (struct.pack('<I', reg('edi') - stats) if not zf else b'')
        elif op == 'allocate':
            call('registry', 0x20095, esi=stats); out = bytes([o.zf(), reg('eax') & 0xff]) + bytes(o.cpu.mem_read(stats + IDS, 256))
        else:
            entry = {'order': 0xb765, 'order_formation': 0xb7b9, 'order_parameter': 0xb851, 'command': 0xb8a2,
                     'order_point': 0xb8ee, 'clear': 0xb942, 'bounds': 0xb977}[op]
            ecx = case['value'] if op == 'order_parameter' else point
            call('orders', entry, eax=p | (case['command'] << 8), ebx=case['squad'], ecx=ecx)
            out = objects() + (struct.pack('<4I', reg('eax'), reg('ebx'), reg('ecx'), reg('edx')) if op == 'bounds' else b'')
        digests.setdefault(op, hashlib.sha256()).update(out); counts[op] = counts.get(op, 0) + 1
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {op: {'case_count': counts[op], 'sha256': digests[op].hexdigest()} for op in sorted(digests)}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
