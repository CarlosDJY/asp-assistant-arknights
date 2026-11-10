"""Offline regression tests: python -m unittest discover -s data/tests -v."""
import importlib.util
import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch
from urllib.parse import unquote


SCRIPT = Path(__file__).resolve().parents[1] / "fetch_spdatabase.py"
spec = importlib.util.spec_from_file_location("fetch_spdatabase", SCRIPT)
fetch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetch)


class EnemyFetchTests(unittest.TestCase):
    def setUp(self):
        self.season = {
            "bossInfoDict": {
                "hidden": {"bossId": "hidden", "sortId": 2},
                "fin": {"bossId": "fin", "sortId": 1},
                "matrix": {"bossId": "matrix", "sortId": 3},
            },
            "specialEnemyInfoDict": {
                "enemy_a": {"type": "CRITIC", "isInFirstHalf": True, "randomWeight": 1},
                "enemy_b": {"type": "CRITIC", "isInFirstHalf": True, "randomWeight": 10},
                "enemy_c": {"type": "REBORN", "isInFirstHalf": False, "randomWeight": 5},
            },
        }
        self.metadata = {
            "bossInfoDict": {
                "fin": {"enemyId": "enemy_fin", "handbookEnemyId": "book_fin"},
                "hidden": {"enemyId": "enemy_fin_2", "handbookEnemyId": "book_fin"},
                "matrix": {"enemyId": "enemy_matrix", "handbookEnemyId": "book_matrix"},
            },
            "enemyTypeDatas": {
                "CRITIC": {"name": "特训敌人·致命", "sortId": 8},
                "REBORN": {"name": "特训敌人·复苏", "sortId": 9},
            },
        }
        self.book = {"enemyData": {
            "book_fin": {"name": "假想敌：鳍", "abilityList": [{"text": "鳍的能力"}]},
            "book_matrix": {"name": "矩兽", "description": "矩兽介绍"},
            "unrelated": {"name": "不属于当前赛季的首领", "enemyLevel": "BOSS"},
            "enemy_a": {"name": "致命敌人甲"},
            "enemy_b": {"name": "致命敌人乙"},
            "enemy_c": {"name": "复苏敌人"},
        }}

    def test_boss_pool_order_deduplication_and_handbook_mapping(self):
        bosses = fetch.process_bosses(self.book, self.season, self.metadata["bossInfoDict"])
        self.assertEqual([b["name"] for b in bosses], ["假想敌：鳍", "矩兽"])
        self.assertEqual(bosses[0]["enemyId"], "enemy_fin")
        self.assertEqual(bosses[0]["iconPath"], "icons/bosses/enemy_fin.png")
        self.assertEqual(bosses[0]["abilityList"], ["鳍的能力"])
        self.assertEqual(bosses[1]["description"], "矩兽介绍")

    def test_known_boss_short_alias_is_preserved(self):
        self.metadata["bossInfoDict"]["matrix"]["enemyId"] = "enemy_9032_aclionk"
        self.book["enemyData"]["book_matrix"]["name"] = "阿利斯泰尔，帝国余晖"
        bosses = fetch.process_bosses(self.book, self.season, self.metadata["bossInfoDict"])
        self.assertEqual(bosses[1]["aliases"], ["帝国余晖"])

    def test_missing_boss_mapping_fails(self):
        del self.metadata["bossInfoDict"]["fin"]
        with self.assertRaisesRegex(ValueError, "fin.*全局映射"):
            fetch.process_bosses(self.book, self.season, self.metadata["bossInfoDict"])

    def test_missing_handbook_entry_fails(self):
        del self.book["enemyData"]["book_fin"]
        with self.assertRaisesRegex(ValueError, "book_fin.*未收录"):
            fetch.process_bosses(self.book, self.season, self.metadata["bossInfoDict"])

    def test_empty_boss_pool_fails(self):
        self.season["bossInfoDict"] = {}
        with self.assertRaisesRegex(ValueError, "首领池为空"):
            fetch.process_bosses(self.book, self.season, self.metadata["bossInfoDict"])

    def test_missing_season_field_is_not_treated_as_empty_output(self):
        del self.season["bossInfoDict"]
        with self.assertRaisesRegex(KeyError, "bossInfoDict"):
            fetch.process_bosses(self.book, self.season, self.metadata["bossInfoDict"])

    def test_empty_boss_name_fails(self):
        self.book["enemyData"]["book_fin"]["name"] = " "
        with self.assertRaisesRegex(ValueError, "名称为空"):
            fetch.process_bosses(self.book, self.season, self.metadata["bossInfoDict"])

    def test_new_types_use_localized_metadata_and_keep_halves_and_weights(self):
        types = fetch.process_enemy_types(self.season, self.metadata["enemyTypeDatas"],
                                          fetch.build_enemy_name_map(self.book))
        self.assertEqual([t["typeName"] for t in types.values()], ["致命", "复苏"])
        self.assertEqual([v["enemyName"] for v in types["CRITIC"]["firstHalfVariants"]],
                         ["致命敌人乙", "致命敌人甲"])
        self.assertEqual(types["CRITIC"]["secondHalfVariants"], [])
        self.assertEqual(types["REBORN"]["secondHalfVariants"][0]["enemyName"], "复苏敌人")
        self.assertEqual(types["REBORN"]["typeIconPath"], "icons/enemy_types/REBORN.png")

    def test_future_type_needs_no_script_name_mapping(self):
        self.season["specialEnemyInfoDict"]["enemy_a"]["type"] = "FUTURE"
        self.metadata["enemyTypeDatas"]["FUTURE"] = {"name": "特训敌人·新分类", "sortId": 1}
        types = fetch.process_enemy_types(self.season, self.metadata["enemyTypeDatas"], {})
        self.assertEqual(list(types)[0], "FUTURE")
        self.assertEqual(types["FUTURE"]["typeName"], "新分类")

    def test_unknown_type_without_metadata_fails(self):
        del self.metadata["enemyTypeDatas"]["CRITIC"]
        with self.assertRaisesRegex(ValueError, "未知特训类型 CRITIC"):
            fetch.process_enemy_types(self.season, self.metadata["enemyTypeDatas"], {})

    def test_invalid_type_name_fails(self):
        self.metadata["enemyTypeDatas"]["CRITIC"]["name"] = "CRITIC"
        with self.assertRaisesRegex(ValueError, "中文名称无效"):
            fetch.process_enemy_types(self.season, self.metadata["enemyTypeDatas"], {})

    def test_empty_special_enemy_pool_fails(self):
        self.season["specialEnemyInfoDict"] = {}
        with self.assertRaisesRegex(ValueError, "特训敌人列表为空"):
            fetch.process_enemy_types(self.season, self.metadata["enemyTypeDatas"], {})

    def test_type_icons_follow_current_localized_names(self):
        types = fetch.process_enemy_types(self.season, self.metadata["enemyTypeDatas"], {})
        with tempfile.TemporaryDirectory() as directory, patch.object(fetch, "download_image", return_value=True) as download:
            with redirect_stdout(io.StringIO()):
                fetch.download_enemy_type_icons(types, Path(directory))
            self.assertEqual([unquote(c.args[0]).rsplit("/", 1)[1] for c in download.call_args_list],
                             ["特训敌人_致命.png", "特训敌人_复苏.png"])
            self.assertEqual([c.args[1].name for c in download.call_args_list], ["CRITIC.png", "REBORN.png"])

    def test_invalid_boss_does_not_replace_either_catalogue_or_download_icons(self):
        del self.book["enemyData"]["book_matrix"]
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory)
            for name in ("bosses.json", "enemy_types.json"):
                (destination / name).write_bytes(b"original data")
            with patch.object(fetch, "fetch_json", return_value=self.book), \
                    patch.object(fetch, "download_enemy_icons") as icons, redirect_stdout(io.StringIO()):
                with self.assertRaisesRegex(ValueError, "book_matrix"):
                    fetch.update_enemy_data(self.season, self.metadata, destination,
                                            skip_icons=False, skip_bosses=False, skip_enemies=False)
                icons.assert_not_called()
            for name in ("bosses.json", "enemy_types.json"):
                self.assertEqual((destination / name).read_bytes(), b"original data")

    def test_skip_bosses_does_not_require_boss_fields_or_replace_boss_file(self):
        del self.metadata["bossInfoDict"]
        del self.season["bossInfoDict"]
        with tempfile.TemporaryDirectory() as directory, patch.object(fetch, "fetch_json", return_value=self.book):
            destination = Path(directory)
            (destination / "bosses.json").write_bytes(b"original boss data")
            with redirect_stdout(io.StringIO()):
                fetch.update_enemy_data(self.season, self.metadata, destination,
                                        skip_icons=True, skip_bosses=True, skip_enemies=False)
            self.assertEqual((destination / "bosses.json").read_bytes(), b"original boss data")
            self.assertEqual(len(json.loads((destination / "enemy_types.json").read_text(encoding="utf-8"))["types"]), 2)

    def test_only_enemies_preserves_other_files_for_current_and_archive_seasons(self):
        for season_no in (1, 2):
            with self.subTest(season=season_no), tempfile.TemporaryDirectory() as directory:
                destination = Path(directory)
                other_files = ("operators.json", "equipment.json", "covenants.json", "equipment_categories.json")
                for name in other_files:
                    (destination / name).write_bytes(b"keep unchanged")
                config = fetch.SP_SEASONS[season_no]
                if season_no == 1:
                    activity = {"activity": {"AUTOCHESS": {config["seasonCode"]: {config["versionId"]: self.season}}}}
                else:
                    activity = {"activity": {"AUTOCHESS_SEASON": {config["seasonCode"]: self.season}}}
                activity["autoChessData"] = self.metadata
                with patch.object(fetch, "__file__", str(destination / "fetch_spdatabase.py")), \
                        patch("sys.argv", ["fetch_spdatabase.py", "--only-enemies", "--skip-icons", "--season", str(season_no)]), \
                        patch.object(fetch, "fetch_json", side_effect=[activity, self.book]) as download, \
                        patch.object(fetch, "process_operators") as operators, redirect_stdout(io.StringIO()):
                    fetch.main()
                    self.assertEqual(download.call_count, 2)
                    self.assertNotIn("character_table", download.call_args_list[0].args[0])
                    operators.assert_not_called()
                for name in other_files:
                    self.assertEqual((destination / name).read_bytes(), b"keep unchanged")
                self.assertEqual([b["name"] for b in json.loads((destination / "bosses.json").read_text(encoding="utf-8"))["bosses"]],
                                 ["假想敌：鳍", "矩兽"])

    def test_skip_special_enemies_does_not_require_type_fields(self):
        del self.metadata["enemyTypeDatas"]
        del self.season["specialEnemyInfoDict"]
        with tempfile.TemporaryDirectory() as directory, patch.object(fetch, "fetch_json", return_value=self.book):
            destination = Path(directory)
            (destination / "enemy_types.json").write_bytes(b"original type data")
            with redirect_stdout(io.StringIO()):
                fetch.update_enemy_data(self.season, self.metadata, destination,
                                        skip_icons=True, skip_bosses=False, skip_enemies=True)
            self.assertEqual((destination / "enemy_types.json").read_bytes(), b"original type data")
            self.assertEqual(len(json.loads((destination / "bosses.json").read_text(encoding="utf-8"))["bosses"]), 2)

    def test_full_update_still_processes_regular_data_and_enemy_catalogues(self):
        activity = {
            "activity": {"AUTOCHESS": {"act3autochess": {"V3_1": self.season}}},
            "autoChessData": {**self.metadata, "bondInfoDict": {}},
        }
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory)
            with patch.object(fetch, "__file__", str(destination / "fetch_spdatabase.py")), \
                    patch("sys.argv", ["fetch_spdatabase.py", "--skip-icons"]), \
                    patch.object(fetch, "fetch_json", side_effect=[activity, {}, self.book]) as download, \
                    patch.object(fetch, "process_operators", return_value=[{"name": "测试干员", "_charId": "char_test"}]), \
                    patch.object(fetch, "process_equipment", return_value=[{"name": "测试装备"}]), \
                    patch.object(fetch, "process_covenants", return_value=[]), \
                    patch.object(fetch, "build_job_change_equipment_map", return_value={}), redirect_stdout(io.StringIO()):
                fetch.main()
                self.assertEqual(download.call_count, 3)
                self.assertIn("activity_table", download.call_args_list[0].args[0])
                self.assertIn("character_table", download.call_args_list[1].args[0])
                self.assertEqual(download.call_args_list[2].args[0], fetch.ARKGAMEDATA_HANDBOOK_URL)
            self.assertEqual(json.loads((destination / "operators.json").read_text(encoding="utf-8")),
                             {"operators": [{"name": "测试干员"}]})
            self.assertEqual(json.loads((destination / "equipment.json").read_text(encoding="utf-8"))["equipment"],
                             [{"name": "测试装备"}])
            self.assertTrue((destination / "covenants.json").is_file())
            self.assertTrue((destination / "bosses.json").is_file())
            self.assertTrue((destination / "enemy_types.json").is_file())

    def test_incompatible_flags_fail_before_network(self):
        for flags in (["--download-skins"], ["--apply-mask"], ["--skip-enemies", "--skip-bosses"]):
            with self.subTest(flags=flags), patch("sys.argv", ["fetch_spdatabase.py", "--only-enemies", *flags]), \
                    patch.object(fetch, "fetch_json") as download, redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as error:
                    fetch.main()
                self.assertEqual(error.exception.code, 2)
                download.assert_not_called()


if __name__ == "__main__":
    unittest.main()
