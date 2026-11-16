import importlib.util
from pathlib import Path
import unittest
from PIL import Image
spec = importlib.util.spec_from_file_location('match_reference', Path(__file__).resolve().parents[1] / 'tools/match_reference.py')
matcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(matcher)


class ReferenceTests(unittest.TestCase):
    def test_camera_intersection(self):
        image = Image.new('RGB', (60, 60))
        image.putdata([(x, y, x*y % 256) for y in range(60) for x in range(60)])
        screenshot = Image.new('RGB', (40, 40))
        screenshot.paste(image.crop((10, 12, 42, 44)), (2, 3))
        report = matcher.match_patches(image, screenshot, (2, 3, 32, 32), [(1, 2), (12, 10)], size=4)
        self.assertEqual(report['status'], 'unique')
        self.assertEqual(report['camera_candidates'], [[10, 12]])

    def test_ambiguity_and_no_match_are_not_success(self):
        image = Image.new('RGB', (10, 10))
        report = matcher.match_patches(image, image, (0, 0, 10, 10), [(0, 0)], size=2)
        self.assertEqual(report['status'], 'ambiguous')
        different = Image.new('RGB', (10, 10), 'white')
        self.assertEqual(matcher.match_patches(image, different, (0, 0, 10, 10), [(0, 0)], size=2)['status'], 'no_match')

    def test_bounds_and_dac_conversion(self):
        image = Image.new('RGB', (10, 10), (66, 9, 255))
        self.assertEqual(matcher.dac6(image).getpixel((0, 0)), (65, 8, 255))
        with self.assertRaises(ValueError):
            matcher.match_patches(image, image, (1, 1, 10, 10), [(0, 0)], size=2)
        with self.assertRaises(ValueError):
            matcher.match_patches(image, image, (0, 0, 10, 10), [(9, 9)], size=2)
        with self.assertRaises(ValueError):
            matcher.find_patch_positions(image, image.crop((0, 0, 2, 2)), limit=1)


if __name__ == '__main__':
    unittest.main()
