import struct
import json
import sys
import unittest
from pathlib import Path
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from interface_source import unpack_screen, glyphs, spr_records, spr_image  # noqa: E402


class InterfaceSourceTest(unittest.TestCase):
    def test_bundled_arrow_frames_are_complete(self):
        root = Path(__file__).resolve().parents[1] / 'assets/imported/interface'
        cursor = json.loads((root / 'interface.json').read_text())['arrow_cursor']
        self.assertEqual(cursor['sprites'], list(range(15)))
        self.assertEqual(cursor['cell'], [32, 32])
        with Image.open(root / cursor['atlas']) as atlas:
            self.assertEqual(atlas.size, (256, 64))
            for k in range(15):
                frame = atlas.crop((k % 8 * 32, k // 8 * 32, k % 8 * 32 + 32, k // 8 * 32 + 32))
                self.assertIsNotNone(frame.getchannel('A').getbbox())

    def test_packaged_command_metadata_matches_verified_dat_import(self):
        root = Path(__file__).resolve().parents[1]
        interface = json.loads((root / 'assets/imported/interface/interface.json').read_text())
        templates = json.loads((root / 'assets/imported/templates.json').read_text())['armies']['ARMY__02.DAT']['templates']
        self.assertEqual(interface['format_version'], 3)
        for panel, template in zip(interface['armies'][0]['templates'], templates):
            self.assertEqual(len(panel['slots']), 6)
            for slot, command in zip(panel['slots'], template['commands']):
                self.assertNotIn('handler', slot)  # The relative pointer is text, not executable WRK.
                self.assertEqual(slot['sprite'], command['icon_frame'])
                for key in ['present', 'mode', 'raw_group', 'label', 'hotkey_scan_code']:
                    self.assertEqual(slot[key], command[key])

    def test_screen_rle(self):
        packed = bytes([0xc3, 7, 5, 0xc1, 0xc8, 0xc3, 1])   # 3×7, 5, literal C8h escaped as a run of one, 3×1
        data = struct.pack('<IHH', len(packed), 4, 2) + packed
        self.assertEqual(unpack_screen(data, 0), (4, 2, bytes([7, 7, 7, 5, 0xc8, 1, 1, 1])))

    def test_short_screen_is_rejected(self):
        with self.assertRaises(ValueError):
            unpack_screen(struct.pack('<IHH', 1, 4, 4) + b'\x05', 0)

    def test_spr_with_omitted_trailing_transparency(self):
        record = bytes([1, 0x82, 5, 6, 0x81, 7])   # 1 clear, 2 pixels, then 1 pixel; the rest of 2x3 is omitted
        data = struct.pack('<H', 1) + (0).to_bytes(3, 'little') + record
        [only] = spr_records(data)
        im = spr_image(only, 2, 3, bytes(v for i in range(256) for v in (i, i, i)))
        self.assertEqual([im.getpixel((x, y))[3] for y in range(3) for x in range(2)], [0, 255, 255, 255, 0, 0])
        self.assertEqual(im.getpixel((0, 1))[0], 6)

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
