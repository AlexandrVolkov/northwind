import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from interface_source import unpack_screen, glyphs  # noqa: E402


class InterfaceSourceTest(unittest.TestCase):
    def test_screen_rle(self):
        packed = bytes([0xc3, 7, 5, 0xc1, 0xc8, 0xc3, 1])   # 3×7, 5, literal C8h escaped as a run of one, 3×1
        data = struct.pack('<IHH', len(packed), 4, 2) + packed
        self.assertEqual(unpack_screen(data, 0), (4, 2, bytes([7, 7, 7, 5, 0xc8, 1, 1, 1])))

    def test_short_screen_is_rejected(self):
        with self.assertRaises(ValueError):
            unpack_screen(struct.pack('<IHH', 1, 4, 4) + b'\x05', 0)

    def test_glyphs(self):
        table = bytearray(512); body = bytearray()
        for code in range(256):
            struct.pack_into('<H', table, 2 * code, 512 + len(body))
            body += bytes([2, 2, 1, 9, 0]) if code == 0x41 else bytes([0, 3, 0])
        g = glyphs(bytes(table + body))
        self.assertEqual(g[0x41], (2, 2, 1, bytes([9, 0])))
        self.assertEqual(g[0x20][:3], (0, 3, 0))


if __name__ == '__main__':
    unittest.main()
