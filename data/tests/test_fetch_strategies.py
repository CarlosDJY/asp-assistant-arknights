# Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from fetch_strategies import build_strategies


class StrategyFetchTests(unittest.TestCase):
    def setUp(self):
        self.season = {'bandDataListDict': {
            'later': {'bandId': 'later', 'sortId': 2, 'bandDesc': '【复制】\\n<@autochess.dgreen>拖拽使用</>：复制干员',
                      'totalHp': 28, 'inactiveModeIdList': ['mode_single_funny']},
            'initial': {'bandId': 'initial', 'sortId': 1, 'bandDesc': '【协同】\\n被动效果：增强干员',
                        'totalHp': 25, 'inactiveModeIdList': []},
        }}
        self.activity = {
            'activity': {'AUTOCHESS': {'act3autochess': {'V3_1': self.season}}},
            'autoChessData': {'bandDataDict': {
                'initial': {'bandName': '初始发起人', 'unlockDesc': None},
                'later': {'bandName': '后续发起人', 'unlockDesc': '通关任意模拟8次解锁'},
                'old-season': {'bandName': '旧赛季发起人', 'unlockDesc': None},
            }},
        }

    def test_current_pool_order_unlocks_effects_and_portrait_ids(self):
        strategies = build_strategies(self.activity)
        self.assertEqual([s['id'] for s in strategies], ['initial', 'later'])
        self.assertEqual(strategies[0]['unlockRequirement'], '')
        self.assertEqual(strategies[1]['unlockRequirement'], '通关任意模拟8次解锁')
        self.assertEqual(strategies[1]['name'], '复制')
        self.assertEqual(strategies[1]['description'], '拖拽使用：复制干员')
        self.assertEqual(strategies[1]['iconPath'], 'icons/strategies/later.png')
        self.assertEqual(strategies[1]['inactiveModes'], ['mode_single_funny'])

    def test_empty_pool_is_rejected(self):
        self.season['bandDataListDict'] = {}
        with self.assertRaisesRegex(ValueError, '策略池为空'):
            build_strategies(self.activity)

    def test_malformed_title_is_rejected(self):
        self.season['bandDataListDict']['later']['bandDesc'] = '无标题\\n效果'
        with self.assertRaisesRegex(ValueError, '策略描述格式异常'):
            build_strategies(self.activity)


if __name__ == '__main__':
    unittest.main()
