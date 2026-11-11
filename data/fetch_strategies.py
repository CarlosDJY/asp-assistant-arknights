#!/usr/bin/env python3
# Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
"""从 activity_table.json 导出规程序列 I 策略，并下载 PRTS 发起人头像。"""
import argparse
import json
import re
import time
import urllib.request
from pathlib import Path

from fetch_spdatabase import get_prts_media_url, strip_rich_text


def build_strategies(activity: dict) -> list[dict]:
    season = activity['activity']['AUTOCHESS']['act3autochess']['V3_1']
    bands = activity['autoChessData']['bandDataDict']
    strategies = []
    for entry in sorted(season['bandDataListDict'].values(), key=lambda b: b['sortId']):
        band = bands[entry['bandId']]
        description = strip_rich_text(entry['bandDesc'])
        title, effect = description.split('\n', 1)
        if not re.fullmatch(r'【.+】', title) or not effect.strip():
            raise ValueError(f"策略描述格式异常：{entry['bandId']}")
        strategies.append({
            'id': entry['bandId'],
            'name': title[1:-1],
            'initiator': band['bandName'],
            'iconPath': f"icons/strategies/{entry['bandId']}.png",
            'description': effect.strip(),
            'unlockRequirement': band['unlockDesc'] or '',
            'initialHp': entry['totalHp'],
            'inactiveModes': entry['inactiveModeIdList'],
        })
    if not strategies:
        raise ValueError('当前赛季策略池为空')
    return strategies


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('activity_table', type=Path, help='PRTS activity_table.json 路径')
    args = parser.parse_args()
    strategies = build_strategies(json.loads(args.activity_table.read_text(encoding='utf-8')))
    data_dir = Path(__file__).resolve().parent
    (data_dir / 'icons' / 'strategies').mkdir(parents=True, exist_ok=True)
    for strategy in strategies:
        path = data_dir / strategy['iconPath']
        # 支持下载中断后续跑，已有头像不重复请求。
        if path.is_file():
            continue
        filename = f"卫戍协议：盟约_策略发起人_{strategy['initiator']}.png"
        request = urllib.request.Request(get_prts_media_url(filename), headers={'User-Agent': 'ASPAssistant-Data/1.0'})
        with urllib.request.urlopen(request, timeout=30) as response:
            content = response.read()
        if not content.startswith(b'\x89PNG\r\n\x1a\n'):
            raise ValueError(f'头像不是 PNG：{filename}')
        path.write_bytes(content)
        time.sleep(0.2)
    (data_dir / 'strategies.json').write_text(json.dumps({
        '_modificationNotice': 'Current-season strategy data extracted from PRTS game data; see NOTICE. Game assets belong to their respective owners.',
        'source': 'https://torappu.prts.wiki/gamedata/latest/excel/activity_table.json',
        'reference': 'https://prts.wiki/w/卫戍协议·规程/PRTS盟约记录',
        'season': 'act3autochess/V3_1',
        'strategies': strategies,
    }, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Exported {len(strategies)} strategies and portraits')


if __name__ == '__main__':
    main()
