#!/usr/bin/env python3
"""Extract the AI character table (LE:3 ED43, 9 x 53h; fixup 1E654 -> 3:ED43) from NORWIND.EXE into data/original_ai.json.

1E53A copies record [participant +16] (SCN slot +13; the player's own slot gets 0) to the AI block +5..+57."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from forestry_source import read_le_objects, INNER_MZ, EXE_SHA256

TABLE, SIZE, COUNT = 0xed43, 0x53, 9
FIELDS = {'u32_%02x' % at: at for at in [0x31, 0x35, 0x39, 0x3d, 0x41, 0x45, 0x49, 0x4d, 0x51]}


def fortifications(payload):
    """LE:3 F0F0: pointers (until 0) to lists of 5-byte entries (DAT class byte, dx0, dy0, dx1, dy1; FFh ends)."""
    out, at = [], 0xf0f0
    while (pointer := struct.unpack_from('<I', payload, at)[0]) != 0:
        end = pointer
        while payload[end] != 0xff: end += 5
        out.append(payload[pointer:end + 1].hex()); at += 4
    return out


def formations(payload):
    """LE:3 3D45: zero-terminated pointers (object-relative) to formation records: +0 kind, +1 scale flags, +12 size,
    +16 place count, places of 15h bytes from +17 (two points, count, class byte)."""
    out, at = [], 0x3d45
    while (pointer := struct.unpack_from('<I', payload, at)[0]) != 0:
        count = payload[pointer + 0x16]
        out.append(payload[pointer:pointer + 0x17 + count * 0x15].hex()); at += 4
    return out


def extract(game):
    data = (game / 'NORWIND.EXE').read_bytes()
    if hashlib.sha256(data).hexdigest() != EXE_SHA256: raise ValueError('Unknown EXE version')
    le, objects = read_le_objects(data, INNER_MZ)
    u32 = lambda at: struct.unpack_from('<I', data, at)[0]
    pages = le + u32(le + 0x68); records = le + u32(le + 0x6c); source = 0x1e654
    blob = data[records + u32(pages + source // 4096 * 4):records + u32(pages + (source // 4096 + 1) * 4)]
    if blob.count(struct.pack('<BBHBH', 7, 0, source % 4096, 3, TABLE)) != 1: raise ValueError('Unexpected LE fixup record')
    payload = objects[2]['payload']
    characters = []
    for k in range(COUNT):
        raw = payload[TABLE + SIZE * k:TABLE + SIZE * (k + 1)]
        item = {'index': k, 'raw_hex': raw.hex(), 'mode': raw[0]}
        item.update({name: struct.unpack_from('<I', raw, at - 5)[0] for name, at in FIELDS.items()})
        characters.append(item)
    # Tables used by the squad states (6996 direction, 20071 messages) and military requests (20DD0 mode 7 army classes).
    tables = {'arctangent_hex': objects[1]['payload'][0x287e:0x297e].hex(),        # DS:287E (LE:2), 256 bytes
              'squad_messages_hex': payload[0xf02e:0xf06a].hex(),                    # LE:3 F02E, 5 rows of three text ids
              'army_classes_hex': payload[0xf0e6:0xf0eb].hex(),                      # LE:3 F0E6, mode-8 class bytes
              'fortifications': fortifications(payload),                               # LE:3 F0F0, order mode 7
              'sine_hex': objects[1]['payload'][0x2a7e:0x2abe].hex(),                  # DS:2A7E (LE:2), 32 words of a half wave (10262)
              'formations': formations(payload)}                                       # LE:3 3D45, step 4 (1EF26)
    return {'format_version': 1, 'source_sha256': EXE_SHA256, 'fixup': {'source_operand': source, 'target_object': 3, 'target_offset': TABLE},
            'block_offset': 5, 'characters': characters, **tables}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'data/original_ai.json')
    args = parser.parse_args()
    args.output.write_text(json.dumps(extract(args.game), ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
