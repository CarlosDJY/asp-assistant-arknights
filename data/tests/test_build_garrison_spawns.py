# Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_garrison_spawns import traits


def enemy(desc: str, talents: dict, **attrs) -> dict:
    return {
        "description": {"m_value": desc},
        "talentBlackboard": [{"key": k, "value": v if not isinstance(v, str) else 0.0,
                              "valueStr": v if isinstance(v, str) else None} for k, v in talents.items()],
        "attributes": {k: {"m_value": v} for k, v in attrs.items()},
    }


class TraitsTest(unittest.TestCase):
    def test_invisible_but_not_only_in_water(self):
        self.assertTrue(traits(enemy("<@eb.key>在被阻挡前无法被攻击</>。", {}))[0]["invisible"])
        self.assertNotIn("invisible", traits(enemy("免疫水蚀，在水中攻击力提升并获得隐匿", {}))[0])

    def test_reborn_as_another_enemy_is_a_death_spawn(self):
        out, children = traits(enemy("首次倒下后重生为<@eb.key><大君之触></>。",
                                     {"deathrattle.enemy_key": "enemy_1220_dzoms", "deathrattle.delay": 1.0}))
        self.assertEqual(out["onDeath"], [{"enemy": "enemy_1220_dzoms", "count": 1, "delay": 1.0}])
        self.assertEqual(children, ["enemy_1220_dzoms"])

    def test_ember_takes_the_hit_count_from_the_description(self):
        out, _ = traits(enemy("傀儡需要30次伤害击倒且不可被阻挡，一定时间后复活。",
                              {"Revive[Trigger].interval": 15.0, "Revive[Trigger].prop_max_hp": 15.0}))
        self.assertEqual(out["ember"], {"hits": 30, "seconds": 15.0})

    def test_element_kind_and_ratio(self):
        out, _ = traits(enemy("攻击造成<@eb.key>法术伤害</>与<@eb.key>凋亡损伤</>。", {"epdamage.attack@ep_damage_ratio": 0.37}))
        self.assertEqual(out["element"], {"kind": "apoptosis", "ratio": 0.37})

    def test_refraction_shield_block_and_regen(self):
        self.assertEqual(traits(enemy("折射生效时，法术抗性增加70", {"refracting.magic_resistance": 70.0}))[0]["refraction"], 70.0)
        self.assertEqual(traits(enemy("可以抵挡一次物理或法术伤害", {"Shield.max_block_damage_cnt": 1.0}))[0]["shield"], 1)
        self.assertEqual(traits(enemy("只能被阻挡数大于等于3的单位阻挡", {}))[0]["minBlock"], 3)
        self.assertEqual(traits(enemy("能快速自然恢复生命。", {}, hpRecoveryPerSec=80.0))[0]["regen"], 80.0)


    def test_stun_every_few_attacks_and_chains(self):
        d = enemy("能在数次攻击后晕眩我方单位。", {})
        d["skills"] = [{"prefabKey": "stuncombat", "spCost": 2, "blackboard": [{"key": "stun", "value": 7.0}]}]
        self.assertEqual(traits(d)[0]["stun"], {"every": 3, "seconds": 7.0, "radius": 0})
        out, _ = traits(enemy("攻击会在4个敌人间跳跃", {"epdamage.attack@max_target": 4.0, "epdamage.attack@chain.atk_scale": 0.85}))
        self.assertEqual(out["chain"], {"targets": 4, "scale": 0.85})

    def test_splash_only_on_the_first_attack(self):
        out, _ = traits(enemy("首次攻击时造成溅射伤害", {"AOEAttack.atk_scale": 1.5}))
        self.assertEqual(out["splash"], {"radius": 1.0, "first": True, "scale": 1.5})
        self.assertNotIn("splash", traits(enemy("并在释放完成时造成群体凋亡损伤", {}))[0])


if __name__ == "__main__":
    unittest.main()
