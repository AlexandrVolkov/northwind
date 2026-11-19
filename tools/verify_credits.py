#!/usr/bin/env python3
"""Verify authors MENU sprites with the original large-width RLE renderer 19465.

3EEA dirty bookkeeping is stubbed to RET; all pixel writes use native instructions.
Two distinct backgrounds independently recover transparent runs.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX, UC_X86_REG_EDX, UC_X86_REG_ESI
from analyze_executable import analyze
from interface_source import spr_records
from verify_texts import TextsOracle


class CreditsOracle(TextsOracle):
    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if not (0x19465 <= at < 0x194b2 or at == 0x3eea): raise ValueError(f'Unexpected credits renderer escape: {at:X}')


def generate(game, manifest):
    report, code = analyze(game); o = CreditsOracle(code); c = o.cpu
    c.mem_write(o.CODE + 0x3eea, b'\xc3')
    records = spr_records((game / 'MENU.SPR').read_bytes())
    palette = bytes((v & 252) | (v >> 6) for v in (game / 'FROM_AUT.PAL').read_bytes())
    c.mem_write(0x68e, struct.pack('<I', o.FRAME))
    c.mem_write(0x91ae, struct.pack('<480I', *(y * 640 for y in range(480))))
    out = []
    for entry in manifest['sprites']:
        width, height = entry['width'], entry['height']; draws = []
        c.mem_write(o.BANK, records[entry['sprite']])
        for bg in [0xa5, 0x5a]:
            c.mem_write(o.FRAME, bytes([bg]) * (640 * 480))
            for register, value in [(UC_X86_REG_EAX, 16), (UC_X86_REG_EBX, 16), (UC_X86_REG_ECX, width), (UC_X86_REG_EDX, height), (UC_X86_REG_ESI, o.BANK)]:
                c.reg_write(register, value)
            o.run('sprite', 0x19465)
            draws.append(b''.join(bytes(c.mem_read(o.FRAME + (16 + y) * 640 + 16, width)) for y in range(height)))
        rgba = bytearray()
        for a, b in zip(*draws):
            if a != b:
                if (a, b) != (0xa5, 0x5a): raise ValueError('Unexpected background-dependent sprite pixel')
                rgba += palette[:3] + b'\0'
            else: rgba += palette[a * 3:a * 3 + 3] + b'\xff'
        digest = hashlib.sha256(rgba).hexdigest()
        if digest != entry['rgba_sha256']: raise ValueError(f'Sprite {entry["sprite"]} differs from native RLE renderer')
        out.append(dict(sprite=entry['sprite'], width=width, height=height, rgba_sha256=digest))
    return dict(format_version=1, source_sha256={name: hashlib.sha256((game / name).read_bytes()).hexdigest()
                for name in ['NORWIND.EXE', 'MENU.SPR', 'FROM_AUT.PAL']}, sprites=out)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--manifest', type=Path, default=Path('assets/imported/credits/manifest.json'))
    parser.add_argument('--fixture', type=Path, required=True)
    args = parser.parse_args(); result = generate(args.game, json.loads(args.manifest.read_text()))
    args.fixture.write_text(json.dumps(result, indent=2) + '\n')
    print(f'Native credits images: {len(result["sprites"])} RGBA matches')
