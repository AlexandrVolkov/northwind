import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from episode_source import parse_episode


class EpisodeSourceTests(unittest.TestCase):
    def test_bundled_game_topology(self):
        episodes = json.loads((ROOT / 'data/original_episodes.json').read_text(encoding='utf-8'))['episodes']
        game = episodes['GAME.EPI']
        self.assertEqual((game['width'], game['height']), (4, 3))
        self.assertEqual(game['area_ids'], [[1, 2, 3, 10], [4, 5, 6, 10], [7, 8, 9, 10]])
        self.assertEqual(game['preload_ids'], [0, 1, 2, 3, 5])
        self.assertEqual(game['preload_count'], len(game['preload_ids']))
        self.assertEqual([p['name'] for p in game['people'][:3]], ['Олег', 'Игорь', 'Эйрик'])
        self.assertEqual([p['start_area'] for p in game['people'][:3]], [[0, 0], [0, 2], [2, 2]])
        self.assertEqual(episodes['DEMO.EPI']['area_ids'], [[21], [22]])
        self.assertEqual(episodes['DEMO2.EPI']['area_ids'], [[22]])

    def test_rejects_length_mismatch(self):
        raw = bytearray([1, 1, 0, 0, 5, 0] + [255] * (16 * 22) + [22, 0, 1, 2, 3, 5])
        self.assertEqual(parse_episode(bytes(raw))['area_ids'], [[22]])
        with self.assertRaises(ValueError):
            parse_episode(bytes(raw[:-1]))
        raw[0] = 0
        with self.assertRaises(ValueError):
            parse_episode(bytes(raw))
