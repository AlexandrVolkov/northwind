import json
from pathlib import Path
import unittest


class OrderButtonsSourceChecks(unittest.TestCase):
    def setUp(self):
        root = Path(__file__).resolve().parents[1]
        self.catalog = json.loads((root / 'data/original_order_buttons.json').read_text())
        self.fixture = json.loads((root / 'tests/fixtures/order-dispatch.json').read_text())

    def test_all_mode_and_wrk_routes_match_native_execution(self):
        self.assertEqual(self.catalog['source_sha256'], self.fixture['wrk_source_sha256'])
        self.assertEqual(self.catalog['mode_targets'], self.fixture['destinations'])
        for template, state, target in self.fixture['wrk_targets']:
            self.assertEqual(self.catalog['templates'][template]['states'][state]['wrk_target'], target)
        self.assertEqual(len(self.fixture['wrk_targets']), 162)

    def test_icons_are_not_command_identifiers(self):
        templates = self.catalog['templates']
        forest = templates[10]['commands'][3]
        delivery = templates[11]['commands'][4]
        self.assertEqual((forest['icon_frame'], delivery['icon_frame']), (1172, 1172))
        self.assertEqual((forest['mode'], delivery['mode']), (4, 0))
        self.assertEqual((forest['label'], delivery['label']), ('Рубить лес', 'Отнести бревно'))
        castle = templates[2]['commands']
        self.assertEqual(castle[3]['icon_frame'], castle[4]['icon_frame'])
        self.assertNotEqual(castle[3]['mode'], castle[4]['mode'])

    def test_gate_and_hero_keep_original_slots_and_absent_entries(self):
        gate = self.catalog['templates'][14]
        self.assertEqual(gate['dispatcher']['kind'], 'custom_gate')
        self.assertEqual([c['icon_frame'] for c in gate['commands'] if c['present']], [1627, 1628, 1625])
        self.assertEqual([c['queued_state'] for c in gate['commands'] if c['present']], [2, 3, 4])
        hero = self.catalog['templates'][20]
        self.assertEqual(len(hero['commands']), 6)
        self.assertEqual([c['mode'] for c in hero['commands']], [0, 1, 2, 0, 5, 11])
        self.assertEqual([c['slot'] for c in hero['commands'] if 7 & (1 << c['slot'])], [0, 1, 2])


if __name__ == '__main__':
    unittest.main()
