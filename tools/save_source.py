#!/usr/bin/env python3
"""Inspect the DOS GAME_00.SAV tagged-record container without exporting payloads."""
import argparse
import hashlib
import json
from pathlib import Path
import struct


def parse(data):
    if len(data) < 4: raise ValueError('Truncated save header')
    count = struct.unpack_from('<I', data)[0]
    if count > 4096: raise ValueError('Unreasonable save record count')
    offset = 4
    records = []
    for index in range(count):
        if offset + 8 > len(data): raise ValueError('Truncated record header')
        size, tag = struct.unpack_from('<II', data, offset)
        payload_offset = offset + 8
        end = payload_offset + size
        if end > len(data): raise ValueError('Truncated record payload')
        payload = data[payload_offset:end]
        records.append(dict(index=index, header_offset=offset, payload_offset=payload_offset,
                            length=size, tag=tag, tag_hex=f'{tag:08X}',
                            payload_sha256=hashlib.sha256(payload).hexdigest()))
        offset = end
    if offset != len(data): raise ValueError('Trailing bytes after declared records')
    return dict(format_version=1, file_size=len(data), file_sha256=hashlib.sha256(data).hexdigest(),
                record_count=count, records=records)


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('save', type=Path)
    ap.add_argument('--output', type=Path, default=Path('data/original_save_layout.json'))
    args = ap.parse_args()
    report = parse(args.save.read_bytes())
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(f'{report["record_count"]} records in {report["file_size"]} bytes: '+
          ', '.join(f'{x["tag_hex"]}:{x["length"]}' for x in report['records']))
