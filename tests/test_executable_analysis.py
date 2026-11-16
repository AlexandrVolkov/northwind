import struct
import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from analyze_executable import read_le_objects, fs_initializers


class LeMappingTests(unittest.TestCase):
    def fixture(self):
        data = bytearray(128 + 0x300 + 24)
        mz, le = 128, 192
        data[mz:mz+2] = b'MZ'
        struct.pack_into('<I', data, mz+0x3c, le-mz)
        data[le:le+4] = b'LE\0\0'
        for offset, value in {0x14: 2, 0x28: 16, 0x2c: 8, 0x40: 0xb0,
                              0x44: 1, 0x48: 0xc8, 0x80: 0x300}.items():
            struct.pack_into('<I', data, le+offset, value)
        struct.pack_into('<6I', data, le+0xb0, 40, 0x10000, 0x2005, 1, 2, 0)
        data[le+0xc8:le+0xd0] = b'\0\0\2\0\0\0\1\0'
        data[mz+0x300:] = bytes(range(24))
        return data

    def test_nested_offsets_page_order_short_last_page_and_unmapped_tail(self):
        le, objects = read_le_objects(self.fixture(), 128)
        self.assertEqual(le, 192)
        self.assertEqual(objects[0]['payload'], bytes(range(16,24)) + bytes(8) + bytes(range(16)) + bytes(8))
        self.assertTrue(objects[0]['is_32_bit'])
        self.assertEqual(objects[0]['pages'][0]['file_offset'], 128+0x310)

    def test_truncation_and_unsupported_pages_fail_closed(self):
        with self.assertRaisesRegex(ValueError, 'Truncated'):
            read_le_objects(self.fixture()[:-1], 128)
        bad = self.fixture(); bad[192+0xcb] = 1
        with self.assertRaisesRegex(ValueError, 'Unsupported'):
            read_le_objects(bad, 128)
        bad = self.fixture(); struct.pack_into('<I', bad, 192+0xb0+12, 3)
        with self.assertRaisesRegex(ValueError, 'page range'):
            read_le_objects(bad, 128)

    def test_only_contiguous_fs_initializer_instructions_are_accepted(self):
        instructions = b''.join(b'\x64\x67\xc7\x06' + struct.pack('<HI', slot, target)
                                for slot, target in [(0x41, 0xc88c), (0xc, 0x1c8e)])
        result = fs_initializers(instructions, 0, len(instructions))
        self.assertEqual([r['target_offset'] for r in result], [0xc88c, 0x1c8e])
        with self.assertRaisesRegex(ValueError, 'Unexpected'):
            fs_initializers(b'\x90' + instructions[1:], 0, len(instructions))


if __name__ == '__main__':
    unittest.main()
