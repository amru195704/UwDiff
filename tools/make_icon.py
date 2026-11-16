#!/usr/bin/env python3
# > パス: tools/make_icon.py
"""UwDiff のアイコンを描いて、png・ico・icns のもとを作る（要るもの: Pillow。icns は mac の iconutil）。
絵は Snap ストア用の 256px のアイコン（snap-uwview/uwdiff/store/uwdiff-snap-icon-256.png）と同じ:
濃紺の角丸の上に 2 枚の紙、左は削除（赤）・右は追加（緑）の行、間に両向きの矢印。
使い方: python3 tools/make_icon.py   → src/UwDiff.App/Assets/Icon.png・Icon.ico、src/UwDiff.App/macos/UwDiff.icns"""
import pathlib, subprocess, shutil, sys, tempfile
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / 'src/UwDiff.App/Assets'; MACOS = ROOT / 'src/UwDiff.App/macos'
BG, PAPER, GRAY, RED, GREEN, ARROW = (16, 22, 34), (239, 244, 251), (152, 164, 185), (239, 90, 88), (85, 179, 89), (110, 200, 235)

def draw(size=1024, ss=4):
    k = size * ss / 256.0                      # もとの絵は 256 の升目
    im = Image.new('RGBA', (size * ss, size * ss), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    def rr(x0, y0, x1, y1, r, c): d.rounded_rectangle([x0 * k, y0 * k, x1 * k, y1 * k], radius=r * k, fill=c)
    rr(7, 7, 249, 249, 42, BG)
    widths = [51, 40, 56, 46, 35, 53]          # 行の長さ（3・5 行目が変わった行）
    for px0, changed in ((42, RED), (136, GREEN)):
        rr(px0, 53, px0 + 77, 202, 7, PAPER)
        for i, w in enumerate(widths):
            y = 72 + 20 * i
            rr(px0 + 10, y, px0 + 10 + w, y + 8.6, 4.3, changed if i in (2, 4) else GRAY)
    cx, cy = 128, 128                           # 両向きの矢印
    d.polygon([(119.5 * k, cy * k), (127 * k, 121.5 * k), (127 * k, 134.5 * k)], fill=ARROW)
    d.polygon([(136.5 * k, cy * k), (129 * k, 121.5 * k), (129 * k, 134.5 * k)], fill=ARROW)
    d.rectangle([126 * k, 125.6 * k, 130 * k, 130.4 * k], fill=ARROW)
    return im.resize((size, size), Image.LANCZOS)

def main():
    ASSETS.mkdir(parents=True, exist_ok=True); MACOS.mkdir(parents=True, exist_ok=True)
    master = draw(1024)
    master.resize((512, 512), Image.LANCZOS).save(ASSETS / 'Icon.png')
    master.save(ASSETS / 'Icon.ico', sizes=[(s, s) for s in (16, 32, 48, 64, 128, 256)])
    if shutil.which('iconutil'):
        with tempfile.TemporaryDirectory() as t:
            iset = pathlib.Path(t) / 'UwDiff.iconset'; iset.mkdir()
            for pt in (16, 32, 128, 256, 512):
                master.resize((pt, pt), Image.LANCZOS).save(iset / f'icon_{pt}x{pt}.png')
                master.resize((pt * 2, pt * 2), Image.LANCZOS).save(iset / f'icon_{pt}x{pt}@2x.png')
            subprocess.run(['iconutil', '-c', 'icns', str(iset), '-o', str(MACOS / 'UwDiff.icns')], check=True)
    else:
        print('iconutil が無いので icns は作っていません（mac で実行してください）', file=sys.stderr)
    print('wrote', ASSETS / 'Icon.png', ASSETS / 'Icon.ico', MACOS / 'UwDiff.icns')

if __name__ == '__main__':
    main()
