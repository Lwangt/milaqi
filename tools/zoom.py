# -*- coding: utf-8 -*-
import glob, os
from PIL import Image
files = sorted(glob.glob(r"E:\code\milaqi\build\shots\shot_*.png"), key=os.path.getmtime)
p = files[-1]
im = Image.open(p).convert("RGBA")
im.crop((0, 0, 1600, 120)).resize((2400, 180), Image.LANCZOS).save(r"E:\code\milaqi\build\shots\topbar_zoom.png")
print("zoomed", os.path.basename(p))
