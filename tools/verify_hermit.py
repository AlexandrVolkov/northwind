#!/usr/bin/env python3
"""Verify the quest script of the hermit Onis (ARMY__03.WRK DBF..11F1, AH = 1; the scripts of the other hermits are verified the same way) with Unicorn. The FS services that act on the world — FS:143 (EE38, say a message),
FS:167 (11C3D, reveal an area), FS:16F (11C73, sweep a participant's buildings), FS:17B (D895, look for an attacker), FS:45 (A758, create an object), FS:18F (103EC),
FS:15 (98CC), FS:1D, FS:163 (11C1A, objects of the territory), FS:DD / FS:E1 (the standard unit step) — are recorded stubs; FS:147 (124D8), FS:153 (EE92 / EE69),
FS:14F (11BE0), FS:6D and FS:71 run as original code."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle
from forestry_source import source_constants


PRESETS = {
    'onis': {'entry': 0xdbf, 'end': 0x11fd, 'items': [0xc, 0xd, 8, 9, 0xb, 10, 12, 13], 'queue': [0x11, 0x50, 0x26, 0x43, 0x59, 0x5a, 0x4a, 0x4d], 'templates': [2, 5, 0x1a], 'owner_items': [0xc, 0xd], 'scripted': [2, 0x1a, 5, 5, 5, 5, 5, 5]},
    'irvil': {'entry': 0x1297, 'end': 0x162e, 'items': [0xb, 0xa, 0xe, 0xd, 8, 0x10], 'queue': [0x11, 0x60, 0x26, 0x54, 0x59, 0x5a], 'templates': [2, 0x19], 'owner_items': [0xe, 0xd], 'scripted': [2, 0x19, 0x19]},
}
PRESETS['miron'] = {'entry': 0x16c8, 'end': 0x1a08, 'items': [9, 0xb, 0xc], 'queue': [0x50, 0x74, 0x65, 0x6b, 0x59, 0x5a], 'templates': [4, 3], 'owner_items': [9, 0xb], 'scripted': [4, 4], 'record': 0x54}
for _p in PRESETS.values(): _p.setdefault('record', 0x50)
PRESETS['kevin'] = {'entry': 0x1d72, 'end': 0x1ed3, 'items': [0, 0], 'queue': [0x59, 0x5a, 0x85], 'templates': [2], 'owner_items': [0], 'scripted': [2], 'record': 0x50}
PRESETS['garik'] = {'entry': 0x1f39, 'end': 0x2077, 'items': [0, 0], 'queue': [0x59, 0x5a], 'templates': [2], 'owner_items': [0], 'scripted': [2], 'record': 0x50}
PRESETS['magus'] = {'entry': 0x1a6e, 'end': 0x1cd8, 'items': [0x14, 0x14], 'queue': [0x59, 0x5a, 0x7b, 0x81], 'templates': [1, 0x17], 'owner_items': [0], 'scripted': [], 'record': 0x50}
PRESETS['efil'] = {'entry': 0x2111, 'end': 0x2433, 'items': [9, 0xa, 0x13, 0xc], 'queue': [0x50, 0x65, 0x6b, 0x59, 0x5a], 'templates': [7, 3], 'owner_items': [9, 0xa], 'scripted': [7] * 8, 'record': 0x54}
PRESETS['kochet'] = {'entry': 0x2499, 'end': 0x2676, 'items': [0x14, 0x14], 'queue': [0x59, 0x5a, 0x7b, 0x81], 'templates': [2], 'owner_items': [0], 'scripted': [], 'record': 0x50}
PRESET = PRESETS['onis']


class HermitOracle(CombatOracle):
    RANGES = {'hermit': ([(0x124d8, 0x124fd), (0xee69, 0xeea3), (0x11be0, 0x11c1a), (0x6877, 0x68a0), (0x68bd, 0x68d8), (0x9e41, 0x9e69)], [(PRESET['entry'], PRESET['end'])])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=300000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('rufin did not return')


def cases():
    rng = random.Random(PRESET['entry'])
    out = []
    for n in range(400):
        count = rng.choice([2, 3, 4])
        case = {'count': count, 'owner': rng.randrange(count), 'tick': rng.choice([0, 32, 64, 1, 17, 255, 96]), 'ah': rng.choice([1, 1, 1, 1, 0, 2]),
                'condition': rng.choice([100, 100, 60, 61, 50, 0x78, 1, 0, 0xfe]), 'flags14': rng.choice([0, 0x80, 0x81]), 'flags17': rng.randrange(65536),
                'relations': [[rng.choice([0, 1, 1, 2, 3]) for _ in range(8)] for _ in range(count)],
                'inventory': [[rng.choice([0, 0, 0] + PRESET['items']) for _ in range(20)] for _ in range(count)],
                'queue': [[[rng.choice([0xff, 0xff] + PRESET['queue']), rng.randrange(count)] for _ in range(8)] for _ in range(count)],
                'seed': rng.randrange(256), 'masks': {k: rng.choice([0, 0, rng.randrange(16), rng.randrange(1 << 8)]) for k in ('33', '35', '37', '39', '3f', '41', '43', '45', '49', '4b')},
                'word47': rng.choice([0, 0, 0, 1]),
                'objects': [{'owner': rng.randrange(count), 'army': rng.choice([0, 1, 2]), 'template': rng.choice(PRESET['templates'] + [2, 1, 3]), 'layer': rng.choice([0, 1]), 'mask22': rng.randrange(256), 'mask28': rng.randrange(256),
                             'condition': rng.choice([100, 5, 50]), 'flags': rng.choice([0, 8, 0x28, 0x20])} for _ in range(rng.choice([0, 3, 6]))],
                'scan': {'zf': rng.choice([0, 1]), 'object': rng.randrange(6)}, 'create_zf': rng.choice([0, 1]), 'territory': [rng.randrange(6) for _ in range(rng.choice([0, 2, 4]))]}
        if rng.random() < 0.6:   # a scripted conversation: the answers of the participants and the staffs
            owner = case['owner']
            case['tick'] = rng.choice([0, 32, 64, 0])
            case['ah'] = 1; case['condition'] = 100; case['flags17'] = rng.randrange(65536)
            case['inventory'][owner] = PRESET['owner_items'] + [0] * 18
            for p in range(count):
                if p != owner:
                    case['relations'][owner][p] = rng.choice([1, 1, 3, 2]); case['relations'][p][owner] = 1
                    if rng.random() < 0.7: case['inventory'][p] = [8, 9 if rng.random() < 0.2 else 0] + [0] * 18
                    for s_ in range(3): case['queue'][p][s_] = [rng.choice(PRESET['queue'] + [0xff]), owner]
            for g in case['objects']: g['army'] = rng.choice([3, 3, 1])
            case['word47'] = 0
            for t in PRESET['scripted']: case['objects'].append({'owner': owner, 'army': 3, 'template': t, 'layer': 0, 'mask22': rng.randrange(256), 'mask28': rng.randrange(256), 'condition': 100, 'flags': 0})
        case['time'] = rng.choice([0, 100, 0x2710, 5000, rng.randrange(1 << 20)]); case['dim4'] = rng.choice([1024, 2048, 3000]); case['dim0'] = rng.choice([1024, 1536, 2048]); case['deadline'] = rng.choice([0, 3000, 5000, rng.randrange(1 << 20)])
        case['near'] = [[rng.choice([0, 0, 1]), rng.randrange(4)] for _ in range(30)]
        case['free'] = rng.choice([0, 3, 4, 5, 6, 7, 8, 12])
        case['spot'] = {'ok': rng.choice([0, 1, 1]), 'x': rng.randrange(0, 2048), 'y': rng.randrange(0, 2048)}
        for k in ('3b', '3d'): case['masks'][k] = rng.choice([0, 0, rng.randrange(16)])
        out.append(case)
    return out


def generate(game):
    HermitOracle.RANGES = {'hermit': (HermitOracle.RANGES['hermit'][0], [(PRESET['entry'], PRESET['end'])])}
    constants = source_constants(game); report, code = analyze(game)
    o = HermitOracle(code, report['fs_assignments'], constants)
    wrk = (game / 'ARMY__03.WRK').read_bytes(); o.cpu.mem_write(o.WRK, wrk)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    last_edx = {}
    def stub_log(letter, *regs, mask=0xffffffff, rel=False):
        def f():
            log.append(letter + b''.join(struct.pack('<I', (reg(r) - (o.POOL if rel else 0)) & mask) for r in regs)); o.ret()
        return f
    def reveal():   # FS:167 = 11C3D: EAX, EBX the point, EDX the size; the port treats it as register-preserving (the caller goes on with its loop variable in EDX)
        keep = struct.unpack('<I', bytes(o.cpu.mem_read(reg(UC_X86_REG_ESP) + 4, 4)))[0]   # the loop variable the script pushed
        log.append(b'A' + struct.pack('<III', reg(UC_X86_REG_EAX), reg(UC_X86_REG_EBX), reg(UC_X86_REG_EDX)))
        o.ret(); o.cpu.reg_write(UC_X86_REG_EDX, keep)
    def say():   # FS:143 = EE38: AL the message, DL the participant, ESI the speaker
        log.append(b'M' + struct.pack('<BBB', reg(UC_X86_REG_EAX) & 255, reg(UC_X86_REG_EDX) & 255, o.cpu.mem_read(reg(UC_X86_REG_ESI) + 3, 1)[0])); o.ret()
    def scan():  # FS:17B = D895
        log.append(b'F' + struct.pack('<B', reg(UC_X86_REG_EAX) & 255))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x40 * state['scan']['object'])
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if state['scan']['zf'] else 0)); o.ret()
    def spot():   # FS:187 = 10AD2
        log.append(b'P' + struct.pack('<BBBII', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255, reg(UC_X86_REG_EBX) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        o.cpu.reg_write(UC_X86_REG_ECX, state['spot']['x']); o.cpu.reg_write(UC_X86_REG_EDX, state['spot']['y'])
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if state['spot']['ok'] else 0)); o.ret()
    def free_regions():   # FS:18B = 11CC9
        log.append(b'K' + struct.pack('<B', reg(UC_X86_REG_EAX) & 255)); o.cpu.reg_write(UC_X86_REG_EAX, state['free']); o.ret()
    def nearest():   # FS:75 = C439
        log.append(b'N' + struct.pack('<BI', o.cpu.mem_read(reg(UC_X86_REG_ESI) + 3, 1)[0], reg(UC_X86_REG_EBX)))
        zf, idx = state['near'].pop(0) if state['near'] else (1, 0)
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x40 * idx)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if zf else 0)); o.ret()
    def convert():   # FS:193 = D1BA
        log.append(b'X' + struct.pack('<BI', reg(UC_X86_REG_EAX) & 255, reg(UC_X86_REG_ESI) - o.POOL)); o.ret()
    def occupy():  # FS:19B = 43E5
        log.append(b'O' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.ret()
    def create():  # FS:45 = A758
        log.append(b'C' + struct.pack('<BBBBII', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255, reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x600)
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if state['create_zf'] else 0)); o.ret()
    def init():  # FS:18F = 103EC
        log.append(b'I' + struct.pack('<III', reg(UC_X86_REG_ESI) - o.POOL, reg(UC_X86_REG_EAX), reg(UC_X86_REG_ECX))); o.ret()
    def territory():  # FS:163 = 11C1A: the list of objects at 1F45
        o.cpu.reg_write(UC_X86_REG_EDI, 0x1f45); log.append(b'T'); o.ret()
    def standard_vitals():
        log.append(b'V'); o.ret()
    def standard_actions():
        log.append(b'U'); o.cpu.emu_stop(); o.stopped = True
    o.stubs = {0xc439: nearest, 0xd1ba: convert, 0x11cc9: free_regions, 0x10ad2: spot, 0x43e5: occupy, 0xee38: say, 0x11c3d: reveal, 0x11c73: stub_log(b'S', UC_X86_REG_EAX, mask=0xff), 0xd895: scan, 0xa758: create, 0x103ec: init,
               0x98cc: stub_log(b'R', UC_X86_REG_ESI, rel=True), 0x81d9: stub_log(b'D'), 0x11c1a: territory, 0x90e9: standard_vitals, 0x8227: standard_actions, 0xbebb: stub_log(b'Y', UC_X86_REG_EAX, mask=0xffff), 0xae3a: stub_log(b'N', UC_X86_REG_EAX, mask=0xff)}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state.update(scan=case['scan'], create_zf=case['create_zf'], spot=case['spot'], free=case['free'], near=[list(x) for x in case['near']]); o.prepare(32, 32); o.mode = 'hermit'; o.boundary = None
        o.cpu.mem_write(0x6ba, struct.pack('<I', o.PARTICIPANTS)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x6c6, bytes([case['count']]))
        o.cpu.mem_write(o.FS + 0x142, bytes([case['count']])); o.cpu.mem_write(o.FS + 0x10, bytes([case['tick']])); o.cpu.mem_write(o.FS + 0x17f, struct.pack('<I', case['time'])); o.cpu.mem_write(o.FS + 4, struct.pack('<I', case['dim4'])); o.cpu.mem_write(o.FS + 0, struct.pack('<I', case['dim0'])); o.cpu.mem_write(o.FS + 8, struct.pack('<I', o.POOL))
        o.cpu.mem_write(o.POOL, bytes(0x2000)); o.cpu.mem_write(0x19eb, bytes([case['seed']])); o.cpu.mem_write(0x19ec, bytes([case['seed'] ^ 0x5a]))
        for army in range(4):
            o.cpu.mem_write(0x6ca + 0x56 * army + 4, struct.pack('<I', o.DAT + army * 0x2000))
            for index in range(0x20):
                raw = bytearray(0xab); raw[0x46] = 10; o.cpu.mem_write(o.DAT + army * 0x2000 + index * 0xab, bytes(raw))
        count = len(case['objects'])
        flagmap = {}
        for k, g in enumerate(case['objects']):
            raw = bytearray(0x40); raw[0] = g['army']; raw[3] = g['owner']; raw[0x0d] = g['layer']; raw[0x0e] = g['template']; raw[0x19] = g['condition']; struct.pack_into('<I', raw, 0x10, 0x40)
            raw[0x22] = g['mask22']; raw[0x28] = g['mask28']
            o.cpu.mem_write(o.POOL + 0x40 * k, bytes(raw)); flagmap[(g['army'], g['template'])] = g['flags']
        for (a, t), f in flagmap.items(): o.cpu.mem_write(o.DAT + a * 0x2000 + t * 0xab + 0x40, struct.pack('<I', f))
        o.cpu.mem_write(0x1f45, b''.join(struct.pack('<I', o.POOL + 0x40 * i) for i in case['territory'] if i < count) + bytes(4))
        for p in range(case['count']):
            part = bytearray(0xf5); part[0xcd:0xcd + 20] = bytes(case['inventory'][p]); part[0xb9:0xb9 + 8] = bytes(case['relations'][p])
            struct.pack_into('<I', part, 0x0e, 0); struct.pack_into('<I', part, 0x09, 0x40 * count)
            for s, (t, other) in enumerate(case['queue'][p]): part[0x34 + 14 * s] = t; part[0x34 + 14 * s + 9] = other
            o.cpu.mem_write(0x97a + 0xf5 * p, bytes(part))
        rufin = bytearray(PRESET['record']); rufin[0] = 3; rufin[3] = case['owner']; rufin[0x14] = case['flags14']; struct.pack_into('<H', rufin, 0x17, case['flags17']); rufin[0x19] = case['condition']
        for k, v in case['masks'].items(): struct.pack_into('<I', rufin, int(k, 16), v)
        struct.pack_into('<H', rufin, 0x47, case['word47'])
        if PRESET['record'] > 0x50: struct.pack_into('<I', rufin, 0x4d, case['deadline'])
        o.cpu.mem_write(o.POOL + 0x1000, bytes(rufin))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x1000); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x1000); o.cpu.reg_write(UC_X86_REG_EAX, case['ah'] << 8); o.cpu.reg_write(UC_X86_REG_EBX, 7)
        o.run(o.WRK + PRESET['entry'])
        out = bytes(o.cpu.mem_read(o.POOL + 0x1000, PRESET['record'])) + bytes(o.cpu.mem_read(o.POOL, 0x40 * count)) + bytes(o.cpu.mem_read(o.POOL + 0x600, 0x40)) \
            + b''.join(bytes(o.cpu.mem_read(0x97a + 0xf5 * p + 0x34, 14 * 8)) for p in range(case['count'])) + b''.join(log)
        if __import__('os').environ.get('DEBUG_CASE') == str(all_cases.index(case)): print(out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']} | {'ARMY__03.WRK': hashlib.sha256(wrk).hexdigest()},
            'random_values': constants['random_values'], 'record_size': PRESET['record'], 'cases': all_cases, 'results': {PRESET_NAME: {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


PRESET_NAME = 'onis'


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--script', choices=sorted(PRESETS), default='onis')
    args = parser.parse_args()
    PRESET_NAME = args.script; PRESET = PRESETS[args.script]; globals().update(PRESET_NAME=PRESET_NAME, PRESET=PRESET)
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
