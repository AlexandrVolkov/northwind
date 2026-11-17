"""Bundled minimap colors must reproduce the original imported minimap."""
import json
from pathlib import Path
import unittest
from PIL import Image


class TerrainColorsTests(unittest.TestCase):
    def test_all_bundled_cells_keep_the_verified_minimap_colors(self):
        root = Path(__file__).resolve().parents[1] / 'assets/imported'
        data = json.loads((root / 'map.json').read_text())
        colors = json.loads((root / 'terrain_colors.json').read_text())
        self.assertEqual(colors['format_version'], 1)
        self.assertEqual(len(colors['colors']), len(bytes.fromhex(data['segment_properties_hex'])) // 8)
        with Image.open(root / 'minimap.png') as image:
            self.assertEqual(image.size, (data['width'], data['height']))
            for index, cell in enumerate(data['cells']):
                self.assertEqual(tuple(colors['colors'][cell & 65535]), image.getpixel((index % data['width'], index // data['width'])))


if __name__ == '__main__':
    unittest.main()
