#!/usr/bin/env python3
"""Verify all confirmed large WORK images with original RAW/RLE blitters.

Dirty-rectangle bookkeeping 3EEA is stubbed. Framebuffer writes execute natively;
two backgrounds recover RLE alpha independently of the Python decoder.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_ESI, UC_X86_REG_ESP, UC_X86_REG_EIP
from analyze_executable import analyze
from interface_source import spr_records
from verify_texts import TextsOracle
from work_sprite_source import layouts, decode


class SpriteOracle(TextsOracle):
    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if at == 0x3eea:
            sp = cpu.reg_read(UC_X86_REG_ESP)
            cpu.reg_write(UC_X86_REG_EIP, struct.unpack('<I', cpu.mem_read(sp, 4))[0])
            cpu.reg_write(UC_X86_REG_ESP, sp + 4)
            return
        bounds = (0x1df08, 0x1df3c) if self.mode == 'raw' else (0x1e098, 0x1e0de)
        if not bounds[0] <= at < bounds[1]:
            raise ValueError(f'Unexpected WORK blitter escape: {address:#x}')

    def draw(self, record, entry, background):
        width, height = entry['width'], entry['height']
        self.cpu.mem_write(self.BANK, record)
        self.cpu.mem_write(self.FRAME, bytes([background]) * (640 * 480))
        self.cpu.mem_write(0x68e, struct.pack('<I', self.FRAME))
        for register, value in [(UC_X86_REG_EAX, 16), (UC_X86_REG_EBX, 16), (UC_X86_REG_ECX, width),
                                (UC_X86_REG_EDX, height), (UC_X86_REG_ESI, self.BANK)]: self.cpu.reg_write(register, value)
        self.run(entry['encoding'], 0x1df08 if entry['encoding'] == 'raw' else 0x1e098)
        return b''.join(bytes(self.cpu.mem_read(self.FRAME + (16 + y) * 640 + 16, width)) for y in range(height))


def generate(game):
    report, code = analyze(game)
    oracle = SpriteOracle(code)
    executable = (game / 'NORWIND.EXE').read_bytes(); raw = (game / 'WORK.SPR').read_bytes()
    records = spr_records(raw)
    source_palette = (game / 'COLOR1.PAL').read_bytes()
    palette = bytes((v & 252) | (v >> 6) for v in source_palette)
    results = []
    for entry in layouts(executable):
        record = records[entry['sprite']]
        first = oracle.draw(record, entry, 0xa5)
        second = oracle.draw(record, entry, 0x5a) if entry['encoding'] == 'sprite_rle' else first
        rgba = bytearray()
        for a, b in zip(first, second):
            transparent = entry['encoding'] == 'sprite_rle' and a != b
            if transparent:
                if (a, b) != (0xa5, 0x5a): raise ValueError('RLE output depends on background unexpectedly')
                rgba += palette[:3] + b'\0'
            else:
                rgba += palette[a * 3:a * 3 + 3] + b'\xff'
        if rgba != decode(record, entry, palette).tobytes():
            raise ValueError(f'WORK {entry["sprite"]}: converter differs from native blitter')
        results.append(dict(sprite=entry['sprite'], width=entry['width'], height=entry['height'],
                            native_renderer=0x1df08 if entry['encoding'] == 'raw' else 0x1e098,
                            rgba_sha256=hashlib.sha256(rgba).hexdigest()))
    return dict(format_version=1, executable_sha256=report['source_sha256']['NORWIND.EXE'],
                work_sha256=hashlib.sha256(raw).hexdigest(), palette_sha256=hashlib.sha256(source_palette).hexdigest(), records=results)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True); parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args(); result = generate(args.game)
    args.fixture.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'Native WORK sprites: {len(result["records"])} complete RGBA images match original blitters')
