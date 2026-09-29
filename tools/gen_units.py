# -*- coding: utf-8 -*-
"""程序化生成《米拉奇战纪》兵种精灵 v2：更大、更精细、更有辨识度。"""
import json, math, os
from PIL import Image, ImageDraw, ImageFilter

SS = 3
FW, FH = 128, 128
W, H = FW*SS, FH*SS

def clamp(v, a=0.0, b=1.0): return max(a, min(b, v))
def shade(c, k):
    r, g, b = c[0], c[1], c[2]; a = c[3] if len(c) > 3 else 255
    if k <= 1.0: return (int(r*k), int(g*k), int(b*k), a)
    t = k-1.0
    return (int(r+(255-r)*t), int(g+(255-g)*t), int(b+(255-b)*t), a)
def mix(a, b, t):
    t = clamp(t); n = min(len(a), len(b))
    return tuple(int(a[i]+(b[i]-a[i])*t) for i in range(n))

OL = (24, 20, 30, 255)
OL2 = (44, 38, 54, 255)

class C:
    def __init__(self):
        self.img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
    def ell(self, cx, cy, rx, ry, fill, outline=OL, ow=1.8):
        x, y, a, b = cx*SS, cy*SS, rx*SS, ry*SS
        if outline:
            w = ow*SS
            self.d.ellipse([x-a-w, y-b-w, x+a+w, y+b+w], fill=outline)
        if fill: self.d.ellipse([x-a, y-b, x+a, y+b], fill=fill)
    def cap(self, x1, y1, x2, y2, r, fill, outline=OL, ow=1.5):
        p = [(x1*SS, y1*SS), (x2*SS, y2*SS)]
        if outline:
            R = (r+ow)*SS
            self.d.line(p, fill=outline, width=int(R*2))
            for q in p: self.d.ellipse([q[0]-R, q[1]-R, q[0]+R, q[1]+R], fill=outline)
        R = r*SS
        self.d.line(p, fill=fill, width=int(R*2))
        for q in p: self.d.ellipse([q[0]-R, q[1]-R, q[0]+R, q[1]+R], fill=fill)
    def poly(self, pts, fill, outline=OL, ow=1.6):
        P = [(x*SS, y*SS) for x, y in pts]
        if outline: self.d.polygon(P, fill=outline, outline=outline, width=int(ow*SS))
        if fill: self.d.polygon(P, fill=fill)
    def line(self, pts, fill, w=1.5):
        self.d.line([(x*SS, y*SS) for x, y in pts], fill=fill, width=max(1, int(w*SS)), joint="curve")
    def arc(self, cx, cy, r, s, e, fill, w=1.8):
        self.d.arc([(cx-r)*SS, (cy-r)*SS, (cx+r)*SS, (cy+r)*SS], s, e, fill=fill, width=max(1, int(w*SS)))
    def glow(self, cx, cy, r, col, blur=0.55):
        lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        ImageDraw.Draw(lay).ellipse([(cx-r)*SS, (cy-r)*SS, (cx+r)*SS, (cy+r)*SS], fill=col)
        lay = lay.filter(ImageFilter.GaussianBlur(radius=max(1.0, r*blur*SS)))
        self.img.alpha_composite(lay)

# ============ 外观规格 ============
def spec_of(u):
    t = set(u.get("tags") or [])
    uid = u.get("id", "x")
    s = dict(kind="human", skin=(240, 202, 170, 255), hair=(92, 62, 42, 255),
             cloth=(74, 104, 176, 255), armor=(182, 190, 204, 255), accent=(226, 176, 72, 255),
             trim=(206, 214, 228, 255), weapon="sword", helm="cap", cape=None,
             eyes=(38, 36, 54, 255), aura=None, mount=None, wings=None, build=1.0, legs=True)
    if "undead" in t:
        s.update(kind="undead", skin=(208, 214, 226, 255), hair=(58, 56, 72, 255),
                 cloth=(60, 48, 88, 255), armor=(126, 128, 148, 255), accent=(132, 84, 198, 255),
                 eyes=(126, 255, 176, 255), aura=(120, 255, 170, 46), trim=(150, 154, 176, 255))
    if "machine" in t:
        s.update(kind="machine", skin=(158, 164, 176, 255), cloth=(66, 70, 82, 255),
                 armor=(196, 202, 216, 255), accent=(238, 146, 58, 255), eyes=(255, 180, 72, 255),
                 aura=(255, 150, 50, 42), helm="mech")
    if "magic" in t:
        s.update(cloth=(96, 72, 182, 255), accent=(204, 168, 96, 255), helm="hat",
                 eyes=(202, 168, 255, 255), aura=(152, 112, 255, 52))
    if "ranged" in t: s["weapon"] = "bow"
    if "cavalry" in t: s["mount"] = "horse"
    if "shield" in t: s["weapon"] = "sword"
    if "assassin" in t:
        s.update(kind="assassin", cloth=(50, 46, 66, 255), armor=(100, 96, 122, 255),
                 helm="hood", accent=(154, 114, 206, 255), eyes=(255, 96, 124, 255),
                 weapon="claw", trim=(72, 68, 94, 255))
    if "hero" in t:
        s.update(accent=(244, 204, 100, 255), cape=(196, 62, 68, 255), helm="plume",
                 armor=(212, 202, 172, 255))
    if "giant" in t:
        s.update(skin=(158, 148, 124, 255), cloth=(100, 88, 70, 255), armor=(150, 140, 120, 255),
                 build=1.22)
    if "beast" in t:
        s.update(kind="beast", skin=(158, 124, 88, 255), hair=(102, 74, 48, 255),
                 cloth=(96, 76, 52, 255), armor=(140, 118, 88, 255))
    if "demon" in t:
        s.update(kind="demon", skin=(196, 70, 70, 255), cloth=(74, 32, 44, 255),
                 armor=(126, 54, 60, 255), accent=(255, 148, 64, 255), eyes=(255, 232, 96, 255),
                 aura=(255, 84, 62, 58), wings="demon", helm="horns")
    if "dragon" in t:
        s.update(kind="dragon", skin=(98, 146, 116, 255), armor=(202, 178, 100, 255),
                 accent=(244, 204, 96, 255), wings="dragon", eyes=(255, 214, 84, 255),
                 helm="horns")
    if "elemental" in t:
        s.update(kind="elemental", cloth=(255, 144, 62, 255), accent=(255, 214, 96, 255),
                 aura=(255, 152, 52, 96), eyes=(255, 246, 196, 255), legs=False)
    if "plant" in t:
        s.update(kind="plant", skin=(126, 100, 64, 255), cloth=(72, 112, 62, 255),
                 armor=(100, 82, 56, 255), accent=(116, 178, 84, 255), eyes=(196, 255, 156, 255))
    if uid == "spider_queen":
        s.update(kind="spider", skin=(92, 76, 130, 255), cloth=(64, 52, 96, 255),
                 armor=(120, 100, 160, 255), legs=True)
    if uid == "gargoyle":
        s.update(kind="gargoyle", skin=(146, 152, 166, 255), armor=(160, 166, 180, 255),
                 wings="stone", eyes=(255, 124, 84, 255), aura=(255, 120, 70, 40))
    if uid == "phoenix":
        s.update(kind="elemental", wings="fire", cloth=(255, 132, 52, 255),
                 accent=(255, 220, 120, 255), aura=(255, 172, 62, 104), legs=False)
    if uid == "siren":
        s.update(kind="siren", cloth=(84, 176, 196, 255), accent=(216, 244, 252, 255),
                 hair=(70, 156, 180, 255), legs=False)
    if uid == "ballista":
        s.update(kind="machine", weapon="bow", build=1.16)
    if "hero" in t and uid in ("guan_yu", "zhao_yun"):
        s.update(cloth=(58, 112, 84, 255), armor=(198, 182, 132, 255), accent=(240, 196, 96, 255))
    hue = sum(ord(ch) for ch in uid)
    tw = ((hue % 9) - 4) * 0.045
    s["cloth"] = shade(s["cloth"], 1.0+tw)
    s["armor"] = shade(s["armor"], 1.0+tw*0.45)
    s["armor"] = shade(s["armor"], 1.0 + (((hue // 7) % 5) - 2) * 0.05)
    s["accent"] = shade(s["accent"], 1.0 + (((hue // 11) % 5) - 2) * 0.06)
    s["_tags"] = list(t)
    return s

# ============ 武器 ============
def draw_weapon(c, s, hx, hy, ang, sc):
    a = math.radians(ang); ux, uy = math.cos(a), math.sin(a)
    col, acc, trim = s["armor"], s["accent"], s["trim"]
    w = s["weapon"]; L = 34*sc
    if w == "sword":
        ex, ey = hx+ux*L, hy+uy*L
        c.cap(hx-ux*6, hy-uy*6, hx+ux*4, hy+uy*4, 2.4, (96, 66, 40, 255))
        c.cap(hx+ux*2, hy+uy*2, ex, ey, 3.0, shade(col, 1.06))
        c.line([(hx+ux*4-uy*1.0, hy+uy*4+ux*1.0), (ex-ux*3-uy*1.0, ey-uy*3+ux*1.0)], shade(trim, 1.18), 1.1)
        c.poly([(ex+ux*7, ey+uy*7), (ex-uy*4.2, ey+ux*4.2), (ex+uy*4.2, ey-ux*4.2)], shade(trim, 1.15))
    elif w == "spear":
        ex, ey = hx+ux*L*1.5, hy+uy*L*1.5
        c.cap(hx-ux*10, hy-uy*10, ex, ey, 1.9, (128, 92, 54, 255))
        c.poly([(ex+ux*13, ey+uy*13), (ex-uy*4.4, ey+ux*4.4), (ex+uy*4.4, ey-ux*4.4)], shade(trim, 1.16))
        c.poly([(ex-ux*2, ey-uy*2), (ex-uy*5, ey+ux*5), (ex-ux*9, ey-uy*9)], shade(acc, 1.0))
    elif w == "bow":
        cx, cy = hx+ux*10, hy+uy*10
        c.arc(cx, cy, 15*sc, ang-74, ang+74, shade((146, 100, 58), 1.05), 2.6)
        p1 = (cx+math.cos(math.radians(ang-74))*15*sc, cy+math.sin(math.radians(ang-74))*15*sc)
        p2 = (cx+math.cos(math.radians(ang+74))*15*sc, cy+math.sin(math.radians(ang+74))*15*sc)
        c.line([p1, p2], (240, 238, 232, 255), 1.0)
        c.cap(hx, hy, cx, cy, 1.6, (150, 108, 64, 255))
    elif w == "staff":
        ex, ey = hx+ux*L*1.15, hy+uy*L*1.15
        c.cap(hx-ux*9, hy-uy*9, ex, ey, 2.1, (116, 84, 54, 255))
        c.glow(ex, ey, 11*sc, (acc[0], acc[1], acc[2], 150))
        c.ell(ex, ey, 6.2*sc, 6.2*sc, shade(acc, 1.2))
        c.ell(ex, ey, 3.0*sc, 3.0*sc, (255, 255, 245, 255), None)
    elif w == "hammer":
        ex, ey = hx+ux*L*0.95, hy+uy*L*0.95
        c.cap(hx-ux*7, hy-uy*7, ex, ey, 2.3, (124, 90, 56, 255))
        c.poly([(ex-uy*8, ey+ux*8), (ex+uy*8, ey-ux*8),
                (ex+ux*13+uy*7, ey+uy*13-ux*7), (ex+ux*13-uy*7, ey+uy*13+ux*7)], shade(col, 1.1))
    elif w == "scythe":
        ex, ey = hx+ux*L*1.25, hy+uy*L*1.25
        c.cap(hx-ux*9, hy-uy*9, ex, ey, 2.1, (76, 64, 80, 255))
        c.arc(ex, ey, 14*sc, ang+142, ang+338, shade(trim, 1.2), 3.0)
    elif w == "claw":
        for k in (-19, 0, 19):
            c.cap(hx, hy, hx+math.cos(math.radians(ang+k))*14*sc,
                  hy+math.sin(math.radians(ang+k))*14*sc, 1.9, (238, 234, 226, 255))
    else:
        c.cap(hx, hy, hx+ux*L, hy+uy*L, 2.6, shade(col, 1.08))

# ============ 角色 ============
def draw_char(c, s, p):
    bob = p["bob"]; lean = p.get("lean", 0.0)
    big = s["build"]
    base = 124.0
    cx = 64 + lean*3.5
    headY = 30.0
    hr = 16.0*big
    shY = headY + hr + 5.0          # 肩线
    hipY = 86.0 + bob               # 胯线
    shoulderW = 16.0*big
    # 影子
    c.d.ellipse([(cx-21*big)*SS, (base-2)*SS, (cx+21*big)*SS, (base+6)*SS], fill=(0, 0, 0, 62))
    if s["aura"]: c.glow(cx, 72, 28*big, s["aura"])
    # 披风
    if s["cape"]:
        sw = p.get("cape", 0.0)
        c.poly([(cx-13, shY-2), (cx+13, shY-2), (cx+18+sw*4, base-6), (cx-19-sw*4, base-6)],
               shade(s["cape"], 0.9))
        c.poly([(cx-7, shY-1), (cx+7, shY-1), (cx+10+sw*2, base-12), (cx-11-sw*2, base-12)],
               shade(s["cape"], 1.14))
    # 翅膀
    if s["wings"]:
        wc = {"dragon": (240, 224, 194, 255), "stone": (174, 180, 194, 255),
              "demon": (98, 44, 56, 255), "fire": (255, 178, 78, 235)}.get(s["wings"], (220, 220, 220, 255))
        fl = 5 + math.sin(p.get("t", 0.0)*2.4)*4
        for sg in (-1, 1):
            c.poly([(cx+sg*(8*big), shY+2), (cx+sg*(40+fl), shY-16-fl), (cx+sg*(46+fl*0.4), shY+12),
                    (cx+sg*(13*big), shY+16)], wc)
            c.line([(cx+sg*10, shY+6), (cx+sg*(38+fl*0.6), shY-8-fl*0.5)], shade(wc, 0.78), 1.0)
    # 坐骑
    if s["mount"]:
        mc = (134, 102, 70, 255); my = 88
        c.ell(cx, my+10, 27, 12, mc)
        c.ell(cx-22, my+2, 9, 10, shade(mc, 1.06))
        c.poly([(cx-30, my-6), (cx-23, my-10), (cx-22, my+1)], shade(mc, 0.94))
        c.ell(cx-25.5, my-2, 1.8, 2.2, (20, 18, 26, 255), None)
        for dx in (-15, 15):
            c.cap(cx+dx, my+16, cx+dx+2, base-2, 3.6, shade(mc, 0.84))
        c.poly([(cx-9, my-12), (cx+11, my-14), (cx+10, my+4), (cx-8, my+6)], shade(s["cloth"], 0.92))
    legs = s.get("legs", True)
    # ---- 后腿 ----
    lg = p["legL"]
    if legs:
        lx = cx - 9.0*big; knee = (hipY+base)/2
        c.cap(lx, hipY-2, lx+lg*5, knee, 5.0*big, shade(s["cloth"], 0.62))
        c.cap(lx+lg*5, knee, lx+lg*8, base-7, 4.6*big, shade(s["cloth"], 0.62))
        c.poly([(lx+lg*8-6.5*big, base-10), (lx+lg*8+6.5*big, base-10),
                (lx+lg*8+6*big, base-1), (lx+lg*8-6*big, base-1)], shade(s["armor"], 0.6))
    # ---- 后臂 ----
    ag = p["armL"]
    c.cap(cx-shoulderW*0.9, shY+3, cx-shoulderW*0.9-ag*5, shY+20, 4.0*big, shade(s["armor"], 0.68))
    # ---- 躯干 ----
    c.poly([(cx-shoulderW, shY), (cx+shoulderW, shY),
            (cx+shoulderW*0.82, hipY), (cx-shoulderW*0.82, hipY)], s["cloth"])
    c.poly([(cx-shoulderW-0.5, shY-1), (cx+shoulderW+0.5, shY-1),
            (cx+shoulderW*0.88, shY+15), (cx-shoulderW*0.88, shY+15)], s["armor"])
    c.poly([(cx-shoulderW*0.88, shY+14), (cx+shoulderW*0.88, shY+14),
            (cx+shoulderW*0.84, shY+17), (cx-shoulderW*0.84, shY+17)], shade(s["accent"], 1.0))
    c.line([(cx-shoulderW*0.8, hipY-4), (cx+shoulderW*0.8, hipY-4)], (58, 46, 36, 255), 2.4)
    c.ell(cx, hipY-4, 2.6*big, 2.6*big, shade(s["accent"], 1.3), OL, 1.1)
    # 护肩
    for sg in (-1, 1):
        c.ell(cx+sg*shoulderW*0.92, shY+2, 6.4*big, 5.2*big, shade(s["armor"], 1.12))
    # ---- 前腿 ----
    if legs:
        rg = p["legR"]; rx = cx + 9.0*big
        c.cap(rx, hipY-2, rx+rg*5, knee, 5.0*big, s["cloth"])
        c.cap(rx+rg*5, knee, rx+rg*8, base-7, 4.6*big, s["cloth"])
        c.poly([(rx+rg*8-6.5*big, base-10), (rx+rg*8+6.5*big, base-10),
                (rx+rg*8+6*big, base-1), (rx+rg*8-6*big, base-1)], s["armor"])
    # ---- 头 ----
    hy = headY + bob*0.35
    c.ell(cx, hy, hr, hr*1.02, s["skin"], OL, 1.8)
    c.ell(cx-hr*0.30, hy-hr*0.34, hr*0.52, hr*0.44, shade(s["skin"], 1.13), None)
    c.ell(cx, hy+hr*0.72, hr*0.5, hr*0.24, shade(s["skin"], 0.86), None)
    # 眼睛
    ec = s["eyes"]; eo = hr*0.30; eyy = hy + 1.5
    if s["kind"] == "undead":
        c.ell(cx-eo, eyy, hr*0.17, hr*0.20, (16, 14, 20, 255), None)
        c.ell(cx+eo, eyy, hr*0.17, hr*0.20, (16, 14, 20, 255), None)
    c.ell(cx-eo, eyy, hr*0.135, hr*0.175, ec, None)
    c.ell(cx+eo, eyy, hr*0.135, hr*0.175, ec, None)
    c.ell(cx-eo-hr*0.05, eyy-hr*0.07, hr*0.055, hr*0.07, (255, 255, 255, 225), None)
    c.ell(cx+eo-hr*0.05, eyy-hr*0.07, hr*0.055, hr*0.07, (255, 255, 255, 225), None)
    if s["kind"] in ("undead", "demon", "elemental", "gargoyle", "machine"):
        c.glow(cx-eo, eyy, hr*0.3, (ec[0], ec[1], ec[2], 130))
        c.glow(cx+eo, eyy, hr*0.3, (ec[0], ec[1], ec[2], 130))
    # 嘴
    if s["kind"] == "undead":
        c.line([(cx-hr*0.28, hy+hr*0.46), (cx+hr*0.28, hy+hr*0.46)], (70, 66, 84, 255), 1.0)
        for k in (-1, 0, 1):
            c.line([(cx+k*hr*0.22, hy+hr*0.34), (cx+k*hr*0.22, hy+hr*0.58)], (70, 66, 84, 255), 0.7)
    # ---- 头饰 ----
    hh = s["helm"]
    if hh == "hat":
        c.poly([(cx-hr*1.7, hy-hr*0.34), (cx+hr*1.7, hy-hr*0.34),
                (cx+hr*0.72, hy-hr*1.05), (cx-hr*0.72, hy-hr*1.05)], shade(s["cloth"], 1.16))
        c.ell(cx, hy-hr*1.12, hr*0.78, hr*0.26, shade(s["cloth"], 1.22), OL, 1.3)
        c.ell(cx, hy-hr*1.02, hr*0.8, hr*0.2, shade(s["accent"], 1.05))
    elif hh == "hood":
        c.poly([(cx-hr*1.12, hy+hr*0.5), (cx-hr*1.06, hy-hr*0.5), (cx, hy-hr*1.08),
                (cx+hr*1.06, hy-hr*0.5), (cx+hr*1.12, hy+hr*0.5)], shade(s["cloth"], 1.08))
        c.ell(cx, hy+hr*0.16, hr*0.76, hr*0.66, (18, 16, 26, 255), None)
        c.ell(cx-eo, eyy, hr*0.125, hr*0.165, ec, None)
        c.ell(cx+eo, eyy, hr*0.125, hr*0.165, ec, None)
    elif hh == "plume":
        c.ell(cx, hy-hr*0.30, hr*1.06, hr*0.84, shade(s["armor"], 1.15))
        c.ell(cx, hy-hr*0.86, hr*0.98, hr*0.30, shade(s["armor"], 1.22))
        c.poly([(cx-hr*0.16, hy-hr*1.0), (cx+hr*0.16, hy-hr*1.0),
                (cx+hr*0.12, hy-hr*1.85), (cx-hr*0.12, hy-hr*1.85)], shade(s["accent"], 1.06))
    elif hh == "mech":
        c.poly([(cx-hr*1.02, hy+hr*0.2), (cx-hr*0.96, hy-hr*0.72), (cx, hy-hr*1.06),
                (cx+hr*0.96, hy-hr*0.72), (cx+hr*1.02, hy+hr*0.2)], shade(s["armor"], 1.12))
        c.ell(cx, eyy, hr*0.58, hr*0.22, (24, 26, 34, 255), None)
        c.ell(cx-eo, eyy, hr*0.14, hr*0.14, ec, None)
        c.ell(cx+eo, eyy, hr*0.14, hr*0.14, ec, None)
    elif hh == "horns":
        c.ell(cx, hy-hr*0.42, hr*1.04, hr*0.72, shade(s["armor"], 1.1))
        c.poly([(cx-hr*0.6, hy-hr*0.8), (cx-hr*0.22, hy-hr*0.9), (cx-hr*0.85, hy-hr*1.7)], (244, 236, 218, 255))
        c.poly([(cx+hr*0.6, hy-hr*0.8), (cx+hr*0.22, hy-hr*0.9), (cx+hr*0.85, hy-hr*1.7)], (244, 236, 218, 255))
    else:
        c.ell(cx, hy-hr*0.42, hr*1.05, hr*0.76, s["hair"])
        c.poly([(cx-hr*1.0, hy-hr*0.1), (cx-hr*0.86, hy-hr*0.78), (cx, hy-hr*1.04),
                (cx+hr*0.86, hy-hr*0.78), (cx+hr*1.0, hy-hr*0.1),
                (cx+hr*0.72, hy-hr*0.5), (cx-hr*0.72, hy-hr*0.5)], shade(s["armor"], 1.08))
        c.ell(cx, hy-hr*0.52, hr*0.96, hr*0.22, shade(s["accent"], 1.06))
    if s["kind"] == "plant":
        for k in (-1, 0, 1):
            c.poly([(cx+k*hr*0.5-3, hy-hr*0.9), (cx+k*hr*0.5+3, hy-hr*0.9),
                    (cx+k*hr*0.66, hy-hr*1.6)], s["accent"])
    if s["kind"] == "beast":
        c.poly([(cx-hr*0.86, hy-hr*0.5), (cx-hr*0.38, hy-hr*0.62), (cx-hr*0.58, hy-hr*1.35)], shade(s["skin"], 1.05))
        c.poly([(cx+hr*0.86, hy-hr*0.5), (cx+hr*0.38, hy-hr*0.62), (cx+hr*0.58, hy-hr*1.35)], shade(s["skin"], 1.05))
        c.poly([(cx-hr*0.26, hy+hr*0.44), (cx+hr*0.26, hy+hr*0.44), (cx, hy+hr*0.72)], (250, 250, 244, 255))
    # ---- 盾 ----
    if "shield" in s["_tags"]:
        sx = cx - shoulderW - 8*big
        c.poly([(sx-7, shY-2), (sx+9, shY-7), (sx+9, shY+22), (sx-7, shY+26)], shade(s["armor"], 1.08))
        c.poly([(sx-4, shY+2), (sx+6, shY-1), (sx+6, shY+19), (sx-4, shY+22)], shade(s["accent"], 1.0))
    # ---- 前臂 + 武器 ----
    sw = p.get("swing", 0.0); ag2 = p["armR"]
    ax = cx + shoulderW*0.9 + ag2*7
    ay = shY + 19 + ag2*8
    c.cap(cx+shoulderW*0.9, shY+3, ax, ay, 4.0*big, s["armor"])
    c.ell(ax, ay, 3.6*big, 3.6*big, shade(s["skin"], 0.94))
    draw_weapon(c, s, ax+2, ay+3, p.get("wangle", 14.0), big)
    if s["kind"] == "elemental":
        for k in range(6):
            fx = cx-13+k*5.2; fh = 10+math.sin(p.get("t", 0)*3+k)*5
            c.poly([(fx-3, hipY+2), (fx+3, hipY+2), (fx, hipY-fh)], shade(s["cloth"], 1.0+0.07*k))

def frames_for(s):
    out = []
    N = 6
    for i in range(N):
        t = i/N*math.tau
        out.append(("idle", dict(bob=math.sin(t)*1.2, legL=0.0, legR=0.0, armL=math.sin(t)*0.10,
                                 armR=math.sin(t+1.2)*0.10, lean=math.sin(t)*0.06, swing=0.0,
                                 wangle=14+math.sin(t)*5, t=t, cape=math.sin(t)*0.6)))
    N = 8
    for i in range(N):
        t = i/N*math.tau
        out.append(("walk", dict(bob=abs(math.sin(t))*1.8-0.7, legL=math.sin(t), legR=-math.sin(t),
                                 armL=-math.sin(t)*0.85, armR=math.sin(t)*0.85, lean=0.30,
                                 swing=0.0, wangle=18+math.sin(t)*8, t=t, cape=math.sin(t)*1.2)))
    key = [(-16, -0.9, 0.10), (-34, -1.1, 0.26), (40, 1.0, 1.0), (56, 0.9, 0.70), (32, 0.3, 0.34), (18, 0.0, 0.13)]
    for k, (wa, lean, sw) in enumerate(key):
        out.append(("attack", dict(bob=-sw*1.8, legL=-sw*0.3, legR=sw*0.6, armL=-sw*0.55,
                                   armR=sw*1.2, lean=lean, swing=sw, wangle=wa, t=k*0.9,
                                   cape=-sw*1.8)))
    return out

def build(unit, outdir):
    s = spec_of(unit)
    fr = frames_for(s)
    strip = Image.new("RGBA", (FW*len(fr), FH), (0, 0, 0, 0))
    anim = {}
    for i, (a, pose) in enumerate(fr):
        c = C(); draw_char(c, s, pose)
        strip.alpha_composite(c.img.resize((FW, FH), Image.LANCZOS), (i*FW, 0))
        anim.setdefault(a, []).append(i)
    strip.save(os.path.join(outdir, unit["id"] + ".png"))
    return {"frames": anim, "count": len(fr)}

def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    data = json.load(open(os.path.join(root, "data", "units.json"), encoding="utf-8"))
    outdir = os.path.join(root, "art", "units2"); os.makedirs(outdir, exist_ok=True)
    man = {}
    for u in data["units"]:
        man[u["id"]] = build(u, outdir)
    json.dump({"frameW": FW, "frameH": FH, "units": man},
              open(os.path.join(outdir, "manifest.json"), "w", encoding="utf-8"), ensure_ascii=False)
    print("generated", len(man), "units")

if __name__ == "__main__":
    main()
