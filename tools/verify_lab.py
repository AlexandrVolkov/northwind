#!/usr/bin/env python3
"""Execute the research executor of the laboratory, LE:1 CCCB (FS 81), under Unicorn. With no research running it takes a random
item of the researchable list (A716, 9E41: recorded stubs); every 256th update it pays rate · 10 gold (participant +20) and
reduces the remaining cost (+24); at zero it completes the item (A6AE, recorded) and lowers the settlement's counter +73.
Compared: the record bytes +15, +22..+28, +73, the participant's resources, the statistics block and the recorded calls."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, unit
from forestry_source import source_constants

STATS = 0x8000


class LabOracle(CombatOracle):
    RANGES = {'lab': ([(0xcccb, 0xcd72)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=100000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('lab did not return')


def cases():
    rng = random.Random(0xccc8)
    out = []
    for n in range(4000):
        items = [{'id': rng.randrange(0, 30), 'cost': rng.choice([0, 300, 500, 1000, 100000])} for _ in range(rng.choice([0, 1, 2, 3, 5]))]
        out.append({'owner': rng.choice([0, 1, 2, 3]), 'running': rng.random() < 0.6, 'rate': [rng.choice([1, 5, 5, 20, 255]), rng.choice([1, 5, 5, 20, 255])],
                    'remaining': rng.choice([0, 1, 100, 250, 500, 1000]), 'item': rng.randrange(30), 'gold': rng.choice([0, 100, 250, 500, 100000]),
                    'tick': rng.choice([0, 0, 0, 1, 255]), 'items': items, 'random': rng.randrange(256), 'counter': rng.choice([0, 1, 5, 0x271, 0x300, 100000]),
                    'counter_lab': rng.choice([0, 5, 700])})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = LabOracle(code, report['fs_assignments'], constants)
    log = []; state = {'items': [], 'random': 0}
    def flag_ret(**r): o.ret(**r)
    def research():
        items = state['items']; log.append(b'P' + struct.pack('<HH', o.cpu.reg_read(UC_X86_REG_EAX) & 0xffff, len(items)))
        for k, item in enumerate(items):
            o.cpu.mem_write(0x1f45 + 16 * k, bytes(16)); o.cpu.mem_write(0x1f45 + 16 * k + 1, bytes([item['id']])); o.cpu.mem_write(0x1f45 + 16 * k + 7, struct.pack('<I', item['cost']))
        o.cpu.reg_write(UC_X86_REG_EAX, len(items)); o.ret()
    def rnd():
        o.cpu.reg_write(UC_X86_REG_EAX, (o.cpu.reg_read(UC_X86_REG_EAX) & 0xffffff00) | state['random']); o.ret()
    def tree():
        log.append(b'T' + struct.pack('<H', o.cpu.reg_read(UC_X86_REG_EAX) & 0xffff)); o.ret()
    o.stubs = {0xa716: research, 0x9e41: rnd, 0xa6ae: tree}
    all_cases = cases(); digest = hashlib.sha256()
    for case in all_cases:
        log.clear(); state['items'] = case['items']; state['random'] = case['random']
        o.prepare(8, 8)
        owner = case['owner']; base = 0x97a + 0xf5 * owner
        o.cpu.mem_write(base, bytes(0xf5)); o.cpu.mem_write(base + 0x20, struct.pack('<I', case['gold']))
        o.cpu.mem_write(0x6ba, struct.pack('<I', STATS)); o.cpu.mem_write(STATS, bytes(0x1a35 * 4))
        o.cpu.mem_write(STATS + 0x1a35 * owner + 0x73, struct.pack('<I', case['counter']))
        rec = bytearray(unit(0, owner, 10, 0, 100, 100, 1) + bytes(10)); rec[0] = 2; rec[3] = owner; rec[0x15] = 1 if case['running'] else 0
        rec[0x22], rec[0x23] = case['rate']; rec[0x28] = case['item']; struct.pack_into('<I', rec, 0x24, case['remaining']); struct.pack_into('<I', rec, 0x73 - 0x40 + 0x40, 0) if False else None
        rec += bytes(0x80)
        struct.pack_into('<I', rec, 0x73, case['counter_lab'])
        o.cpu.mem_write(o.POOL + 0x100, bytes(rec)); o.cpu.mem_write(0x19ea, bytes([case['tick']]))
        o.mode = 'lab'; o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.CODE + 0xcccb)
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x80)) + bytes(o.cpu.mem_read(base + 0x18, 0x10)) + bytes(o.cpu.mem_read(STATS + 0x1a35 * owner + 0x70, 8)) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases,
            'results': {'lab': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
