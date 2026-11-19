#!/usr/bin/env python3
"""Extract the message type table (LE:3 0EFB, 30 x 13h), third-party reactions (LE:3 0DDB, 6 x 12)
and the message texts from NORWIND.EXE into data/original_diplomacy.json."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from forestry_source import read_le_objects, INNER_MZ, EXE_SHA256

TYPES = 30


def extract(game):
    data = (game / 'NORWIND.EXE').read_bytes()
    if hashlib.sha256(data).hexdigest() != EXE_SHA256: raise ValueError('Unknown EXE version')
    le, objects = read_le_objects(data, INNER_MZ)
    payload = objects[2]['payload']
    types = []
    for k in range(TYPES):
        text, _, handler, duration = struct.unpack_from('<IIII', payload, 0xefb + 0x13 * k)
        raw = payload[text:payload.index(b'\0', text)].decode('cp866')
        types.append({'type': k, 'handler': handler, 'duration': duration, 'parameters': list(payload[0xefb + 0x13 * k + 0x10:0xefb + 0x13 * (k + 1)]),
                      'text': raw})
    return {'format_version': 1, 'source_sha256': EXE_SHA256, 'types': types, 'reactions': list(payload[0xddb:0xddb + 72])}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'data/original_diplomacy.json')
    args = parser.parse_args()
    args.output.write_text(json.dumps(extract(args.game), ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
