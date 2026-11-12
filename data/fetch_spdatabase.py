#!/usr/bin/env python3
# Modified for ASP Assistant Regulation on 2026-11-10; see NOTICE.
"""
SPDatabase 数据抓取工具
从 PRTS 与游戏图鉴获取卫戍协议/盟约数据，输出干员、装备、盟约、首领与特训敌人 JSON。
仅更新敌人（不下载图标）：python fetch_spdatabase.py --only-enemies --skip-icons

数据来源: https://static.prts.wiki/app/spdatabase/index.html
参考项目: https://github.com/Yanstory/SPDatabase
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Optional

try:
    from PIL import Image as _PilImage
    _PIL_AVAILABLE = True
except ImportError:
    _PIL_AVAILABLE = False

# ── 常量 ────────────────────────────────────────────────────────────────────

TORAPPU_ENDPOINT = "https://torappu.prts.wiki"
ARCHIVE_ENDPOINT = "https://static.prts.wiki/app/spdatabase"
MEDIA_ENDPOINT = "https://media.prts.wiki"

# Boss data sources (ArknightsGameData / ArknightsGameResource on GitHub)
ARKGAMEDATA_HANDBOOK_URL = (
    "https://raw.githubusercontent.com/Kengxxiao/ArknightsGameData"
    "/master/zh_CN/gamedata/excel/enemy_handbook_table.json"
)
YUANYAN_ENEMY_BASE_URL = (
    "https://raw.githubusercontent.com/yuanyan3060/ArknightsGameResource"
    "/main/enemy"
)

PROFESSION_MAP = {
    "PIONEER": "先锋",
    "WARRIOR": "近卫",
    "SNIPER": "狙击",
    "SUPPORT": "辅助",
    "CASTER": "术师",
    "SPECIAL": "特种",
    "MEDIC": "医疗",
    "TANK": "重装",
}

SP_CHAR_REDIRECT = {
    "char_601_cguard": "预备干员-近卫(卫戍协议)",
    "char_607_cspec": "预备干员-特种(卫戍协议)",
    "char_600_cpione": "预备干员-先锋(卫戍协议)",
    "char_605_cmedic": "预备干员-医疗(卫戍协议)",
    "char_604_ccast": "预备干员-术师(卫戍协议)",
    "char_603_csnipe": "预备干员-狙击(卫戍协议)",
    "char_602_cdfend": "预备干员-重装(卫戍协议)",
    "char_606_csuppo": "预备干员-辅助(卫戍协议)",
    "char_612_accast": "Pith(卫戍协议)",
    "char_614_acsupo": "Raidian(卫戍协议)",
    "char_609_acguad": "Sharp(卫戍协议)",
    "char_610_acfend": "Mechanist(卫戍协议)",
    "char_611_acnipe": "Stormeye(卫戍协议)",
    "char_615_acspec": "Misery(卫戍协议)",
    "char_608_acpion": "郁金香(卫戍协议)",
    "char_613_acmedc": "Touch(卫戍协议)",
}

# 「绝技」(bondId = suntShip) 是 noSelect / isActiveInDeck=false 的战斗专属盟约，没有任何
# 棋子归属；早期抓取时被整表排除过。但它会出现在「确认本局信息」界面的附加盟约列表里，
# 缺了名字，ban 位识别就无法给该图标配上盟约，因此不再排除。
# 若将来确实需要排除某个盟约，把名字加进 BOND_BAN 即可。
BOND_BAN: set[str] = set()

# 预备干员/协议特供棋子，不会出现在 ban 选角色界面
BAN_SCREEN_EXCLUDE_PREFIXES = ("预备干员-",)
BAN_SCREEN_EXCLUDE_SUFFIXES = ("(卫戍协议)",)

# 赛季配置
SP_SEASONS = {
    1: {
        "seasonCode": "act3autochess",
        "versionId": "V3_1",
        "name": "规程·序列 I",
        "mode": "ACTIVITY_SEASON",
        "equipIconMap": {
            "盟约之币": "幸运硬币",
            "奥术法阵": "口袋法阵",
            "骑士储蓄罐": "竞技投币机",
        },
    },
    2: {
        "seasonCode": "act1autochess",
        "name": "盟约 (Alliance)",
        "mode": "ACTIVITY_SEASON_ARC",
        "archiveFile": "alliance_1st.json",
        "equipIconMap": {
            # "数据中的名称": "Wiki图片中的名称",
        },
    },
}

# ── 工具函数 ──────────────────────────────────────────────────────────────────

def fetch_json(url: str) -> dict:
    print(f"  正在获取: {url}")
    req = urllib.request.Request(url, headers={"User-Agent": "SPDatabase-Scraper/1.0"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))


def strip_rich_text(text: str) -> str:
    return re.sub(r'<[@$][^>]*>|</>', '', text).replace('\\n', '\n')


def get_prts_media_url(filename: str) -> str:
    """构造 PRTS Media Wiki 图片 URL（MD5 路径）"""
    md5 = hashlib.md5(filename.encode("utf-8")).hexdigest()
    encoded = urllib.parse.quote(filename)
    return f"{MEDIA_ENDPOINT}/{md5[0]}/{md5[:2]}/{encoded}"


def download_image(url: str, save_path: Path) -> bool:
    """下载图片到本地，已存在则跳过。返回是否新下载。"""
    if save_path.exists():
        return False
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "SPDatabase-Scraper/1.0"})
        with urllib.request.urlopen(req, timeout=15) as resp:
            save_path.write_bytes(resp.read())
        return True
    except Exception as e:
        print(f"    [FAIL] {save_path.name}: {e}")
        return False


# ── 干员处理 ──────────────────────────────────────────────────────────────────

def build_traits(chess_entry: dict, garrison_dict: dict) -> list[dict]:
    """从棋子条目构建特质列表（只保留第一条）"""
    for gar_id in chess_entry.get("garrisonIds", []):
        gar = garrison_dict.get(gar_id)
        if gar:
            return [{
                "traitType": gar["eventTypeDesc"],
                "traitDescription": strip_rich_text(gar["garrisonDesc"]),
            }]
    return []


def classify_bonds(chess_entry: dict, bond_info_dict: dict) -> tuple[list[str], list[str]]:
    """分类盟约为核心盟约列表和附加盟约列表"""
    core_covenants = []
    additional_covenants = []

    for bond_id in chess_entry.get("bondIds", []):
        bond = bond_info_dict.get(bond_id)
        if not bond:
            continue
        bond_name = bond["name"]
        bond_type = bond["bondType"]

        if bond_type == "SEASON":
            core_covenants.append(bond_name)
        elif bond_type == "REGULAR" and bond_name not in BOND_BAN:
            additional_covenants.append(bond_name)

    return core_covenants, additional_covenants


def process_operators(
    char_table: dict,
    season_data: dict,
    bond_info_dict: dict,
) -> list[dict]:
    """处理干员数据，返回匹配 C# 模型的列表"""
    char_chess_dict = season_data["charChessDataDict"]
    char_shop_dict = season_data["charShopChessDatas"]
    garrison_dict = season_data["garrisonDataDict"]

    operators = []

    for chess_id, chess in char_chess_dict.items():
        # 只处理有升级路径的非 diy 棋子（即基础版本）
        if "diy" in chess_id or not chess.get("upgradeChessId"):
            continue

        shop_entry = char_shop_dict.get(chess_id)
        if not shop_entry:
            continue
        strategy_id = "band_mech" if shop_entry["charId"] == "char_618_acrbot" else ""
        if shop_entry["isHidden"] and not strategy_id:
            continue

        char_id = shop_entry["charId"]
        tier = shop_entry["chessLevel"]

        # 获取干员名称
        if char_id in SP_CHAR_REDIRECT:
            name = SP_CHAR_REDIRECT[char_id]
        else:
            char_raw = char_table.get(char_id, {})
            name = char_raw.get("name", char_id)

        # 盟约分类
        core_covenants, additional_covenants = classify_bonds(chess, bond_info_dict)

        # 普通版本特质
        normal_traits = build_traits(chess, garrison_dict)

        # 精锐版本特质
        upgrade_id = chess["upgradeChessId"]
        upgrade_chess = char_chess_dict.get(upgrade_id)
        elite_traits = build_traits(upgrade_chess, garrison_dict) if upgrade_chess else []

        operator = {
            "name": name,
            "iconPath": f"icons/operators/{name}.png",
            "_charId": char_id,
            "tier": tier,
            "coreCovenants": core_covenants,
            "additionalCovenants": additional_covenants,
            "normal": {
                "traits": normal_traits,
            },
            "elite": {
                "traits": elite_traits,
            },
        }
        if strategy_id:
            operator["requiredStrategyId"] = strategy_id
        operators.append(operator)

    return operators


# ── 装备处理 ──────────────────────────────────────────────────────────────────

def process_equipment(season_data: dict) -> list[dict]:
    """处理装备数据，返回匹配 C# 模型的列表"""
    trap_chess_dict = season_data["trapChessDataDict"]
    trap_shop_dict = season_data["trapShopChessDatas"]
    effect_info_dict = season_data["effectInfoDataDict"]

    equipment_list = []

    for item_id, shop_entry in trap_shop_dict.items():
        if shop_entry.get("itemType") != "EQUIP":
            continue

        tier = shop_entry["itemLevel"]
        golden_item_id = shop_entry.get("goldenItemId")

        # 普通版本
        normal_chess = trap_chess_dict.get(item_id)
        if not normal_chess:
            continue

        normal_effect_id = normal_chess.get("effectId")
        normal_effect = effect_info_dict.get(normal_effect_id, {}) if normal_effect_id else {}
        name = normal_effect.get("effectName", item_id)
        normal_desc = strip_rich_text(normal_effect.get("effectDesc", ""))

        # 精锐（金色）版本
        elite_desc = ""
        if golden_item_id:
            golden_chess = trap_chess_dict.get(golden_item_id)
            if golden_chess:
                golden_effect_id = golden_chess.get("effectId")
                golden_effect = effect_info_dict.get(golden_effect_id, {}) if golden_effect_id else {}
                elite_desc = strip_rich_text(golden_effect.get("effectDesc", ""))

        equip = {
            "name": name,
            "iconPath": f"icons/equipment/{name}.png",
            "tier": tier,
            "normal": {
                "effectDescription": normal_desc,
            },
            "elite": {
                "effectDescription": elite_desc,
            },
        }
        equipment_list.append(equip)

    return equipment_list


# ── 盟约（激活阈值 + 转职装备映射）────────────────────────────────────────────

def process_covenants(
    season_bond_dict: dict,
    global_bond_dict: dict,
) -> list[dict]:
    """
    输出每个盟约的：name、bondType（SEASON/REGULAR）、activateCount。
    数据源：
      - 激活阈值来自当前赛季的 bondInfoDict（含 activeCount/activeParamList 等字段）
      - bondType 通过 bondId 跨表查 global bondInfoDict（autoChessData.bondInfoDict）
    阈值缺失时写入 null，便于在 covenants.user.json 中手动补齐。
    """
    covenants: list[dict] = []

    for bond_id, season_bond in season_bond_dict.items():
        name = (season_bond.get("name") or "").strip()
        if not name or name in BOND_BAN:
            continue

        active_count: Optional[int] = None
        if isinstance(season_bond.get("activeCount"), int):
            active_count = season_bond["activeCount"]
        elif season_bond.get("activeParamList"):
            try:
                active_count = int(season_bond["activeParamList"][0])
            except (ValueError, TypeError, IndexError):
                active_count = None

        if active_count is None:
            print(f"  [WARN] 盟约 {name} ({bond_id}) 无法解析 activeCount，"
                  f"请在 covenants.user.json 中手动补齐")

        global_bond = global_bond_dict.get(bond_id) or {}
        bond_type = global_bond.get("bondType") or "REGULAR"

        covenants.append({
            "name":          name,
            "bondType":      bond_type,
            "activateCount": active_count,
        })

    # 以 SEASON 在前、REGULAR 在后；同类内按 name 排序，保证 diff 稳定
    covenants.sort(key=lambda c: (0 if c["bondType"] == "SEASON" else 1, c["name"]))
    return covenants


def build_job_change_equipment_map(
    season_data: dict,
    bond_info_dict: dict,
) -> dict[str, str]:
    """从棋子的 giveBondId 构建装备 → 核心或附加盟约映射。"""
    result: dict[str, str] = {}
    for item_id, shop_entry in season_data["trapShopChessDatas"].items():
        if shop_entry["itemType"] != "EQUIP":
            continue
        chess = season_data["trapChessDataDict"][item_id]
        if not chess["giveBondId"]:
            continue
        bond = bond_info_dict[chess["giveBondId"]]
        effect = season_data["effectInfoDataDict"][chess["effectId"]]
        result[effect["effectName"]] = bond["name"]

    return result


# ── 图片后处理 ────────────────────────────────────────────────────────────────

def apply_white_background(img: "_PilImage.Image") -> "_PilImage.Image":
    """将透明背景合成为白色，返回完全不透明的 RGBA 图像。"""
    img = img.convert("RGBA")
    white_bg = _PilImage.new("RGBA", img.size, (255, 255, 255, 255))
    return _PilImage.alpha_composite(white_bg, img)


def apply_white_background_to_dir(skin_dir: Path) -> None:
    """对 skin_dir 目录内所有 PNG 文件原地应用白色背景。"""
    if not _PIL_AVAILABLE:
        print("  [ERROR] 需要 Pillow 库：pip install Pillow")
        return

    pngs = list(skin_dir.glob("*.png"))
    if not pngs:
        print(f"  [WARN] 目录中无 PNG 文件: {skin_dir}")
        return

    for path in pngs:
        img = _PilImage.open(path)
        img = apply_white_background(img)
        img.save(path)

    print(f"  白色背景已应用: {len(pngs)} 张图片 ({skin_dir})")


# ── 皮肤头像处理 ──────────────────────────────────────────────────────────────

def process_skin_avatars(
    operators: list[dict],
    skin_table: dict,
    data_dir: Path,
) -> dict[str, str]:
    """为干员池中的干员下载所有皮肤头像，返回 {filename: operator_name} 映射。

    跳过 BAN_SCREEN_EXCLUDE 中的特殊干员（预备干员/卫戍协议棋子）。
    """
    char_skins: dict = skin_table.get("charSkins", {})

    char_id_to_name: dict[str, str] = {}
    for op in operators:
        name = op["name"]
        if any(name.startswith(p) for p in BAN_SCREEN_EXCLUDE_PREFIXES):
            continue
        if any(name.endswith(s) for s in BAN_SCREEN_EXCLUDE_SUFFIXES):
            continue
        char_id = op.get("_charId", "")
        if char_id:
            char_id_to_name[char_id] = name

    skin_dir = data_dir / "icons" / "skin_avatars"
    skin_dir.mkdir(parents=True, exist_ok=True)

    avatar_map: dict[str, str] = {}
    dl_new, dl_skip, dl_fail = 0, 0, 0

    for skin_id, skin in char_skins.items():
        char_id = skin.get("charId", "")
        if char_id not in char_id_to_name:
            continue

        avatar_id = skin.get("avatarId", "")
        if not avatar_id:
            continue

        op_name = char_id_to_name[char_id]
        filename = f"{avatar_id}.png"
        safe_filename = filename.replace("#", "_")

        avatar_map[safe_filename] = op_name

        url_avatar = urllib.parse.quote(avatar_id, safe="")
        url = f"{TORAPPU_ENDPOINT}/assets/char_avatar/{url_avatar}.png"
        save_path = skin_dir / safe_filename

        result = download_image(url, save_path)
        if result:
            dl_new += 1
            if _PIL_AVAILABLE:
                img = _PilImage.open(save_path)
                img = apply_white_background(img)
                img.save(save_path)
        elif save_path.exists():
            dl_skip += 1
        else:
            dl_fail += 1

    print(f"  皮肤头像: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")
    print(f"  映射条目: {len(avatar_map)} (覆盖 {len(char_id_to_name)} 名干员)")

    return avatar_map


# ── 特训敌人类型处理 ──────────────────────────────────────────────────────────

def process_enemy_types(season_data: dict, type_metadata: dict, enemy_name_map: dict[str, str]) -> dict:
    """
    从赛季数据的 specialEnemyInfoDict 中提取特训敌人类型信息。

    返回 dict keyed by typeCode，每条记录包含：
      - typeCode / typeName
      - firstHalfVariants: isInFirstHalf=True 的代表敌人列表（iconPath 已填充）
      - secondHalfVariants: isInFirstHalf=False 的代表敌人列表
    """
    spec_dict: dict = season_data["specialEnemyInfoDict"]
    if not spec_dict:
        raise ValueError("赛季特训敌人列表为空，停止更新")

    types: dict[str, dict] = {}

    def pick_enemy_name(enemy_id: str, e: dict) -> str:
        mapped_name = enemy_name_map.get(enemy_id)
        if mapped_name:
            return mapped_name

        # The upstream schema is not fully stable. Try common name fields first.
        for key in ("enemyName", "name", "displayName", "enemyDisplayName"):
            value = e.get(key)
            if isinstance(value, str) and value.strip():
                return value.strip()

        # Some payloads may nest enemy metadata in a sub-object.
        for node_key in ("enemyData", "enemy", "enemyInfo"):
            node = e.get(node_key)
            if not isinstance(node, dict):
                continue
            for key in ("enemyName", "name", "displayName"):
                value = node.get(key)
                if isinstance(value, str) and value.strip():
                    return value.strip()

        return ""

    for enemy_id, entry in spec_dict.items():
        type_code: str = entry["type"]
        if type_code not in type_metadata:
            raise ValueError(f"未知特训类型 {type_code}：enemyTypeDatas 中缺少类型资料，停止更新")
        full_name = type_metadata[type_code]["name"]
        type_name = full_name.removeprefix("特训敌人·").strip()
        if not full_name.startswith("特训敌人·") or not type_name:
            raise ValueError(f"特训类型 {type_code} 的中文名称无效：{full_name!r}")
        is_first_half: bool = entry["isInFirstHalf"]
        weight: int = entry["randomWeight"]

        if type_code not in types:
            types[type_code] = {
                "typeCode":     type_code,
                "typeName":     type_name,
                "typeIconPath": f"icons/enemy_types/{type_code}.png",
                "firstHalfVariants":  [],
                "secondHalfVariants": [],
            }

        variant = {
            "specialEnemyId": enemy_id,
            "enemyName":       pick_enemy_name(enemy_id, entry),
            "iconPath":        f"icons/enemies/{enemy_id}.png",
            "weight":          weight,
        }

        if is_first_half:
            types[type_code]["firstHalfVariants"].append(variant)
        else:
            types[type_code]["secondHalfVariants"].append(variant)

    # 按 weight 降序排列，最常见的放前面
    for t in types.values():
        t["firstHalfVariants"].sort(key=lambda v: -v["weight"])
        t["secondHalfVariants"].sort(key=lambda v: -v["weight"])

    return dict(sorted(types.items(), key=lambda item: type_metadata[item[0]]["sortId"]))


def collect_enemy_ids_for_icons(enemy_types: dict) -> set[str]:
    """收集所有需要下载头像的敌人 ID。"""
    ids: set[str] = set()
    for t in enemy_types.values():
        for v in t["firstHalfVariants"] + t["secondHalfVariants"]:
            ids.add(v["specialEnemyId"])
    return ids


def download_enemy_icons(enemy_ids: set[str], data_dir: Path) -> None:
    """从 yuanyan3060/ArknightsGameResource 下载特训小怪头像。"""
    icon_dir = data_dir / "icons" / "enemies"
    icon_dir.mkdir(parents=True, exist_ok=True)

    dl_new, dl_skip, dl_fail = 0, 0, 0
    for enemy_id in sorted(enemy_ids):
        save_path = icon_dir / f"{enemy_id}.png"
        if save_path.exists():
            dl_skip += 1
            continue
        url = f"{YUANYAN_ENEMY_BASE_URL}/{enemy_id}.png"
        if download_image(url, save_path):
            dl_new += 1
        else:
            dl_fail += 1

    print(f"  特训小怪头像: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")


def download_enemy_type_icons(enemy_types: dict, data_dir: Path) -> None:
    """从 PRTS Media Wiki 下载当前赛季特训敌人类型图标。
    保存为 data/icons/enemy_types/{typeCode}.png。
    """
    icon_dir = data_dir / "icons" / "enemy_types"
    icon_dir.mkdir(parents=True, exist_ok=True)

    dl_new, dl_skip, dl_fail = 0, 0, 0
    for type_code, entry in enemy_types.items():
        filename = f"特训敌人_{entry['typeName']}.png"
        save_path = icon_dir / f"{type_code}.png"
        if save_path.exists():
            dl_skip += 1
            continue
        url = get_prts_media_url(filename)
        if download_image(url, save_path):
            dl_new += 1
        else:
            dl_fail += 1

    print(f"  类型图标: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")


# ── Boss（卫戍协议「领袖」池）处理 ────────────────────────────────────────────

# 在 handbook 名称之外补充 OCR 常见简称（enemyId -> 额外 aliases）
BOSS_EXTRA_ALIASES: dict[str, list[str]] = {
    "enemy_9032_aclionk": ["帝国余晖"],
}


def boss_hp_levels(season_data: dict, boss: dict) -> list[dict]:
    """按模式排序的各难度血量：单人（极速模拟、规程等级 1–20）与多人（标准 / 险境 / 绝境 / 终极模拟）。"""
    blood = boss.get("bloodDataDict") or {}
    levels = []
    modes = sorted(season_data.get("modeDataDict", {}).values(), key=lambda m: m["sortId"])
    for mode in modes:
        if mode.get("modeType") not in ("SINGLE", "MULTI") or mode["modeId"] not in blood:
            continue
        levels.append({
            "mode": mode["modeId"],
            "name": mode["name"],
            "group": "single" if mode["modeType"] == "SINGLE" else "multi",
            "hp": blood[mode["modeId"]],
        })
    return levels


def process_bosses(handbook_data: dict, season_data: dict, boss_metadata: dict) -> list[dict]:
    """
    按赛季首领池和 sortId 选取图鉴资料，同名强化形态只保留一条。
    enemyId 用于战斗头像，handbookEnemyId 用于图鉴；两者可能不同。
    """
    enemy_data: dict = handbook_data["enemyData"]
    pool = season_data["bossInfoDict"]
    if not pool:
        raise ValueError("赛季首领池为空，停止更新")
    bosses = []
    seen_names: set[str] = set()

    for boss in sorted(pool.values(), key=lambda entry: entry["sortId"]):
        boss_id = boss["bossId"]
        if boss_id not in boss_metadata:
            raise ValueError(f"首领 {boss_id} 缺少全局映射，停止更新")
        metadata = boss_metadata[boss_id]
        enemy_id = metadata["enemyId"]
        handbook_id = metadata["handbookEnemyId"]
        if handbook_id not in enemy_data:
            raise ValueError(f"首领 {boss_id} 的图鉴 {handbook_id} 未收录，停止更新")
        entry = enemy_data[handbook_id]
        name: str = entry["name"].strip()
        if not name:
            raise ValueError(f"首领 {boss_id} 的图鉴名称为空，停止更新")
        if name in seen_names:
            # 隐藏首领（isHidingBoss，enemyId 带 _2）与普通形态同名，只补它的各难度血量。
            if boss.get("isHidingBoss"):
                row = next(r for r in bosses if r["name"] == name)
                for level in row["hp"]:
                    hidden = (boss.get("bloodDataDict") or {}).get(level["mode"])
                    if hidden is not None:
                        level["hiddenHp"] = hidden
            continue
        seen_names.add(name)

        ability_list = [
            a["text"]
            for a in (entry.get("abilityList") or [])
            if a.get("text")
        ]
        damage_types = entry.get("damageType") or []

        row = {
            "enemyId": enemy_id,
            "enemyIndex": entry.get("enemyIndex") or "",
            "name": name,
            "aliases": [],
            "description": entry.get("description"),
            "attackType": entry.get("attackType"),
            "damageTypes": damage_types,
            "abilityList": ability_list,
            "iconPath": f"icons/bosses/{enemy_id}.png",
            "hp": boss_hp_levels(season_data, boss),
        }
        extras = BOSS_EXTRA_ALIASES.get(enemy_id)
        if extras:
            for a in extras:
                if a not in row["aliases"]:
                    row["aliases"].append(a)
        bosses.append(row)

    return bosses


def build_enemy_name_map(handbook_data: dict) -> dict[str, str]:
    """从 enemy_handbook_table.json 构建 enemyId -> 中文名 映射。"""
    enemy_data: dict = handbook_data.get("enemyData", {})
    name_map: dict[str, str] = {}
    for enemy_id, entry in enemy_data.items():
        if not enemy_id or not entry:
            continue
        name = (entry.get("name") or "").strip()
        if name:
            name_map[enemy_id] = name
    return name_map


def download_boss_icons(bosses: list[dict], data_dir: Path) -> None:
    """从 yuanyan3060/ArknightsGameResource 下载 boss 头像。"""
    icon_dir = data_dir / "icons" / "bosses"
    icon_dir.mkdir(parents=True, exist_ok=True)

    dl_new, dl_skip, dl_fail = 0, 0, 0
    for boss in bosses:
        enemy_id = boss["enemyId"]
        save_path = icon_dir / f"{enemy_id}.png"
        if save_path.exists():
            dl_skip += 1
            continue
        url = f"{YUANYAN_ENEMY_BASE_URL}/{enemy_id}.png"
        if download_image(url, save_path):
            dl_new += 1
        else:
            dl_fail += 1

    print(f"  Boss 头像: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")


# ── 主流程 ────────────────────────────────────────────────────────────────────

def update_enemy_data(season_data: dict, auto_chess_data: dict, data_dir: Path, *,
                      skip_icons: bool, skip_bosses: bool, skip_enemies: bool) -> None:
    if skip_enemies and skip_bosses:
        return

    print("\n获取敌人图鉴数据 (enemy_handbook_table.json)...")
    handbook_data = fetch_json(ARKGAMEDATA_HANDBOOK_URL)
    outputs = []

    # Validate both catalogues before writing either file or downloading icons.
    if not skip_enemies:
        enemy_types = process_enemy_types(
            season_data, auto_chess_data["enemyTypeDatas"], build_enemy_name_map(handbook_data))
        outputs.append((data_dir / "enemy_types.json", {"types": enemy_types}))
        print(f"  类型数: {len(enemy_types)}, 敌人变体数: {sum(len(t['firstHalfVariants']) + len(t['secondHalfVariants']) for t in enemy_types.values())}")
    if not skip_bosses:
        bosses = process_bosses(handbook_data, season_data, auto_chess_data["bossInfoDict"])
        outputs.append((data_dir / "bosses.json", {"bosses": bosses}))
        print(f"  Boss 数: {len(bosses)}")

    if not skip_icons:
        if not skip_enemies:
            download_enemy_icons(collect_enemy_ids_for_icons(enemy_types), data_dir)
            download_enemy_type_icons(enemy_types, data_dir)
        if not skip_bosses:
            download_boss_icons(bosses, data_dir)

    for path, payload in outputs:
        with path.open("w", encoding="utf-8") as file:
            json.dump(payload, file, ensure_ascii=False, indent=2)
        print(f"  {path.resolve()} ({path.stat().st_size / 1024:.1f} KB)")


def main():
    parser = argparse.ArgumentParser(description="SPDatabase 数据抓取工具")
    parser.add_argument(
        "--season",
        type=int,
        default=1,
        choices=SP_SEASONS.keys(),
        help="赛季编号 (默认: 1 = 最新赛季)",
    )
    parser.add_argument("--skip-icons", action="store_true", help="跳过图标下载")
    parser.add_argument("--skip-bosses",   action="store_true", help="跳过 Boss 数据获取")
    parser.add_argument("--skip-enemies", action="store_true", help="跳过特训敌人类型数据获取")
    parser.add_argument("--only-enemies", action="store_true", help="仅更新首领和特训敌人，不处理干员、装备、盟约或皮肤")
    parser.add_argument("--download-skins", action="store_true", help="下载皮肤头像并生成映射文件")
    parser.add_argument("--apply-mask", action="store_true",
                        help="对 skin_avatars/ 目录内所有现有 PNG 原地补白色背景（需要 Pillow）")
    args = parser.parse_args()
    if args.only_enemies and (args.download_skins or args.apply_mask):
        parser.error("--only-enemies 不能与 --download-skins 或 --apply-mask 同时使用")
    if args.only_enemies and args.skip_bosses and args.skip_enemies:
        parser.error("--only-enemies 不能同时跳过首领和特训敌人")

    # --apply-mask 是独立操作，不需要拉取网络数据
    if args.apply_mask:
        data_dir = Path(__file__).parent
        print("=== 皮肤头像后处理 ===")
        apply_white_background_to_dir(data_dir / "icons" / "skin_avatars")
        return

    season_no = args.season
    season_cfg = SP_SEASONS[season_no]
    season_code = season_cfg["seasonCode"]
    data_dir = Path(__file__).parent

    print(f"=== SPDatabase 数据抓取工具 ===")
    print(f"赛季: {season_cfg['name']} (season {season_no})\n")

    # 1. 获取活动/存档数据
    if season_cfg["mode"] == "ACTIVITY_SEASON_ARC":
        archive_file = season_cfg["archiveFile"]
        print(f"[1/2] 获取存档数据 ({archive_file})...")
        act_data = fetch_json(f"{ARCHIVE_ENDPOINT}/archive/{archive_file}")
        season_data = act_data["activity"]["AUTOCHESS_SEASON"][season_code]
    else:
        print("[1/2] 获取活动数据 (activity_table.json)...")
        act_data = fetch_json(
            f"{TORAPPU_ENDPOINT}/gamedata/latest/excel/activity_table.json"
        )
        season_data = act_data["activity"]["AUTOCHESS"][season_code][season_cfg["versionId"]]

    if args.only_enemies:
        update_enemy_data(season_data, act_data["autoChessData"], data_dir,
                          skip_icons=args.skip_icons, skip_bosses=args.skip_bosses, skip_enemies=args.skip_enemies)
        return

    # 2. 获取干员名称表
    print("[2/2] 获取干员数据 (character_table.json)...")
    char_table = fetch_json(
        f"{TORAPPU_ENDPOINT}/gamedata/latest/excel/character_table.json"
    )
    print(f"      共 {len(char_table)} 名干员\n")

    # 3. 定位赛季数据和全局盟约信息
    bond_info_dict = act_data["autoChessData"]["bondInfoDict"]

    # 4. 处理干员
    print("处理干员数据...")
    operators = process_operators(char_table, season_data, bond_info_dict)
    # Deduplicate by name (keep first occurrence)
    seen_names: set[str] = set()
    unique_operators = []
    for op in operators:
        if op["name"] not in seen_names:
            seen_names.add(op["name"])
            unique_operators.append(op)
    if len(unique_operators) < len(operators):
        print(f"  重复干员 (已去重): {len(operators) - len(unique_operators)}")
    operators = unique_operators
    print(f"  干员数: {len(operators)}")

    # 5. 处理装备
    print("处理装备数据...")
    equipment = process_equipment(season_data)
    print(f"  装备数: {len(equipment)}")

    # 5b. 处理盟约（激活阈值 + 转职装备映射）
    print("处理盟约数据...")
    season_bond_dict = season_data.get("bondInfoDict", {})
    covenants_data = process_covenants(season_bond_dict, bond_info_dict)
    core_covenant_names = [c["name"] for c in covenants_data if c["bondType"] == "SEASON"]
    job_change_map = build_job_change_equipment_map(season_data, bond_info_dict)
    print(f"  盟约数: {len(covenants_data)} (核心 SEASON: {len(core_covenant_names)})")
    print(f"  转职装备 → 盟约映射: {len(job_change_map)}")

    covenants_path = data_dir / "covenants.json"
    with open(covenants_path, "w", encoding="utf-8") as f:
        json.dump(
            {
                "covenants": covenants_data,
                "jobChangeEquipmentToCovenant": job_change_map,
            },
            f,
            ensure_ascii=False,
            indent=2,
        )
    print(f"  {covenants_path.resolve()} ({covenants_path.stat().st_size / 1024:.1f} KB)")

    # 若用户未创建过 covenants.user.json，则生成模板（仅含未解析阈值的盟约）
    user_template_path = data_dir / "covenants.user.json"
    missing = [c for c in covenants_data if c["activateCount"] is None]
    if missing and not user_template_path.exists():
        with open(user_template_path, "w", encoding="utf-8") as f:
            json.dump(
                {
                    "_comment": "手动覆盖 covenants.json 中无法自动解析的字段；本文件应用启动时会与 covenants.json 合并（user 优先）。",
                    "covenants": [
                        {"name": c["name"], "activateCount": None}
                        for c in missing
                    ],
                },
                f,
                ensure_ascii=False,
                indent=2,
            )
        print(f"  已生成手动覆盖模板：{user_template_path.name}")

    # 6. 下载图标
    if not args.skip_icons:
        op_icon_dir = data_dir / "icons" / "operators"
        eq_icon_dir = data_dir / "icons" / "equipment"
        op_icon_dir.mkdir(parents=True, exist_ok=True)
        eq_icon_dir.mkdir(parents=True, exist_ok=True)

        print("下载干员图标...")
        dl_new, dl_skip, dl_fail = 0, 0, 0
        for op in operators:
            char_id = op.get("_charId", "")
            if not char_id:
                dl_fail += 1
                continue
            url = f"{TORAPPU_ENDPOINT}/assets/char_avatar/{char_id}.png"
            save_path = op_icon_dir / f"{op['name']}.png"
            result = download_image(url, save_path)
            if result:
                dl_new += 1
            elif save_path.exists():
                dl_skip += 1
            else:
                dl_fail += 1
        print(f"  干员图标: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")

        print("下载装备图标...")
        dl_new, dl_skip, dl_fail = 0, 0, 0
        equip_icon_map = season_cfg.get("equipIconMap", {})
        for eq in equipment:
            icon_name = equip_icon_map.get(eq["name"], eq["name"])
            filename = f"卫戍协议_道具_{icon_name}.png"
            url = get_prts_media_url(filename)
            save_path = eq_icon_dir / f"{eq['name']}.png"
            result = download_image(url, save_path)
            if result:
                dl_new += 1
            elif save_path.exists():
                dl_skip += 1
            else:
                dl_fail += 1
        print(f"  装备图标: {dl_new} 新下载, {dl_skip} 已存在, {dl_fail} 失败")
    else:
        print("跳过图标下载 (--skip-icons)")

    # 7. 更新当前赛季的首领和特训敌人
    update_enemy_data(season_data, act_data["autoChessData"], data_dir,
                      skip_icons=args.skip_icons, skip_bosses=args.skip_bosses, skip_enemies=args.skip_enemies)

    # 8. 下载皮肤头像并生成映射
    if args.download_skins:
        print("\n获取皮肤数据 (skin_table.json)...")
        skin_table = fetch_json(
            f"{TORAPPU_ENDPOINT}/gamedata/latest/excel/skin_table.json"
        )
        print("下载皮肤头像...")
        avatar_map = process_skin_avatars(operators, skin_table, data_dir)

        avatar_map_path = data_dir / "skin_avatar_map.json"
        with open(avatar_map_path, "w", encoding="utf-8") as f:
            json.dump(avatar_map, f, ensure_ascii=False, indent=2)
        print(f"  映射已保存: {avatar_map_path.name} ({avatar_map_path.stat().st_size / 1024:.1f} KB)")

    # 清理临时字段
    for op in operators:
        op.pop("_charId", None)

    # 10. 输出 JSON
    operators_path = data_dir / "operators.json"
    equipment_path = data_dir / "equipment.json"

    with open(operators_path, "w", encoding="utf-8") as f:
        json.dump({"operators": operators}, f, ensure_ascii=False, indent=2)

    with open(equipment_path, "w", encoding="utf-8") as f:
        json.dump(
            {
                "manualJobChangeEquipments": list(job_change_map),
                "equipment": equipment,
            },
            f,
            ensure_ascii=False,
            indent=2,
        )

    print(f"\n完成！")
    print(f"  {operators_path.resolve()} ({operators_path.stat().st_size / 1024:.1f} KB)")
    print(f"  {equipment_path.resolve()} ({equipment_path.stat().st_size / 1024:.1f} KB)")
    print(f"\n概要: {len(operators)} 名干员, {len(equipment)} 件装备")


if __name__ == "__main__":
    main()
