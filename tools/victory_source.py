#!/usr/bin/env python3
"""Extract the ending scroll's LE:3 records without assigning an unverified palette."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from analyze_executable import EXE_SHA256, INNER_MZ, read_le_objects


def extract(executable):
    if hashlib.sha256(executable).hexdigest() != EXE_SHA256:
        raise ValueError('Unknown executable profile')
    data = read_le_objects(executable, INNER_MZ)[1][2]['payload']
    records = []
    for i in range(256):
        address = 0xDDDC + 36 * i
        x, y, initial_x, initial_y, pointer, saved_x, saved_y, drawn, saved_extra = struct.unpack_from('<9I', data, address)
        if pointer == 0:
            if any((x, y, initial_x, initial_y, saved_x, saved_y, drawn, saved_extra)):
                raise ValueError('Unexpected ending scroll terminator')
            break
        if pointer >= len(data): raise ValueError('Ending text pointer outside LE:3')
        end = data.find(b'\xff', pointer)
        if end < 0: raise ValueError('Unterminated ending text')
        records.append(dict(index=i, le3=address, x=x, y=y, initial_x=initial_x,
                            initial_y=initial_y, text_le3=pointer, text=data[pointer:end].decode('cp866'),
                            runtime_initial=[saved_x, saved_y, drawn, saved_extra]))
    else: raise ValueError('No ending scroll terminator')
    return dict(format_version=1, source_sha256=hashlib.sha256(executable).hexdigest(),
                first_record_le3=0xDDDC, record_stride=36, terminator_le3=address, records=records)


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--game', type=Path, required=True)
    ap.add_argument('--output', type=Path, default=Path('data/original_victory_scroll.json'))
    args = ap.parse_args()
    result = extract((args.game / 'NORWIND.EXE').read_bytes())
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print(f'Extracted {len(result["records"])} ending scroll records')
