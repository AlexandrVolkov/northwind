#!/usr/bin/env python3
"""Item (cargo kind) table of LE:3 +4191: 25-byte records, the item code is the index (docs/campaign-items.md).
Fields: +0 u32 icon/text id, +4 u32 handler (LE:1 offset), +8 u32 name pointer (LE:3), +0C byte class, +10.. parameters. Writes data/original_items.json."""
import json
import struct
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1]
data = (root / 'build/assembly/LE_3.bin').read_bytes()
items = []
for code in range(36):
    o = 0x4191 + 0x19 * code
    icon, handler, name_ptr, klass = struct.unpack_from('<IIIB', data, o)
    name = data[name_ptr:name_ptr + 40].split(b'\xff')[0].decode('cp866') if name_ptr else ''
    items.append({'code': code, 'icon': icon, 'handler': f'{handler:X}', 'name': name, 'class': klass, 'parameter': data[o + 0x10], 'static_hex': data[o + 0x0c:o + 0x19].hex()})
(root / 'data/original_items.json').write_text(json.dumps({'source': 'LE:3 4191, stride 19h', 'items': items}, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
print(len(items), 'items')
