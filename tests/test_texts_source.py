import hashlib
import json
from pathlib import Path
import struct
import sys
import unittest
from PIL import Image, ImageChops

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from texts_source import messages, pictures, text_runs, unpack_rle


class TextsSourceTest(unittest.TestCase):
    def test_messages_keep_cp866_offsets_and_inline_color(self):
        raw = b'[ERRORS]\r\n[90] ~\xd0' + 'Нет файла!'.encode('cp866') + b'\r\n[01] OK\r\n'
        parsed = messages(raw)
        first = parsed['messages'][0]
        self.assertEqual(first['code'], 0x90)
        self.assertEqual(first['source_offset'], len(b'[ERRORS]\r\n'))
        self.assertEqual(first['text'], ' Нет файла!')
        self.assertEqual(first['runs'], [dict(palette_offset=0, text=' '), dict(palette_offset=208, text='Нет файла!')])
        self.assertEqual(first['original_text'].encode('cp866'), b' ~\xd0' + 'Нет файла!'.encode('cp866'))
        self.assertEqual(parsed['message_count'], 2)
        self.assertEqual(parsed['unparsed_lines'], [])
        with self.assertRaises(ValueError): messages(b'[01] first\n[01] duplicate')
        with self.assertRaises(ValueError): text_runs(b'dangling~')

    def test_picture_rle_and_trailer(self):
        packed = b'\xc4a\x0c'
        data = struct.pack('<HIIHH', 1, 6, len(packed), 2, 2) + packed
        decoded = pictures(data)
        self.assertEqual(decoded[0]['pixels'], b'aaaa')
        self.assertEqual(decoded[0]['trailer_hex'], '0c')
        self.assertEqual(unpack_rle(b'\xc0x', 256), (b'x' * 256, b''))
        for malformed in [data[:-1], b'\0', struct.pack('<H', 0)]:
            with self.assertRaises(ValueError): pictures(malformed)
        with self.assertRaises(ValueError): unpack_rle(b'\xc1', 1)
        with self.assertRaises(ValueError): unpack_rle(b'\xc5a', 4)
        with self.assertRaises(ValueError): unpack_rle(b'a', 4)

    def test_prepared_help_pages_preserve_every_rgb_pixel(self):
        root = Path(__file__).resolve().parents[1]
        folder = root / 'assets/imported/help'
        manifest = json.loads((folder / 'manifest.json').read_text())
        self.assertEqual(len(manifest['pages']), 32)
        for page in manifest['pages']:
            with Image.open(folder / page['file']) as image:
                self.assertEqual(image.size, (448, 448))
                self.assertEqual(hashlib.sha256(image.convert('RGB').tobytes()).hexdigest(), page['rgb_sha256'])
            self.assertEqual(page['trailer_hex'], '0c')

    def test_help_palette_matches_actual_dosbox_screenshots(self):
        root = Path(__file__).resolve().parents[1]
        for page in [0, 6, 31]:
            with Image.open(root / f'docs/reference/original-help-{page:02d}.png') as screenshot:
                crop = screenshot.convert('RGB').crop((16, 16, 464, 464))
            with Image.open(root / f'assets/imported/help/page_{page:02d}.png') as exported:
                self.assertIsNone(ImageChops.difference(crop, exported.convert('RGB')).getbbox())


if __name__ == '__main__':
    unittest.main()
