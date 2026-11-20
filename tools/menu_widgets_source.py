#!/usr/bin/env python3
"""Extract the nine main-menu widgets from the verified NORWIND.EXE profile."""
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
    address = 0xADBC
    widgets = []
    while True:
        length = struct.unpack_from('<I', data, address)[0]
        if length == 0xFFFFFFFF: break
        if length not in (45, 49): raise ValueError('Unexpected widget size')
        callback, action = struct.unpack_from('<II', data, address + 6)
        x, y, width, height, sprite = struct.unpack_from('<5I', data, address + 0x14)
        widgets.append(dict(le3=address, length=length, type=data[address + 4], flags=data[address + 5],
                            callback_le1=callback, action_le1=action, scan_code=struct.unpack_from('<H', data, address + 0xE)[0],
                            position=[x, y], size=[width, height], sprite_base=sprite,
                            parameters=list(data[address + 0x2C:address + min(length, 0x30)])))
        address += length
    if len(widgets) != 9 or address != 0xAF65: raise ValueError('Unexpected main menu widget count')
    return dict(format_version=1, exe_sha256=hashlib.sha256(executable).hexdigest(),
                first_record_le3=0xADBC, terminator_le3=address, widgets=widgets)


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--game', type=Path, required=True)
    ap.add_argument('--output', type=Path, default=Path('data/original_menu_widgets.json'))
    args = ap.parse_args()
    data = extract((args.game / 'NORWIND.EXE').read_bytes())
    args.output.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n')
    print(f'Extracted {len(data["widgets"])} main-menu widget records')
