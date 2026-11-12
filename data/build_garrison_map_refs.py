#!/usr/bin/env python3
"""
生成 data/garrison_map_refs.json：识别当前地图用的颜色指纹。

每张地图在“商店收起 / 商店展开”两种镜头下各取一张休整期截图，按 garrison_maps.json 的镜头坐标，
在每个格子里取两个小块的平均颜色（避开格子中心和左上角，叠加层的路线和标记画在那里）。
运行时用同样的方法取当前画面的指纹，和这里逐格比较，最接近的就是当前地图和镜头。

用法：python build_garrison_map_refs.py <截图目录>
截图文件名见下方 SAMPLES；缺少某个镜头的截图时，运行时会用另一个镜头的指纹代替。
"""
import json
import sys
from pathlib import Path

from PIL import Image

DATA_DIR = Path(__file__).resolve().parent
# 与 Core/Services/BoardRecognizer.cs 保持一致
COLUMNS = range(-1, 8)
ROWS = range(0, 5)
SAMPLE_POINTS = [(0.32, 0.72), (0.72, 0.32)]
PATCH_RADIUS = 0.1  # 以格子宽度为单位

SAMPLES = {
    "desert_crates": {"expanded": "ScreenShot_2026-11-12_151844_985.png"},
    "palace": {"collapsed": "ScreenShot_2026-11-12_152322_903.png", "expanded": "ScreenShot_2026-11-12_152309_520.png"},
    "snow": {"collapsed": "ScreenShot_2026-11-12_152604_326.png", "expanded": "ScreenShot_2026-11-12_152555_278.png"},
    "desert_camo": {"collapsed": "ScreenShot_2026-11-12_152800_403.png", "expanded": "ScreenShot_2026-11-12_152748_662.png"},
    "desert_steel": {"collapsed": "ScreenShot_2026-11-12_153022_372.png", "expanded": "ScreenShot_2026-11-12_153032_228.png"},
    "palace_arrows": {"collapsed": "ScreenShot_2026-11-12_153651_867.png", "expanded": "ScreenShot_2026-11-12_153643_677.png"},
    "desert_laser": {"collapsed": "ScreenShot_2026-11-12_153819_663.png", "expanded": "ScreenShot_2026-11-12_153810_839.png"},
    "city": {"collapsed": "ScreenShot_2026-11-12_154004_676.png", "expanded": "ScreenShot_2026-11-12_153956_632.png"},
    "desert_cactus": {"collapsed": "ScreenShot_2026-11-12_154738_522.png", "expanded": "ScreenShot_2026-11-12_154730_215.png"},
    "snow_ice": {"collapsed": "ScreenShot_2026-11-12_154901_629.png", "expanded": "ScreenShot_2026-11-12_154851_169.png"},
}


def homography(points):
    """4 组 (col,row) -> (x,y) 对应点求单应矩阵（与 C# BoardCamera 相同的解法）。"""
    a, b = [], []
    for (c, r), (x, y) in points:
        a.append([c, r, 1, 0, 0, 0, -x * c, -x * r]); b.append(x)
        a.append([0, 0, 0, c, r, 1, -y * c, -y * r]); b.append(y)
    n = 8
    m = [row[:] + [v] for row, v in zip(a, b)]
    for i in range(n):
        p = max(range(i, n), key=lambda k: abs(m[k][i]))
        m[i], m[p] = m[p], m[i]
        for k in range(n):
            if k != i:
                f = m[k][i] / m[i][i]
                m[k] = [vk - f * vi for vk, vi in zip(m[k], m[i])]
    h = [m[i][n] / m[i][i] for i in range(n)] + [1.0]
    return lambda c, r: ((h[0] * c + h[1] * r + h[2]) / (h[6] * c + h[7] * r + 1),
                         (h[3] * c + h[4] * r + h[5]) / (h[6] * c + h[7] * r + 1))


def fingerprint(image: Image.Image, project) -> list[list[int]]:
    w, h = image.size
    px = image.load()
    out = []
    for r in ROWS:
        for c in COLUMNS:
            for sx, sy in SAMPLE_POINTS:
                x, y = project(c + sx, r + sy)
                x2, _ = project(c + sx + PATCH_RADIUS, r + sy)
                rad = max(2, int(abs(x2 - x) * w))
                cx, cy = int(x * w), int(y * h)
                acc, n = [0, 0, 0], 0
                for yy in range(max(0, cy - rad), min(h, cy + rad + 1)):
                    for xx in range(max(0, cx - rad), min(w, cx + rad + 1)):
                        p = px[xx, yy]
                        acc = [acc[0] + p[0], acc[1] + p[1], acc[2] + p[2]]; n += 1
                out.append([round(v / max(n, 1)) for v in acc])
    return out


def main() -> None:
    samples = Path(sys.argv[1])
    maps = json.loads((DATA_DIR / "garrison_maps.json").read_text(encoding="utf-8"))
    cameras = {k: homography(v) for k, v in maps["cameras"].items()}
    refs = {}
    for map_id, shots in SAMPLES.items():
        refs[map_id] = {}
        for camera, file in shots.items():
            with Image.open(samples / file) as im:
                refs[map_id][camera] = fingerprint(im.convert("RGB"), cameras[camera])
    out = {
        "_notice": "由 build_garrison_map_refs.py 从休整期截图生成；每项是逐格取样的 RGB 平均色，顺序为行 0–4、列 -1–7、每格两个取样点。",
        "columns": [COLUMNS.start, COLUMNS.stop - 1],
        "rows": [ROWS.start, ROWS.stop - 1],
        "samplePoints": SAMPLE_POINTS,
        "patchRadius": PATCH_RADIUS,
        "maps": refs,
    }
    (DATA_DIR / "garrison_map_refs.json").write_text(json.dumps(out, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(f"已写入 {len(refs)} 张地图的指纹", file=sys.stderr)


if __name__ == "__main__":
    main()
