#!/usr/bin/env python3
"""Execute the camp of the horde (ARMY__00 «Лагерь», LE:1 11943..11B4D, FS 131) under Unicorn: the waiting state founds a castle-class
building after a random timer (22E46 template, 10AD2 spot, A758 object), loads the AI character 4 (1E740, real, with 1EB33) and sets the strength
7D0h; the founded state feeds the participant every 1000h updates and returns to waiting when the settlement is gone; the alert spawn (A758,
103EC, 98DB) is recorded. Compared: the camp record, the participant, the AI block of the owner and the recorded calls."""
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
from ai_source import extract

BLOCKS = 0x420000
SCRATCH = 0x300000


class CampOracle(CombatOracle):
    RANGES = {'camp': ([(0x11943, 0x11b4e), (0x1e740, 0x1e7ea), (0x1eb33, 0x1eb40), (0x6877, 0x68a0), (0x9e41, 0x9e55)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=300000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('camp did not return')


def cases():
    rng = random.Random(0x11943)
    out = []
    for n in range(2000):
        out.append({'owner': rng.choice([2, 4, 5, 7]), 'state': rng.choice([0, 0, 1, 1, 2]),
                    'timer': rng.choice([0, 1, 0x2710, 0xffffffff, 0x7fffffff, 0x80000000, 0xfff, 0x1fff, 0xffe, rng.randrange(1 << 32), rng.randrange(1 << 13)]),
                    'castles': rng.choice([0, 0, 1, 2]), 'alert': rng.choice([0, 1, 3]), 'strength': [rng.choice([0, 0, 5, 2000]) for _ in range(3)],
                    'pick': rng.choice([True, True, True, False]), 'spot': [rng.randrange(0, 4096), rng.randrange(0, 4096), rng.choice([True, True, False])],
                    'create': rng.choice([True, True, True, False]), 'template': rng.choice([18, 2, 14]), 'army': rng.choice([1, 1, 2]), 'index': rng.randrange(256),
                    'resources': [rng.randrange(1 << 20) for _ in range(4)], 'cursor': [rng.randrange(256), rng.randrange(256)], 'size': [rng.choice([32, 64, 128]), rng.choice([32, 64, 128])],
                    'x': rng.randrange(4096), 'y': rng.randrange(4096), 'variant_seed': rng.randrange(4)})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = CampOracle(code, report['fs_assignments'], constants)
    _, objects = read_le_objects((game / 'NORWIND.EXE').read_bytes(), INNER_MZ); o.cpu.mem_write(0x19c7, objects[1]['payload'][0x19c7:0x19d7])
    dat1 = (game / 'ARMY__01.DAT').read_bytes(); dat2 = (game / 'ARMY__02.DAT').read_bytes()
    o.cpu.mem_write(o.DAT, dat2); o.cpu.mem_write(o.DAT + 0x2000, dat1)
    template = lambda army, i: (dat1 if army == 1 else dat2)[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    table = b''.join(bytes.fromhex(c['raw_hex']) for c in extract(game)['characters']); o.cpu.mem_write(0xed43, table)
    o.cpu.mem_map(SCRATCH, 0x10000) if False else None
    log = []; state = {}
    def flag(z): o.cpu.reg_write(UC_X86_REG_EFLAGS, (o.cpu.reg_read(UC_X86_REG_EFLAGS) & ~0x40) | (0x40 if z else 0))
    def pick():
        r = o.cpu.reg_read; log.append(b'P' + struct.pack('<BII', r(UC_X86_REG_EAX) & 0xff, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX)))
        base = o.DAT + 2 + state['template'] * STRIDE + (0x2000 if state["army"] == 1 else 0)
        o.cpu.reg_write(UC_X86_REG_EAX, state['template'] | state['army'] << 8); o.cpu.reg_write(UC_X86_REG_EDX, base); flag(not state['pick']); o.ret()
    def spot():
        r = o.cpu.reg_read; log.append(b'S' + struct.pack('<HBII', r(UC_X86_REG_EAX) & 0xffff, r(UC_X86_REG_EBX) & 0xff, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX)))
        o.cpu.reg_write(UC_X86_REG_ECX, state['spot'][0]); o.cpu.reg_write(UC_X86_REG_EDX, state['spot'][1]); flag(state['spot'][2]); o.ret()
    def create():
        r = o.cpu.reg_read
        log.append(b'A' + struct.pack('<BBBBII', r(UC_X86_REG_EAX) & 0xff, (r(UC_X86_REG_EAX) >> 8) & 0xff, r(UC_X86_REG_EBX) & 0xff, (r(UC_X86_REG_EBX) >> 8) & 0xff, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX)))
        record = o.POOL + 0x4000 + 0x40 * state['made']; state['made'] += 1
        o.cpu.mem_write(record, bytes(0x40)); o.cpu.reg_write(UC_X86_REG_EDI, record); flag(state['create']); o.ret()
    def register():
        log.append(b'R' + struct.pack('<I', o.cpu.reg_read(UC_X86_REG_EAX) - 0)); o.ret()
    def place():
        r = o.cpu.reg_read; esi = r(UC_X86_REG_ESI)
        log.append(b'B' + struct.pack('<BB', r(UC_X86_REG_ECX) & 0xff, (r(UC_X86_REG_ECX) >> 8) & 0xff) + bytes(o.cpu.mem_read(esi + 0x15, 1)) + bytes(o.cpu.mem_read(esi + 0x22, 1)) + bytes(o.cpu.mem_read(esi + 0x27, 1))); o.ret()
    def retire():
        log.append(b'D'); o.ret()
    o.stubs = {0x22e46: pick, 0x10ad2: spot, 0xa758: create, 0x43e5: register, 0x103ec: place, 0x98db: retire}
    all_cases = cases(); digest = hashlib.sha256()
    for case in all_cases:
        log.clear(); state.update(template=case['template'], army=case['army'], pick=case['pick'], spot=case['spot'], create=case['create'], made=0)
        o.prepare(32, 32)
        o.cpu.mem_write(0x6ba, struct.pack('<I', BLOCKS)); o.cpu.mem_write(0x6ca + 1 * 0x56 + 4, struct.pack('<I', o.DAT + 0x2000 + 2)); o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
        o.cpu.mem_write(0x1cc5, bytes([case['size'][0], case['size'][1], case['cursor'][0], case['cursor'][1]]))
        o.cpu.mem_write(0x19eb, bytes([case['index']]))
        o.cpu.mem_write(BLOCKS, bytes(0x1a35 * 8))
        owner = case['owner']
        for p in range(8):
            o.cpu.mem_write(BLOCKS + 0x1a35 * p, bytes([p]))
        o.cpu.mem_write(BLOCKS + 0x1a35 * owner + 0x5c, bytes([case['castles']]))
        for p in range(3): o.cpu.mem_write(BLOCKS + 0x1a35 * p + 0x19e9, struct.pack('<I', case['strength'][p]))
        o.cpu.mem_write(0x97a + 0xf5 * owner, bytes(0xf5)); o.cpu.mem_write(0x97a + 0xf5 * owner + 0x18, struct.pack('<4I', *case['resources']))
        camp = bytearray(unit(0, owner, 100, 0, case['x'], case['y'], 1) + bytes(10)); camp[0] = 0; camp[3] = owner; camp[0x0e] = 0
        camp[0x15] = case['state']; struct.pack_into('<I', camp, 0x23, case['timer']); camp[0x27] = case['alert']
        o.cpu.mem_write(o.POOL + 0x100, bytes(camp))
        o.mode = 'camp'; o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.run(o.CODE + 0x11943)
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x30)) + bytes(o.cpu.mem_read(0x97a + 0xf5 * owner + 0x16, 0x10)) + bytes(o.cpu.mem_read(BLOCKS + 0x1a35 * owner, 0x1a35)) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']}, 'cases': all_cases,
            'results': {'camp': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
