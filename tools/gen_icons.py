# -*- coding: utf-8 -*-
"""生成精美 UI 图标：錾金底盘 + 立体符号。输出 art/ui2/icon_<name>.png"""
import os, math
from PIL import Image, ImageDraw, ImageFilter

SS = 4
SZ = 96
GOLD = (240, 198, 104, 255)
GOLD_D = (146, 106, 38, 255)
GOLD_L = (255, 238, 180, 255)
DARK = (26, 22, 34, 255)

def new():
    return Image.new("RGBA", (SZ*SS, SZ*SS), (0, 0, 0, 0))

def plate(img, radius=0.30, tint=None, shape="round"):
    d = ImageDraw.Draw(img)
    S = SZ*SS
    if shape == "none":
        return d
    if shape == "round":
        box = [S*0.06, S*0.06, S*0.94, S*0.94]
        d.ellipse(box, fill=DARK)
        d.ellipse(box, outline=GOLD_D, width=int(S*0.035))
        d.ellipse([S*0.09, S*0.09, S*0.91, S*0.91], outline=GOLD, width=int(S*0.028))
        d.ellipse([S*0.13, S*0.13, S*0.87, S*0.87], outline=GOLD_L, width=int(S*0.010))
        # 顶部高光
        d.arc([S*0.12, S*0.12, S*0.88, S*0.88], 200, 340, fill=(255, 255, 255, 70), width=int(S*0.02))
    return d

def sym(d, name):
    S = SZ*SS
    cx = cy = S/2
    def C(x, y, r, col, ol=None, w=None):
        if ol: d.ellipse([cx+x*S-r-w, cy+y*S-r-w, cx+x*S+r+w, cy+y*S+r+w], fill=ol)
        d.ellipse([cx+x*S-r, cy+y*S-r, cx+x*S+r, cy+y*S+r], fill=col)
    def P(pts, col, ol=None, w=None):
        Q = [(cx+x*S, cy+y*S) for x, y in pts]
        if ol:
            d.polygon(Q, fill=ol, outline=ol, width=int(S*0.012))
        d.polygon(Q, fill=col)
    if name == "gold":
        C(0, 0, S*0.26, GOLD, GOLD_D, S*0.022)
        C(0, 0, S*0.20, (255, 222, 132, 255))
        C(-0.05, -0.06, S*0.07, (255, 250, 220, 220))
        P([(0, -0.10), (0.08, 0.02), (-0.08, 0.02)], (168, 122, 40, 255))
        P([(-0.11, 0.06), (0.11, 0.06), (0.09, 0.14), (-0.09, 0.14)], (168, 122, 40, 255))
    elif name == "gem":
        P([(0, -0.30), (0.22, -0.08), (0.13, 0.28), (-0.13, 0.28), (-0.22, -0.08)], (110, 226, 245, 255), (24, 84, 122, 255))
        P([(0, -0.30), (0.22, -0.08), (0, 0.04), (-0.22, -0.08)], (198, 250, 255, 235))
        P([(0, 0.04), (0.13, 0.28), (-0.13, 0.28)], (58, 168, 200, 255))
    elif name == "pop":
        C(0, -0.10, S*0.10, (238, 226, 206, 255), GOLD_D, S*0.016)
        P([(-0.20, 0.26), (-0.17, 0.02), (0, -0.04), (0.17, 0.02), (0.20, 0.26)], (120, 170, 235, 255), (30, 60, 110, 255))
        C(-0.20, 0.14, S*0.075, (238, 226, 206, 255), GOLD_D, S*0.014)
        C(0.20, 0.14, S*0.075, (238, 226, 206, 255), GOLD_D, S*0.014)
    elif name == "level":
        pts = []
        for k in range(10):
            a = -math.pi/2 + k*math.pi/5
            r = S*0.31 if k % 2 == 0 else S*0.13
            pts.append((math.cos(a)*r/S, math.sin(a)*r/S))
        P(pts, (255, 206, 84, 255), (150, 100, 20, 255))
        C(0, 0, S*0.08, (255, 248, 210, 235))
    elif name == "xp":
        P([(-0.22, -0.24), (0.22, -0.24), (0.22, 0.24), (-0.22, 0.24)], (112, 84, 200, 255), (48, 30, 100, 255))
        d.line([(cx, cy-S*0.22), (cx, cy+S*0.22)], fill=GOLD, width=int(S*0.022))
        for k in range(3):
            y = -0.12 + k*0.12
            d.line([(cx+S*0.03, cy+y*S), (cx+S*0.17, cy+y*S)], fill=(255, 240, 200, 200), width=int(S*0.018))
    elif name == "hp":
        P([(0, 0.30), (-0.30, 0.00), (-0.26, -0.18), (-0.12, -0.28), (0, -0.16),
           (0.12, -0.28), (0.26, -0.18), (0.30, 0.00)], (238, 76, 92, 255), (110, 20, 34, 255))
        C(-0.12, -0.14, S*0.055, (255, 190, 195, 235))
    elif name == "atk":
        d.line([(cx-S*0.20, cy+S*0.24), (cx+S*0.18, cy-S*0.18)], fill=(96, 66, 40, 255), width=int(S*0.055))
        P([(0.14, -0.30), (0.30, -0.14), (0.22, 0.02), (0.06, -0.06)], (226, 232, 244, 255), (90, 98, 118, 255))
        d.line([(cx-S*0.26, cy+S*0.16), (cx-S*0.10, cy+S*0.30)], fill=GOLD, width=int(S*0.05))
    elif name == "matk":
        C(0, 0, S*0.22, (168, 116, 250, 255), (60, 30, 120, 255), S*0.02)
        C(-0.06, -0.07, S*0.085, (238, 226, 255, 235))
        for k in range(4):
            a = k*math.tau/4 + 0.4
            d.line([(cx+math.cos(a)*S*0.26, cy+math.sin(a)*S*0.26),
                    (cx+math.cos(a)*S*0.36, cy+math.sin(a)*S*0.36)], fill=(206, 178, 255, 230), width=int(S*0.026))
    elif name == "pdef":
        P([(0, -0.30), (0.26, -0.18), (0.22, 0.14), (0, 0.32), (-0.22, 0.14), (-0.26, -0.18)],
          (150, 160, 180, 255), (50, 56, 74, 255))
        P([(0, -0.22), (0.16, -0.13), (0.14, 0.10), (0, 0.22), (-0.14, 0.10), (-0.16, -0.13)], (206, 214, 230, 255))
    elif name == "mdef":
        P([(0, -0.30), (0.26, -0.18), (0.22, 0.14), (0, 0.32), (-0.22, 0.14), (-0.26, -0.18)],
          (108, 148, 236, 255), (28, 52, 110, 255))
        d.line([(cx, cy-S*0.18), (cx, cy+S*0.18)], fill=(220, 234, 255, 230), width=int(S*0.024))
        d.line([(cx-S*0.14, cy+S*0.02), (cx+S*0.14, cy+S*0.02)], fill=(220, 234, 255, 230), width=int(S*0.024))
    elif name == "speed":
        P([(-0.06, -0.26), (0.18, -0.02), (0.02, 0.02), (0.16, 0.26), (-0.18, 0.00), (-0.02, -0.04)],
          (255, 216, 96, 255), (150, 100, 20, 255))
    elif name == "atkspeed":
        for k, off in enumerate((-0.14, 0.0, 0.14)):
            P([(off-0.06, -0.22), (off+0.07, -0.02), (off-0.01, 0.00), (off+0.06, 0.22), (off-0.08, 0.02), (off, -0.02)],
              (110, 226, 255, 255 - k*40), (20, 80, 110, 255))
    elif name == "range":
        d.arc([cx-S*0.30, cy-S*0.30, cx+S*0.30, cy+S*0.30], 130, 50, fill=(150, 110, 60, 255), width=int(S*0.05))
        d.line([(cx-S*0.06, cy-S*0.24), (cx-S*0.06, cy+S*0.24)], fill=(240, 238, 230, 220), width=int(S*0.018))
        P([(0.28, 0), (0.14, -0.07), (0.14, 0.07)], (240, 236, 226, 255), (90, 84, 74, 255))
    elif name == "kill":
        C(0, -0.03, S*0.23, (232, 228, 216, 255), (70, 66, 62, 255), S*0.02)
        P([(-0.23, 0.06), (0.23, 0.06), (0.17, 0.24), (-0.17, 0.24)], (232, 228, 216, 255), (70, 66, 62, 255))
        C(-0.09, -0.05, S*0.062, (28, 24, 28, 255))
        C(0.09, -0.05, S*0.062, (28, 24, 28, 255))
        P([(0, 0.02), (-0.035, 0.10), (0.035, 0.10)], (28, 24, 28, 255))
    elif name == "flame":
        P([(0, -0.32), (0.16, -0.06), (0.10, 0.10), (0.18, 0.20), (0, 0.32),
           (-0.18, 0.20), (-0.10, 0.10), (-0.16, -0.06)], (255, 138, 48, 255), (150, 50, 16, 255))
        P([(0, -0.10), (0.08, 0.06), (0, 0.24), (-0.08, 0.06)], (255, 226, 120, 245))
    elif name == "tier":
        for k, y in enumerate((-0.20, 0.0, 0.20)):
            P([(0, y-0.11), (0.22, y), (0, y+0.11), (-0.22, y)], GOLD if k < 3 else GOLD_D, GOLD_D)
    elif name in ("arrowL", "arrowR"):
        sgn = -1 if name == "arrowL" else 1
        P([(0.20*sgn, -0.26), (0.20*sgn, 0.26), (-0.22*sgn, 0)], (255, 214, 104, 255), (150, 100, 20, 255))
    elif name == "close":
        d.line([(cx-S*0.20, cy-S*0.20), (cx+S*0.20, cy+S*0.20)], fill=(248, 220, 160, 255), width=int(S*0.075))
        d.line([(cx+S*0.20, cy-S*0.20), (cx-S*0.20, cy+S*0.20)], fill=(248, 220, 160, 255), width=int(S*0.075))

ICONS = ["gold","gem","pop","level","xp","hp","atk","matk","pdef","mdef","speed",
         "atkspeed","range","kill","flame","tier","arrowL","arrowR","close"]

def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = os.path.join(root, "art", "ui2"); os.makedirs(out, exist_ok=True)
    tiles = []
    for n in ICONS:
        img = new()
        shape = "none" if n in ("arrowL", "arrowR", "close") else "round"
        if shape != "none":
            glow = Image.new("RGBA", img.size, (0,0,0,0))
            ImageDraw.Draw(glow).ellipse([SZ*SS*0.06, SZ*SS*0.06, SZ*SS*0.94, SZ*SS*0.94], fill=(255, 210, 120, 90))
            img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(SZ*SS*0.03)))
        d = plate(img, shape=shape)
        sym(d, n)
        img = img.resize((SZ, SZ), Image.LANCZOS)
        img.save(os.path.join(out, "icon_" + n + ".png"))
        tiles.append((n, img))
    cols = 7
    rows = (len(tiles)+cols-1)//cols
    sheet = Image.new("RGBA", (cols*SZ*2, rows*SZ*2), (22, 26, 30, 255))
    for i, (n, im) in enumerate(tiles):
        sheet.alpha_composite(im.resize((SZ*2, SZ*2), Image.LANCZOS), ((i % cols)*SZ*2, (i//cols)*SZ*2))
    sheet.save(os.path.join(root, "build", "shots", "icons_preview.png"))
    print("icons:", len(ICONS))

if __name__ == "__main__":
    main()
