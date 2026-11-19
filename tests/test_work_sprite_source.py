import hashlib
import json
from pathlib import Path
import sys
import unittest
from PIL import Image, ImageChops

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from work_sprite_source import decode


class WorkSpriteSourceTest(unittest.TestCase):
    def test_raw_is_opaque_and_not_mistaken_for_rle(self):
        palette = bytes(v for i in range(256) for v in [i, i, i])
        image = decode(b'\x00\x81\x02\xff', dict(width=2, height=2, encoding='raw'), palette)
        self.assertEqual(image.tobytes(), bytes([0, 0, 0, 255, 129, 129, 129, 255, 2, 2, 2, 255, 255, 255, 255, 255]))
        with self.assertRaises(ValueError): decode(b'\0', dict(width=2, height=2, encoding='raw'), palette)

    def test_rle_keeps_transparent_runs(self):
        palette = bytes(v for i in range(256) for v in [i, i, i])
        image = decode(b'\x01\x81\x02', dict(width=2, height=1, encoding='sprite_rle'), palette)
        self.assertEqual(image.tobytes(), bytes([0, 0, 0, 0, 2, 2, 2, 255]))

    def test_prepared_layouts_keep_unconfirmed_record_out(self):
        folder = Path(__file__).resolve().parents[1] / 'assets/imported/work_large'
        manifest = json.loads((folder / 'manifest.json').read_text())
        self.assertEqual(len(manifest['records']), 59)
        self.assertEqual([r['sprite'] for r in manifest['unresolved']], [358])
        self.assertFalse((folder / '358.png').exists())
        for record in manifest['records']:
            with Image.open(folder / record['file']) as image:
                self.assertEqual(image.size, (record['width'], record['height']))
                self.assertEqual(hashlib.sha256(image.convert('RGBA').tobytes()).hexdigest(), record['rgba_sha256'])
            if record['encoding'] == 'raw': self.assertEqual(record['record_bytes'], record['width'] * record['height'])

    def test_all_pngs_match_native_blitter_fixture(self):
        root = Path(__file__).resolve().parents[1]
        fixture = json.loads((root / 'tests/fixtures/work-sprites.json').read_text())
        for record in fixture['records']:
            with Image.open(root / f'assets/imported/work_large/{record["sprite"]:03d}.png') as image:
                self.assertEqual(image.size, (record['width'], record['height']))
                self.assertEqual(hashlib.sha256(image.convert('RGBA').tobytes()).hexdigest(), record['rgba_sha256'])

    def test_raw_widgets_match_original_dosbox_pixels(self):
        root = Path(__file__).resolve().parents[1]
        folder = root / 'assets/imported/work_large'
        records = {r['sprite']: r for r in json.loads((folder / 'manifest.json').read_text())['records']}
        for filename, ids in [('original-help-00.png', [228, 230]),
                              ('original-work-menu-panel.png', [316, 318, 320, 322, 324, 326, 328, 348, 350, 351, 354])]:
            with Image.open(root / 'docs/reference' / filename) as screenshot:
                for number in ids:
                    record = records[number]; x, y = record['position']
                    crop = screenshot.convert('RGB').crop((x, y, x + record['width'], y + record['height']))
                    with Image.open(folder / record['file']) as image:
                        self.assertIsNone(ImageChops.difference(crop, image.convert('RGB')).getbbox(), f'RAW sprite {number} differs from original')


if __name__ == '__main__':
    unittest.main()
