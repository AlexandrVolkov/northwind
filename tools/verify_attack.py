#!/usr/bin/env python3
"""Execute the original melee attack helpers under Unicorn; store hashed results.

  range   LE:1 8B5E: attack class DAT[71] vs defence, plane mask DAT[74], footprint gap 9B86,
          water gap limit through D157, maximum DAT[72] and minimum DAT[73] distance in cells
  gate    LE:1 8AF4: swing delay OBE[2F] against DAT[75] - hi8(DAT[75] * experience)
  reward  LE:1 8ABF: dead target check against DAT[46] and experience reward DAT[76] / 4
"""
import argparse
import hashlib
from itertools import product
import json
from pathlib import Path
import struct
from unicorn.x86_const import *
from analyze_executable import analyze
from verify_combat import CombatOracle, unit, STRIDE
from forestry_source import source_constants


class AttackOracle(CombatOracle):
    RANGES = {
        'range': ([(0x8b5e, 0x8bc7), (0x9b86, 0x9bf9), (0xd157, 0xd182), (0x68bd, 0x68d8), (0x6885, 0x68a0)], []),
        'gate': ([(0x8af4, 0x8b0f)], []),
        'reward': ([(0x8abf, 0x8af4), (0x68bd, 0x68d8), (0x6885, 0x68a0)], []),
        'swing': ([(0x93ad, 0x9429)], []),
    }


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = AttackOracle(code, report['fs_assignments'], constants)
    dat = (game / 'ARMY__02.DAT').read_bytes(); o.cpu.mem_write(o.DAT, dat)
    o.cpu.mem_write(0x6ca + 2 * 0x56 + 4, struct.pack('<I', o.DAT + 2))
    template = lambda i: dat[2 + i * STRIDE:2 + (i + 1) * STRIDE]
    fixture = {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE', 'ARMY__02.DAT']},
               'templates_sha256': {str(i): hashlib.sha256(template(i)).hexdigest() for i in [0, 1, 2]}, 'results': {}}

    def save(name, digest, count):
        fixture['results'][name] = {'case_count': count, 'sha256': digest.hexdigest()}; print(name, count, flush=True)

    def put(index, **fields):
        base = o.DAT + 2 + index * STRIDE; o.cpu.mem_write(base, template(index))
        for key, value in fields.items():
            o.cpu.mem_write(base + int(key[1:], 16), value if isinstance(value, bytes) else bytes([value & 255]))

    o.prepare(16, 16); o.terrain(16, 16, [1 if (i % 16) >= 12 else 0 for i in range(256)])
    o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT + 2))   # attacker = template 0
    spec = {'attacker': [0, 1], 'targets': [0, 2], 'pierce': [0, 4, 5, 255], 'maximum': [0, 1, 7, 255], 'minimum': [0, 1, 2],
            'masks': [0, 2, 0x0f], 'layers': [0, 1, 2, 3], 'offsets': [[32, 0], [64, 0], [96, 32], [200, 0], [0, 230], [320, 0], [16, 16]],
            'water': [0, 1]}
    fixture['range'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'range'
    for attacker, target, pierce, maximum, minimum, mask, layer, (dx, dy), water in product(*(spec[k] for k in [
            'attacker', 'targets', 'pierce', 'maximum', 'minimum', 'masks', 'layers', 'offsets', 'water'])):
        put(0, o71=pierce, o72=maximum, o73=minimum, o74=bytes([mask, 0, 0, 0]))
        raw = unit(0, 0, 10, 0, 48, 80, 1); struct.pack_into('<I', raw, 0x1c, 0x100); o.cpu.mem_write(o.POOL + 1, bytes(raw))
        tx = 48 + dx + (water * 352 if dx < 200 else 0)
        foe = unit(target, 3, 10, 0, tx, 80 + dy, layer); o.cpu.mem_write(o.POOL + 0x100, bytes(foe))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.run(o.CODE + 0x8b5e)
        digest.update(bytes([o.cpu.reg_read(UC_X86_REG_EBX) & 255, o.zf()])); count += 1
    put(0); save('range', digest, count)

    spec = {'delays': [0, 1, 4, 15, 255], 'experience': [0, 1, 63, 64, 127, 128, 255], 'counters': [0, 1, 2, 3, 4, 5, 14, 15, 254, 255]}
    fixture['gate'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'gate'
    for delay, experience, counter in product(*(spec[k] for k in ['delays', 'experience', 'counters'])):
        put(0, o75=delay); raw = unit(); raw[0x16] = experience; raw[0x2f] = counter; o.cpu.mem_write(o.POOL + 1, bytes(raw))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.run(o.CODE + 0x8af4)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 54)) + bytes([o.zf()])); count += 1
    put(0); save('gate', digest, count)

    spec = {'conditions': [128, 255, 0, 1, 9, 10, 11, 127], 'thresholds': [0, 10, 128], 'experience': [0, 1, 63, 64, 100, 127, 200],
            'rewards': [0, 3, 4, 32, 96, 255], 'present': [0, 1]}
    fixture['reward'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'reward'
    for condition, threshold, experience, reward, present in product(*(spec[k] for k in ['conditions', 'thresholds', 'experience', 'rewards', 'present'])):
        put(2, o46=threshold, o76=reward)
        raw = unit(); raw[0x16] = experience; struct.pack_into('<I', raw, 0x1c, 0x100 if present else 0); o.cpu.mem_write(o.POOL + 1, bytes(raw))
        o.cpu.mem_write(o.POOL + 0x100, bytes(unit(2, 3, condition)))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.run(o.CODE + 0x8abf)
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 54)) + bytes([o.zf()])); count += 1
    put(2); save('reward', digest, count)

    # 93AD..9424 swing start with an object target (OBE[22] = FF); stops at the common exit 934E.
    o.boundary = 0x934e; o.cpu.mem_write(0x1c49, struct.pack('<I', o.DAT + 2))
    spec = {'headings': list(range(0, 256, 5)), 'positions': [[0, 0], [96, 80], [70000, 65535], [4294967295, 131071]], 'layers': [0, 1, 2, 7]}
    fixture['swing'] = spec; digest = hashlib.sha256(); count = 0; o.mode = 'swing'
    for heading, (x, y), layer in product(*(spec[k] for k in ['headings', 'positions', 'layers'])):
        raw = unit(); raw[0x22] = 0xff; raw[0x25] = heading; raw[0x24] = 0; raw[0x2d] = 0x30  # state after 9275 accepted request 3
        struct.pack_into('<I', raw, 0x1c, 0x100); o.cpu.mem_write(o.POOL + 1, bytes(raw))
        o.cpu.mem_write(o.POOL + 0x100, bytes(unit(2, 3, 10, 0, x, y, layer)))
        o.cpu.reg_write(UC_X86_REG_ESI, o.POOL + 1); o.cpu.reg_write(UC_X86_REG_EBP, o.DAT + 2); o.run(o.CODE + 0x93ad)
        if not o.stopped: raise ValueError('Swing start did not reach 934E')
        digest.update(bytes(o.cpu.mem_read(o.POOL + 1, 54))); count += 1
    o.boundary = None; save('swing', digest, count)
    return fixture


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game), indent=2) + '\n', encoding='utf-8')
