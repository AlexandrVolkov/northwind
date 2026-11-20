import json
from pathlib import Path
import unittest


class MenuWidgetSourceChecks(unittest.TestCase):
    def test_native_record_boundaries_and_animation_parameters(self):
        root = Path(__file__).resolve().parents[1]
        data = json.loads((root / 'data/original_menu_widgets.json').read_text())
        records = data['widgets']
        self.assertEqual(len(records), 9)
        self.assertEqual([r['le3'] for r in records], [0xADBC, 0xADED, 0xAE1E, 0xAE4F, 0xAE7C, 0xAEA9, 0xAED6, 0xAF07, 0xAF38])
        self.assertEqual([r['le3'] + r['length'] for r in records], [r['le3'] for r in records[1:]] + [data['terminator_le3']])
        self.assertEqual([(r['sprite_base'], r['parameters'][:2]) for r in records[3:6]],
                         [(60, [30]), (122, [51]), (173, [30])])
        self.assertEqual((records[8]['sprite_base'], records[8]['parameters']), (204, [50]))
        self.assertEqual([r['action_le1'] for r in records[:3]], [0x19279, 0x19425, 0x193EB])


if __name__ == '__main__': unittest.main()
