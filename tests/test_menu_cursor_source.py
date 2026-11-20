import json
from pathlib import Path
import unittest


class MenuCursorSourceChecks(unittest.TestCase):
    def test_native_families_are_disjoint_and_cover_the_menu_cursor_sets(self):
        root = Path(__file__).resolve().parents[1]
        data = json.loads((root / 'tests/fixtures/menu-cursor.json').read_text())
        self.assertEqual([(x['name'], x['first_sprite'], x['last_sprite']) for x in data['cursor_families']],
                         [('main', 0, 14), ('cabinet', 15, 29), ('campaign', 30, 44),
                          ('victory', 45, 59), ('authors', 307, 321)])
        self.assertTrue(all((x['center_x'], x['center_y']) == (320, 240) for x in data['cursor_families']))


if __name__ == '__main__': unittest.main()
