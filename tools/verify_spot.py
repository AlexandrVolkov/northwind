#!/usr/bin/env python3
"""Verify the search of a free place next to an object (LE:1 9064 = FS:16B, used by the mages to summon phantoms) with Unicorn. The placement check AC63 is a recorded stub (its
answer comes from the case); the tables of templates (68A0, 6885), the random stream 9E41 and the direction table DS:19C7 run as original data and code."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze, read_le_objects, INNER_MZ
from verify_combat import CombatOracle
from forestry_source import source_constants


class SpotOracle(CombatOracle):
    RANGES = {'spot': ([(0x9064, 0x90e9), (0x68a0, 0x68bd), (0x6885, 0x68a0), (0x9e41, 0x9e55)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=20000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('spot did not return')


def cases():
    rng = random.Random(0x9064)
    out = []
    for n in range(3000):
        out.append({'template': rng.choice([3, 3, 2, 5]), 'army': rng.choice([1, 3]), 'target_army': rng.choice([0, 1, 3]), 'target_template': rng.randrange(3), 'seed': rng.randrange(256),
                    'x': rng.randrange(100, 4000), 'y': rng.randrange(100, 4000), 'free': [rng.choice([0, 0, 1, 1, 1]) for _ in range(8)] if rng.random() < 0.8 else [0] * 8,
                    'new': {'w': rng.choice([1, 2]), 'h': rng.choice([1, 2]), 'layer': rng.randrange(4)}, 'old': {'w': rng.choice([1, 2, 3]), 'h': rng.choice([1, 2, 3])}})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = SpotOracle(code, report['fs_assignments'], constants)
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ)
    table = bytes(objects[1]['payload'][0x19c7:0x19d7]); o.cpu.mem_write(0x19c7, table)
    log = []; state = {}
    def reg(r): return o.cpu.reg_read(r)
    def placement():   # AC63: BL the layer, ECX/EDX the point; EAX the template and army; the answer comes from the case by call number
        k = len(state['calls']); state['calls'].append(1)
        log.append(b'P' + struct.pack('<BIIB', reg(UC_X86_REG_EBX) & 255, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX), reg(UC_X86_REG_EAX) & 255))
        ok = state['free'][k % 8]
        o.cpu.reg_write(UC_X86_REG_EFLAGS, (reg(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if ok else 0)); o.ret()
    o.stubs = {0xac63: placement}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); state.update(free=case['free'], calls=[]); o.prepare(32, 32); o.mode = 'spot'
        o.cpu.mem_write(0x19eb, bytes([case['seed']]))
        for army in range(4):
            o.cpu.mem_write(0x6ca + 0x56 * army + 4, struct.pack('<I', o.DAT + army * 0x2000))
            for index in range(8):
                raw = bytearray(0xab); raw[3] = case['old']['w']; raw[4] = case['old']['h']; o.cpu.mem_write(o.DAT + army * 0x2000 + index * 0xab, bytes(raw))
        raw = bytearray(0xab); raw[3] = case['new']['w']; raw[4] = case['new']['h']; raw[0x77] = case['new']['layer']
        o.cpu.mem_write(o.DAT + case['army'] * 0x2000 + case['template'] * 0xab, bytes(raw))
        target = bytearray(0x40); target[0] = case['target_army']; target[0x0e] = case['target_template']; struct.pack_into('<II', target, 4, case['x'], case['y'])
        if (case['target_army'], case['target_template']) == (case['army'], case['template']): target[0x0e] = (case['template'] + 1) % 8
        o.cpu.mem_write(o.POOL + 0x100, bytes(target))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EBX, 0); o.cpu.reg_write(UC_X86_REG_EAX, case['template'] | (case['army'] << 8))
        o.run(o.CODE + 0x9064)
        out = struct.pack('<IIIIB', reg(UC_X86_REG_EAX), reg(UC_X86_REG_EBX) & 0xffff, reg(UC_X86_REG_ECX), reg(UC_X86_REG_EDX), (reg(UC_X86_REG_EFLAGS) >> 6) & 1) + b''.join(log)
        if __import__('os').environ.get('DEBUG_CASE') == str(all_cases.index(case)): print(out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'direction_table': list(table), 'random_values': constants['random_values'],
            'cases': all_cases, 'results': {'spot': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
