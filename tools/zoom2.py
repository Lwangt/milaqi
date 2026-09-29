# -*- coding: utf-8 -*-
import glob, os
from PIL import Image
files = sorted(glob.glob(r"E:\code\milaqi\build\shots\shot_*.png"), key=os.path.getmtime)
im = Image.open(files[-1]).convert("RGBA")
im.crop((0, 620, 1120, 800)).resize((2240, 360), Image.LANCZOS).save(r"E:\code\milaqi\build\shots\bottom_zoom.png")
print("ok")
