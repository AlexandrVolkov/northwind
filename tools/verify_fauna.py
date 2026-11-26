#!/usr/bin/env python3
"""Verify the scripts of ARMY__05.WRK (bird 0, bird generator 70, wolf EA, bear 1A8, wolf 2A2, remains 313) with Unicorn. The services — the random stream FS:109, FS:1D (redraw), FS:129 (A444),
FS:DD / FS:E1 (the unit step: the check stops there), FS:17B (D895, enemy search), FS:1B3, FS:16B, FS:45, FS:19B, FS:A1 and the tail calls FS:E9 / ED / F1 — are recorded stubs;
FS:6D (6877) and FS:71 (68BD) run as original code."""
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

SCRIPTS = {   # name: (entry, handler of the state 0 or None)
    'bird': (0x0, None), 'generator': (0x70, None), 'wolf': (0xea, 0x13f), 'bear': (0x1a8, 0x239), 'wolf2': (0x2a2, 0x2f7), 'remains': (0x313, None),
}


class FaunaOracle(CombatOracle):
    RANGES = {'fauna': ([(0x6877, 0x68a0), (0x68bd, 0x68d8)], [(0, 0x372)])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=100000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('fauna did not return')


def cases():
    rng = random.Random(0x505)
    out = []
    for n in range(3000):
        script = rng.choice(list(SCRIPTS))
        count = 4
        out.append({'script': script, 'handler': SCRIPTS[script][1] is not None and rng.random() < 0.5, 'ah': rng.choice([0, 1, 1, 2]), 'rec': bytes(rng.randrange(256) for _ in range(0x50)).hex(),
                    'owner': rng.randrange(count), 'army': rng.randrange(3), 'health': rng.choice([0, 0, 1, 100, 0x80]), 'flags14': rng.choice([0, 0x80, 0x81, 0x50, 0x52]),
                    'counter2e': rng.choice([0, 8, 16, 1, 7, rng.randrange(256)]), 'tick': rng.choice([0, 0, 0x100 - 1, 1, 0x20]), 'random': rng.choice([0, 1, 7, 0xff, rng.randrange(1 << 32)]),
                    'masks': [rng.choice([0, 0xffffffff, rng.randrange(1 << 32)]) for _ in range(count)], 'found': rng.choice([0, 1]), 'target': rng.randrange(4), 'flags40': rng.choice([0, 8, 0x28, 2, 0x22]),
                    'free': rng.choice([0, 1]), 'create': rng.choice([0, 1]), 'children': [rng.choice([0, 1, 1, 0, 2, 5]) for _ in range(rng.choice([0, 1, 4, 8, 12, 20]))],
                    'lookup': rng.choice([0, 1]), 'al': rng.choice([0, 1, 2, 3]), 'dir': rng.randrange(8), 'al_in': rng.choice([0, 2, 5]), 'c18': rng.choice([0, 1, 2, 0x258, rng.randrange(1 << 32)]), 'c17': rng.choice([1, 2, 4, 0, rng.randrange(256)]),
                    'src': bytes(rng.randrange(256) for _ in range(0x30)).hex()})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = FaunaOracle(code, report['fs_assignments'], constants)
    wrk = (game / 'ARMY__05.WRK').read_bytes(); o.cpu.mem_write(o.WRK, wrk)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def zf(on): o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if on else 0))
    def rand_a(): o.cpu.reg_write(UC_X86_REG_EAX, state['random']); o.ret()
    def redraw(): log.append(b'D'); o.ret()
    def wounded(): log.append(b'W' + struct.pack('<II', reg(UC_X86_REG_EAX), reg(UC_X86_REG_EBX))); o.ret()
    def vitals(): log.append(b'V'); o.ret()
    def unit_step(): log.append(b'U'); o.cpu.emu_stop(); o.stopped = True
    def tail(k):
        def f(): log.append(b'J' + bytes([k])); o.cpu.emu_stop(); o.stopped = True
        return f
    def scan():
        log.append(b'F' + bytes([reg(UC_X86_REG_EAX) & 255]) + struct.pack('<I', struct.unpack('<I', bytes(o.cpu.mem_read(state['part_ecx'] + 0x12, 4)))[0]))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x40 * state['target']); zf(state['found']); o.ret()
    def sound():
        log.append(b'B' + struct.pack('<II', reg(UC_X86_REG_EAX), reg(UC_X86_REG_ESI) - o.POOL)); o.ret()
    def spot():
        log.append(b'P' + struct.pack('<BB', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255))
        o.cpu.reg_write(UC_X86_REG_EAX, state['al']); o.cpu.reg_write(UC_X86_REG_EBX, state['dir']); zf(state['free']); o.ret()
    def create():
        log.append(b'C' + struct.pack('<BBBB', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255, reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255)
                   + struct.pack('<II', reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX)))
        o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x600); zf(state['create']); o.ret()
    def occupy(): log.append(b'O' + struct.pack('<I', reg(UC_X86_REG_EAX))); o.ret()
    def lookup():
        log.append(b'L' + struct.pack('<I', reg(UC_X86_REG_ESI) - o.POOL)); o.cpu.reg_write(UC_X86_REG_EDX, o.DAT + 0x2000 * 3 + 0xab * 0); zf(state['lookup']); o.ret()
    o.stubs = {0x9e41: rand_a, 0x81d9: redraw, 0xa444: wounded, 0x90e9: vitals, 0x8227: unit_step, 0xd895: scan, 0xa6ae: sound, 0x9064: spot, 0xa758: create, 0x43e5: occupy, 0x982b: lookup,
               0x8273: tail(1), 0x82c3: tail(2), 0x839f: tail(3)}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state.update(random=case['random'], found=case['found'], target=case['target'], free=case['free'], create=case['create'], lookup=case['lookup'], al=case['al'], dir=case['dir'])
        o.prepare(32, 32); o.mode = 'fauna'; o.boundary = None
        o.cpu.mem_write(0x6ba, struct.pack('<I', o.PARTICIPANTS)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x6c6, bytes([4]))
        o.cpu.mem_write(o.FS + 8, struct.pack('<I', o.POOL)); o.cpu.mem_write(o.FS + 0x10, bytes([case['tick'] & 255]))
        o.cpu.mem_write(o.POOL, bytes(0x2000))
        for army in range(4):
            o.cpu.mem_write(0x6ca + 0x56 * army + 4, struct.pack('<I', o.DAT + army * 0x2000))
            for index in range(0x20):
                raw = bytearray(0xab); raw[0x46] = 10; struct.pack_into('<I', raw, 0x40, case['flags40']); o.cpu.mem_write(o.DAT + army * 0x2000 + index * 0xab, bytes(raw))
        rec = bytearray(bytes.fromhex(case['rec'])); rec[0] = case['army']; rec[3] = case['owner']; rec[0x0e] = 0; rec[0x19] = case['health']; rec[0x14] = case['flags14']; rec[0x2e] = case['counter2e']
        if case['script'] == 'remains': struct.pack_into('<I', rec, 0x18, case['c18']); rec[0x17] = case['c17']
        o.cpu.mem_write(o.POOL + 0x200, bytes.fromhex(case['src']))
        children = case['children']; base = 0x1000
        for k, t in enumerate(children):
            raw = bytearray(0x40); raw[0x0e] = t; struct.pack_into('<I', raw, 0x10, 0x40); o.cpu.mem_write(o.POOL + base + 0x40 * k, bytes(raw))
        for p in range(4):
            part = bytearray(0xf5); struct.pack_into('<I', part, 0x12, case['masks'][p]); struct.pack_into('<I', part, 0x0e, base); struct.pack_into('<I', part, 0x09, base + 0x40 * len(children))
            o.cpu.mem_write(0x97a + 0xf5 * p, bytes(part))
        state['part_ecx'] = 0x97a + 0xf5 * case['owner']
        o.cpu.mem_write(o.POOL + 0x100, bytes(rec)); o.cpu.mem_write(o.POOL + 0x40 * 0 + 0x300, bytes(0x40))
        for k in range(4): o.cpu.mem_write(o.POOL + 0x40 * k, bytes([k]) + bytes(0x3f)) if False else None
        entry, handler = SCRIPTS[case['script']]
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EAX, case['ah'] << 8 | case['al_in']); o.cpu.reg_write(UC_X86_REG_EBX, o.POOL + 0x200)
        o.run(o.WRK + (handler if case['handler'] else entry))
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x50)) + bytes(o.cpu.mem_read(o.POOL + 0x600, 0x40)) + b''.join(bytes(o.cpu.mem_read(0x97a + 0xf5 * p + 0x12, 4)) for p in range(4)) + b''.join(log)
        if __import__('os').environ.get('DEBUG_CASE') == str(all_cases.index(case)): print(out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']} | {'ARMY__05.WRK': hashlib.sha256(wrk).hexdigest()}, 'cases': all_cases,
            'results': {'fauna': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
