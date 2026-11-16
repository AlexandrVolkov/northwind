import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('prepare_dosbox', Path(__file__).resolve().parents[1] / 'tools/prepare_dosbox.py')
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)


class SetupTests(unittest.TestCase):
    def fixture(self, root):
        game = root / 'original game'
        game.mkdir()
        (game / 'NORWIND.EXE').write_bytes(b'MZ original executable')
        (game / 'CD').write_bytes(b'')
        iso = root / 'game disc.iso'
        iso.write_bytes(b'ISO fixture')
        return game, iso

    def test_configures_letter_without_changing_source_or_executable(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            game, iso = self.fixture(root)
            destination = root / 'reference run'
            report = setup.prepare(game, iso, destination, 'e')
            self.assertEqual((game / 'CD').read_bytes(), b'')
            self.assertEqual((destination / 'game/CD').read_bytes(), b'E')
            self.assertEqual((destination / 'original-CD.bin').read_bytes(), b'')
            self.assertEqual((destination / 'game/NORWIND.EXE').read_bytes(), (game / 'NORWIND.EXE').read_bytes())
            self.assertTrue(report['executable_unchanged'])
            config = (destination / 'dosbox.conf').read_text()
            self.assertIn(f'imgmount e "{iso}" -t iso', config)
            self.assertIn('norwind.exe -nosound -noearth', config)

    def test_refuses_overwrite_and_recursive_output(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            game, iso = self.fixture(root)
            destination = root / 'existing'
            destination.mkdir()
            with self.assertRaisesRegex(ValueError, 'already exists'):
                setup.prepare(game, iso, destination)
            with self.assertRaisesRegex(ValueError, 'outside'):
                setup.prepare(game, iso, game / 'recursive')
            self.assertFalse((game / 'recursive').exists())

    def test_rejects_invalid_drive_and_config_injection(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            game, iso = self.fixture(root)
            for drive in ['C', 'D:', 'DD', 'D\nexit']:
                with self.assertRaises(ValueError):
                    setup.prepare(game, iso, root / 'new', drive)
            with self.assertRaises(ValueError):
                setup.prepare(game, iso, root / 'bad"path')
            self.assertFalse((root / 'bad"path').exists())


if __name__ == '__main__':
    unittest.main()
