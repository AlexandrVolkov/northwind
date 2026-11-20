import json
from pathlib import Path
import unittest


class MenuOrbSourceChecks(unittest.TestCase):
    def test_orb_transition_uses_main_menu_reset_and_sets_phase_three(self):
        root = Path(__file__).resolve().parents[1]
        native = json.loads((root / 'tests/fixtures/menu-orb.json').read_text())
        widget = json.loads((root / 'data/original_menu_widgets.json').read_text())['widgets'][6]
        self.assertEqual((native['widget_le3'], native['action_le1']), (widget['le3'], widget['action_le1']))
        self.assertEqual((native['pressed_latch'], native['frame'], native['phase']), (0, 264, 3))
        self.assertEqual(native['external_calls'], [1, 1, 1])


if __name__ == '__main__': unittest.main()
