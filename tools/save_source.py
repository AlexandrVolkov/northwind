#!/usr/bin/env python3
"""Inspect the DOS GAME_00.SAV tagged-record container without exporting payloads."""
import argparse
import hashlib
import json
from pathlib import Path
import struct




def parse_scenario11(payload):
    """Only the known FAB4..FBC4 prefix and exploration block."""
    header_length = 0xF9D
    if len(payload) < header_length: raise ValueError('Truncated 11h scenario header')
    width, height = payload[0x22], payload[0x23]
    # FBB2/FBB7 multiply the low width byte by [18DE] again, not by height.
    visibility_length = width * width
    if not width or not height or len(payload) < header_length + visibility_length:
        raise ValueError('Truncated 11h exploration block')
    filename = payload[0x14:0x21].split(b'\0', 1)[0].decode('cp866')
    visibility = payload[header_length:header_length + visibility_length]
    return dict(episode_filename=filename, width=width, height=height,
                byte_1ec5=payload[0x21], byte_6c6=payload[0x2c],
                byte_6c7=payload[0x2d], byte_6c8=payload[0x2e],
                participant_records_offset=0x4D, participant_records_length=0xF50,
                participant_records_sha256=hashlib.sha256(payload[0x4D:header_length]).hexdigest(),
                header_length=header_length, visibility_length=visibility_length,
                visibility_sha256=hashlib.sha256(visibility).hexdigest(),
                remaining_length=len(payload) - header_length - visibility_length)

def parse_session99(payload):
    """Fields written by FEC6..FF39; record body blocks remain opaque."""
    if len(payload) < 25 + 512 or (len(payload) - 25 - 512) % 0x841:
        raise ValueError('Invalid campaign 99h payload length')
    current_participant = payload[0]
    field_1cad, field_1cb1 = struct.unpack_from('<II', payload, 1)
    field_1cb5 = list(payload[9:25])
    table = payload[25:537]
    regions = payload[537:]
    return dict(current_participant=current_participant, field_1cad=field_1cad,
                field_1cb1=field_1cb1, field_1cb5=field_1cb5,
                table_length=512, table_sha256=hashlib.sha256(table).hexdigest(),
                region_record_size=0x841, region_count=len(regions) // 0x841,
                region_sha256=[hashlib.sha256(regions[i:i + 0x841]).hexdigest()
                               for i in range(0, len(regions), 0x841)])


def parse_area22(payload):
    """Structure created by 1058F..105E7; opaque blocks remain hashed."""
    prefix = 0xF50
    descriptor = 0x40
    bucket_size = 0x2000
    if len(payload) < prefix + descriptor or (len(payload) - prefix - descriptor) % bucket_size:
        raise ValueError('Invalid area 22h payload length')
    counts = list(struct.unpack_from('<16I', payload, prefix))
    buckets = payload[prefix + descriptor:]
    bucket_count = len(buckets) // bucket_size
    if bucket_count > 16 or any(x > bucket_size for x in counts[:bucket_count]):
        raise ValueError('Invalid area 22h allocation counts')
    return dict(prefix_length=prefix, prefix_sha256=hashlib.sha256(payload[:prefix]).hexdigest(),
                descriptor_offset=prefix, descriptor_counts=counts,
                bucket_size=bucket_size, bucket_count=bucket_count,
                bucket_sha256=[hashlib.sha256(buckets[i:i + bucket_size]).hexdigest()
                               for i in range(0, len(buckets), bucket_size)])

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
        record = dict(index=index, header_offset=offset, payload_offset=payload_offset,
                      length=size, tag=tag, tag_hex=f'{tag:08X}',
                      payload_sha256=hashlib.sha256(payload).hexdigest())
        if tag & 0xFFFF == 0x11: record['scenario11'] = parse_scenario11(payload)
        if tag == 0x99: record['campaign99'] = parse_session99(payload)
        if tag & 0xFFFF == 0x22: record['area22'] = parse_area22(payload)
        records.append(record)
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
