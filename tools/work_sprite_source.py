#!/usr/bin/env python3
"""Export source-confirmed large WORK.SPR records without changing the UI importer.

Most are opaque RAW rectangles, not sprite RLE. Record 358 stays unresolved.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from PIL import Image
from analyze_executable import EXE_SHA256, INNER_MZ, read_le_objects
from interface_source import spr_records, spr_image

RAW_GROUPS = [(227, 228, 0x1b3, 'Дипломатия'), (229, 230, 0x195, 'Меню'),
              (315, 316, 0x3e98, 'Назад'), (317, 318, 0x3eb6, 'Скорость'),
              (319, 320, 0x3ed4, 'Панорама'), (321, 322, 0x3f6a, 'Игра'),
              (323, 324, 0x4000, 'Громкость'), (325, 326, 0x4096, 'Реверс'),
              (327, 346, 0x40b4, 'Тест — состояния и анимация'), (347, 348, 0x40d2, 'Пауза'),
              (349, 350, 0x40f0, 'Выход'), (351, 353, 0x3ef2, 'Минус'),
              (354, 356, 0x3f10, 'Плюс'), (357, 357, 0x410e, 'Время — фон')]


def layouts(executable):
    if hashlib.sha256(executable).hexdigest() != EXE_SHA256: raise ValueError('Unknown original executable version')
    _, objects = read_le_objects(executable, INNER_MZ)
    code, data = objects[0]['payload'], objects[2]['payload']
    if code[0xe161:0xe169].hex() != '0fb74f120fb75714': raise ValueError('RAW dimensions no longer come from widget words +12/+14')
    entries = []
    for first, last, widget, label in RAW_GROUPS:
        if data[widget] != 0x80 or data[widget + 0x16] != 255 or data[widget + 0x10] != 8:
            raise ValueError('Expected opaque WORK widget with direct palette')
        width, height = struct.unpack_from('<HH', data, widget + 0x12)
        default = struct.unpack_from('<H', data, widget + 0x17)[0]
        if not first <= default <= last: raise ValueError('Widget no longer selects the expected source range')
        handler = struct.unpack_from('<I', data, widget + 0x0c)[0]
        for sprite in range(first, last + 1):
            entries.append(dict(sprite=sprite, encoding='raw', width=width, height=height, label=label,
                                le3_widget=widget, default_sprite=default, handler=handler,
                                position=list(struct.unpack_from('<ii', data, widget + 4)),
                                dimensions_read=0xe161, draw_call=0xe17d, renderer=0x1df08))
    size, initializer = struct.unpack_from('<II', data, 0x46da)
    if size != 120 or initializer != 0x127e9 or code[0x127e9:0x127f1].hex() != 'c1e0020584010000':
        raise ValueError('Manuscript tile initializer changed')
    if code[0x12848:0x1284b].hex() != '83f803': raise ValueError('Expected three source manuscript groups')
    for group in range(3):
        for tile in range(4):
            widget = 0x46e3 + 30 * tile
            width, height, bank = data[widget + 0x12:widget + 0x15]
            relative = struct.unpack_from('<H', data, widget + 0x15)[0]
            if data[widget] != 0x85 or bank != 255 or relative != tile or data[widget + 0x10] != 8:
                raise ValueError('Expected clipped RLE manuscript widget')
            entries.append(dict(sprite=388 + 4 * group + relative, encoding='sprite_rle', width=width, height=height,
                                label=f'Рукописный свиток {group + 1}, часть {tile + 1}', le3_widget=widget,
                                initializer=initializer, selector=0x12814, manuscript_group=group, tile=tile,
                                initial_x=struct.unpack_from('<i', data, widget + 4)[0],
                                y_from_caller_plus=240 if tile >= 2 else 0,
                                dimensions_read=0xe29d, draw_call=0xe2a4, renderer=0x1e153))
    return sorted(entries, key=lambda r: r['sprite'])


def decode(record, entry, palette):
    width, height = entry['width'], entry['height']
    if entry['encoding'] == 'raw':
        if len(record) != width * height: raise ValueError('RAW record does not match source dimensions')
        image = Image.frombytes('P', (width, height), record); image.putpalette(palette)
        return image.convert('RGBA')
    if entry['encoding'] == 'sprite_rle':
        return spr_image(record, width, height, palette)
    raise ValueError('Unconfirmed sprite encoding')


def export(game, output):
    executable = (game / 'NORWIND.EXE').read_bytes()
    entries = layouts(executable)
    raw = (game / 'WORK.SPR').read_bytes(); records = spr_records(raw)
    source_palette = (game / 'COLOR1.PAL').read_bytes()
    palette = bytes((v & 252) | (v >> 6) for v in source_palette)
    output.mkdir(parents=True, exist_ok=True)
    for entry in entries:
        record = records[entry['sprite']]
        image = decode(record, entry, palette)
        name = f'{entry["sprite"]:03d}.png'; image.save(output / name)
        entry.update(file=name, record_bytes=len(record), rgba_sha256=hashlib.sha256(image.tobytes()).hexdigest())
    manifest = dict(format_version=1, executable_sha256=EXE_SHA256, source='WORK.SPR',
                    source_sha256=hashlib.sha256(raw).hexdigest(), palette='COLOR1.PAL',
                    palette_sha256=hashlib.sha256(source_palette).hexdigest(), records=entries,
                    unresolved=[dict(sprite=358, record_bytes=len(records[358]),
                                     reason='No code/data consumer or source dimensions found; no PNG exported')])
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path('assets/imported/work_large'))
    args = parser.parse_args(); result = export(args.game, args.output)
    print(f'Exported {len(result["records"])} source-confirmed WORK records; {len(result["unresolved"])} unresolved')
