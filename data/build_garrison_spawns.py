#!/usr/bin/env python3
# Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
"""
生成 data/garrison_spawns.json：卫戍协议每回合实际出的怪（模板 + 怪组替换）、每回合的敌人加成，以及敌人属性。
站位规划的战斗模拟按它出怪：本局抽到的 3 类敌人里，每回合出其中一类的一个怪组。

数据来源：
  路标档案馆 · 卫戍协议出怪预览（https://www.lubiao.wiki/tools/garrison-spawn-preview）页面脚本里内嵌的数据：
    variants.seq1        规程·序列I：每回合每类的怪组（上 / 下路、普通 / 精英 / 特殊、数量、开始时间、间隔）与模板
    buffSchedulesByVariant.seq1   每个模式每回合的累计敌人加成（攻击 / 生命倍率、攻速加数、移速倍率）
    endgamesByVariant.seq1        第 14 回合（首领）/ 第 15 回合的出怪
    enemyAttributes      敌人血量 / 攻击 / 防御 / 法抗
  Kengxxiao/ArknightsGameData enemy_database.json：移速、攻击间隔、攻击半径（远程 / 近战）、飞行、等级（补站点没有的字段），
    以及从天赋（talentBlackboard）和描述读出的特殊机制（traits：隐匿、折射、护盾、余烬 / 重生、死亡召唤、元素损伤、
    流血、死亡毒雾、首击暴击、光环、阻挡要求、自然回复），死亡召唤出的敌人也一并收录
  Kengxxiao/ArknightsGameData activity_table.json（act3autochess V3_1）：每个模式不出的敌人（inactiveEnemyKey）

用法：python build_garrison_spawns.py <站点数据目录> [--enemy-db enemy_database.json] [--activity activity_table.json]
  站点数据目录里放从页面脚本取出的 variants.json、buffSchedulesByVariant.json、endgamesByVariant.json、enemyAttributes.json
  （脚本里 `名字:JSON.parse(`...`)` 的内容，各存为 名字.json）。不给 --enemy-db 则从 GitHub 下载。
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import urllib.request
from pathlib import Path

ENEMY_DB_URL = "https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData/master/zh_CN/gamedata/levels/enemydata/enemy_database.json"
ACTIVITY_URL = "https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData/master/zh_CN/gamedata/excel/activity_table.json"
ELEMENTS = [("灼燃", "burning"), ("凋亡", "apoptosis"), ("神经", "neural"), ("侵蚀", "erosion")]
VARIANT = "seq1"
DATA_DIR = Path(__file__).resolve().parent


def value(attrs: dict, key: str, default=0):
    v = attrs.get(key)
    return v["m_value"] if isinstance(v, dict) and "m_value" in v else default


def blackboard(d: dict) -> dict:
    return {x["key"]: (x.get("valueStr") if x.get("valueStr") is not None else x.get("value"))
            for x in d.get("talentBlackboard") or []}


def first(bb: dict, *suffixes):
    for k, v in bb.items():
        if any(k.endswith(s) for s in suffixes):
            return v
    return None


def traits(d: dict) -> tuple[dict, list[str]]:
    """The special mechanics the simulation models, read from the enemy's talents and description."""
    desc = ((d.get("description") or {}).get("m_value") or "")
    plain = re.sub(r"<[^>]*>", "", desc)
    bb = blackboard(d)
    out, children = {}, []
    if ("被阻挡前无法被攻击" in plain or "隐匿" in plain) and "在水中" not in plain:
        out["invisible"] = True
    if (v := first(bb, "efracting.magic_resistance")) is not None:
        out["refraction"] = v
    if (v := bb.get("Refracting.max_hp")) is not None:
        out["refractionHp"] = 1 + v
    if (v := bb.get("Shield.max_block_damage_cnt")):
        out["shield"] = int(v)
    if (v := bb.get("Aura.max_damage_block_cnt")):
        out["deathShieldAura"] = int(v)
    if (v := bb.get("Revive[Trigger].interval")):
        m = re.search(r"需要(\d+)次", plain)
        out["ember"] = {"hits": int(m.group(1)) if m else int(bb.get("Revive[Trigger].prop_max_hp") or 5), "seconds": v}
    if (v := bb.get("reborn.duration") or bb.get("stone.duration")):
        out["reborn"] = v
    spawns = []
    for prefix in ("deathrattle", "DeadSpawn", "Attack"):
        key = bb.get(f"{prefix}.enemy_key")
        if not isinstance(key, str) or not key.startswith("enemy_"):
            continue
        if prefix == "Attack" and "倒下后重生为" not in plain:
            continue
        spawns.append({"enemy": key, "count": int(bb.get(f"{prefix}.cnt") or 1), "delay": bb.get(f"{prefix}.delay") or 0})
        children.append(key)
    if spawns:
        out["onDeath"] = spawns
    ratio = next((v for k, v in bb.items() if k.endswith("ep_damage_ratio") and not k.lower().startswith("aura")
                  and "normal" not in k), None)
    element = next((code for word, code in ELEMENTS if word + "损伤" in plain), None)
    if ratio and element:
        out["element"] = {"kind": element, "ratio": ratio}
    if (v := bb.get("Bleeding.attack@bleeding_damage")):
        out["bleed"] = {"dps": v, "seconds": bb.get("Bleeding.attack@duration") or 10}
    if (v := bb.get("PollutedDie.polluted_damage_low")):
        out["cloud"] = {"dps": v, "range": bb.get("PollutedDie.projectile_range") or 2, "seconds": bb.get("PollutedDie.projectile_life_time") or 8}
    if (v := bb.get("1.damage_atk_scale")) and "毒雾" in plain:
        out["cloud"] = {"atkScale": v / (bb.get("1.interval") or 1), "range": bb.get("1.projectile_range") or 0.8,
                        "seconds": bb.get("1.projectile_life_time") or 8, "atSource": True}
    if (v := bb.get("CriticalHit.damage_scale")):
        out["crit"] = {"scale": v, "noHeal": bb.get("CriticalHit.duration") or 5}
    aura = {}
    if (v := bb.get("defup.def")):
        aura["def"] = v
        aura["range"] = bb.get("defup.range_radius") or 2.5
    if (v := bb.get("auraDefup.def")):
        aura["def"] = v
        aura["range"] = 1.5
    if (v := bb.get("magdef_add.magic_resistance")):
        aura["res"] = v
        aura["range"] = 2.5
    if aura:
        out["aura"] = aura
    if (v := bb.get("atkSpeedDown.attack_speed") or bb.get("traitAbility.attack_speed")):
        # -0.5: attack speed halved; -30: 30 attack-speed points off.
        out["slowOperators"] = {"factor": 1 + v if abs(v) < 1 else (100 + v) / 100,
                                "range": bb.get("defup.range_radius") or bb.get("traitAbility.range_radius") or 2.5}
    if (v := bb.get("DmgResist.damage_resistance")):
        out["unblockedTaken"] = 1 - v
    if "占用" in plain and "2阻挡数" in plain:
        out["blockCost"] = 2
    if m := re.search(r"阻挡数大于等于(\d)", plain):
        out["minBlock"] = int(m.group(1))
    if (v := value(d.get("attributes") or {}, "hpRecoveryPerSec")):
        out["regen"] = v
    out.update(attack_traits(d, plain, bb))
    return out, children


def attack_traits(d: dict, plain: str, bb: dict) -> dict:
    """
    How its attacks land beyond one target: stuns every few attacks (skills stuncombat / StunAttack: the skill fires
    once its SP — one per attack — reaches the cost, so every cost + 1 attacks), splash (every attack, every few, or
    only the first), chains that jump between operators, and a periodic line attack.
    """
    out = {}
    skills = d.get("skills") or []
    radius = value(d, "rangeRadius", 0.0)
    for s in skills:
        sbb = {x["key"]: x.get("value") for x in s.get("blackboard") or []}
        if s["prefabKey"] in ("stuncombat", "StunAttack") and sbb.get("stun"):
            out["stun"] = {"every": int(s.get("spCost") or 0) + 1, "seconds": sbb["stun"],
                           "radius": 1.3 if "晕眩周围" in plain else 0}
        elif s["prefabKey"].startswith("aoeAttack"):
            out["splash"] = {"radius": max(1.0, radius), "every": int(s.get("spCost") or 0) + 1}
        elif s["prefabKey"] == "Laser":
            out["line"] = {"cooldown": s.get("cooldown") or 15, "scale": 2.0}
    if "splash" not in out:
        if (v := bb.get("AOEAttack.atk_scale")):
            out["splash"] = {"radius": 1.0, "first": True, "scale": v}
        elif (v := bb.get("FirstAttack.attack@first_atk_scale")) and "溅射" in plain:
            out["splash"] = {"radius": 1.0, "first": True, "scale": v}
        elif "pow.attack@range_id" in bb:
            out["splash"] = {"radius": 1.0, "first": True}
        elif (v := bb.get("Attack.attack@projectile_radius") or bb.get("ProjectileBoomRange.attack@projectile_range")):
            out["splash"] = {"radius": v}
        elif "群体" in plain and "投掷" in plain:
            out["splash"] = {"radius": 1.0, "first": True}
        elif ("群体" in plain and "释放完成时" not in plain) or "溅射" in plain or "攻击对自身周围所有单位" in plain:
            out["splash"] = {"radius": max(1.0, radius) if "周围所有" in plain else 1.0}
    hops = first(bb, "attack@max_target")
    scale = first(bb, "attack@chain.atk_scale")
    if scale and (v := first(bb, "chain.max_target")):
        hops = 1 + v
    if scale and (v := bb.get("Summon.attack@hit_times")):
        hops = v
    if hops and scale and hops > 1:
        out["chain"] = {"targets": int(hops), "scale": scale}
    return out


def act(a: dict) -> dict:
    """One spawn line: which gate (upper / lower), which slot it fills, which enemy, how many, when."""
    out = {"gate": a["gate"], "enemy": a.get("enemy") or a.get("key"), "count": a["count"]}
    if a.get("slot"):
        out["slot"] = a["slot"]
    if "pre" in a:
        out["pre"] = a["pre"]
    if "interval" in a:
        out["interval"] = a["interval"]
    return out


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("site", type=Path)
    parser.add_argument("--enemy-db", type=Path)
    parser.add_argument("--activity", type=Path)
    args = parser.parse_args()

    load = lambda name: json.loads((args.site / f"{name}.json").read_text(encoding="utf-8"))
    variant = load("variants")[VARIANT]
    buffs = load("buffSchedulesByVariant")[VARIANT]
    endgames = load("endgamesByVariant")[VARIANT]
    attributes = load("enemyAttributes")
    if args.enemy_db:
        db_raw = json.loads(args.enemy_db.read_text(encoding="utf-8"))
    else:
        print(f"  下载 {ENEMY_DB_URL}", file=sys.stderr)
        with urllib.request.urlopen(ENEMY_DB_URL, timeout=300) as resp:
            db_raw = json.loads(resp.read().decode("utf-8"))
    db = {e["Key"]: e["Value"][0]["enemyData"] for e in db_raw["enemies"]}
    if args.activity:
        activity = json.loads(args.activity.read_text(encoding="utf-8"))
    else:
        print(f"  下载 {ACTIVITY_URL}", file=sys.stderr)
        with urllib.request.urlopen(ACTIVITY_URL, timeout=300) as resp:
            activity = json.loads(resp.read().decode("utf-8"))
    season = next(iter(activity["activity"]["AUTOCHESS"]["act3autochess"].values()))
    inactive = {m: sorted(v.get("inactiveEnemyKey") or []) for m, v in season["modeDataDict"].items() if v.get("inactiveEnemyKey")}

    rounds = {r: [{"key": g["key"], "name": g["name"], "type": g["type"], "weight": g["weight"], "prob": g["prob"],
                   "flying": g["fly"], "acts": [act(a) for a in g["acts"]]} for g in groups]
              for r, groups in variant["rounds"].items()}
    templates = {t["id"]: {"units": t["units"], "rounds": {r: [act(a) for a in acts] for r, acts in t["actions"].items()}}
                 for t in variant["templates"]}
    endgame = {r: [{"id": e.get("id"), "bossKey": e.get("bossKey"), "name": e.get("name"), "stageId": e.get("stageId"),
                    "actions": [act(a) for a in e.get("fixedActions", []) + e.get("actions", [])]} for e in entries]
               for r, entries in endgames.get("templates", {}).items()}
    schedule = {mode: [[r["attackMultiplier"], r["hpMultiplier"], r["attackSpeedAdd"], r["moveSpeedMultiplier"]]
                       for r in s["rounds"]] for mode, s in buffs.items()}

    used = set()
    for groups in rounds.values():
        for g in groups:
            used.update(a["enemy"] for a in g["acts"])
    for t in templates.values():
        for acts in t["rounds"].values():
            used.update(a["enemy"] for a in acts)
    for entries in endgame.values():
        for e in entries:
            used.update(a["enemy"] for a in e["actions"])
            if e["bossKey"]:
                used.add(e["bossKey"])

    enemies, missing = {}, []
    queue = sorted(k for k in used if k)
    while queue:
        key = queue.pop(0)
        if key in enemies or key in missing:
            continue
        d = db.get(key)
        site = attributes.get(key, {})
        if d is None and not site:
            missing.append(key)
            continue
        a = d["attributes"] if d else {}
        special, children = traits(d) if d else ({}, [])
        queue.extend(c for c in children if c not in enemies)
        radius = value(d, "rangeRadius", -1.0) if d else -1.0
        desc = ((d or {}).get("description") or {}).get("m_value") or ""
        enemies[key] = {
            "name": ((d or {}).get("name") or {}).get("m_value") or variant["names"].get(key, key),
            "hp": site.get("hp", value(a, "maxHp")),
            "atk": site.get("attack", value(a, "atk")),
            "def": site.get("defense", value(a, "def")),
            "res": site.get("resistance", value(a, "magicResistance")),
            "speed": value(a, "moveSpeed", 1.0),
            "interval": round(value(a, "baseAttackTime", 1.0) * 100 / max(1.0, value(a, "attackSpeed", 100.0)), 3),
            "range": radius if radius > 0 else 0,
            "flying": value(d, "motion") == "FLY" if d else False,
            "arts": "法术伤害" in desc,
            "rank": value(d, "levelType", "NORMAL") if d else "NORMAL",
            "lifePoints": value(d, "lifePointReduce", 1) if d else 1,
            "weight": value(a, "massLevel", 1) if d else 1,
        }
        if special:
            enemies[key]["traits"] = special
    if missing:
        print(f"警告：{len(missing)} 种敌人没有属性：{missing}", file=sys.stderr)
    with_traits = sum(1 for e in enemies.values() if 'traits' in e)
    print(f"{with_traits} 种敌人带特殊机制", file=sys.stderr)

    modes = {m["name"]: m["id"] for m in variant["modes"]}
    out = {
        "_modificationNotice": "Generated for ASP Assistant Regulation; see NOTICE.",
        "_notice": "由 build_garrison_spawns.py 生成。出怪、模板、回合加成和敌人血量 / 攻击 / 防御 / 法抗来自路标档案馆"
                   "「卫戍协议出怪预览」（https://www.lubiao.wiki/tools/garrison-spawn-preview，数据生成于 "
                   f"{variant['meta'].get('generated')}）；移速、攻击间隔、攻击半径等补自 ArknightsGameData enemy_database。"
                   "rounds：每回合每类的怪组（acts 的数量为每名玩家），gate 为上路 / 下路；buffs：每个模式每回合累计的"
                   " [攻击倍率, 生命倍率, 攻速加数, 移速倍率]；inactive：每个模式不出的敌人（怪组的特殊敌人在其中时整组不出）；"
                   "enemies.traits：模拟用到的特殊机制（由天赋和描述读出）。",
        "season": variant["meta"].get("season"),
        "modes": modes,
        "buffs": schedule,
        "inactive": inactive,
        "rounds": rounds,
        "templates": templates,
        "endgame": endgame,
        "enemies": enemies,
    }
    (DATA_DIR / "garrison_spawns.json").write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"已写入 {len(rounds)} 回合怪组、{len(endgame)} 个收尾回合、{len(enemies)} 种敌人、{len(schedule)} 个模式的加成",
          file=sys.stderr)


if __name__ == "__main__":
    main()
