import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from reference_frames import composite, render_observations


class ObservationTests(unittest.TestCase):
    def sample(self):
        return {'image': 'observed_test.png', 'spr': 'TEST.SPR', 'table': 'TEST.TAB',
                'frame': 0, 'size': [2, 2], 'camera': [2, 2], 'viewport_size': [4, 4],
                'world_top_left': [3, 3], 'exclude': [[1, 1, 1, 1]],
                'compared_opaque_pixels': 2}

    def palette(self):
        palette = bytearray(768)
        palette[3:9] = bytes([66, 9, 255, 0, 255, 0])
        return bytes(palette)

    def test_composition_uses_world_camera_table_dac_and_explicit_mask(self):
        terrain = Image.new('RGB', (8, 8), (20, 30, 40))
        table = bytearray(range(256)); table[1], table[2] = 2, 1
        result = composite(terrain, [1, 2, -1, 2], self.palette(), table, self.sample())
        self.assertEqual(result.size, (4, 4))
        self.assertEqual(result.getpixel((1, 1)), (0, 255, 0))
        self.assertEqual(result.getpixel((2, 1)), (65, 8, 255))
        self.assertEqual(result.getpixel((1, 2)), (20, 30, 40))
        self.assertEqual(result.getpixel((2, 2)), (20, 30, 40))
        self.assertEqual(terrain.getpixel((3, 3)), (20, 30, 40))
        bad_count = self.sample() | {'compared_opaque_pixels': 3}
        with self.assertRaisesRegex(ValueError, 'mask differs'):
            composite(terrain, [1, 2, -1, 2], self.palette(), table, bad_count)
        with self.assertRaisesRegex(ValueError, 'viewport outside'):
            composite(terrain, [1, 2, -1, 2], self.palette(), table,
                      self.sample() | {'camera': [6, 6]})

    def test_verified_source_gate_rejects_changes_and_hides_stale_samples(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); source = root/'source'; source.mkdir()
            output = root/'output'; output.mkdir()
            for name, data in {'TEST.SPR': b'frame', 'TEST.TAB': bytes(range(256)),
                               'COLOR1.PAL': self.palette()}.items():
                (source/name).write_bytes(data)
            spec = {'map_number': 1, 'samples': [self.sample()], 'source_sha256': {
                p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.iterdir()}}
            decoder = Mock(return_value=[1, 2, -1, 2])
            with patch('reference_frames.load_map', return_value=Image.new('RGB', (8, 8))):
                result = render_observations(source, output, 1, decoder, spec)
            self.assertEqual(result['status'], 'ready')
            self.assertTrue((output/'observed_test.png').exists())
            decoder.reset_mock()
            (source/'TEST.SPR').write_bytes(b'changed')
            for map_number in [1, 2]:
                result = render_observations(source, output, map_number, decoder, spec)
                self.assertEqual(result['status'], 'unavailable')
                self.assertEqual(result['samples'], [])
                self.assertEqual(json.loads((output/'observations.json').read_text())['status'], 'unavailable')
            decoder.assert_not_called()


if __name__ == '__main__':
    unittest.main()
