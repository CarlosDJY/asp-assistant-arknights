# Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
"""
从天赋描述中读出规划和战斗模拟用得上的效果（build_operator_profiles.py 调用）。

天赋写法五花八门，这里只认常见的几类，读不懂的分句原样记进 notes 方便人工检查：
  自身属性   攻击力/攻速/防御/生命/法抗/阻挡/闪避/庇护/回血/技力
  攻击附带   暴击式加成（x% 几率攻击力提升至 y%）、额外法伤、中毒（每秒法伤）、无视防御/法抗、
             几率晕眩/束缚/停顿、范围内敌人脆弱、减速
  光环       全场 / 攻击范围内 / 周围 8 格 / 身后一格 / 身前一格 / 指定盟约或势力或职业的干员
条件触发的效果按大致的生效比例折算（技能期间 0.4、血量条件 0.5、精英或空中 0.3……），
别的模式才生效的（水月、萨卡兹肉鸽）和只对【海怪】【萨卡兹】敌人生效的不计。
"""
from __future__ import annotations

import copy
import re

# 只在别的模式 / 特定敌人时生效：整句不计
SKIP = ("水月与深蓝之树", "萨卡兹的无终奇语", "海怪", "萨卡兹】敌人", "部署费用", "再部署时间", "希望消耗",
        "被招募", "负荷", "费用的自然回复")
# 条件 → 大致生效比例（取命中的最小值）
CONDITIONS = [
    ("技能期间", 0.4), ("开启技能时", 0.4), ("技能开启时", 0.4), ("技能持续", 0.4), ("技能未开启", 0.6),
    ("精英或领袖", 0.3), ("空中", 0.3), ("飞行", 0.3),
    ("生命值高于", 0.5), ("生命值低于", 0.4), ("生命少于", 0.4), ("生命值不高于", 0.4), ("生命大于", 0.6),
    ("生命低于", 0.4), ("生命比例", 0.5), ("生命值在", 0.5), ("剩余生命", 0.5),
    ("晕眩", 0.4), ("束缚", 0.4), ("停顿", 0.4), ("寒冷", 0.4), ("冻结", 0.3), ("沉睡", 0.1), ("战栗", 0.4),
    ("凋亡", 0.3), ("灼燃", 0.3), ("麻痹", 0.3), ("神经损伤爆发", 0.3), ("元素损伤", 0.3),
    ("闪避成功", 0.5), ("触发闪避", 0.5), ("击杀", 0.5), ("击倒", 0.5), ("倒下", 0.5),
    ("每在场上停留", 0.6), ("周围存在3名", 0.5), ("2秒内没有主动攻击", 0.3), ("4秒内没有主动攻击", 0.3),
    ("未受伤害", 0.5), ("未受到伤害", 0.5), ("重量小于等于", 0.7), ("没有其他敌人", 0.5),
    ("未阻挡", 0.5), ("阻挡敌人时", 0.8), ("2格以上的地面地形", 0.5), ("存在其他干员", 0.8), ("存在我方干员", 0.8),
    ("周围4格没有敌人", 0.5), ("周围四格没有敌人", 0.5), ("不高于自身", 0.5), ("80%以上", 0.4), ("高于80%", 0.4),
    ("高于70%", 0.5), ("超过一半", 0.4), ("少于40%", 0.3), ("低于一半", 0.5), ("低于30%", 0.3),
    ("同时攻击两个", 1.0), ("三名不同职业", 0.7), ("三名相同职业", 0.4), ("四名", 0.2),
    ("受到法术攻击", 0.5), ("受到攻击", 0.8), ("受到伤害时", 0.8),
]
NUM = r"(\d+(?:\.\d+)?)"


def clean(text: str) -> str:
    """去掉潜能提升的说明（"（+3%）"）和全角空格。"""
    text = re.sub(r"（[+\-][^）]*）", "", text)
    return text.replace("％", "%").replace(" ", "")


def weight_of(text: str) -> float:
    w = 1.0
    text = re.sub(r"免疫[^，；。]*", "", text)
    for key, v in CONDITIONS:
        if key in text:
            w = min(w, v)
    return w


class Groups:
    """盟约 / 势力 / 职业名 → 干员名单。"""

    def __init__(self, members: dict[str, set[str]]):
        self.members = members

    def find(self, text: str) -> list[str] | None:
        """文中第一个【X】所指的干员；不是已知分组时返回 None。"""
        for m in re.finditer(r"【([^】]+)】", text):
            name = m.group(1)
            if name in self.members:
                return sorted(self.members[name])
        return None


def scope_of(before: str, groups: Groups) -> tuple[str, list[str] | None]:
    """效果作用于谁：看效果前面的文字。"""
    if re.search(r"【[^】]+】(?:职业|势力的)?干员|【[^】]+】干员", before):
        members = groups.find(before)
        if members is not None:
            return "members", members
    if any(k in before for k in ("全场友方", "所有友方单位", "所有干员", "全场", "所有场上干员", "场上干员")):
        return "all", None
    if re.search(r"周围(?:8|八)格", before) and ("干员" in before or "友方" in before):
        return "around8", None
    if "身后" in before:
        return "behind", None
    if "身前一格" in before:
        return "front", None
    if re.search(r"(?:攻击)?范围内(?:的|所有|一名)?(?:友方|友军|生命)", before):
        return "range", None
    return "self", None


# (效果名, 正则, 换算)：值都是"每份"效果，按条件比例折算
SELF_PATTERNS = [
    ("atk", re.compile(r"攻击力\+" + NUM + "%"), lambda v: v / 100),
    ("atk", re.compile(r"攻击力-" + NUM + "%"), lambda v: -v / 100),
    ("aspd", re.compile(r"(?:攻击速度|攻速)\+" + NUM + r"(?!%)"), lambda v: v),
    ("def", re.compile(r"防御力\+" + NUM + "%"), lambda v: v / 100),
    ("def", re.compile(r"防御力-" + NUM + "%"), lambda v: -v / 100),
    ("defFlat", re.compile(r"防御力\+" + NUM + r"(?![\d.%])"), lambda v: v),
    ("hp", re.compile(r"(?:生命上限|生命值|最大生命值?)\+" + NUM + "%"), lambda v: v / 100),
    ("res", re.compile(r"法术抗性\+" + NUM), lambda v: v),
    ("block", re.compile(r"阻挡数\+" + NUM), lambda v: v),
    ("dodge", re.compile(NUM + r"%的?物理(?:和法术)?闪避"), lambda v: v / 100),
    ("sanctuary", re.compile(NUM + r"%的?庇护"), lambda v: v / 100),
    ("sanctuary", re.compile(r"受到的(?:物理和法术|物理与法术)?伤害(?:减少|降低|-)" + NUM + "%"), lambda v: v / 100),
    ("sanctuary", re.compile(r"受到的物理伤害(?:减少|降低)" + NUM + "%"), lambda v: 0.7 * v / 100),
    ("regen", re.compile(r"每秒(?:回复|恢复)(?:自身)?(?:最大生命值?|生命上限)" + NUM + "%"), lambda v: v / 100),
    ("regen", re.compile(r"每秒(?:回复|恢复)(?:相当于)?(?:生命上限|最大生命值?)" + NUM + "%"), lambda v: v / 100),
    ("regen", re.compile(r"每秒(?:回复|恢复)" + NUM + r"%(?:的)?最大生命"), lambda v: v / 100),
    ("regenFlat", re.compile(r"每秒(?:回复|恢复)" + NUM + r"(?:点)?生命"), lambda v: v),
    ("sp", re.compile(r"技力(?:自然)?回复速度\+" + NUM + "/秒"), lambda v: v),
    ("hp", re.compile(r"获得相当于(?:自身)?(?:生命上限|最大生命值?)" + NUM + "%的屏障"), lambda v: v / 100),
    ("sp", re.compile(r"每" + NUM + r"秒(?:获得|回复)1点技力"), lambda v: 1 / v),
    ("initSp", re.compile(r"初始技力\+" + NUM), lambda v: v),
]
CRIT = re.compile(NUM + r"%的?(?:几率|概率)[^；。]*?(?:攻击力|伤害)提升至" + NUM + "%")
CRIT_PLUS = re.compile(NUM + r"%的?(?:几率|概率)[^；。]*?攻击力\+" + NUM + "%")
DOUBLE = re.compile(NUM + r"%的?(?:几率|概率)连续攻击两次")
ATK_UP = re.compile(r"(?:攻击力|伤害)提升至" + NUM + "%")
DMG_UP = re.compile(r"(?:造成|攻击造成)" + NUM + r"%伤害|伤害(?:\+|提升|提高)" + NUM + "%")
CONTROL = [
    re.compile(NUM + r"%的?(?:几率|概率)使(?:攻击)?目标(束缚|晕眩|停顿)" + NUM + "秒"),
    re.compile(NUM + r"%的?(?:几率|概率)对攻击目标造成" + NUM + r"秒的?(束缚|晕眩|停顿)"),
]
STUN_PLAIN = re.compile(r"(?:并)?(晕眩|束缚)目标" + NUM + "秒")
DOT_FLAT = re.compile(r"(?:在" + NUM + r"秒内)?每秒受到" + NUM + r"点法术伤害(?:[^，；。]*?持续" + NUM + "秒)?")
DOT_SCALE = re.compile(r"每秒受到相当于[^，；。]*?攻击力" + NUM + r"%的法术伤害(?:[^，；。]*?持续" + NUM + "秒)?")
EXTRA = re.compile(r"(?:额外造成(?:一次)?|附带)相当于(?:自身)?攻击力" + NUM + r"%的(?:法术|神经|元素)?(?:伤害|损伤)")
DEF_FLAT = re.compile(r"无视(?:攻击)?(?:目标|其|敌人)?(?:的)?" + NUM + r"(?:点)?防御力?")
DEF_RATIO = re.compile(r"无视(?:攻击)?(?:目标|其|敌人)?(?:的)?" + NUM + r"%的?防御力")
RES_IGNORE = re.compile(r"无视(?:攻击)?目标" + NUM + r"法术抗性|法术抗性-" + NUM)
FRAGILE = re.compile(NUM + r"%的?脆弱")
SLOW = re.compile(r"移动速度-" + NUM + "%")


STACKS = re.compile(r"最多(?:可以|可)?叠加" + NUM + "(?:次|层)")


def weight_at(sentence: str, m: re.Match) -> float:
    """一处效果的生效比例：句首到这处效果所在分句末尾的条件；"每…最多叠加 n 次"的按叠满的 6 成算。"""
    end = sentence.find("，", m.end())
    clause = sentence[:end if end >= 0 else len(sentence)]
    w = weight_of(clause)
    if (k := STACKS.search(sentence)) and k.start() >= m.start():
        w *= float(k.group(1))
    return w


def is_enemy(before: str) -> bool:
    """效果前文最后提到的是敌人（敌人身上的减益），不是我方。"""
    e = max(before.rfind("敌"), before.rfind("目标"))
    f = max(before.rfind("友"), before.rfind("干员"), before.rfind("自身"))
    return e > f


def _add(d: dict, key: str, value: float) -> None:
    d[key] = round(d.get(key, 0) + value, 4)


def parse_talent(text: str, groups: Groups) -> tuple[dict, list[dict], list[str]]:
    """一条天赋（已渲染的描述）→ (自身效果, 光环列表, 没读懂的分句)。"""
    own: dict = {}
    auras: list[dict] = []
    notes: list[str] = []
    text = clean(text)
    for sentence in re.split(r"[；。\n]", text):
        if not sentence or any(k in sentence for k in SKIP):
            continue
        found = False
        # 攻击附带的效果（只对自身）
        if m := CRIT.search(sentence):
            _add(own, "crit", float(m.group(1)) / 100 * (float(m.group(2)) / 100 - 1))
            found = True
        elif m := CRIT_PLUS.search(sentence):
            _add(own, "crit", float(m.group(1)) / 100 * float(m.group(2)) / 100)
            found = True
        elif m := DOUBLE.search(sentence):
            _add(own, "crit", float(m.group(1)) / 100)
            found = True
        elif "几率" not in sentence and "概率" not in sentence and (m := ATK_UP.search(sentence)):
            _add(own, "dmg", weight_at(sentence, m) * (float(m.group(1)) / 100 - 1))
            found = True
        elif m := DMG_UP.search(sentence):
            w = weight_at(sentence, m)
            v = float(m.group(1) or m.group(2))
            if "敌人受到" not in sentence and "受到的" not in sentence[:m.start()]:
                _add(own, "dmg", w * (v / 100 - (1 if m.group(1) else 0)))
                found = True
        for pat in CONTROL:
            if m := pat.search(sentence):
                g = m.groups()
                chance = float(g[0]) / 100
                kind, secs = (g[1], float(g[2])) if pat is CONTROL[0] else (g[2], float(g[1]))
                own["control"] = {"kind": "stun" if kind == "晕眩" else "stop", "chance": chance, "seconds": secs}
                found = True
                break
        else:
            if (m := STUN_PLAIN.search(sentence)) and "control" not in own and "部署时" not in sentence:
                own["control"] = {"kind": "stun" if m.group(1) == "晕眩" else "stop",
                                  "chance": round(min(1.0, weight_at(sentence, m)), 2), "seconds": float(m.group(2))}
                found = True
        if m := DOT_SCALE.search(sentence):
            w = weight_at(sentence, m)
            own["dotScale"] = round(w * float(m.group(1)) / 100, 4)
            own["dotSeconds"] = float(m.group(2) or 3)
            found = True
        elif m := DOT_FLAT.search(sentence):
            w = weight_at(sentence, m)
            own["dotDps"] = round(w * float(m.group(2)), 2)
            own["dotSeconds"] = float(m.group(1) or m.group(3) or 3)
            found = True
        if m := EXTRA.search(sentence):
            _add(own, "extraArts", weight_at(sentence, m) * float(m.group(1)) / 100)
            found = True
        if m := DEF_RATIO.search(sentence):
            _add(own, "defIgnoreRatio", weight_at(sentence, m) * float(m.group(1)) / 100)
            found = True
        elif m := DEF_FLAT.search(sentence):
            _add(own, "defIgnore", weight_at(sentence, m) * float(m.group(1)))
            found = True
        if m := RES_IGNORE.search(sentence):
            _add(own, "resIgnore", weight_at(sentence, m) * float(m.group(1) or m.group(2)))
            found = True
        if m := FRAGILE.search(sentence):
            _add(own, "fragile", weight_at(sentence, m) * float(m.group(1)) / 100)
            found = True
        if (m := SLOW.search(sentence)) and "敌" in sentence:
            _add(own, "slow", weight_at(sentence, m) * float(m.group(1)) / 100)
            found = True
        # 属性：作用于谁看前文
        for key, pat, conv in SELF_PATTERNS:
            for m in pat.finditer(sentence):
                before = sentence[:m.start()]
                if key == "atk" and (CRIT_PLUS.search(sentence) or "提升至" in sentence[m.start():m.end() + 4]):
                    continue
                # 敌人身上的减益（"敌方单位攻击速度-5"）不是我方属性
                if is_enemy(before):
                    continue
                scope, members = scope_of(before, groups)
                v = round(weight_at(sentence, m) * conv(float(m.group(1))), 4)
                if scope == "self":
                    _add(own, key, v)
                else:
                    aura = next((a for a in auras if a["scope"] == scope and a.get("members") == members), None)
                    if aura is None:
                        aura = {"scope": scope}
                        if members is not None:
                            aura["members"] = members
                        if "自身与" in before or "自身和" in before or "自己" in before:
                            aura["self"] = True
                        auras.append(aura)
                    _add(aura, key, v)
                found = True
        if not found:
            notes.append(sentence)
    return own, auras, notes


def merge(parts: list[tuple[dict, list[dict], list[str]]]) -> dict:
    """几条天赋合成一份。"""
    own: dict = {}
    auras: list[dict] = []
    notes: list[str] = []
    for o, a, n in parts:
        for k, v in o.items():
            if isinstance(v, (int, float)) and k not in ("dotSeconds",):
                _add(own, k, v)
            else:
                own[k] = v
        auras.extend(a)
        notes.extend(n)
    if auras:
        own["auras"] = auras
    if notes:
        own["notes"] = notes
    return own


# 规则读不准的天赋：干员名 → 覆盖的效果（键为 None 表示去掉；合并进每个潜能档）
OVERRIDES: dict[str, dict] = {
    "宴": {"aspd": 40, "notes": None},                 # 坚忍：最高 +100 攻速（损失 70% 生命时），按 4 成计
    "赫拉格": {"aspd": 40, "regenFlat": 35, "notes": None},
    "夕": {"atk": 0.2},                                # 击杀叠攻击（2% × 最多约 18 层），按一场打到 10 层计
    "塔露拉": {"arts": True, "dmg": 0.09},            # 攻击造成法术伤害；停手后的增伤按一半
    "帕拉斯": {"auras": [{"scope": "members", "group": "米诺斯", "atk": 0.15}], "notes": None},
    "阿米娅": {"sp": 1.5, "notes": None},             # 每次攻击 +3 技力、击杀 +10
    "艾丽妮": {"defIgnoreRatio": 0.3},                # 50% 几率无视 50% 防御（对空 100%）
    "逻各斯": {"extraArts": 0.26},                    # 40% 几率对另一目标 65% 法伤
    "忍冬": {"extraArts": 0.3, "notes": None},
    "维娜·维多利亚": {"atk": 0.18, "auras": [{"scope": "around8", "self": True, "sanctuary": 0.22}]},
    "罗比菈塔": {"def": None},                         # 加防的是造型仪的目标
    "风絮": {"block": None, "sanctuary": None},         # 只给飞行友方
    "机械师": {"hp": 0.4},                             # 部署时 40% 最大生命的屏障
    "乌尔比安": {"atkFlat": 150, "notes": None},   # 击倒叠攻击（每次 +30，最多 10 次），按叠一半
    "瑕光": {"dmg": None},                             # 只打沉睡目标
    "Mon3tr": {"atk": None, "aspd": 22},               # 加攻的是重构体周围
    "迷迭香": {"auras": None, "atk": 0.08},
}


def apply_override(name: str, effect: dict) -> dict:
    """把 OVERRIDES 合进一份效果（帕拉斯这类按分组名写的光环换成名单要在调用处做）。"""
    patch = OVERRIDES.get(name)
    if not patch:
        return effect
    out = dict(effect)
    for k, v in patch.items():
        if v is None:
            out.pop(k, None)
        else:
            out[k] = copy.deepcopy(v)
    return out
