#!/usr/bin/env python3
"""Map the shipped LE code and verify the DAT -> WRK dispatch chain.

This is a static, version-specific analysis, not a relocated executable loader.
Original bytes and disassembly are written only when explicitly requested locally.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
from import_game import decode_templates

EXE_SHA256 = 'eb52f043143d763f5a595ae2175249976f278d1eebf0336ae032caf7bfa0c3a8'
INNER_MZ = 217764


def read_le_objects(data, mz_offset):
    def require(start, count):
        if start < 0 or count < 0 or start + count > len(data):
            raise ValueError('Truncated LE structure or page')

    def u32(offset):
        require(offset, 4)
        return struct.unpack_from('<I', data, offset)[0]

    require(mz_offset, 64)
    if data[mz_offset:mz_offset+2] != b'MZ':
        raise ValueError('Expected containing MZ')
    le = mz_offset + u32(mz_offset + 0x3c)
    require(le, 0xb0)
    if data[le:le+4] != b'LE\0\0':
        raise ValueError('Expected little-endian LE')
    page_count = u32(le + 0x14)
    page_size = u32(le + 0x28)
    last_page_size = u32(le + 0x2c) or page_size
    object_count = u32(le + 0x44)
    if not (0 < page_size <= 65536 and 0 < last_page_size <= page_size and 0 < object_count <= 256):
        raise ValueError('Invalid LE counts or page size')
    table = le + u32(le + 0x40)
    page_map = le + u32(le + 0x48)
    page_data = mz_offset + u32(le + 0x80)
    require(table, object_count * 24)
    require(page_map, page_count * 4)
    objects = []
    for index in range(object_count):
        size, base, flags, first, count, reserved = struct.unpack_from('<6I', data, table + index*24)
        if size > 64*1024*1024 or (count and (first < 1 or first-1+count > page_count)) or count*page_size > size + page_size:
            raise ValueError('Invalid LE object page range')
        payload = bytearray(size)
        pages = []
        for ordinal in range(count):
            entry = page_map + (first-1+ordinal)*4
            physical = int.from_bytes(data[entry:entry+3], 'big')
            if data[entry+3] != 0 or not 1 <= physical <= page_count:
                raise ValueError('Unsupported LE page encoding')
            offset = page_data + (physical-1)*page_size
            length = last_page_size if physical == page_count else page_size
            require(offset, length)
            length = min(length, size-ordinal*page_size)
            if length < 0:
                raise ValueError('LE page exceeds object')
            payload[ordinal*page_size:ordinal*page_size+length] = data[offset:offset+length]
            pages.append({'object_offset': ordinal*page_size, 'file_offset': offset, 'bytes': length})
        objects.append({'index': index+1, 'size': size, 'preferred_base': base, 'flags': flags,
                        'is_32_bit': bool(flags & 0x2000), 'pages': pages, 'payload': bytes(payload)})
    return le, objects


def fs_initializers(code, start=0x28a9, end=0x2cd7):
    """The verified initialization block is a contiguous sequence of fixed MOVs.

    Do not scan arbitrary instruction/data bytes for accidental opcode matches.
    This is a table of assignments, not an assertion that every slot
    represents an engine function; the target type needs its use sites.
    """
    if not 0 <= start < end <= len(code) or (end-start) % 10:
        raise ValueError('Invalid initializer bounds')
    result = []
    for offset in range(start, end, 10):
        if code[offset:offset+4] != b'\x64\x67\xc7\x06':
            raise ValueError('Unexpected FS initializer instruction')
        slot, target = struct.unpack_from('<HI', code, offset+4)
        result.append({'code_offset': offset, 'fs_slot': slot, 'target_offset': target})
    return result


def analyze(game):
    files = {p.name.upper(): p for p in game.iterdir() if p.is_file()}
    data = files['NORWIND.EXE'].read_bytes()
    if hashlib.sha256(data).hexdigest() != EXE_SHA256:
        raise ValueError('Unknown EXE version; analysis profile does not apply')
    le, objects = read_le_objects(data, INNER_MZ)
    code = objects[0]['payload']
    # Guards retain the precise code sites used to infer each data offset.
    guards = {
        'dat_header_and_stride': (0x3593, '0fb70283c20289570403d089570db2abf6f2884755'),
        'wrk_base': (0x35b9, '8b15311f0000895709'),
        'wrk_dispatch': (0x433a, '034f3b890dc3190000b40156ff15c3190000'),
    }
    evidence = {}
    for name, (offset, expected) in guards.items():
        if code[offset:offset+len(bytes.fromhex(expected))] != bytes.fromhex(expected):
            raise ValueError(f'Code evidence mismatch: {name}')
        evidence[name] = {'code_offset': offset, 'length': len(bytes.fromhex(expected))}
    armies = []
    hashes = {'NORWIND.EXE': EXE_SHA256}
    for filename in sorted(name for name in files if name.startswith('ARMY__') and name.endswith('.DAT')):
        dat = files[filename].read_bytes()
        wrk_name = filename.replace('.DAT', '.WRK')
        wrk = files[wrk_name].read_bytes()
        hashes[filename] = hashlib.sha256(dat).hexdigest()
        hashes[wrk_name] = hashlib.sha256(wrk).hexdigest()
        templates = decode_templates(dat)['templates']
        entries = sorted(set(t['wrk_entry_offset'] for t in templates))
        if any(offset >= len(wrk) for offset in entries):
            raise ValueError(f'WRK callback outside file: {filename}')
        armies.append({'dat': filename, 'wrk': wrk_name, 'template_count': len(templates),
                       'callbacks': [{'template_index': t['template_index'], 'name': t['name'],
                                      'dat_record_offset': t['file_offset'], 'wrk_offset': t['wrk_entry_offset']}
                                     for t in templates], 'unique_entry_count': len(entries)})
    report = {'format_version': 1, 'source_sha256': hashes, 'inner_mz_offset': INNER_MZ,
              'le_offset': le, 'objects': [{k:v for k,v in obj.items() if k != 'payload'} for obj in objects],
              'code_evidence': evidence, 'fs_assignments': fs_initializers(code), 'armies': armies,
              'limitations': ['No relocations applied; addresses are object-relative, not runtime linear addresses.',
                              'No complete control-flow or function recovery; WRK embeds data between instructions.',
                              'FS target types and meanings require individual use-site analysis.']}
    return report, code


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--code-output', type=Path, help='Optional local unrelocated code for objdump; never commit')
    args = parser.parse_args()
    report, code = analyze(args.game)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    if args.code_output:
        args.code_output.write_bytes(code)
    print(f"Verified {sum(a['template_count'] for a in report['armies'])} DAT -> WRK links; {len(report['fs_assignments'])} FS assignments")


if __name__ == '__main__':
    main()
