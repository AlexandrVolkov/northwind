#!/usr/bin/env python3
"""Prepare the source-confirmed authors background, widgets and ten rule pages.

The occupied interface converter is not changed. Screens 8/13 remain source-only
palette findings until captured in the corresponding original game flow.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from PIL import Image
from analyze_executable import EXE_SHA256, INNER_MZ, read_le_objects
from interface_source import screens, spr_records, spr_image


def layouts(executable):
    if hashlib.sha256(executable).hexdigest() != EXE_SHA256: raise ValueError('Unknown EXE profile')
    _, objects = read_le_objects(executable, INNER_MZ); data = objects[2]['payload']
    out = []
    for i in range(6):
        at = 0xdc9c + 49 * i
        if struct.unpack_from('<I', data, at)[0] != 49 or data[at + 4:at + 6] != b'\x0b\x09':
            raise ValueError('Unexpected authors widget format')
        callback, action = struct.unpack_from('<II', data, at + 6)
        x, y, width, height, sprite = struct.unpack_from('<5I', data, at + 0x14)
        out.append(dict(le3=at, callback=callback, action=action, hotkey_scan=struct.unpack_from('<H', data, at + 0xe)[0],
                        position=[x, y], size=[width, height], sprite_base=sprite, state_parameters=list(data[at + 0x2c:at + 0x30])))
    return out


def convert(game, output):
    names = ['NORWIND.EXE', 'SCREENS.SCR', 'MENU.SPR', 'FROM_AUT.PAL']
    source = {name: (game / name).read_bytes() for name in names}
    widgets = layouts(source['NORWIND.EXE'])
    palette = bytes((v & 252) | (v >> 6) for v in source['FROM_AUT.PAL'])
    if len(palette) != 768: raise ValueError('Invalid FROM_AUT palette')
    output.mkdir(parents=True, exist_ok=True)
    width, height, pixels = screens(source['SCREENS.SCR'])[9]
    if (width, height) != (640, 480): raise ValueError('Invalid authors background dimensions')
    background = Image.frombytes('P', (width, height), pixels); background.putpalette(palette)
    background.convert('RGB').save(output / 'background.png')
    records = spr_records(source['MENU.SPR']); entries = []
    for sprite in range(280, 307):
        size = (64, 66) if sprite < 289 else (45, 52) if sprite < 295 else (64, 36) if sprite < 297 else (320, 180)
        image = spr_image(records[sprite], *size, palette)
        filename = f'{sprite:03d}.png'; image.save(output / filename)
        entries.append(dict(sprite=sprite, file=filename, width=size[0], height=size[1],
                            rgba_sha256=hashlib.sha256(image.tobytes()).hexdigest()))
    result = dict(format_version=1, source_sha256={name: hashlib.sha256(data).hexdigest() for name, data in source.items()},
                  palette='FROM_AUT.PAL', background=dict(file='background.png', screen=9, width=width, height=height,
                  rgb_sha256=hashlib.sha256(background.convert('RGB').tobytes()).hexdigest()),
                  widgets=widgets, sprites=entries, rules=dict(first_sprite=297, pages=10, position=[224, 90], size=[320, 180]),
                  pending_screens=[dict(screen=8, purpose='Ваш ход', palette_candidate='COLOR1.PAL', evidence='19BF6; palette not switched locally', screenshot_verified=False),
                                   dict(screen=13, purpose='Victory scroll', palette_candidate='VICTORY.PAL', evidence='1CE9A/1CF11/1CF2A', screenshot_verified=False)])
    (output / 'manifest.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('assets/imported/credits'))
    args = parser.parse_args(); result = convert(args.game, args.output)
    print(f'Exported authors background, {len(result["sprites"])} sprites/pages; screens 8/13 remain unverified in DOSBox')
