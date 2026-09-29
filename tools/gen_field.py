# -*- coding: utf-8 -*-
"""生成战场草地贴图（可平铺）与装饰叠加层。"""
import os, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

def value_noise(size, cells, seed):
    rng = np.random.default_rng(seed)
    g = rng.random((cells + 1, cells + 1)).astype(np.float32)
    g[-1, :] = g[0, :]; g[:, -1] = g[:, 0]
    xs = np.linspace(0, cells, size, endpoint=False)
    x0 = xs.astype(int); x1 = (x0 + 1) % cells
    fx = (xs - x0).astype(np.float32); fx = fx * fx * (3 - 2 * fx)
    ys = np.linspace(0, cells, size, endpoint=False)
    y0 = ys.astype(int); y1 = (y0 + 1) % cells
    fy = (ys - y0).astype(np.float32); fy = fy * fy * (3 - 2 * fy)
    a = g[np.ix_(y0, x0)]; b = g[np.ix_(y0, x1)]
    c = g[np.ix_(y1, x0)]; d = g[np.ix_(y1, x1)]
    top = a + (b - a) * fx[None, :]
    bot = c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]

def make_grass(size=512, seed=7):
    n1 = value_noise(size, 4, seed)
    n2 = value_noise(size, 12, seed + 1)
    n3 = value_noise(size, 32, seed + 2)
    base = 0.52 + n1 * 0.30 + n2 * 0.16 + n3 * 0.08
    base = np.clip(base, 0, 1.25)
    # 主色：偏亮的草绿；明暗处偏黄/偏深蓝绿
    dark = np.array([0.196, 0.353, 0.169], np.float32)
    mid  = np.array([0.353, 0.573, 0.239], np.float32)
    lite = np.array([0.549, 0.749, 0.325], np.float32)
    t = np.clip(base, 0, 1)[..., None]
    col = np.where(t < 0.55, dark + (mid - dark) * (t / 0.55),
                   mid + (lite - mid) * np.clip((t - 0.55) / 0.5, 0, 1))
    img = Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8), "RGB").convert("RGBA")
    d = ImageDraw.Draw(img, "RGBA")
    rng = random.Random(seed)
    # 草叶
    for _ in range(5200):
        x = rng.random() * size; y = rng.random() * size
        h = rng.uniform(3.5, 11.0); w = rng.uniform(0.7, 1.7)
        lean = rng.uniform(-1.6, 1.6)
        g0 = rng.uniform(0.30, 0.62)
        c = (int(70 + 120 * g0), int(120 + 130 * g0), int(40 + 70 * g0), rng.randint(120, 215))
        for ox in (-size, 0, size):
            for oy in (-size, 0, size):
                d.line([(x + ox, y + oy), (x + ox + lean, y + oy - h)], fill=c, width=max(1, int(w)))
    # 高光草尖
    for _ in range(900):
        x = rng.random() * size; y = rng.random() * size
        h = rng.uniform(4, 10)
        c = (rng.randint(150, 210), rng.randint(200, 240), rng.randint(90, 140), rng.randint(70, 140))
        d.line([(x, y), (x + rng.uniform(-1.2, 1.2), y - h)], fill=c, width=1)
    return img

def make_deco(size=512, seed=21):
    """透明装饰层：小花、三叶草、碎石。"""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img, "RGBA")
    rng = random.Random(seed)
    for _ in range(150):
        x = rng.random() * size; y = rng.random() * size
        kind = rng.random()
        if kind < 0.52:
            pc = rng.choice([(255, 255, 245), (255, 236, 150), (250, 200, 220), (210, 230, 255)])
            r = rng.uniform(1.6, 3.0)
            for k in range(5):
                a = k * math.tau / 5 + rng.random()
                d.ellipse([x + math.cos(a) * r - r * 0.8, y + math.sin(a) * r - r * 0.8,
                           x + math.cos(a) * r + r * 0.8, y + math.sin(a) * r + r * 0.8], fill=pc + (235,))
            d.ellipse([x - r * 0.6, y - r * 0.6, x + r * 0.6, y + r * 0.6], fill=(250, 210, 90, 240))
        elif kind < 0.8:
            gc = (rng.randint(70, 120), rng.randint(140, 190), rng.randint(60, 100), 230)
            for k in range(3):
                a = k * math.tau / 3
                rr = rng.uniform(2.2, 3.4)
                d.ellipse([x + math.cos(a) * 2.4 - rr, y + math.sin(a) * 2.4 - rr,
                           x + math.cos(a) * 2.4 + rr, y + math.sin(a) * 2.4 + rr], fill=gc)
        else:
            sc = rng.randint(120, 175)
            r = rng.uniform(1.2, 2.6)
            d.ellipse([x - r, y - r * 0.8, x + r, y + r * 0.8], fill=(sc, sc - 8, sc - 20, 225))
    return img



def make_arena(w=2100, h=630, seed=31):
    """整张战场图：草皮 + 草叶 + 花石 + 中央踩踏小径 + 边缘压暗。无接缝。"""
    n1 = value_noise(1, 1, seed)  # 占位
    def noise2(w, h, cells, sd):
        rng = np.random.default_rng(sd)
        gx, gy = cells, max(2, int(cells * h / w))
        g = rng.random((gy + 1, gx + 1)).astype(np.float32)
        g[-1, :] = g[0, :]; g[:, -1] = g[:, 0]
        xs = np.linspace(0, gx, w, endpoint=False); x0 = xs.astype(int); x1 = (x0 + 1) % gx
        fx = (xs - x0).astype(np.float32); fx = fx * fx * (3 - 2 * fx)
        ys = np.linspace(0, gy, h, endpoint=False); y0 = ys.astype(int); y1 = (y0 + 1) % gy
        fy = (ys - y0).astype(np.float32); fy = fy * fy * (3 - 2 * fy)
        a = g[np.ix_(y0, x0)]; b = g[np.ix_(y0, x1)]; c = g[np.ix_(y1, x0)]; d = g[np.ix_(y1, x1)]
        top = a + (b - a) * fx[None, :]; bot = c + (d - c) * fx[None, :]
        return top + (bot - top) * fy[:, None]
    base = 0.50 + noise2(w, h, 5, seed)*0.26 + noise2(w, h, 15, seed+1)*0.16 + noise2(w, h, 40, seed+2)*0.09
    dark = np.array([0.196, 0.345, 0.161], np.float32)
    mid  = np.array([0.345, 0.565, 0.231], np.float32)
    lite = np.array([0.561, 0.760, 0.333], np.float32)
    t = np.clip(base, 0, 1)[..., None]
    col = np.where(t < 0.55, dark + (mid-dark)*(t/0.55), mid + (lite-mid)*np.clip((t-0.55)/0.5, 0, 1))
    # 中央小路（横向淡化的踩踏带）
    yy = np.arange(h, dtype=np.float32)[:, None]
    cy = h * 0.5
    path = (np.exp(-((yy - cy) / (h * 0.20)) ** 2) * 0.30)[..., None]
    col = col * (1 - path) + np.array([0.475, 0.435, 0.318], np.float32) * path
    # 边缘压暗
    xx = np.arange(w, dtype=np.float32)[None, :]
    ex = np.clip(1 - np.minimum(xx, w-1-xx) / (w*0.06), 0, 1) * 0.22
    ey = np.clip(1 - np.minimum(yy, h-1-yy) / (h*0.10), 0, 1) * 0.22
    edge = ex + ey
    col = col * (1 - np.clip(edge, 0, 1)[..., None] * 0.55)
    img = Image.fromarray((np.clip(col, 0, 1)*255).astype(np.uint8), "RGB").convert("RGBA")
    d = ImageDraw.Draw(img, "RGBA")
    rng = random.Random(seed)
    for _ in range(int(w*h/95)):
        x = rng.random()*w; y = rng.random()*h
        dh = 3.0 + 9.0*(1 - abs(y-cy)/(h*0.75))
        hh = max(2.5, rng.uniform(3.5, 10.5) * (0.65 + 0.5*(y/h)))
        g0 = rng.uniform(0.28, 0.60)
        c = (int(68+120*g0), int(118+130*g0), int(38+70*g0), rng.randint(115, 205))
        d.line([(x, y), (x + rng.uniform(-1.6, 1.6), y - hh)], fill=c, width=1 if rng.random() < 0.7 else 2)
    for _ in range(int(w*h/620)):
        x = rng.random()*w; y = rng.random()*h
        d.line([(x, y), (x + rng.uniform(-1.1, 1.1), y - rng.uniform(4, 9))],
               fill=(rng.randint(150, 205), rng.randint(200, 240), rng.randint(95, 140), rng.randint(60, 130)), width=1)
    deco = make_deco(512, 55).resize((w, h), Image.NEAREST)
    img.alpha_composite(Image.blend(Image.new("RGBA", (w, h), (0,0,0,0)), deco, 0.85))
    return img

def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = os.path.join(root, "art", "field"); os.makedirs(out, exist_ok=True)
    g = make_grass(512, 7); g.save(os.path.join(out, "grass.png"))
    deco = make_deco(512, 21); deco.save(os.path.join(out, "deco.png"))
    t = make_grass(512, 99); t.save(os.path.join(out, "grass2.png"))
    make_arena(2100, 630, 31).save(os.path.join(out, "arena.png"))
    # 预览
    pv = Image.new("RGBA", (1024, 512))
    pv.alpha_composite(g, (0, 0)); pv.alpha_composite(g, (512, 0))
    pv.alpha_composite(t, (0, 256)); pv.alpha_composite(t, (512, 256))
    pv.alpha_composite(deco, (0, 0)); pv.alpha_composite(deco, (512, 256))
    pv.save(os.path.join(root, "build", "shots", "field_preview.png"))
    make_arena(1400, 420, 31).save(os.path.join(root, "build", "shots", "arena_preview.png"))
    print("field textures ->", out)

if __name__ == "__main__":
    main()
