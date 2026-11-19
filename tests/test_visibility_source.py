import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
import visibility_source


class VisibilitySourceChecks(unittest.TestCase):
    def test_entire_original_viewport_matches_prepared_fog_weights(self):
        root = Path(__file__).resolve().parents[1]
        folder = root / 'assets/imported/visibility'
        reference = root / 'docs/reference'
        proof = json.loads((reference / 'fog-edge.json').read_text())
        with Image.open(reference / proof['underlay']) as image: underlay = image.copy()
        with Image.open(folder / 'fog_weights.png') as image: atlas = image.copy()
        with Image.open(folder / 'shade_indices.png') as image: shade = image.tobytes()
        with Image.open(folder / 'palette.png') as image: palette = image.tobytes()
        result = Image.new('RGB', (448, 448))
        for i, mask in enumerate(proof['corner_masks']):
            col, row = i % 14, i // 14
            pixels = underlay.crop((col * 32, row * 32, col * 32 + 32, row * 32 + 32)).tobytes()
            if mask == 15: pixels = bytes(1024)
            elif mask:
                sprite = 164 + 2 * mask + ((proof['camera'][0] + col + proof['camera'][1] + row) & 1)
                x, y = ((sprite - 166) % 8) * 32, ((sprite - 166) // 8) * 32
                weights = atlas.crop((x, y, x + 32, y + 32)).tobytes()
                pixels = bytes(shade[weight * 256 + old] for weight, old in zip(weights, pixels))
            rgb = b''.join(palette[index * 3:index * 3 + 3] for index in pixels)
            result.paste(Image.frombytes('RGB', (32, 32), rgb), (col * 32, row * 32))
        with Image.open(reference / proof['screenshot']) as shot:
            self.assertEqual(result.tobytes(), shot.convert('RGB').crop((16, 16, 464, 464)).tobytes())
        self.assertEqual(hashlib.sha256(result.tobytes()).hexdigest(), proof['viewport_rgb_sha256'])
        self.assertEqual((proof['matched_tiles'], proof['partial_tiles']), (196, 16))

    def test_prepared_assets_share_native_source_profile_and_preserve_weights(self):
        root = Path(__file__).resolve().parents[1]
        folder = root / 'assets/imported/visibility'
        manifest = json.loads((folder / 'manifest.json').read_text())
        native = json.loads((root / 'tests/fixtures/fog-raster.json').read_text())
        self.assertEqual(manifest['source_sha256'], native['source_sha256'])
        self.assertEqual([row['sprite'] for row in manifest['weights']], list(range(166, 194)))
        with Image.open(folder / manifest['weights_atlas']) as atlas:
            self.assertEqual(atlas.mode, 'L')
            for row in manifest['weights']:
                x, y, w, h = row['rectangle']
                pixels = atlas.crop((x, y, x + w, y + h)).tobytes()
                self.assertEqual(hashlib.sha256(pixels).hexdigest(), row['sha256'])
                self.assertLessEqual(max(pixels), 8)
        for name, mode, size, digest in [('shade_indices', 'L', (256, 9), 'shade_sha256'), ('palette', 'RGB', (256, 1), 'palette_sha256')]:
            with Image.open(folder / manifest[name]) as image:
                self.assertEqual((image.mode, image.size), (mode, size))
                self.assertEqual(hashlib.sha256(image.tobytes()).hexdigest(), manifest[digest])

    def test_converter_rejects_an_unknown_source_profile(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            for name in ['NORWIND.EXE', 'WORK.SPR', 'FADE_COL.TAB', 'COLOR1.PAL']:
                (folder / name).write_bytes(b'wrong-profile')
            with self.assertRaisesRegex(ValueError, 'Unknown EXE profile'):
                visibility_source.convert(folder, folder / 'out')
            self.assertFalse((folder / 'out').exists())

    def test_converter_rejects_corrupt_weights_before_export(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            (folder / 'NORWIND.EXE').write_bytes(b'profile')
            (folder / 'WORK.SPR').write_bytes(b'bank')
            (folder / 'FADE_COL.TAB').write_bytes(bytes(8192))
            (folder / 'COLOR1.PAL').write_bytes(bytes(768))
            records = [bytes(1024)] * 400
            records[193] = b'\x09' * 1024
            with patch.object(visibility_source, 'EXE_SHA256', hashlib.sha256(b'profile').hexdigest()), patch.object(visibility_source, 'spr_records', return_value=records):
                with self.assertRaisesRegex(ValueError, 'Invalid WORK fog table 193'):
                    visibility_source.convert(folder, folder / 'out')
            self.assertFalse((folder / 'out').exists())


if __name__ == '__main__':
    unittest.main()
