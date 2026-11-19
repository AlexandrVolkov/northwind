#!/usr/bin/env python3
"""Extract the development trees ARMY__0x.TRE into data/original_tech.json.

Record: +0 kind (bit 7 set = open/done), +1 id, +2 unmet conditions, +3 u32 text offset from +3, +7 u32 cost,
+0B..+0E unknown, +0F child count, +10 u16 children (kind, id). The list ends with a record whose +1 is FFh;
texts (CP866, FFh-terminated) follow. Each settlement works on its own copy (descriptor 6CA + 56h*army + 15h + 4*owner).
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct


def decode(data):
    records = []; at = 0
    while data[at + 1] != 0xff:
        n = data[at + 0xf]
        text_offset = struct.unpack_from('<I', data, at + 3)[0]
        text = None
        if text_offset:
            start = at + 3 + text_offset; stop = data.index(b'\xff', start)
            text = data[start:stop].decode('cp866')
        records.append({'offset': at, 'kind': data[at], 'id': data[at + 1], 'unmet': data[at + 2],
                        'text_offset': struct.unpack_from('<I', data, at + 3)[0], 'cost': struct.unpack_from('<I', data, at + 7)[0],
                        'unknown_0b': data[at + 0xb:at + 0xf].hex(),
                        'text': text, 'children': [[data[at + 0x10 + 2 * k], data[at + 0x11 + 2 * k]] for k in range(n)]})
        at += 0x10 + 2 * n
    return records, at


def extract(game):
    armies = []
    for k in range(6):
        path = game / f'ARMY__0{k:d}.TRE'
        data = path.read_bytes(); records, end = decode(data)
        armies.append({'army': k, 'file': path.name, 'sha256': hashlib.sha256(data).hexdigest(), 'end': end, 'terminator': data[end:end + 2].hex(), 'records': records})
    return {'format_version': 1, 'armies': armies}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'data/original_tech.json')
    args = parser.parse_args()
    args.output.write_text(json.dumps(extract(args.game), ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
