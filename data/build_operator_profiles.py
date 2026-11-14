#!/usr/bin/env python3
# Modified for ASP Assistant Regulation on 2026-11-14; see NOTICE.
"""
生成 data/operator_profiles.json：卫戍协议干员的职业 / 分支 / 阻挡 / 技能，以及规划用的角色标签。

数据来源：Kengxxiao/ArknightsGameData（activity_table / character_table / skill_table / uniequip_table /
battle_equip_table），赛季 act3autochess。棋子使用 chess_skill_overrides.json 里玩家实际携带的技能，没有列出时
用 charShopChessDatas.defaultSkillIndex；等级取棋子自身的
status.skillLevel（普通 / 精锐各一份）。

角色标签（规划器使用，可在 overrides 中手工修正）：
  盾位  ——  重装职业
  奶位  ——  医疗职业，以及会治疗的守护者 / 吟游者 / 护佑者
  stack ——  卫戍协议特质（普通版本）中的叠层方式，详见 parse_stack()
  consumes —— 作战能力随【盟约】层数成长的干员（吃层核心）

用法：python build_operator_profiles.py [--gamedata DIR]
  DIR 中需包含上述四个 json；不给则从 GitHub 下载。
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from talent_effects import Groups, apply_override, merge, parse_talent  # noqa: E402

GAMEDATA_URL = "https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData/master/zh_CN/gamedata/excel/{}.json"
SEASON = ("act3autochess", "V3_1")
TABLES = ["activity_table", "character_table", "skill_table", "uniequip_table", "range_table", "battle_equip_table",
          "handbook_team_table"]

PROFESSION = {"PIONEER": "先锋", "WARRIOR": "近卫", "SNIPER": "狙击", "SUPPORT": "辅助",
              "CASTER": "术师", "SPECIAL": "特种", "MEDIC": "医疗", "TANK": "重装"}
HEALER_SUBPROFESSIONS = {"守护者", "吟游者", "护佑者"}
STACK_TRAIT_TYPES = {"持续叠加", "单次叠加"}

# 手工修正：name -> 覆盖字段（roles / stack / consumes）；perRound 是每回合大约叠多少层
OVERRIDES: dict[str, dict] = {
    # 每名在场【乌萨斯】干员转化 1 层临时层数为固定层数（普通版），满编约 6 层
    "乌啾": {"stack": {"kind": "covenant", "covenants": ["乌萨斯"], "amount": 1, "cap": None, "perRound": 5, "temporary": False}},
}

DATA_DIR = Path(__file__).resolve().parent


def load_table(name: str, gamedata: Path | None) -> dict:
    if gamedata:
        return json.loads((gamedata / f"{name}.json").read_text(encoding="utf-8"))
    url = GAMEDATA_URL.format(name)
    print(f"  下载 {url}", file=sys.stderr)
    with urllib.request.urlopen(url, timeout=120) as resp:
        return json.loads(resp.read().decode("utf-8"))


def render(description: str, blackboard: list[dict]) -> str:
    values = {b["key"].lower(): b["value"] for b in blackboard}

    def sub(match: re.Match) -> str:
        key, fmt = match.group(1).lower(), match.group(2) or ""
        value = values.get(key, values.get(key.lstrip("-")))
        if value is None:
            return "{" + key + "}"
        if "%" in fmt:
            return f"{abs(value) * 100:g}%" if key.startswith("-") else f"{value * 100:g}%"
        return f"{abs(value):g}" if key.startswith("-") else f"{value:g}"

    text = re.sub(r"\{([^}:]+)(:[^}]*)?\}", sub, description or "")
    text = re.sub(r"<[^>]*>", "", text)
    return text.replace("\\n", "\n").strip()


def skill_of(char: dict, index: int | None, level: int, skills: dict) -> dict | None:
    if index is None or not char.get("skills") or index >= len(char["skills"]):
        return None
    skill = skills[char["skills"][index]["skillId"]]
    lv = skill["levels"][max(0, min(level, len(skill["levels"])) - 1)]
    sp = lv["spData"]
    return {
        "name": lv["name"],
        "description": render(lv["description"], lv["blackboard"]),
        "spCost": sp["spCost"],
        "initSp": sp["initSp"],
        "duration": lv.get("duration", 0),
        "spType": {"INCREASE_WITH_TIME": "自动回复", "INCREASE_WHEN_ATTACK": "攻击回复",
                   "INCREASE_WHEN_TAKEN_DAMAGE": "受击回复"}.get(str(sp["spType"]), "被动"),
    }


# 战斗中按事件触发的叠层，每回合大约触发几次（无上限时用）
BATTLE_EVENTS_PER_ROUND = 4
# 有上限时，通常能叠到上限的这个比例
CAP_REACH = 0.75


def parse_stack(text: str, kind: str, own: list[str], covenant_names: set[str]) -> dict | None:
    """
    解析一条（普通版本）叠层特质，返回规划器使用的叠层模型：
      kind      covenant 指定盟约 / self 自身已激活的盟约 / adjacent 自身+相邻干员 / grant 只给相邻干员
                / amplifier 放大其他叠层 / once 获得时（规划不计分）
      covenants 被叠层的盟约（covenant）；amplifier 时为限定的盟约
      amount    每次每个盟约叠几层
      cap       每回合（或每场战斗）的叠层上限，没有则为 null
      perRound  每回合每个盟约大约叠几层
      temporary 叠的是【乌萨斯】临时层数（未转化会消失）
      amplify   放大方式 {type: capPercent|capFlat|perEvent|tempBoost, value, scope: around|front|sameCovenant}
    """
    named = list(dict.fromkeys(c for c in re.findall(r"【([^】]+)】", text) if c in covenant_names))
    if text.startswith("<获得时>") or kind == "单次叠加":
        return {"kind": "once", "covenants": named, "amount": 0, "cap": None, "perRound": 0, "temporary": False}

    amp = None
    if m := re.search(r"叠层上限\+(\d+)%", text):
        amp = {"type": "capPercent", "value": int(m.group(1)) / 100,
               "scope": "around" if "周围" in text else "front"}
    elif m := re.search(r"叠层上限\+(\d+)", text):
        amp = {"type": "capFlat", "value": int(m.group(1)), "scope": "sameCovenant"}
    elif m := re.search(r"每次提升的层数额外\+(\d+)", text):
        amp = {"type": "perEvent", "value": int(m.group(1)), "scope": "front"}
    elif "盟约获得强化" in text and (m := re.search(r"临时层数提升至(\d+)层", text)):
        amp = {"type": "tempBoost", "value": int(m.group(1)), "scope": "around"}
    if amp:
        return {"kind": "amplifier", "covenants": named, "amount": 0, "cap": None, "perRound": 0,
                "temporary": False, "amplify": amp}

    cap_m = re.search(r"每(?:回合|场战斗|名干员每回合)至多叠加(\d+)层", text)
    cap = int(cap_m.group(1)) if cap_m else None
    nums = [int(n) for n in re.findall(r"(?:层数|叠加|转化)\+?(\d+)层?", text)]
    amount = nums[0] if nums else (3 if "售出价格" in text else 2)
    battle = text.startswith("<战斗中>") and "每" in text
    if "使身前一格干员获得特质" in text:
        stack_kind = "grant"
        named = []
        battle = True
    elif "已激活的盟约" in text and ("身前" in text or "身后" in text):
        stack_kind = "adjacent"
    elif "自身已激活的盟约" in text or "整备区每名干员已激活的盟约" in text:
        stack_kind = "self"
    elif named:
        stack_kind = "covenant"
    else:
        return None
    if cap is not None:
        per = CAP_REACH * cap
    elif battle:
        per = BATTLE_EVENTS_PER_ROUND * amount
    else:
        per = amount
    return {"kind": stack_kind, "covenants": named if stack_kind == "covenant" else [],
            "amount": amount, "cap": cap, "perRound": round(per, 2), "temporary": "临时层数" in text and "转化" not in text}


PHASES = {"PHASE_0": 0, "PHASE_1": 1, "PHASE_2": 2}
POTENTIAL_ATTRS = {"ATK": "atk", "MAX_HP": "hp", "DEF": "def", "MAGIC_RESISTANCE": "res", "ATTACK_SPEED": "aspd"}


def build_groups(chars: dict, teams: dict, season: dict, data_dir: Path) -> Groups:
    """天赋里【X】可能指的分组：盟约（卫戍协议）、势力（国家 / 组织 / 小队）、职业。"""
    members: dict[str, set[str]] = {}
    operators = json.loads((data_dir / "operators.json").read_text(encoding="utf-8-sig"))["operators"]
    for o in operators:
        for c in o["coreCovenants"] + o["additionalCovenants"]:
            members.setdefault(c, set()).add(o["name"])
    names = {o["name"] for o in operators}
    power = {k: v["powerName"] for k, v in teams.items()}
    for c in chars.values():
        if c.get("name") not in names:
            continue
        for key in ("nationId", "groupId", "teamId"):
            if pid := c.get(key):
                full = power.get(pid, "")
                for n in {full, full.split("-")[-1]} - {""}:
                    members.setdefault(n, set()).add(c["name"])
        prof = PROFESSION.get(c["profession"])
        if prof:
            members.setdefault(prof, set()).add(c["name"])
    return Groups(members)


def talents_of(char: dict, status: dict, potential: int, groups: Groups) -> dict:
    """这一状态（精英阶段 / 等级）和潜能下生效的天赋，读成效果（见 talent_effects.py）。"""
    phase = phase_index(status)
    parts = []
    for talent in char.get("talents") or []:
        best = None
        for cand in talent.get("candidates") or []:
            cond = cand["unlockCondition"]
            need = PHASES[cond["phase"]]
            if (phase > need or phase == need and status["charLevel"] >= cond["level"])                     and cand["requiredPotentialRank"] <= potential and cand.get("description"):
                best = cand
        if best is not None:
            parts.append(parse_talent(render(best["description"], best["blackboard"]), groups))
    effect = apply_override(char["name"], merge(parts))
    for aura in effect.get("auras", []):
        if "group" in aura:
            aura["members"] = sorted(groups.members.get(aura.pop("group"), set()))
    return effect


def talent_levels(char: dict, status: dict, groups: Groups) -> list[dict]:
    """天赋随潜能的变化：[{pot: 最低潜能, ...效果}]，相同的合并。"""
    out: list[dict] = []
    for pot in range(6):
        t = talents_of(char, status, pot, groups)
        if not out or {k: v for k, v in out[-1].items() if k != "pot"} != t:
            out.append({"pot": pot, **t})
    return out


def potential_bonus(char: dict) -> list[dict]:
    """每一级潜能（2 潜 … 6 潜）带来的属性加成（不累计）。"""
    out = []
    for rank in char.get("potentialRanks") or []:
        bonus = {}
        for mod in ((rank.get("buff") or {}).get("attributes") or {}).get("attributeModifiers") or []:
            if key := POTENTIAL_ATTRS.get(mod["attributeType"]):
                bonus[key] = bonus.get(key, 0) + mod["value"]
        out.append(bonus)
    return out


# 参与战斗的召唤物：干员 → 召唤物与出场方式
#   duration 在场秒数（0 = 一直在场，被击败后 cooldown 秒刷新）；appear 开场后几秒出现
#   lane     放在攻击范围内敌人路线上的地面格（阻挡 / 近战召唤物）；否则与干员同格同朝向（远程装置）
#   hits     每次攻击打几下（狼群每只狼多一下）；block 覆盖数据里的阻挡数
SUMMONS = {
    "渡桥": {"duration": 0, "cooldown": 15, "appear": 3, "lane": True},
    "伺夜": {"duration": 0, "cooldown": 10, "appear": 3, "lane": True, "block": 3, "hits": 2},
    "缪尔赛思": {"duration": 0, "cooldown": 25, "appear": 3, "lane": True, "block": 2},
    "夕": {"duration": 25, "cooldown": 9999, "appear": 4, "lane": True},
    "鸿雪": {"duration": 25, "cooldown": 40, "appear": 2, "lane": False},
    "温蒂": {"duration": 20, "cooldown": 35, "appear": 2, "lane": False, "push": {"cells": 0.4, "pull": False}},
    "涤火杰西卡": {"duration": 50, "cooldown": 30, "appear": 2, "lane": True},
    "机械师": {"duration": 30, "cooldown": 40, "appear": 2, "lane": True},
}


def summon_of(name: str, char: dict, status: dict, chars: dict, used: set[str]) -> dict | None:
    """这名干员在这一状态下带的召唤物（见 SUMMONS），属性按干员的精英阶段和等级插值。"""
    how = SUMMONS.get(name)
    tokens = list((char.get("displayTokenDict") or {}).keys())
    if not how or not tokens or tokens[0] not in chars:
        return None
    token = chars[tokens[0]]
    phases = token.get("phases") or []
    if not phases:
        return None
    phase = phases[min(phase_index(status), len(phases) - 1)]
    frames = phase["attributesKeyFrames"]
    lo, hi = frames[0], frames[-1]
    t = min(1.0, max(0.0, (status["charLevel"] - lo["level"]) / max(1, hi["level"] - lo["level"])))

    def at(key: str) -> float:
        return lo["data"][key] + (hi["data"][key] - lo["data"][key]) * t

    if phase.get("rangeId"):
        used.add(phase["rangeId"])
    out = {"name": token["name"], "hp": round(at("maxHp")), "atk": round(at("atk")), "def": round(at("def")),
           "res": round(at("magicResistance"), 1), "interval": hi["data"]["baseAttackTime"],
           "aspd": hi["data"]["attackSpeed"], "block": how.get("block", hi["data"]["blockCnt"]),
           "range": phase.get("rangeId"), "melee": token.get("position") == "MELEE"}
    out.update({k: v for k, v in how.items() if k != "block"})
    if how.get("cooldown") is None:
        out["cooldown"] = hi["data"].get("respawnTime", 30)
    return out


def phase_index(status: dict) -> int:
    return {"PHASE_0": 0, "PHASE_1": 1, "PHASE_2": 2}[status["evolvePhase"]]


# 模组属性键 → 输出字段
EQUIP_ATTRS = {"atk": "atk", "max_hp": "hp", "def": "def", "magic_resistance": "res", "attack_speed": "aspd"}


# 技能收益估算：一场战斗按这么长算（秒），自动回复技能每秒 1 技力
BATTLE_SECONDS = 45
# 同时命中多个目标时，额外每个目标按这个比例计（敌人不总是扎堆）
EXTRA_TARGET_VALUE = 0.35


def skill_factor(char: dict, index: int | None, level: int, skills: dict, interval: float, aspd: float) -> float:
    """
    携带技能让平均输出提高多少倍：1 + 技能覆盖率 × (技能期间的输出倍率 − 1)。
    倍率来自技能 blackboard 的常见键（攻击、攻击倍率、攻速、攻击间隔、多段、目标数、伤害倍率）；
    召唤、控制、治疗类技能没有这些键时按 1。覆盖率按 BATTLE_SECONDS 秒的战斗时间线计算。
    """
    if index is None or not char.get("skills") or index >= len(char["skills"]):
        return 1.0
    skill = skills.get(char["skills"][index]["skillId"])
    if not skill:
        return 1.0
    lv = skill["levels"][max(0, min(level, len(skill["levels"])) - 1)]
    bb = {b["key"]: b["value"] for b in lv["blackboard"]}

    def val(*keys, default=0.0):
        return next((bb[k] for k in keys if k in bb), default)

    atk = val("atk", "attack@atk")
    scale = val("atk_scale", "attack@atk_scale", default=1.0) or 1.0
    times = val("times", "attack@times", default=1.0) or 1.0
    damage = val("damage_scale", default=1.0) or 1.0
    speed = val("attack_speed", "attack@attack_speed")
    bat = val("base_attack_time")
    targets = val("attack@max_target", "max_target", default=1.0) or 1.0
    if not any(k in bb for k in ("atk", "attack@atk", "atk_scale", "attack@atk_scale", "times", "attack@times",
                                 "damage_scale", "attack_speed", "attack@attack_speed", "base_attack_time",
                                 "attack@max_target", "max_target")):
        return 1.0
    # base_attack_time：攻击间隔的增减（秒），如机械师 +2.3、Mon3tr −1.5
    new_interval = max(0.2, interval + bat)
    rate = (aspd + speed) / aspd * interval / new_interval
    gain = (1 + atk) * scale * times * damage * rate * (1 + EXTRA_TARGET_VALUE * (min(targets, 3) - 1))

    sp = lv["spData"]
    cost, init = sp["spCost"], sp["initSp"]
    duration = lv.get("duration") or 0
    sp_type = str(sp["spType"])
    if cost <= 0 or sp_type in ("8", "PASSIVE"):
        # 被动 / 特殊触发：有持续时间的按持续时间占战斗的比例，没有的视为常驻
        uptime = min(1.0, duration / BATTLE_SECONDS) if duration > 0 else 1.0
        return round(1 + uptime * (gain - 1), 3)
    seconds_per_sp = interval / ((aspd + 0) / 100) if sp_type == "INCREASE_WHEN_ATTACK" else 1.0
    if sp_type == "INCREASE_WHEN_TAKEN_DAMAGE":
        seconds_per_sp = 2.0
    active = duration if duration > 0 else new_interval  # 瞬发：一次强化攻击
    t, on = max(0.0, (cost - init) * seconds_per_sp), 0.0
    while t < BATTLE_SECONDS:
        on += min(active, BATTLE_SECONDS - t)
        t += active + cost * seconds_per_sp
    uptime = on / BATTLE_SECONDS
    return round(1 + uptime * (gain - 1), 3)


CN_NUMBERS = {"一": 1, "两": 2, "二": 2, "三": 3, "四": 4, "五": 5, "六": 6}


def skill_effect(char: dict, index: int | None, level: int, skills: dict, interval: float, aspd: float) -> dict:
    """
    What the carried skill does while it runs, for the simulation (only what it can play out):
      skillGain       attack (or healing) multiplier while it runs — attack, scale, hits, damage scale and attack
                      speed from the blackboard, without the target count (that is skillTargets)
      skillTargets    enemies hit per attack while it runs (99: every enemy in range); absent = one
      skillBlocked    hits every enemy it blocks
      skillDamage     "true" / "arts" when the skill changes the damage type
      skillResIgnore  arts resistance ignored
      skillEnemyDef   enemies in range: defence × this; skillEnemyRes: arts resistance + this (a debuff)
      skillHeal       {"burst": scale} healing everyone in range once on cast; {"hot": scale} per second to allies
                      in range while it runs
    """
    if index is None or not char.get("skills") or index >= len(char["skills"]):
        return {}
    skill = skills.get(char["skills"][index]["skillId"])
    if not skill:
        return {}
    lv = skill["levels"][max(0, min(level, len(skill["levels"])) - 1)]
    bb = {b["key"]: b["value"] for b in lv["blackboard"]}
    text = render(lv["description"], lv["blackboard"])

    def val(*keys, default=0.0):
        return next((bb[k] for k in keys if k in bb), default)

    out = {}
    atk = val("atk", "attack@atk")
    scale = val("atk_scale", "attack@atk_scale", default=1.0) or 1.0
    times = val("times", "attack@times", default=1.0) or 1.0
    damage = val("damage_scale", default=1.0) or 1.0
    speed = val("attack_speed", "attack@attack_speed")
    bat = val("base_attack_time")
    new_interval = max(0.2, interval + bat)
    rate = (aspd + speed) / aspd * interval / new_interval
    gain = (1 + atk) * scale * times * damage * rate
    # "持续进行五次…锤击": one cast, that many strikes.
    if m := re.search(r"持续进行([一二两三四五六七八九十]|\d+)次", text):
        n = m.group(1)
        hits = int(n) if n.isdigit() else {"一": 1, "二": 2, "两": 2, "三": 3, "四": 4, "五": 5, "六": 6, "七": 7, "八": 8, "九": 9, "十": 10}[n]
        if hits > 1:
            out["skillHits"] = hits
    if abs(gain - 1) > 1e-3:
        out["skillGain"] = round(gain, 3)
    targets = int(val("attack@max_target", "max_target", "max_target_cnt", default=0) or 0)
    if m := re.search(r"额外攻击([一两二三四1-4])个", text):
        targets = 1 + CN_NUMBERS.get(m.group(1), 0) + (int(m.group(1)) if m.group(1).isdigit() else 0)
    if re.search(r"攻击范围内的所有敌人|十字范围内所有敌人|对周围所有敌人|每秒对周围所有地面敌人", text):
        targets = 99
    if m := re.search(r"目标及其周围最多(\d+)名", text):
        targets = 1 + int(m.group(1))
    if targets > 1:
        out["skillTargets"] = targets
    if "同时攻击阻挡的所有敌人" in text:
        out["skillBlocked"] = True
    if "伤害类型变为真实" in text:
        out["skillDamage"] = "true"
    elif "伤害类型变为法术" in text:
        out["skillDamage"] = "arts"
    if (v := bb.get("magic_resist_penetrate_fixed")):
        out["skillResIgnore"] = v
    if m := re.search(r"攻击范围内所有敌人防御力-(\d+)%", text):
        out["skillEnemyDef"] = round(1 - int(m.group(1)) / 100, 3)
    if m := re.search(r"法术抗性-(\d+)%", text):
        out["skillEnemyRes"] = -int(m.group(1))
    # Control on the enemies it hits while it runs: stun-like (晕眩 / 浮空 / 冻结: no moving or attacking) or
    # stop-like (停顿 / 束缚: no moving), with its chance.
    # Its own stun once the skill ends (森蚺, 阿米娅) is not control on enemies.
    others = re.sub(r"[^，；。\n]*(自身晕眩|结束后[^，；。\n]{0,6}晕眩)[^，；。\n]*", "", text)
    stun = val("attack@stun_main", "attack@stun", "stun", "levitate", "attack@levitate") if re.search("晕眩|浮空", others) else 0
    stop = val("attack@sluggish", "sluggish", "attack@root", "root", "attack@unmoveable") if re.search("停顿|束缚", text) else 0
    if stun or stop:
        out["skillControl"] = {"kind": "stun" if stun else "stop", "seconds": stun or stop}
        if "概率" in text and (p := val("attack@prob", "prob")):
            out["skillControl"]["chance"] = p
    # What it gives itself while it runs.
    if "防御力+" in text and (v := val("def")) > 0:
        out["skillDef"] = v
    if re.search("生命上限\\+|最大生命值\\+", text) and (v := val("max_hp")) > 0:
        out["skillHp"] = v
    if "阻挡数+" in text and (v := val("block_cnt")) > 0:
        out["skillBlock"] = int(v)
    if m := re.search(r"治疗附近一定范围内的所有友军相当于[^%]*?(\d+)%", text):
        out["skillHeal"] = {"burst": int(m.group(1)) / 100}
    elif m := re.search(r"每秒(?:受到|恢复)[^，；。]*?(?:治疗量为|相当于)?(\d+)%[^，；。]*?攻击力[^，；。]*?(?:治疗|生命)", text):
        if "治疗" in text or "恢复" in text:
            out["skillHeal"] = {"hot": int(m.group(1)) / 100}
    # Damage per second on the ground in its reach while it runs (zones, fields, vortices).
    zone = 0.0
    for m in re.finditer(r"每秒(?:对[^，；。]*?)?(?:受到|造成)(?:一次)?(?:相当于)?[^，；。%]*?攻击力(\d+)%的(物理|法术|真实)?伤害", text):
        zone += int(m.group(1)) / 100
    for m in re.finditer(r"每(\d+(?:\.\d+)?)秒造成(\d+)%攻击力的(?:物理|法术)伤害", text):
        zone += int(m.group(2)) / 100 / float(m.group(1))
    if zone > 0 and out.get("skillTargets") != 99 and not re.search(r"自身每秒|流失", text[:text.find("每秒")] if "每秒" in text else ""):
        out["skillZone"] = round(zone, 3)
    if m := re.search(r"(?:敌人|敌方)[^，；。]*?移动速度-(\d+)%", text):
        out["skillSlow"] = int(m.group(1)) / 100
    if push := push_of(text):
        out["skillPush"] = push
    return out


FORCE = {"小": 0.4, "中等": 0.8, "较大": 1.2, "大": 1.6, "巨大": 2.0}


def push_of(text: str) -> dict | None:
    """推开 / 拖拽：{cells: 大致位移格数, pull: 是否拉向自己}（按 1 重量的敌人）。"""
    m = re.search(r"(小|中等|较大|巨大|大)(?:力度|力)地?(推开|推动|拖拽|拉)", text)         or re.search(r"(推开|拖拽)\((小|中等|较大|巨大|大)力度\)", text)
    if not m:
        return None
    a, b = m.groups()
    level, verb = (a, b) if a in FORCE else (b, a)
    return {"cells": FORCE[level], "pull": verb in ("拖拽", "拉")}


def stats_of(char: dict, status: dict, uniequip_id: str | None, equips: dict,
             skill_index: int | None = None, skills: dict | None = None) -> dict:
    """
    棋子在这一状态下的面板：按精英阶段的属性关键帧在 status.charLevel 处线性插值（游戏同样插值），
    再加上 status.equipLevel 级模组的属性加成。不含信赖（favorPoint 为 0）、潜能和天赋。
    """
    phase = char["phases"][min(phase_index(status), len(char["phases"]) - 1)]
    frames = phase["attributesKeyFrames"]
    lo, hi = frames[0], frames[-1]
    span = max(1, hi["level"] - lo["level"])
    t = min(1.0, max(0.0, (status["charLevel"] - lo["level"]) / span))

    def at(key: str) -> float:
        return lo["data"][key] + (hi["data"][key] - lo["data"][key]) * t

    out = {"atk": at("atk"), "hp": at("maxHp"), "def": at("def"), "res": at("magicResistance"),
           "interval": hi["data"]["baseAttackTime"], "aspd": hi["data"]["attackSpeed"],
           "block": hi["data"]["blockCnt"], "level": status["charLevel"], "phase": phase_index(status)}
    level = status.get("equipLevel", 0)
    equip = equips.get(uniequip_id or "")
    if level > 0 and equip and len(equip["phases"]) >= level:
        for b in equip["phases"][level - 1].get("attributeBlackboard", []):
            if b["key"] in EQUIP_ATTRS:
                out[EQUIP_ATTRS[b["key"]]] += b["value"]
    for k in ("atk", "hp", "def", "aspd"):
        out[k] = round(out[k])
    out["res"] = round(out["res"], 1)
    # 平均输出：面板 DPS × 携带技能的平均提升
    out["dps"] = round(out["atk"] / out["interval"] * out["aspd"] / 100)
    out["skillFactor"] = skill_factor(char, skill_index, status["skillLevel"], skills or {}, out["interval"], out["aspd"])
    out.update(skill_effect(char, skill_index, status["skillLevel"], skills or {}, out["interval"], out["aspd"]))
    return out


def attack_range(char: dict, phase: int, index: int | None, level: int, skills: dict, used: set[str]) -> dict:
    """平时的攻击范围，以及技能期间替换的范围（没有则为 null）。范围 id 记入 used，最后统一导出格子。"""
    base = char["phases"][min(phase, len(char["phases"]) - 1)].get("rangeId")
    skill_range = None
    if index is not None and 0 <= index < len(char.get("skills", [])):
        sid = char["skills"][index].get("skillId")
        levels = skills.get(sid, {}).get("levels", []) if sid else []
        if levels:
            skill_range = levels[min(level, len(levels)) - 1].get("rangeId")
    if skill_range == base:
        skill_range = None
    used.update(r for r in (base, skill_range) if r)
    return {"base": base, "skill": skill_range}


def stack_info(traits: list[dict], own: list[str], covenant_names: set[str]) -> tuple[dict | None, list[str]]:
    stack, consumes = None, []
    for trait in traits:
        text, kind = trait["traitDescription"], trait["traitType"]
        named = list(dict.fromkeys(c for c in re.findall(r"【([^】]+)】", text) if c in covenant_names))
        if "每叠加" in text and named and ("作战能力" == kind or "本干员" in text):
            consumes = sorted(set(consumes) | set(named))
        if kind == "作战能力" or kind not in STACK_TRAIT_TYPES:
            continue
        parsed = parse_stack(text.split("\n")[0], kind, own, covenant_names)
        if parsed is not None and (stack is None or stack["kind"] == "once"):
            stack = parsed
    return stack, consumes


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--gamedata", type=Path)
    args = parser.parse_args()
    tables = {name: load_table(name, args.gamedata) for name in TABLES}

    season = tables["activity_table"]["activity"]["AUTOCHESS"][SEASON[0]][SEASON[1]]
    chars, skills = tables["character_table"], tables["skill_table"]
    subprof = {k: v["subProfessionName"] for k, v in tables["uniequip_table"]["subProfDict"].items()}
    chess_dict, shop = season["charChessDataDict"], season["charShopChessDatas"]
    groups = build_groups(chars, tables["handbook_team_table"], season, DATA_DIR)

    equips = tables["battle_equip_table"]
    # 实际携带的技能（1 = S1），与游戏默认 defaultSkillIndex 不同时以这里为准
    skill_overrides = json.loads((DATA_DIR / "chess_skill_overrides.json").read_text(encoding="utf-8-sig"))["skills"]

    operators = json.loads((DATA_DIR / "operators.json").read_text(encoding="utf-8-sig"))["operators"]
    by_name = {o["name"]: o for o in operators}
    covenant_names = {c["name"] for c in json.loads((DATA_DIR / "covenants.json").read_text(encoding="utf-8-sig"))["covenants"]}

    profiles: dict[str, dict] = {}
    used_ranges: set[str] = set()
    for chess_id, entry in shop.items():
        chess = chess_dict.get(chess_id)
        if not chess or not chess.get("upgradeChessId") or entry.get("isHidden") or "diy" in chess_id:
            continue
        char = chars.get(entry["charId"])
        if not char or char["name"] not in by_name:
            continue
        name = char["name"]
        elite = chess_dict.get(chess["upgradeChessId"]) or chess
        phase = phase_index(elite["status"])
        normal_phase = phase_index(chess["status"])
        block = char["phases"][min(phase, len(char["phases"]) - 1)]["attributesKeyFrames"][-1]["data"]["blockCnt"]
        profession = PROFESSION.get(char["profession"], char["profession"])
        sub = subprof.get(char["subProfessionId"], char["subProfessionId"])

        roles = []
        if profession == "重装":
            roles.append("盾位")
        if profession == "医疗" or sub in HEALER_SUBPROFESSIONS:
            roles.append("奶位")
        skill_index = skill_overrides[name] - 1 if name in skill_overrides else entry.get("defaultSkillIndex")
        uniequip = entry.get("defaultUniEquipId")
        own = by_name[name]["coreCovenants"] + by_name[name]["additionalCovenants"]
        # 规划按普通版本计算（精锐需要三连，道中不能指望）
        stack, consumes = stack_info(by_name[name]["normal"]["traits"], own, covenant_names)

        profile = {
            "profession": profession,
            "subProfession": sub,
            "position": "近战" if char["position"] == "MELEE" else "远程",
            "blockCount": block,
            "skillIndex": skill_index,
            "normalSkill": skill_of(char, skill_index, chess["status"]["skillLevel"], skills),
            "eliteSkill": skill_of(char, skill_index, elite["status"]["skillLevel"], skills),
            "normalRange": attack_range(char, normal_phase, skill_index, chess["status"]["skillLevel"], skills, used_ranges),
            "eliteRange": attack_range(char, phase, skill_index, elite["status"]["skillLevel"], skills, used_ranges),
            "normalStats": stats_of(char, chess["status"], uniequip, equips, skill_index, skills),
            "eliteStats": stats_of(char, elite["status"], uniequip, equips, skill_index, skills),
            "potential": potential_bonus(char),
            "roles": roles,
            "stack": stack,
            "consumes": consumes,
        }
        for key, st in (("normalStats", chess["status"]), ("eliteStats", elite["status"])):
            if summon := summon_of(name, char, st, chars, used_ranges):
                profile[key]["summon"] = summon
        profile["normalStats"]["talents"] = talent_levels(char, chess["status"], groups)
        profile["eliteStats"]["talents"] = talent_levels(char, elite["status"], groups)
        profile.update(OVERRIDES.get(name, {}))
        profiles[name] = profile

    missing = sorted(set(by_name) - set(profiles))
    if missing:
        print(f"警告：{len(missing)} 名干员没有匹配到游戏数据：{missing}", file=sys.stderr)
    out = {
        "_modificationNotice": "Generated for ASP Assistant Arknights; see NOTICE.",
        "_notice": "由 build_operator_profiles.py 从 ArknightsGameData 生成；角色标签供规划器使用，可在脚本 OVERRIDES 中手工修正；攻击范围见 ranges（不含天赋和模组带来的范围变化）。",
        "season": "/".join(SEASON),
        "operators": dict(sorted(profiles.items())),
        # 攻击范围格子：[前方格数, 横向格数]，按朝右摆放；朝其他方向时整体旋转
        "ranges": {rid: sorted([g["col"], g["row"]] for g in tables["range_table"][rid]["grids"])
                   for rid in sorted(used_ranges) if rid in tables["range_table"]},
    }
    (DATA_DIR / "operator_profiles.json").write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"已写入 {len(profiles)} 名干员", file=sys.stderr)


if __name__ == "__main__":
    main()
