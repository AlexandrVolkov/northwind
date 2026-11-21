#!/usr/bin/env python3
"""Execute the frame and condition tail of the generic building update, LE:1 C88C from C8E0 (the dispatch before it is the WRK entry's),
under Unicorn: the working animation (frames [base+4A, base+4A+4B) advance every second update), the stage frame that follows the
condition (division by DAT[45]+1), a ruined building (condition not above DAT[46]) losing 1 per 256 updates, and the collapse of a
dead one (removal 98CC and debris 9727 are recorded stubs). Compared: the record, the result flag and the recorded calls."""
import argparse
import hashlib
import json
from pathlib import Path
import random
import struct
from unicorn import UcError
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, unit, STRIDE
from forestry_source import source_constants

TEMPLATES = [2, 3, 4, 5, 6, 7, 9, 12, 15, 22, 23, 24, 25]


class FrameOracle(CombatOracle):
    RANGES = {'frames': ([(0xc8e0, 0xca49)], [])}

    def run(self, address):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=100000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('frames did not return')


def cases():
    rng = random.Random(0xc8e0)
    out = []
    for n in range(6000):
        t = rng.choice(TEMPLATES)
        out.append({'t': t, 'cond': rng.choice([0, 1, 5, 9, 10, 15, 16, 30, 60, 100, 127, 128, 200, 250, 251, 255, rng.randrange(256)]),
                    'max': rng.choice([0, 20, 60, 100, 127]), 'frame_off': rng.choice([-3, 0, 1, 7, 8, 9, 12, 17, 18, 19, 25, 400]),
                    'tick': rng.choice([0, 1, 2, 3, 255, rng.randrange(256)]), 'flags': rng.choice([0, 1, 0x24, 4]), 'act': rng.choice([0, 1, 2, 0x48]),
                    'rate': rng.choice([0, 1, 3, 9]), 'x': rng.randrange(4096), 'y': rng.randrange(4096), 'f17': rng.choice([0, 0x80, 0x20])})
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = FrameOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    log = []
    def effect():
        r = o.cpu.reg_read
        log.append(b'E' + struct.pack('<BHII', r(UC_X86_REG_EAX) & 0xff, r(UC_X86_REG_EBX) & 0xffff, r(UC_X86_REG_ECX), r(UC_X86_REG_EDX)))
        o.ret()
    def removal():
        log.append(b'R'); o.cpu.reg_write(UC_X86_REG_EAX, 0); o.ret()
    o.stubs = {0x9727: effect, 0x98cc: removal}
    all_cases = cases(); digest = hashlib.sha256()
    for case in all_cases:
        log.clear(); t = case['t']; tp = template(t); base = struct.unpack_from('<H', tp, 0x48)[0]
        o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT + 2 + t * STRIDE)); o.cpu.mem_write(0x19ea, bytes([case['tick']]))
        rec = bytearray(unit(t, 3, 10, 0, case['x'], case['y'], 1) + bytes(10)); rec[0] = 2; rec[0x0e] = t
        rec[0x19] = case['cond']; rec[0x1a] = case['max']; rec[0x0f] = case['flags']; rec[0x14] = case['act']; rec[0x15] = 7; rec[0x22] = case['rate']
        struct.pack_into('<H', rec, 0x17, case['f17']); struct.pack_into('<H', rec, 1, (base + tp[0x4a] + case['frame_off']) & 0xffff)
        o.cpu.mem_write(o.POOL + 0x100, bytes(rec))
        o.mode = 'frames'; o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 0x100); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        try:
            o.run(o.CODE + 0xc8e0)
            fault = b'\0'
        except UcError:
            fault = b'\1'
        out = bytes(o.cpu.mem_read(o.POOL + 0x100, 0x30)) + fault + bytes([1 if o.cpu.reg_read(UC_X86_REG_EFLAGS) & 0x40 else 0]) + b''.join(log)
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
            'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in TEMPLATES}, 'cases': all_cases,
            'results': {'frames': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
