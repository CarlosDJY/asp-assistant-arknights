# Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_operator_profiles import skill_effect


def char_with(description: str, blackboard: dict) -> tuple[dict, dict]:
    skills = {"s": {"levels": [{"description": description, "blackboard": [{"key": k, "value": v} for k, v in blackboard.items()]}]}}
    return {"skills": [{"skillId": "s"}]}, skills


class SkillEffectTest(unittest.TestCase):
    def effect(self, description, blackboard):
        char, skills = char_with(description, blackboard)
        return skill_effect(char, 0, 1, skills, 1.0, 100)

    def test_targets_and_true_damage(self):
        out = self.effect("攻击力+160%，攻击目标数+2，攻击时伤害类型变为真实", {"atk": 1.6, "attack@max_target": 3})
        self.assertEqual(out["skillTargets"], 3)
        self.assertEqual(out["skillDamage"], "true")
        self.assertAlmostEqual(out["skillGain"], 2.6)

    def test_all_in_range_and_extra_targets(self):
        self.assertEqual(self.effect("攻击范围扩大，攻击范围内的所有敌人", {"atk": 0.4})["skillTargets"], 99)
        self.assertEqual(self.effect("攻击力+55%，额外攻击两个目标", {"atk": 0.55})["skillTargets"], 3)

    def test_debuff_and_heal(self):
        out = self.effect("攻击范围内所有敌人防御力-45%，法术抗性-26%", {})
        self.assertEqual((out["skillEnemyDef"], out["skillEnemyRes"]), (0.55, -26))
        self.assertEqual(self.effect("治疗附近一定范围内的所有友军相当于塞雷娅攻击力110%的生命", {"heal_scale": 1.1})["skillHeal"], {"burst": 1.1})


if __name__ == "__main__":
    unittest.main()


class TalentEffectsTest(unittest.TestCase):
    """天赋描述 → 效果（data/talent_effects.py）。"""

    def setUp(self):
        from talent_effects import Groups
        self.groups = Groups({"深海猎人": {"斯卡蒂", "歌蕾蒂娅"}, "近卫": {"佩佩", "山"}})

    def parse(self, text):
        from talent_effects import parse_talent
        return parse_talent(text, self.groups)

    def test_own_stats_and_riders(self):
        own, auras, notes = self.parse("攻击力+13%（+3%），攻击时有30%的几率当次攻击的攻击力提升至150%")
        self.assertAlmostEqual(own["atk"], 0.13)
        self.assertAlmostEqual(own["crit"], 0.15)
        self.assertEqual(auras, [])
        own, _, _ = self.parse("攻击使目标中毒，在3秒内每秒受到85（+10）点法术伤害")
        self.assertEqual((own["dotDps"], own["dotSeconds"]), (85, 3))
        own, _, _ = self.parse("攻击时有25%的几率使目标束缚3秒")
        self.assertEqual(own["control"], {"kind": "stop", "chance": 0.25, "seconds": 3.0})

    def test_auras_and_conditions(self):
        _, auras, _ = self.parse("编入队伍时，所有【深海猎人】干员的攻击力+16%（+2%）")
        self.assertEqual(auras, [{"scope": "members", "members": ["斯卡蒂", "歌蕾蒂娅"], "atk": 0.16}])
        _, auras, _ = self.parse("全场友方单位攻速+8（+1）")
        self.assertEqual(auras, [{"scope": "all", "aspd": 8}])
        # 条件触发的按比例折算；叠层的按叠满的 6 成
        own, _, _ = self.parse("每在场上停留20秒，攻击速度+5，最多可以叠加5次")
        self.assertAlmostEqual(own["aspd"], 15)
        own, _, _ = self.parse("获得33%的物理闪避，闪避成功后攻击速度+12（持续5秒，不可叠加）")
        self.assertAlmostEqual(own["dodge"], 0.33)
        self.assertAlmostEqual(own["aspd"], 6)

    def test_other_modes_and_unknowns(self):
        own, auras, notes = self.parse("在【水月与深蓝之树】中，攻击速度+25，阻挡数+1")
        self.assertEqual((own, auras, notes), ({}, [], []))
        _, _, notes = self.parse("攻击使目标的特殊能力失效，持续6秒")
        self.assertEqual(len(notes), 1)
