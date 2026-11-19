import hashlib
import json
from pathlib import Path
import unittest
from PIL import Image, ImageChops, ImageDraw


class CreditsSourceChecks(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).resolve().parents[1]
        self.folder = self.root / 'assets/imported/credits'
        self.manifest = json.loads((self.folder / 'manifest.json').read_text())

    def test_full_authors_background_and_widgets_match_original_except_cursor(self):
        with Image.open(self.folder / 'background.png') as image: expected = image.convert('RGBA')
        for widget, frame in zip(self.manifest['widgets'], [280, 283, 286, 295, 289, 292]):
            with Image.open(self.folder / f'{frame:03d}.png') as image:
                expected.alpha_composite(image.convert('RGBA'), tuple(widget['position']))
        expected = expected.convert('RGB')
        with Image.open(self.root / 'docs/reference/original-credits-empty.png') as image: actual = image.convert('RGB')
        # Only the independently identified 32x32 animated mouse cursor is excluded.
        for image in [actual, expected]: ImageDraw.Draw(image).rectangle((600, 350, 631, 381), fill=(0, 0, 0))
        self.assertIsNone(ImageChops.difference(actual, expected).getbbox())

    def test_three_complete_rule_pages_match_actual_original_pixels(self):
        for page in [0, 3, 9]:
            with Image.open(self.root / f'docs/reference/original-credits-rules-{page:02d}.png') as shot:
                actual = shot.convert('RGB').crop((224, 90, 544, 270))
            with Image.open(self.folder / f'{297 + page:03d}.png') as image: expected = image.convert('RGB')
            self.assertEqual(actual.tobytes(), expected.tobytes(), f'Original rule page {page}')

    def test_every_sprite_matches_native_large_width_renderer(self):
        native = json.loads((self.root / 'tests/fixtures/credits.json').read_text())
        for entry in native['sprites']:
            with Image.open(self.folder / f'{entry["sprite"]:03d}.png') as image:
                self.assertEqual(image.size, (entry['width'], entry['height']))
                self.assertEqual(hashlib.sha256(image.convert('RGBA').tobytes()).hexdigest(), entry['rgba_sha256'])
        self.assertEqual(len(native['sprites']), 27)

    def test_pending_palettes_do_not_become_runtime_screens(self):
        self.assertEqual([x['screen'] for x in self.manifest['pending_screens']], [8, 13])
        self.assertTrue(all(not x['screenshot_verified'] for x in self.manifest['pending_screens']))
        self.assertFalse((self.folder / 'screen-8.png').exists())
        self.assertFalse((self.folder / 'screen-13.png').exists())


if __name__ == '__main__':
    unittest.main()
