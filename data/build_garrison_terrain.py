# Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
"""Adds each map's special tiles to data/garrison_maps.json ("terrain": {kind: [[col, row], ...]}).

Input: the act3autochess V3_1 level files from Kengxxiao/ArknightsGameData zh_CN
(gamedata/levels/activities/act3autochess/level_act3autochess_mNN.json). The board inside a level is found by matching
our map's defence point and two spawns against the level's tile_end / tile_start tiles. Tile rules (stage_table
tileInfo): 活性源石 tile_infection (deployed operators and passing enemies: attack and attack speed up, HP lost over
time), 沼泽地段 tile_mire (operators' attack speed and passing enemies' attack and move speed drop gradually),
隐匿模块 tile_duel_grassland (units and enemies on it are invisible), 优化模块 tile_speedup (operators' attack speed
up, passing enemies faster).

    python data/build_garrison_terrain.py <directory with level_act3autochess_mNN.json or mNN.json>
"""
import json
import sys
from pathlib import Path

LEVELS = {
    "desert_crates": "m01", "desert_cactus": "m02", "desert_steel": "m03", "desert_laser": "m04", "city": "m05",
    "desert_camo": "m06", "snow": "m09", "palace": "m10", "snow_ice": "m11", "palace_arrows": "m12",
}
KINDS = {"tile_infection": "infection", "tile_mire": "mire", "tile_duel_grassland": "hide", "tile_speedup": "speedup"}
DATA = Path(__file__).resolve().parent / "garrison_maps.json"


def level_grid(path: Path):
    d = json.loads(path.read_text(encoding="utf-8"))
    m = d["mapData"]
    tiles = m["tiles"]
    return [[tiles[i] for i in row] for row in m["map"]]


def locate(grid, cells, first_col):
    """(row, col) of our cell (0, first_col) in the level, and the row direction, from D and the two S cells."""
    ours = {(r, c): ch for r, line in enumerate(cells) for c, ch in enumerate(line)}
    d_cell = next(k for k, v in ours.items() if v == "D")
    s_cells = [k for k, v in ours.items() if v == "S"]
    for y, row in enumerate(grid):
        for x, t in enumerate(row):
            if t["tileKey"] != "tile_end":
                continue
            for flip in (1, -1):
                oy, ox = y - flip * d_cell[0], x - d_cell[1]

                def at(r, c):
                    yy, xx = oy + flip * r, ox + c
                    return grid[yy][xx]["tileKey"] if 0 <= yy < len(grid) and 0 <= xx < len(grid[yy]) else None
                if all(at(r, c) == "tile_start" for r, c in s_cells):
                    return oy, ox, flip, at
    raise SystemExit("board not found")


def main(levels: Path):
    data = json.loads(DATA.read_text(encoding="utf-8"))
    first_col = data["columns"][0]
    for m in data["maps"]:
        name = LEVELS[m["id"]]
        path = next(p for p in (levels / f"level_act3autochess_{name}.json", levels / f"{name}.json") if p.exists())
        grid = level_grid(path)
        *_, at = locate(grid, m["cells"], first_col)
        terrain = {}
        for r, line in enumerate(m["cells"]):
            for c in range(len(line)):
                kind = KINDS.get(at(r, c))
                if kind:
                    terrain.setdefault(kind, []).append([c + first_col, r])
        if terrain:
            m["terrain"] = terrain
        else:
            m.pop("terrain", None)
        print(m["id"], {k: len(v) for k, v in terrain.items()}, file=sys.stderr)
    with open(DATA, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(data, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    main(Path(sys.argv[1]))
