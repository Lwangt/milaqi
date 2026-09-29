# -*- coding: utf-8 -*-
import json, os
from PIL import Image
root = r"E:\code\milaqi"
d = os.path.join(root, "art", "units2")
mf = json.load(open(os.path.join(d, "manifest.json"), encoding="utf-8"))
FW, FH = mf["frameW"], mf["frameH"]
ids = ["militia","archer","knight","archmage","dragon","titan","skeleton","wolf_rider"]
SC = 2
rows = []
maxc = 0
sheet_rows = []
for uid in ids:
    im = Image.open(os.path.join(d, uid + ".png")).convert("RGBA")
    n = im.width // FW
    maxc = max(maxc, n)
    sheet_rows.append((uid, im, n))
sheet = Image.new("RGBA", (maxc*FW*SC, len(ids)*FH*SC), (26, 32, 28, 255))
from PIL import ImageDraw
dr = ImageDraw.Draw(sheet)
for r, (uid, im, n) in enumerate(sheet_rows):
    for i in range(n):
        f = im.crop((i*FW, 0, (i+1)*FW, FH)).resize((FW*SC, FH*SC), Image.LANCZOS)
        sheet.alpha_composite(f, (i*FW*SC, r*FH*SC))
    dr.text((4, r*FH*SC+4), uid, fill=(255, 255, 120, 255))
sheet.save(os.path.join(root, "build", "shots", "sheet_anim.png"))
print("anim sheet", sheet.size)
