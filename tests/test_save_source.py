import json
from pathlib import Path
import struct
import unittest
from tools.save_source import parse, parse_area22


class SaveSourceChecks(unittest.TestCase):
    def test_tagged_records_and_payload_bounds(self):
        file = struct.pack('<I', 3) + struct.pack('<II', 3, 0x11) + b'abc' + \
               struct.pack('<II', 0, 0x55) + struct.pack('<II', 4, 0x44) + b'xyz!'
        result = parse(file)
        self.assertEqual([(r['header_offset'], r['payload_offset'], r['length'], r['tag']) for r in result['records']],
                         [(4, 12, 3, 0x11), (15, 23, 0, 0x55), (23, 31, 4, 0x44)])
        for invalid in (file[:-1], file + b'!', b'\0\0\0', struct.pack('<I', 4097)):
            with self.assertRaises(ValueError): parse(invalid)

    def test_area22_descriptor_and_bucket_bounds(self):
        descriptor = struct.pack('<16I', 0, 0, 0, 0, 80, *([0] * 11))
        payload = bytes(0xF50) + descriptor + bytes(4 * 0x2000) + bytes(range(80)) + bytes(0x2000 - 80)
        result = parse_area22(payload)
        self.assertEqual((result['bucket_count'], result['bucket_size']), (5, 0x2000))
        self.assertEqual(result['descriptor_counts'][4], 80)
        for invalid in (payload[:-1], bytes(0xF50), bytes(0xF50) + struct.pack('<I', 0x2001) + bytes(60 + 0x2000)):
            with self.assertRaises(ValueError): parse_area22(invalid)

    def test_recovered_original_layout_has_no_gaps(self):
        root = Path(__file__).resolve().parents[1]
        data = json.loads((root / 'data/original_save_layout.json').read_text())
        records = data['records']
        self.assertEqual([r['tag'] for r in records], [0x11, 0x99, 0x01000022, 0x00010022])
        self.assertEqual([r['length'] for r in records], [988085, 25893, 69520, 69520])
        self.assertEqual(records[0]['header_offset'], 4)
        self.assertEqual([r['payload_offset'] + r['length'] for r in records[:-1]],
                         [r['header_offset'] for r in records[1:]])
        self.assertEqual(records[-1]['payload_offset'] + records[-1]['length'], data['file_size'])
        section = records[1]['campaign99']
        self.assertEqual(section['region_count'], 12)
        self.assertEqual(section['region_record_size'], 0x841)
        self.assertEqual(len(section['region_sha256']), 12)
        self.assertEqual(25 + section['table_length'] + section['region_count'] * 0x841, records[1]['length'])
        for area in records[2:]:
            block = area['area22']
            self.assertEqual(block['bucket_count'], 8)
            self.assertEqual(block['descriptor_counts'][4], 80)
            self.assertEqual(0xF50 + 0x40 + 8 * 0x2000, area['length'])


if __name__ == '__main__': unittest.main()
