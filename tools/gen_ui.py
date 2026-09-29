# -*- coding: utf-8 -*-
"""生成华丽 UI 贴图：錾金九宫格面板、圆形头像框、血条、卡片框、阵营徽记。"""
import os, math
from PIL import Image, ImageDraw, ImageFilter

OUT = None
GOLD = (238, 196, 104, 255)
GOLD_D = (150, 112, 44, 255)
GOLD_L = (255, 236, 176, 255)
STONE = (30, 26, 38, 245)
STONE_L = (48, 42, 60, 245)
SS = 3

def c(r, g, b, a=255): return (r, g, b, a)

def bevel_rect(d, x0, y0, x1, y1, fill, light, dark, w=2):
    d.rectangle([x0, y0, x1, y1], fill=fill)
    for i in range(w):
        d.line([(x0+i, y0+i), (x1-i, y0+i)], fill=light)
        d.line([(x0+i, y0+i), (x0+i, y1-i)], fill=light)
        d.line([(x0+i, y1-i), (x1-i, y1-i)], fill=dark)
        d.line([(x1-i, y0+i), (x1-i, y1-i)], fill=dark)

def panel(size=128, corner=34, radius=14):
    S = size*SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    C = corner*SS
    # 外发光
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(glow).rounded_rectangle([2*SS, 2*SS, S-3*SS, S-3*SS], radius=radius*SS+2*SS, fill=(255, 210, 120, 70))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(4*SS)))
    # 主体
    d.rounded_rectangle([3*SS, 3*SS, S-4*SS, S-4*SS], radius=radius*SS, fill=STONE)
    # 内层高光
    d.rounded_rectangle([6*SS, 6*SS, S-7*SS, S-7*SS], radius=max(2, radius-3)*SS, outline=(70, 62, 88, 200), width=1*SS)
    # 金边（多层）
    d.rounded_rectangle([3*SS, 3*SS, S-4*SS, S-4*SS], radius=radius*SS, outline=GOLD_D, width=3*SS)
    d.rounded_rectangle([5*SS, 5*SS, S-6*SS, S-6*SS], radius=max(2, radius-2)*SS, outline=GOLD, width=2*SS)
    d.rounded_rectangle([7*SS, 7*SS, S-8*SS, S-8*SS], radius=max(2, radius-4)*SS, outline=GOLD_L, width=1*SS)
    # 四角雕花
    for (cx, cy, sx, sy) in [(C, C, 1, 1), (S-C, C, -1, 1), (C, S-C, 1, -1), (S-C, S-C, -1, -1)]:
        d.line([(cx, cy - 9*SS*sy), (cx + 9*SS*sx, cy)], fill=GOLD, width=2*SS)
        d.line([(cx - 6*SS*sx, cy), (cx, cy + 6*SS*sy)], fill=GOLD, width=2*SS)
        d.ellipse([cx-3*SS, cy-3*SS, cx+3*SS, cy+3*SS], fill=GOLD_L, outline=GOLD_D, width=1*SS)
    return img.resize((size, size), Image.LANCZOS)

def round_frame(size=160, r=None):
    S = size*SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = cy = S//2
    R = (r or size*0.46)*SS
    # 外发光
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([cx-R-3*SS, cy-R-3*SS, cx+R+3*SS, cy+R+3*SS], outline=(255, 214, 130, 110), width=6*SS)
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(5*SS)))
    # 外环
    d.ellipse([cx-R, cy-R, cx+R, cy+R], outline=GOLD_D, width=7*SS)
    d.ellipse([cx-R+2*SS, cy-R+2*SS, cx+R-2*SS, cy+R-2*SS], outline=GOLD, width=5*SS)
    d.ellipse([cx-R+6*SS, cy-R+6*SS, cx+R-6*SS, cy+R-6*SS], outline=GOLD_L, width=1*SS)
    # 内环
    Ri = R - 9*SS
    d.ellipse([cx-Ri, cy-Ri, cx+Ri, cy+Ri], fill=(18, 16, 26, 235), outline=GOLD_D, width=2*SS)
    # 环上的铆钉
    for k in range(16):
        a = k*math.tau/16
        px, py = cx+math.cos(a)*R, cy+math.sin(a)*R
        d.ellipse([px-2.2*SS, py-2.2*SS, px+2.2*SS, py+2.2*SS], fill=GOLD_L)
    # 顶部冠饰
    d.polygon([(cx, cy-R-8*SS), (cx-7*SS, cy-R+3*SS), (cx+7*SS, cy-R+3*SS)], fill=GOLD, outline=GOLD_D)
    return img.resize((size, size), Image.LANCZOS)

def hpbar(w=440, h=36, team=(96, 180, 255)):
    S = SS
    W, H = w*S, h*S
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, W-1, H-1], radius=7*S, fill=(14, 12, 20, 235), outline=GOLD_D, width=2*S)
    inner = [4*S, 4*S, W-5*S, H-5*S]
    d.rounded_rectangle(inner, radius=5*S, fill=(28, 24, 36, 235))
    fill = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    fd = ImageDraw.Draw(fill)
    fd.rounded_rectangle(inner, radius=5*S, fill=(team[0], team[1], team[2], 255))
    ff = fill.filter(ImageFilter.GaussianBlur(1.5*S))
    img.alpha_composite(ff)
    d.rounded_rectangle(inner, radius=5*S, outline=(255, 255, 255, 60), width=1*S)
    # 顶部高光
    d.rounded_rectangle([6*S, 6*S, W-7*S, 13*S], radius=4*S, fill=(255, 255, 255, 45))
    return img.resize((w, h), Image.LANCZOS)

def card(size=128, corner=30):
    S = size*SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([2*SS, 2*SS, S-3*SS, S-3*SS], radius=10*SS, fill=(26, 24, 36, 245))
    d.rounded_rectangle([2*SS, 2*SS, S-3*SS, S-3*SS], radius=10*SS, outline=GOLD_D, width=3*SS)
    d.rounded_rectangle([5*SS, 5*SS, S-6*SS, S-6*SS], radius=8*SS, outline=GOLD, width=2*SS)
    d.rounded_rectangle([8*SS, 8*SS, S-9*SS, S-9*SS], radius=6*SS, outline=(255, 236, 176, 120), width=1*SS)
    return img.resize((size, size), Image.LANCZOS)

def crest(size=128, team="left"):
    S = size*SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = S//2
    body = (74, 146, 226, 255) if team == "left" else (216, 78, 84, 255)
    dark = (32, 66, 122, 255) if team == "left" else (112, 34, 40, 255)
    pts = [(cx, 6*SS), (S-12*SS, 22*SS), (S-18*SS, S-34*SS), (cx, S-6*SS), (18*SS, S-34*SS), (12*SS, 22*SS)]
    d.polygon(pts, fill=body)
    d.line(pts + [pts[0]], fill=GOLD, width=5*SS, joint="curve")
    d.line(pts + [pts[0]], fill=GOLD_D, width=1*SS, joint="curve")
    d.line([(cx, 20*SS), (cx, S-16*SS)], fill=dark, width=7*SS)
    d.line([(24*SS, 46*SS), (S-24*SS, 46*SS)], fill=dark, width=5*SS)
    d.ellipse([cx-12*SS, 54*SS, cx+12*SS, 78*SS], fill=GOLD_L, outline=GOLD_D, width=2*SS)
    for k in range(-1, 2):
        d.ellipse([cx+k*20*SS-4*SS, S-46*SS, cx+k*20*SS+4*SS, S-38*SS], fill=GOLD_L)
    return img.resize((size, size), Image.LANCZOS)

def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = os.path.join(root, "art", "ui2"); os.makedirs(out, exist_ok=True)
    panel(128, 34).save(os.path.join(out, "panel.png"))
    round_frame(160).save(os.path.join(out, "frame_round.png"))
    hpbar(440, 36, (96, 180, 255)).save(os.path.join(out, "bar_left.png"))
    hpbar(440, 36, (240, 104, 108)).save(os.path.join(out, "bar_right.png"))
    card(128, 30).save(os.path.join(out, "card.png"))
    crest(128, "left").save(os.path.join(out, "crest_left.png"))
    crest(128, "right").save(os.path.join(out, "crest_right.png"))
    # 预览
    pv = Image.new("RGBA", (760, 220), (22, 26, 30, 255))
    pv.alpha_composite(panel(128, 34).resize((180, 180), Image.LANCZOS), (10, 20))
    pv.alpha_composite(round_frame(160).resize((180, 180), Image.LANCZOS), (210, 20))
    pv.alpha_composite(card(128).resize((140, 140), Image.LANCZOS), (410, 40))
    pv.alpha_composite(crest(128, "left").resize((120, 120), Image.LANCZOS), (560, 50))
    pv.save(os.path.join(root, "build", "shots", "ui_preview.png"))
    print("ui art ->", out)

if __name__ == "__main__":
    main()
