#!/usr/bin/env python3
"""Execute five original menu cursor-family initializers under Unicorn."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from analyze_executable import analyze
from verify_texts import TextsOracle


class CursorOracle(TextsOracle):
    def guard(self, cpu, address, size, user_data):
        at = address - self.CODE
        if not 0x18CE5 <= at < 0x18EC5:
            raise ValueError(f'Unexpected menu cursor initializer escape: {at:X}')


def generate(game):
    _, code = analyze(game)
    oracle = CursorOracle(code)
    out = []
    for name, entry in [('main', 0x18CE5), ('cabinet', 0x18D45),
                        ('screen_6', 0x18DA5), ('screen_7_and_victory', 0x18E05),
                        ('authors', 0x18E65)]:
        oracle.cpu.mem_write(0x1932, b'\xa5' * 0x40)
        oracle.run('cursor', entry)
        first, last = struct.unpack('<HH', oracle.cpu.mem_read(0x1946, 4))
        center_x, center_y = struct.unpack('<II', oracle.cpu.mem_read(0x1932, 8))
        out.append(dict(name=name, entry_le1=entry, first_sprite=first, last_sprite=last,
                        center_x=center_x, center_y=center_y))
    return dict(format_version=1,
                source_sha256=hashlib.sha256((game / 'NORWIND.EXE').read_bytes()).hexdigest(),
                cursor_families=out)


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--game', type=Path, required=True)
    ap.add_argument('--fixture', type=Path, default=Path('tests/fixtures/menu-cursor.json'))
    args = ap.parse_args()
    result = generate(args.game)
    args.fixture.write_text(json.dumps(result, indent=2) + '\n')
    print(result['cursor_families'])
