"""Small synthetic format tests; real game data is optional and never committed."""
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
from PIL import Image

spec = importlib.util.spec_from_file_location('import_game', Path(__file__).resolve().parents[1] / 'tools/import_game.py')
importer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(importer)


def spr(records):
    offsets, payload = [], b''
    for record in records:
        offsets.append(len(payload).to_bytes(3, 'little'))
        payload += record
    return struct.pack('<H', len(records)) + b''.join(offsets) + payload


class ImportTests(unittest.TestCase):
    def test_transparency_and_literals(self):
        self.assertEqual(importer.decode_spr(spr([b'\x02\x83\x10\x11\x12\x01'])),
                         [[-1, -1, 16, 17, 18, -1]])

    def test_rejects_truncated_literal(self):
        with self.assertRaisesRegex(ValueError, 'literal'):
            importer.decode_spr(spr([b'\x83\x01']))

    def test_rejects_bad_offset(self):
        with self.assertRaises(ValueError):
            importer.decode_spr(b'\x01\x00\xff\xff\xff')

    def test_import_preserves_high_bits_and_pixel_coordinates(self):
        with tempfile.TemporaryDirectory() as temp:
            source, output = Path(temp) / 'game', Path(temp) / 'out'
            source.mkdir()
            palette = bytearray(768)
            palette[3:6] = b'\x10\x80\x20'
            palette[6:9] = b'\x10\x20\x80'
            (source / 'COLOR1.PAL').write_bytes(palette)
            (source / 'MAP_1.MAP').write_bytes(b'MAP2MAPSEG01.SEG\0' + struct.pack('<II2I', 2, 1, 0x10000, 1))
            header = b'SEG2' + struct.pack('<I', 2) + bytes(13)
            (source / 'MAPSEG01.SEG').write_bytes(header + bytes([1, 2]) + bytes(16) + bytes([1]) * 1024 + bytes([2]) * 1024 + b'unknown')
            # 3844 transparent pixels = 30*127 + 34, represented as skip runs.
            frame = bytes([127]) * 30 + bytes([34])
            (source / 'ARMY__01.SPR').write_bytes(spr([frame] * 37))
            manifest = importer.import_directory(source, output)
            import json
            data = json.loads((output / 'map.json').read_text())
            self.assertEqual(data['cells'], [0x10000, 1])
            self.assertEqual(data['segment_unknown_tail_bytes'], 7)
            self.assertEqual(data['terrain'], ['ground', 'water'])
            self.assertEqual(manifest['decoded_sprite_records'], 37)
            with Image.open(output / 'terrain.png') as image:
                self.assertEqual(image.getpixel((0, 0)), (16, 128, 32))
                self.assertEqual(image.getpixel((32, 0)), (16, 32, 128))


if __name__ == '__main__':
    unittest.main()
