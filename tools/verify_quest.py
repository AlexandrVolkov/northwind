#!/usr/bin/env python3
"""Verify the message handlers of the dialogs (LE:1 F072..F5CE: alliance, peace, vassalage, the exchanges of hermits' items) with Unicorn. The sending of a message EDD5,
the pickup 1243A and the reaction EF3F are recorded stubs; the rest (the queue cleaning EEFA, 21490, 23A6E, 23B8E, 124A5, 68BD) is original code. The handler is called as EDD5
does: ESI = the type entry, EDI = the queue record, EDX = the type, BL/BH = the participants, the type is on the stack at [ESP+4] (F28B, F29C, F2AD change it)."""
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

HANDLERS = [0xf072, 0xf0f3, 0xf12b, 0xf14b, 0xf28b, 0xf29c, 0xf2ad, 0xf2be, 0xf2de, 0xf2fe, 0xf31e, 0xf33e, 0xf370, 0xf395, 0xf3ad, 0xf3d2, 0xf3ea, 0xf40f, 0xf427,
            0xf4bc, 0xf4dd, 0xf4fe, 0xf51f, 0xf53b, 0xf55c, 0xf58e, 0xf599, 0xf5a4, 0xf5af]
WORDS = {0x423c: [0xff01, 0x0101, 0], 0x42d2: [0xff09, 0x0909], 0x4304: [0xff07, 0x0707], 0x439a: [0x606, 0x602, 0x600]}


class QuestOracle(CombatOracle):
    RANGES = {'quest': ([(0xf072, 0xf5ce), (0x124a5, 0x124d8), (0x21490, 0x214c3), (0x23a6e, 0x23a95), (0x23b8e, 0x23c11), (0xeefa, 0xef3f), (0x68bd, 0x68d8), (0x1ead8, 0x1eaed)], [])}

    def run(self, address, esp_extra=0):
        self.stopped = False
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00); self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(address, self.STOP, count=200000)
        if not self.stopped and self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP: raise ValueError('quest did not return')


def cases():
    rng = random.Random(0xf12b)
    out = []
    for n in range(1500):
        handler = rng.choice(HANDLERS)
        flagmap = {(a, t): rng.choice([0, 8, 8, 0x20, 0]) for a in range(2) for t in range(4)}   # the flags belong to the template
        owner, other = rng.sample([0, 1, 2], 2)
        out.append({'handler': handler, 'owner': owner, 'other': other, 'type': rng.randrange(1, 153), 'parameter': rng.randrange(1 << 16),
                    'inventory': [[rng.choice([0, 0, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 19, 20, 31, 1]) for _ in range(20)] for _ in range(3)],
                    'wallet': [[rng.randrange(1 << 12) for _ in range(6)] for _ in range(3)], 'mask': [rng.randrange(1 << 8) for _ in range(3)],
                    'relations': [[rng.choice([0, 1, 2, 3, 4, 5]) for _ in range(8)] for _ in range(3)],
                    'queue': [[[rng.choice([0xff, 0xff, 3, 5, 17]), rng.randrange(3)] for _ in range(8)] for _ in range(3)],
                    'stat5': [rng.choice([0, 0, 1]) for _ in range(3)], 'dead': [rng.choice([0, 0, 0, 1]) for _ in range(3)],
                    'words': {str(a): rng.choice(v) for a, v in WORDS.items()},
                    'objects': [{'owner': rng.randrange(3), 'army': rng.randrange(2), 'template': rng.randrange(4), 'condition': rng.choice([0, 5, 50, 100]), 'target': rng.choice([0, 0, 0x100, 0x140, 0x180]),
                                 } for _ in range(rng.choice([0, 2, 5, 8]))]})
        for g in out[-1]['objects']: g['flags'] = flagmap[(g['army'], g['template'])]
    return out


def generate(game):
    constants = source_constants(game); report, code = analyze(game)
    o = QuestOracle(code, report['fs_assignments'], constants)
    log = []
    def reg(r): return o.cpu.reg_read(r)
    def send():   # EDD5: al the type, ah the other, bl the receiver, ebp the parameter; returns AL = 0
        log.append(b'S' + struct.pack('<BBBBI', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255, reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255, reg(UC_X86_REG_EBP)))
        o.cpu.reg_write(UC_X86_REG_EAX, reg(UC_X86_REG_EAX) & ~0xff); o.ret()
    def give():   # 1243A: al the item, ah the owner
        log.append(b'G' + struct.pack('<BB', reg(UC_X86_REG_EAX) & 255, (reg(UC_X86_REG_EAX) >> 8) & 255)); o.ret()
    def react():  # EF3F with EBP the parameter, BL/BH the participants
        log.append(b'R' + struct.pack('<BBI', reg(UC_X86_REG_EBX) & 255, (reg(UC_X86_REG_EBX) >> 8) & 255, reg(UC_X86_REG_EBP))); o.ret()
    o.stubs = {0xedd5: send, 0x1243a: give, 0xef3f: react}
    digest = hashlib.sha256(); all_cases = cases()
    for case in all_cases:
        log.clear(); o.prepare(32, 32); o.mode = 'quest'
        o.cpu.mem_write(0x6ba, struct.pack('<I', o.PARTICIPANTS)); o.cpu.mem_write(0x6aa, struct.pack('<I', o.POOL)); o.cpu.mem_write(0x6c6, b'\x03')
        for army in range(2):
            o.cpu.mem_write(0x6ca + 0x56 * army + 4, struct.pack('<I', o.DAT + army * 0x1000))
            for index in range(4):
                raw = bytearray(0xab); raw[0x46] = 10; struct.pack_into('<I', raw, 0x40, 0); raw[0x45] = 100; o.cpu.mem_write(o.DAT + army * 0x1000 + index * 0xab, bytes(raw))
        for address, value in case['words'].items(): o.cpu.mem_write(int(address), struct.pack('<H', value))
        count = len(case['objects']); o.cpu.mem_write(o.POOL, bytes(0x4000))
        for k, g in enumerate(case['objects']):
            raw = bytearray(0x40); raw[0] = g['army']; raw[3] = g['owner']; raw[0x0e] = g['template']; raw[0x19] = g['condition']; struct.pack_into('<I', raw, 0x10, 0x40); struct.pack_into('<I', raw, 0x1c, g['target'])
            o.cpu.mem_write(o.POOL + 0x40 * k, bytes(raw))
            template = bytearray(o.cpu.mem_read(o.DAT + g['army'] * 0x1000 + g['template'] * 0xab, 0xab)); struct.pack_into('<I', template, 0x40, g['flags']); o.cpu.mem_write(o.DAT + g['army'] * 0x1000 + g['template'] * 0xab, bytes(template))
        for p in range(3):
            part = bytearray(0xf5); part[0xcd:0xcd + 20] = bytes(case['inventory'][p])
            for k, v in zip((0x18, 0x1c, 0x20, 0x24, 0x28, 0x2c), case['wallet'][p]): struct.pack_into('<I', part, k, v)
            struct.pack_into('<I', part, 0x12, case['mask'][p]); part[0xb9:0xb9 + 8] = bytes(case['relations'][p]); part[0xcb] = case['dead'][p]
            struct.pack_into('<I', part, 0x0e, 0); struct.pack_into('<I', part, 0x09, 0x40 * count)
            struct.pack_into('<I', part, 0x03, 0); struct.pack_into('<I', part, 0x05, 0); 
            for s, (t, other) in enumerate(case['queue'][p]): part[0x34 + 14 * s] = t; part[0x34 + 14 * s + 9] = other
            o.cpu.mem_write(0x97a + 0xf5 * p, bytes(part))
            o.cpu.mem_write(o.PARTICIPANTS + 0x1a35 * p + 5, bytes([case['stat5'][p]]))
        # the type entry and the queue record (the handler reads [edi+9] = the other participant, [edi+0xa] the parameter)
        o.cpu.mem_write(0xefb + 0x13 * case['type'], bytes(0x13 * 2))
        record = bytearray(14); record[0] = case['type']; record[9] = case['other']; struct.pack_into('<I', record, 0xa, case['parameter'])
        o.cpu.mem_write(o.POOL + 0x3000, bytes(record))
        esp = o.STACK + 0xf00 - 8
        o.cpu.mem_write(esp + 4, struct.pack('<I', case['type'])); o.cpu.mem_write(esp, struct.pack('<I', o.STOP))
        o.cpu.reg_write(UC_X86_REG_ESP, esp); o.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        o.cpu.reg_write(UC_X86_REG_ESI, 0xefb + 0x13 * case['type']); o.cpu.reg_write(UC_X86_REG_EDI, o.POOL + 0x3000)
        o.cpu.reg_write(UC_X86_REG_EBP, case['parameter']); o.cpu.reg_write(UC_X86_REG_EDX, case['type']); o.cpu.reg_write(UC_X86_REG_EBX, case['owner'] | (case['other'] << 8)); o.cpu.reg_write(UC_X86_REG_EAX, 0)
        o.stopped = False
        o.cpu.emu_start(o.CODE + case['handler'], o.STOP, count=200000)
        patched = struct.unpack('<I', bytes(o.cpu.mem_read(esp + 4, 4)))[0] & 255
        out = b''.join(bytes(o.cpu.mem_read(0x97a + 0xf5 * p, 0xf5)) for p in range(3)) + bytes(o.cpu.mem_read(o.POOL, 0x40 * count)) + bytes([patched]) + b''.join(log)
        if __import__('os').environ.get('DEBUG_CASE') == str(all_cases.index(case)): print(out.hex())
        digest.update(out); case['sha256_prefix'] = hashlib.sha256(out).hexdigest()[:10]
    return {'format_version': 1, 'source_sha256': {k: report['source_sha256'][k] for k in ['NORWIND.EXE']}, 'cases': all_cases,
            'results': {'quest': {'case_count': len(all_cases), 'sha256': digest.hexdigest()}}}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args()
    args.fixture.write_text(json.dumps(generate(args.game)) + '\n', encoding='utf-8')
