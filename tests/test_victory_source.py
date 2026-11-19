import json
from pathlib import Path
import unittest


class VictorySourceChecks(unittest.TestCase):
    def test_scroll_records_preserve_source_coordinates_and_gaps(self):
        root = Path(__file__).resolve().parents[1]
        data = json.loads((root / 'data/original_victory_scroll.json').read_text())
        records = data['records']
        self.assertEqual(len(records), 22)
        self.assertEqual(data['terminator_le3'], data['first_record_le3'] + 36 * len(records))
        self.assertEqual([r['le3'] for r in records], [data['first_record_le3'] + 36 * i for i in range(22)])
        self.assertTrue(all(r['x'] == r['initial_x'] == 50 and r['y'] == r['initial_y'] for r in records))
        self.assertEqual(records[0]['text'], '    Вы встали на землю драконов, огнем и железом')
        self.assertIn('НЕТ ДАННЫХ', records[10]['text'])
        self.assertEqual(records[-1]['text'], '')
        self.assertTrue(all(r['runtime_initial'] == [0, 0, 0, 0] for r in records))


if __name__ == '__main__': unittest.main()
