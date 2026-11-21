#!/usr/bin/env python3
"""Execute the fog-of-war reveal LE:1 813E (square of radius DAT[47] (+1 from experience 28h) around an object, owner bit
cleared in the map [18DA]) under Unicorn, with real 6885 (template of the object). Edge and wrap cases included."""
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
FOG = 0x7000


class FogOracle(CombatOracle):
    RANGES = {'fog': ([(0x813e, 0x81d9), (0x6885, 0x68a0)], [])}

    def run(self, address):
        # Objects stay on the map (outside it the original writes into whatever follows the map, which is not modelled).
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=3000000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('fog did not return')


def cases():
    rng = random.Random(813)
    out = []
    for n in range(4000):
        w, h = rng.choice([(32, 32), (24, 40), (40, 24), (16, 16), (33, 31)])
        edge = rng.random() < 0.4
        cx = rng.choice([0, 1, 2, w - 1, w - 2, w - 3, rng.randrange(w)]) if edge else rng.randrange(w)
        cy = rng.choice([0, 1, 2, h - 1, h - 2, h - 3, rng.randrange(h)]) if edge else rng.randrange(h)
        out.append({'w': w, 'h': h, 'x': cx * 32 + rng.randrange(32), 'y': cy * 32 + rng.randrange(32), 'owner': rng.choice([0, 1, 2, 3, 4, 7, 7, 8, 9, 15]),
                    't': rng.choice(TEMPLATES), 'experience': rng.choice([0, 0, 0x27, 0x28, 0x7f, 0xff]), 'seed': rng.randrange(1 << 16)})
    return out


def fog_bytes(case):
    return bytes(((c * 59 + case['seed']) ^ (c >> 1) ^ (case['seed'] >> 8)) & 255 | (0xff if c % 7 == 0 else 0) for c in range(case['w'] * case['h']))


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = FogOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    all_cases = cases(); digest = hashlib.sha256()
    for case in all_cases:
        o.prepare(case['w'], case['h'])
        o.cpu.mem_write(0x18de, struct.pack('<II', case['w'], case['h']))
        o.cpu.mem_write(0x18da, struct.pack('<I', FOG)); o.cpu.mem_write(FOG, bytes([0xaa]) * 0x2000); o.cpu.mem_write(FOG, fog_bytes(case))
        rec = unit(case['t'], case['owner'], 10, 0, case['x'], case['y'], 1) + bytes(10)
        rec[0x16] = case['experience']; o.cpu.mem_write(o.POOL + 0x100, bytes(rec))
        o.mode = 'fog'; o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.run(o.CODE + 0x813e)
        out = bytes(o.cpu.mem_read(FOG, case['w'] * case['h']))
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'reveal': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
