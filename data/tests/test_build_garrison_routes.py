# Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_garrison_routes import fix_routes


def seg(*pts):
    return [{"col": c, "row": r} for c, r in pts]


class FixRoutesTest(unittest.TestCase):
    def test_cactus_upper_route_follows_the_top_row(self):
        maps = {"desert_cactus": {"levels": {"07": {"routes": [
            {"motion": "walk", "segments": [seg((7.5, 0.5), (4.5, 0.5), (3.5, 1.5), (3.5, 3.5), (-0.5, 3.5))]},
            {"motion": "fly", "segments": [seg((7.5, 0.5), (4.5, 0.5), (3.5, 1.5), (3.5, 3.5))]},
        ]}}}}
        self.assertEqual(fix_routes(maps), 1)
        routes = maps["desert_cactus"]["levels"]["07"]["routes"]
        self.assertEqual(routes[0]["segments"][0], seg((7.5, 0.5), (0.5, 0.5), (0.5, 3.5), (-0.5, 3.5)))
        self.assertEqual(len(routes[1]["segments"][0]), 4, "flying paths are left alone")


if __name__ == "__main__":
    unittest.main()
