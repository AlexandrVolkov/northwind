import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from tech_source import decode  # noqa: E402


class TechSourceTest(unittest.TestCase):
    def test_decode_records_children_and_text(self):
        first = bytes([1, 3, 2]) + struct.pack('<II', 0, 0) + bytes(4) + bytes([1]) + bytes([0x80, 0x0f])
        second = bytes([0x80, 0x0f, 1]) + struct.pack('<II', 13 + 2, 500) + bytes(4) + bytes([0])
        data = first + second + b'\xff\xff' + 'Огонь'.encode('cp866') + b'\xff'
        records, end = decode(data)
        self.assertEqual(end, len(first) + len(second))
        self.assertEqual([(r['kind'], r['id'], r['unmet']) for r in records], [(1, 3, 2), (0x80, 15, 1)])
        self.assertEqual(records[0]['children'], [[0x80, 0x0f]])
        self.assertEqual(records[1]['cost'], 500)
        self.assertEqual(records[1]['text'], 'Огонь')


if __name__ == '__main__':
    unittest.main()
