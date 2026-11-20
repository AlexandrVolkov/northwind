from pathlib import Path
import unittest
from tools.match_menu_reference import bush_scores


class MenuWidgetScreenshotChecks(unittest.TestCase):
    def test_bush_frame_73_is_the_unique_best_fit_for_original_screenshot(self):
        root = Path(__file__).resolve().parents[1]
        scores = bush_scores(root)
        order = sorted(range(len(scores)), key=scores.__getitem__)
        self.assertEqual(order[0], 13)  # MENU.SPR 60 + 13
        self.assertGreater(scores[order[1]] - scores[order[0]], 30000)


if __name__ == '__main__': unittest.main()
