#!/usr/bin/env python3
"""
生成 data/operator_profiles.json：卫戍协议干员的职业 / 分支 / 阻挡 / 技能，以及规划用的角色标签。

数据来源：Kengxxiao/ArknightsGameData（activity_table / character_table / skill_table / uniequip_table），
赛季 act3autochess。棋子使用 charShopChessDatas.defaultSkillIndex 指定的技能，等级取棋子自身的
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

GAMEDATA_URL = "https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData/master/zh_CN/gamedata/excel/{}.json"
SEASON = ("act3autochess", "V3_1")
TABLES = ["activity_table", "character_table", "skill_table", "uniequip_table", "range_table"]

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


def phase_index(status: dict) -> int:
    return {"PHASE_0": 0, "PHASE_1": 1, "PHASE_2": 2}[status["evolvePhase"]]


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
        own = by_name[name]["coreCovenants"] + by_name[name]["additionalCovenants"]
        # 规划按普通版本计算（精锐需要三连，道中不能指望）
        stack, consumes = stack_info(by_name[name]["normal"]["traits"], own, covenant_names)

        profile = {
            "profession": profession,
            "subProfession": sub,
            "position": "近战" if char["position"] == "MELEE" else "远程",
            "blockCount": block,
            "normalSkill": skill_of(char, entry.get("defaultSkillIndex"), chess["status"]["skillLevel"], skills),
            "eliteSkill": skill_of(char, entry.get("defaultSkillIndex"), elite["status"]["skillLevel"], skills),
            "normalRange": attack_range(char, normal_phase, entry.get("defaultSkillIndex"), chess["status"]["skillLevel"], skills, used_ranges),
            "eliteRange": attack_range(char, phase, entry.get("defaultSkillIndex"), elite["status"]["skillLevel"], skills, used_ranges),
            "roles": roles,
            "stack": stack,
            "consumes": consumes,
        }
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
