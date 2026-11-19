#!/usr/bin/env python3
"""Execute original help row decoder, page navigation and message lookup.

The PIC path runs 14FBA on a preloaded input chunk and a real 640-byte stride.
DOS I/O is outside this boundary; incomplete final rows stay in the scratch row.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_BLOCK
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ESI, UC_X86_REG_ESP, UC_X86_REG_EIP, UC_X86_REG_EFLAGS
from analyze_executable import analyze
from texts_source import pictures, messages


class TextsOracle:
    CODE, FRAME, STACK, BANK = 0x10000, 0x400000, 0x600000, 0x1000000
    STOP = STACK + 0x800

    def __init__(self, code):
        self.cpu = Uc(UC_ARCH_X86, UC_MODE_32)
        for at, size in [(0, 0x10000), (self.CODE, 0x30000), (self.FRAME, 0x100000), (self.STACK, 0x1000), (self.BANK, 0x200000)]:
            self.cpu.mem_map(at, size)
        self.cpu.mem_write(self.CODE, code)
        self.cpu.hook_add(UC_HOOK_BLOCK, self.guard)
        self.mode = ''
        self.goal = None

    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if self.mode == 'nav' and at in [0x6956, 0x6976]:
            self.goal = at; cpu.emu_stop(); return
        allowed = {'nav': [(0x68fc, 0x6956)], 'pic': [(0x14fba, 0x15080)],
                   'message': [(0x155ff, 0x15637), (0x15194, 0x151d3)]}[self.mode]
        if not any(lo <= at < hi for lo, hi in allowed):
            raise ValueError(f'Unexpected text control-flow escape: {address:#x} ({self.mode})')

    def run(self, mode, entry):
        self.mode = mode; self.goal = None
        self.cpu.mem_write(self.STACK + 0xf00, struct.pack('<I', self.STOP))
        self.cpu.reg_write(UC_X86_REG_ESP, self.STACK + 0xf00)
        self.cpu.reg_write(UC_X86_REG_EFLAGS, 2)
        self.cpu.emu_start(self.CODE + entry, self.STOP, count=5000000)
        if self.cpu.reg_read(UC_X86_REG_EIP) != self.STOP and self.goal is None:
            raise ValueError('Original text operation hit instruction limit')


def generate(game):
    report, code = analyze(game)
    o = TextsOracle(code)
    raw_pic = (game / 'HELP.PIC').read_bytes()
    palette = bytes((v & 252) | (v >> 6) for v in (game / 'COLOR1.PAL').read_bytes())
    pages = []
    for i, page in enumerate(pictures(raw_pic)):
        at = page['offset'] + 8
        packed = raw_pic[at:at + page['packed_bytes']]
        o.cpu.mem_write(o.BANK + 640, packed)
        o.cpu.mem_write(o.FRAME, b'\xa5' * (640 * 480))
        for pointer, value in [(0x696, o.BANK), (0x9185, o.BANK), (0x9191, o.BANK + page['width']),
                               (0x9189, o.FRAME + 16 * 640 + 16), (0x918d, 640 - page['width']),
                               (0x9195, o.BANK + 640 + len(packed))]:
            o.cpu.mem_write(pointer, struct.pack('<I', value))
        o.cpu.reg_write(UC_X86_REG_ESI, o.BANK + 640)
        o.run('pic', 0x14fba)
        actual = b''.join(bytes(o.cpu.mem_read(o.FRAME + (16 + y) * 640 + 16, page['width'])) for y in range(page['height']))
        if actual != page['pixels']: raise ValueError(f'PIC page {i} disagrees with original row decoder')
        rgb = b''.join(palette[index * 3:index * 3 + 3] for index in actual)
        pending = struct.unpack('<I', o.cpu.mem_read(0x9185, 4))[0] - o.BANK
        pages.append(dict(page=i, indices_sha256=hashlib.sha256(actual).hexdigest(), rgb_sha256=hashlib.sha256(rgb).hexdigest(), pending_row_bytes=pending))
        if i % 8 == 7: print(f'Native help: {i + 1}/32 pages', flush=True)
    keys = [0, 0xe04b, 0xe048, 0xe04d, 0xe050, 0x3b, 1, 0xffff]
    digest = hashlib.sha256(); count = 0
    for page in range(32):
        for key in keys:
            for buttons in range(256):
                o.cpu.mem_write(0x4d, bytes([page]))
                o.cpu.mem_write(0x1c35, struct.pack('<H', key))
                o.cpu.mem_write(0x194b, bytes([buttons]))
                o.cpu.reg_write(UC_X86_REG_EBX, 0)
                o.run('nav', 0x68fc)
                digest.update(bytes([o.cpu.reg_read(UC_X86_REG_EAX) & 255,
                                     o.cpu.reg_read(UC_X86_REG_EBX) & 255, int(o.goal == 0x6956)])); count += 1
    nav = dict(keys=keys, pages=32, button_values=256, case_count=count, sha256=digest.hexdigest())
    raw_messages = (game / 'MESSAGES.TXT').read_bytes()
    catalog = {m['code']: m for m in messages(raw_messages)['messages']}
    processed = raw_messages.replace(b'\r', b'\xff').replace(b'\n', b'\xff')
    o.cpu.mem_write(o.BANK, processed)
    o.cpu.mem_write(0x9116, struct.pack('<I', o.BANK))
    o.cpu.mem_write(0x9112, struct.pack('<I', len(processed)))
    o.cpu.mem_write(0x9130, b'[00]\0')
    digest = hashlib.sha256(); found_count = 0
    for value in range(256):
        o.cpu.reg_write(UC_X86_REG_EAX, value << 8)
        o.cpu.reg_write(UC_X86_REG_ESI, o.BANK)
        o.run('message', 0x155ff)
        found = not o.cpu.reg_read(UC_X86_REG_EFLAGS) & 1
        digest.update(bytes([int(found)]))
        if found:
            offset = o.cpu.reg_read(UC_X86_REG_ESI) - o.BANK
            end = processed.index(255, offset)
            text = processed[offset:end].decode('cp866')
            if offset != catalog[value]['source_offset'] + 4 or text != catalog[value]['original_text']:
                raise ValueError('Message converter disagrees with native search')
            utf8 = text.encode('utf-8'); digest.update(struct.pack('<I', len(utf8)) + utf8); found_count += 1
        elif value in catalog:
            raise ValueError('Original search did not find an exported message')
    return dict(format_version=1, source_sha256=report['source_sha256']['NORWIND.EXE'],
                help_sha256=hashlib.sha256(raw_pic).hexdigest(), messages_sha256=hashlib.sha256(raw_messages).hexdigest(),
                pages=pages, navigation=nav, lookup=dict(case_count=256, found_count=found_count, sha256=digest.hexdigest()))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True); parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args(); result = generate(args.game)
    args.fixture.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'Native texts: 32 help pages, {result["navigation"]["case_count"]} page transitions, 256 message codes')
