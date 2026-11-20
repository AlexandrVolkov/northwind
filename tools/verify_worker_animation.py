#!/usr/bin/env python3
"""Execute stationary worker animation up to the native effect-dispatch boundary."""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_forestry import ForestryOracle, source_constants


class WorkerAnimationOracle(ForestryOracle):
    def guard(self, cpu, address, size, user_data):
        if self.mode == 'animation':
            if address == self.CODE + 0x9727:
                stack = cpu.reg_read(UC_X86_REG_ESP)
                target_x, target_y = struct.unpack('<II', bytes(cpu.mem_read(stack + 8, 8)))
                ebx = cpu.reg_read(UC_X86_REG_EBX)
                self.impact = struct.pack('<BIIHHBBB', 1, cpu.reg_read(UC_X86_REG_ECX), cpu.reg_read(UC_X86_REG_EDX),
                    target_x, target_y, cpu.reg_read(UC_X86_REG_EAX) & 255, ebx & 255, (ebx >> 8) & 255)
                cpu.emu_stop(); return
            allowed = [(0x9210, 0x92c9 + 1), (0x92ca, 0x92fc), (0x9342, 0x9354), (0x93ad, 0x9498)]
            if not any(self.CODE + a <= address < self.CODE + b for a, b in allowed):
                raise ValueError(f'Animation escaped scope: {address:#x}')
            return
        if self.mode == 'work' and self.CODE + 0x8560 <= address < self.CODE + 0x8585:
            return
        super().guard(cpu, address, size, user_data)

    def animation(self):
        self.mode = 'animation'; self.impact = bytes(16)
        self.cpu.mem_write(0x1972, b'\xff')  # Border transition events are outside this stationary core.
        self.start(self.CODE + 0x9210)
        if self.cpu.reg_read(UC_X86_REG_EIP) not in [self.STOP, self.CODE + 0x9727]:
            raise ValueError('Animation failed to reach return or effect boundary')
        return self.impact

    def record(self):
        return bytes(self.cpu.mem_read(self.POOL + 1, 54))


def record(state, timer, progress, heading, flags):
    raw = bytearray((i * 17 + 3) & 255 for i in range(54))
    raw[0] = 2; raw[3] = 12; raw[0x22] = 5; raw[0x23] = 254
    raw[0x24] = progress; raw[0x25] = heading; raw[0x26] = timer; raw[0x2d] = state
    struct.pack_into('<H', raw, 1, 65534); struct.pack_into('<H', raw, 0x17, flags)
    struct.pack_into('<II', raw, 4, 0, 0xfffffff0)
    return raw


def template(config):
    raw = bytearray(171)
    base, stride, count, delay, recovery, hit = config
    struct.pack_into('<H', raw, 0x48, base)
    raw[0x4a:0x4f] = bytes([stride, count, delay, recovery, hit])
    raw[0x60:0x70] = bytes([128, 127, 255, 1, 0, 0, 64, 192, 127, 128, 1, 255, 192, 64, 0, 127])
    raw[0x70] = 134
    return raw


def generate(game):
    report, code = analyze(game)
    oracle = WorkerAnimationOracle(code, report['fs_assignments'], source_constants(game))
    spec = {'states': [0, 48, 51, 3], 'progress': [0, 1, 5, 6, 7, 254, 255], 'headings': [0, 3, 255], 'flags': [0, 128],
        'configurations': [[1174, 11, 7, 1, 1, 6], [65535, 255, 7, 2, 3, 3], [0, 0, 0, 0, 0, 0],
                           [65534, 1, 255, 255, 128, 254], [17, 2, 2, 1, 0, 0]]}
    digest = hashlib.sha256(); count = 0
    for config, state, timer, progress, heading, flags in product(spec['configurations'], spec['states'], range(256), spec['progress'], spec['headings'], spec['flags']):
        oracle.worker(bytes(record(state, timer, progress, heading, flags)), bytes(template(config)))
        event = oracle.animation(); digest.update(oracle.record() + event); count += 1
    spec.update(case_count=count, sha256=digest.hexdigest())
    print('Animation', count, flush=True)
    # 93DF: a swing request at an object target (+22 = FFh) whose +1C was cleared (target dead, 42BB tail) returns to 9275.
    absent = {'states': [48], 'timers': [0, 1, 2, 255], 'case_count': 0}
    digest = hashlib.sha256()
    for config, state, timer, progress, heading, flags in product(spec['configurations'], absent['states'], absent['timers'], spec['progress'], spec['headings'], spec['flags']):
        raw = record(state, timer, progress, heading, flags); raw[0x22] = 255; raw[0x1c:0x20] = bytes(4)
        oracle.worker(bytes(raw), bytes(template(config)))
        event = oracle.animation(); digest.update(oracle.record() + event); absent['case_count'] += 1
    absent['sha256'] = digest.hexdigest()
    source = (game / 'ARMY__02.DAT').read_bytes()[2 + 10 * 171:2 + 11 * 171]
    raw = record(0, 0, 0, 2, 0); raw[0x23] = 3; raw[0x32] = 5; raw[0x33] = 3
    raw[0x15] = 2; raw[0x16] = 0; raw[0x2f] = 0; raw[0x34] = source[0x78]
    struct.pack_into('<II', raw, 4, 4 * 32 + 16, 3 * 32 + 16)
    oracle.worker(bytes(raw), source); oracle.terrain(8, 8, [0] * 64, struct.pack('<II', 1, 0))
    digest = hashlib.sha256(); impacts = []; preview = []; completed = False
    for tick in range(2000):
        event = oracle.animation()
        if event[0]: impacts.append(tick)
        state = oracle.record()[0x2d] & 15
        oracle.mode = 'work'; oracle.boundary = None; oracle.swing = False
        oracle.cpu.reg_write(UC_X86_REG_EDX, state); oracle.start(oracle.CODE + 0x8560)
        if oracle.boundary not in [0x84e4, 0x85cc]: raise ValueError('Worker sequence escaped boundary')
        after = oracle.record(); completed = oracle.boundary == 0x85cc
        digest.update(after + event + bytes([completed]))
        if tick < 26: preview.append([tick, struct.unpack_from('<H', after, 1)[0], after[0x2d], after[0x34], after[0x2f]])
        if completed: break
    if not completed: raise ValueError('Worker did not finish a load')
    return {'format_version': 1, 'source_sha256': {name: report['source_sha256'][name] for name in ['NORWIND.EXE', 'ARMY__02.DAT', 'ARMY__02.WRK']},
            'step': spec, 'absent_target': absent, 'sequence': {'template_sha256': hashlib.sha256(source).hexdigest(), 'ticks': tick + 1,
                'impact_ticks': impacts, 'preview_columns': ['tick', 'frame', 'animation', 'work', 'gate'], 'preview': preview,
                'sha256': digest.hexdigest()}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True); parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args(); args.fixture.write_text(json.dumps(generate(args.game), indent=2) + '\n', encoding='utf-8')
