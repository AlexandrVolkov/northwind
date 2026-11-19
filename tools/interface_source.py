#!/usr/bin/env python3
"""Decode interface graphics of the original into assets/imported/interface.

SCREENS.SCR: u16 count, count u32 offsets; record = u32 packed size, u16 width, u16 height, PCX-style RLE
(a byte >= C0h repeats the next byte (b & 3Fh) times, otherwise it is one pixel). Palettes are not stored in the
record; the ones below were confirmed by pixel comparison with DOSBox screenshots of the original (docs/interface.md).
FNT: 256 u16 glyph offsets; glyph = top offset, width, height, then width*height palette indices (0 = transparent).
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from PIL import Image
from import_game import decode_template_commands
from analyze_executable import read_le_objects, INNER_MZ, EXE_SHA256

SCREENS = {0: ('game_frame', 'COLOR1.PAL'), 1: ('loading', 'COLOR1.PAL'), 2: ('panel_menu', 'COLOR1.PAL'),
           3: ('panel_commands', 'COLOR1.PAL'), 4: ('main_menu', 'MENU1.PAL'), 5: ('save_slots', 'MENU2.PAL'),
           6: ('campaign_map', 'COLOR1.PAL'), 7: ('intro_map', 'INTRO.PAL'), 10: ('stone_a', 'MENU1.PAL'),
           11: ('stone_b', 'MENU1.PAL'), 12: ('stone_c', 'MENU1.PAL')}
MAP_WINDOW = (16, 16, 448, 448)
FONT_VARIANTS = {'MENU.FNT': ['MENU2.PAL'], 'MENU2.FNT': ['COLOR1.PAL']}
FONTS = {'FONT_01.FNT': 'COLOR1.PAL', 'MENU.FNT': 'MENU1.PAL', 'MENU2.FNT': 'MENU2.PAL', 'SCROLL.FNT': 'INTRO.PAL', 'SCROLLV.FNT': 'VICTORY.PAL'}


def unpack_screen(data, offset):
    size, width, height = struct.unpack_from('<IHH', data, offset)
    src = data[offset + 8:offset + 8 + size]; out = bytearray(); j = 0
    while j < len(src) and len(out) < width * height:
        b = src[j]; j += 1
        if b >= 0xc0: out += bytes([src[j]]) * (b & 0x3f); j += 1
        else: out.append(b)
    if len(out) != width * height: raise ValueError('Screen record does not fill its image')
    return width, height, bytes(out)


def screens(data):
    count = struct.unpack_from('<H', data)[0]
    return [unpack_screen(data, o) for o in struct.unpack_from(f'<{count}I', data, 2)]


def glyphs(data):
    offsets = struct.unpack_from('<256H', data)
    out = {}
    for code, at in enumerate(offsets):
        if at + 3 > len(data): out[code] = (0, 0, 0, b''); continue   # unused codes of SCROLL.FNT point past the end
        top, width, height = data[at], data[at + 1], data[at + 2]
        if at + 3 + width * height > len(data): out[code] = (0, 0, 0, b''); continue
        out[code] = (top, width, height, data[at + 3:at + 3 + width * height])
    return out


def spr_records(data):
    """SPR: u16 count, u24 offsets from 2 + 3*count; RLE token >= 80h: (b & 7Fh) literal pixels, else b transparent ones.
    Runs never cross a row (LE:1 1E098); trailing transparency may be omitted, so the width comes from the caller."""
    count = struct.unpack_from('<H', data)[0]
    offsets = [int.from_bytes(data[2 + 3 * i:5 + 3 * i], 'little') for i in range(count)]
    base = 2 + 3 * count; ends = offsets[1:] + [len(data) - base]
    return [data[base + a:base + b] for a, b in zip(offsets, ends)]


def spr_image(record, width, height, palette):
    pixels = bytearray(); mask = bytearray(); j = 0
    while j < len(record):
        b = record[j]; j += 1
        if b & 0x80: k = b & 0x7f; pixels += record[j:j + k]; mask += b'\xff' * k; j += k
        else: pixels += bytes(b); mask += bytes(b)
    if len(pixels) > width * height: raise ValueError('Sprite larger than its size')
    pixels = bytes(pixels.ljust(width * height, b'\0')); mask = bytes(mask.ljust(width * height, b'\0'))
    im = Image.frombytes('P', (width, height), pixels); im.putpalette(palette); im = im.convert('RGBA')
    im.putalpha(Image.frombytes('L', (width, height), mask)); return im


def make_atlas(images, columns):
    w = max(i.width for i in images); h = max(i.height for i in images)
    sheet = Image.new('RGBA', (columns * w, -(-len(images) // columns) * h), (0, 0, 0, 0))
    for k, im in enumerate(images): sheet.paste(im, ((k % columns) * w, (k // columns) * h))
    return sheet, [w, h]


def army_panel(game, army, palette, output):
    """Portraits (DAT +5) and six commands at DAT +0B, decoded by the shared DAT importer."""
    dat = (game / f'ARMY__0{army}.DAT').read_bytes(); records = spr_records((game / f'ARMY__0{army}.SPR').read_bytes())
    count = struct.unpack_from('<H', dat)[0] // 171; portraits = []; buttons = []; templates = []
    for t in range(count):
        r = dat[2 + 171 * t:2 + 171 * (t + 1)]
        portrait = struct.unpack_from('<H', r, 5)[0]
        if portrait not in portraits: portraits.append(portrait)
        slots = []
        for command in decode_template_commands(dat, 2 + 171 * t, 2 + count * 171):
            sprite = command['icon_frame']
            if sprite and sprite not in buttons: buttons.append(sprite)
            slots.append({'sprite': sprite, 'button': buttons.index(sprite) if sprite else None,
                          'present': command['present'], 'mode': command['mode'], 'raw_group': command['raw_group'],
                          'label': command['label'], 'hotkey_scan_code': command['hotkey_scan_code']})
        templates.append({'template': t, 'portrait': portraits.index(portrait), 'slots': slots})
    sheet, cell = make_atlas([spr_image(records[i], 80, 50, palette) for i in portraits], 8)
    sheet.save(output / f'army{army:02d}_portraits.png')
    bsheet, bcell = make_atlas([spr_image(records[i], 69, 35, palette) for i in buttons], 8)
    bsheet.save(output / f'army{army:02d}_buttons.png')
    return {'army': army, 'portraits': {'atlas': f'army{army:02d}_portraits.png', 'cell': cell, 'sprites': portraits},
            'buttons': {'atlas': f'army{army:02d}_buttons.png', 'cell': bcell, 'sprites': buttons}, 'templates': templates}


# MENU.SPR groups: first, last, width, height, palette, position (from the LE:3 widget records / matched on screenshots).
MENU_GROUPS = {
    'menu_bush': (60, 121, 247, 120, 'MENU1.PAL', (1, 269)), 'menu_owl': (122, 172, 409, 387, 'MENU1.PAL', (232, 93)),
    'menu_sign_game': (254, 256, 105, 33, 'MENU1.PAL', (288, 149)), 'menu_sign_exit': (257, 259, 105, 31, 'MENU1.PAL', (238, 177)),
    'menu_sign_authors': (260, 262, 117, 43, 'MENU1.PAL', (260, 268)), 'menu_candles': (173, 203, 640, 125, 'MENU2.PAL', (0, 0)),
    'menu_ball': (263, 266, 97, 89, 'MENU2.PAL', (196, 40)), 'menu_hourglass': (204, 253, 64, 108, 'MENU2.PAL', (0, 0)),
    'menu_cursor': (0, 59, 32, 32, 'MENU1.PAL', (0, 0)),
}
# HISTORY.SPR (campaign map, COLOR1): buttons 90x25 and settlement flags 16x16.
HISTORY_GROUPS = {'campaign_start': (44, 46, 90, 25, (0, 0)), 'campaign_exit': (47, 49, 90, 25, (0, 30)), 'campaign_flags': (0, 34, 16, 16, None)}


def exe_texts(game):
    """LE:3 data of NORWIND.EXE: the intro history lines (table CA0Eh, records 24h: x, y, start x, start y, text pointer, …;
    LE:1 1B112 moves every line up one pixel per 7 timer ticks) and the hero names (C9FDh, FFh-terminated)."""
    data = (game / 'NORWIND.EXE').read_bytes()
    if hashlib.sha256(data).hexdigest() != EXE_SHA256: raise ValueError('Unknown EXE version')
    _, objects = read_le_objects(data, INNER_MZ); le3 = objects[2]['payload']
    text = lambda at: le3[at:le3.index(b'\xff', at)].decode('cp866')
    lines = []; at = 0xca0e
    while struct.unpack_from('<I', le3, at + 0x10)[0]:
        x, y = struct.unpack_from('<II', le3, at + 8); pointer = struct.unpack_from('<I', le3, at + 0x10)[0]
        lines.append({'x': x, 'y': y, 'text': text(pointer)}); at += 0x24
    names = []; at = 0xc9fd
    for _ in range(3):
        names.append(text(at)); at = le3.index(b'\xff', at) + 1
    return {'table': 0xca0e, 'step_ticks': 7, 'timer_hz': 1193182 / 16384, 'clip_bottom': 0x1d0, 'lines': lines}, names


def sprite_groups(records, groups, output, palette_of):
    out = {}
    for name, (first, last, width, height, *rest) in groups.items():
        position = rest[-1]; palette = palette_of(rest[0] if len(rest) == 2 else None)
        sheet, cell = make_atlas([spr_image(records[i], width, height, palette) for i in range(first, last + 1)], 8)
        sheet.save(output / f'{name}.png')
        out[name] = {'atlas': f'{name}.png', 'cell': cell, 'first_sprite': first, 'frames': last - first + 1, 'position': position}
    return out


def image(width, height, pixels, palette, transparent=False):
    im = Image.frombytes('P', (width, height), pixels); im.putpalette(palette)
    if transparent: im.info['transparency'] = 0
    return im.convert('RGBA' if transparent else 'RGB')


def convert(game, output):
    output.mkdir(parents=True, exist_ok=True)
    pal = lambda name: (game / name).read_bytes()
    raw = (game / 'SCREENS.SCR').read_bytes(); manifest = {'format_version': 3, 'screens': [], 'fonts': []}
    for index, (width, height, pixels) in enumerate(screens(raw)):
        if index not in SCREENS: manifest['screens'].append({'index': index, 'size': [width, height], 'palette': None}); continue
        name, palette = SCREENS[index]
        im = image(width, height, pixels, pal(palette)); entry = {'index': index, 'name': name, 'size': [width, height], 'palette': palette}
        if index == 0:   # the map is drawn through the black window 16..463 of the game frame
            im = im.convert('RGBA'); im.paste((0, 0, 0, 0), (MAP_WINDOW[0], MAP_WINDOW[1], MAP_WINDOW[0] + MAP_WINDOW[2], MAP_WINDOW[1] + MAP_WINDOW[3]))
            entry['map_window'] = list(MAP_WINDOW)
        im.save(output / f'{name}.png'); manifest['screens'].append(entry)
    for font, palette in FONTS.items():
        g = glyphs((game / font).read_bytes()); cell = max(t + h for t, w, h, _ in g.values()); span = max(w for t, w, h, _ in g.values())
        atlas = Image.new('RGBA', (16 * span, 16 * cell), (0, 0, 0, 0)); entries = {}
        for code, (top, width, height, pixels) in g.items():
            if width and height:
                atlas.paste(image(width, height, pixels, pal(palette), True), ((code % 16) * span, (code // 16) * cell + top))
            entries[str(code)] = [width, top, height]
        stem = font.split('.')[0].lower(); atlas.save(output / f'font_{stem}.png')
        for variant in FONT_VARIANTS.get(font, []):   # the same glyph indices shown under another screen palette
            other = Image.new('RGBA', atlas.size, (0, 0, 0, 0))
            for code, (top, width, height, pixels) in g.items():
                if width and height: other.paste(image(width, height, pixels, pal(variant), True), ((code % 16) * span, (code // 16) * cell + top))
            other.save(output / f'font_{stem}_{variant.split(".")[0].lower()}.png')
        if font == 'FONT_01.FNT':   # the people counter: the same glyphs with palette indices lowered by 30h
            red = Image.new('RGBA', atlas.size, (0, 0, 0, 0))
            for code, (top, width, height, pixels) in g.items():
                if width and height:
                    shifted = bytes(v - 0x30 if v >= 0x30 else v for v in pixels)
                    red.paste(image(width, height, shifted, pal(palette), True), ((code % 16) * span, (code // 16) * cell + top))
            red.save(output / f'font_{stem}_red.png')
        manifest['fonts'].append({'file': font, 'atlas': f'font_{stem}.png', 'palette': palette, 'cell': [span, cell], 'glyphs': entries})
    work = spr_records((game / 'WORK.SPR').read_bytes()); colour = pal('COLOR1.PAL')
    # Display colors after the original VGA DAC's six-bit conversion, as used by the verified sprite renderer.
    manifest['ui_colors'] = {str(i): [(v & 252) | (v >> 6) for v in colour[i*3:i*3+3]] for i in [0, 0x44, 0xb2, 0x51, 0x22, 0x94]}
    # LE:1 2515 selects the inclusive 0..14 cursor range; 3D67 draws 32x32 WORK.SPR frames.
    arrow, cell = make_atlas([spr_image(work[i], 32, 32, colour) for i in range(15)], 8)
    arrow.save(output / 'arrow_cursor.png')
    manifest['arrow_cursor'] = {'atlas': 'arrow_cursor.png', 'cell': cell, 'first_sprite': 0, 'sprites': list(range(15))}
    # All cursor families of WORK.SPR 0..165 (scroll directions from LE:1 3FA1, other modes are not ported yet).
    cursors, cell = make_atlas([spr_image(work[i], 32, 32, colour) for i in range(166)], 8)
    cursors.save(output / 'work_cursors.png')
    manifest['work_cursors'] = {'atlas': 'work_cursors.png', 'cell': cell, 'first_sprite': 0, 'sprites': list(range(166))}
    heroes, cell = make_atlas([spr_image(work[i], 64, 70, colour) for i in range(195, 227)], 8)
    heroes.save(output / 'hero_portraits.png')
    manifest['hero_portraits'] = {'atlas': 'hero_portraits.png', 'cell': cell, 'first_sprite': 195, 'source': 'WORK.SPR',
                                  'known': {'Олег': 196}}
    manifest['armies'] = [army_panel(game, 2, colour, output)]
    manifest['menu_sprites'] = sprite_groups(spr_records((game / 'MENU.SPR').read_bytes()), MENU_GROUPS, output, lambda p: pal(p))
    manifest['campaign_sprites'] = sprite_groups(spr_records((game / 'HISTORY.SPR').read_bytes()), HISTORY_GROUPS, output, lambda p: colour)
    manifest['intro_text'], manifest['hero_names'] = exe_texts(game)
    manifest['source_sha256'] = {n: hashlib.sha256((game / n).read_bytes()).hexdigest() for n in ['SCREENS.SCR', 'WORK.SPR', 'ARMY__02.SPR', 'ARMY__02.DAT', 'MENU.SPR', 'HISTORY.SPR', 'NORWIND.EXE', 'COLOR1.PAL', *FONTS]}
    (output / 'interface.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'assets/imported/interface')
    args = parser.parse_args(); convert(args.game, args.output)
