# -*- coding: utf-8 -*-
import json, os, sys
from PIL import Image
root = r"E:\code\milaqi"
d = os.path.join(root, "art", "units2")
mf = json.load(open(os.path.join(d, "manifest.json"), encoding="utf-8"))
FW, FH = mf["frameW"], mf["frameH"]
ids = ["militia","spearman","archer","skeleton","goblin","swordsman","shieldman","crossbowman",
       "knight","guardian","ranger","vampire","zealot","ice_mage","templar","sniper",
       "cannoneer","chaos_golem","necromancer","shadow_assassin","guan_yu","zhao_yun","titan","archmage",
       "dragon_knight","war_beast","lich","gargoyle","centaur","dwarf","orc","elf_ranger",
       "fire_elemental","ogre","griffin","treant","spider_queen","bone_dragon","frost_giant","reaper",
       "siren","dragon","demon_lord","phoenix","arch_treant","wolf_rider","ballista","apprentice_mage",
       "pikeman","reinforcement","zealot"]
ids = list(dict.fromkeys(ids))
SC = 2
cols = 8
rows = (len(ids)+cols-1)//cols
sheet = Image.new("RGBA", (cols*FW*SC, rows*FH*SC), (28, 34, 30, 255))
for i, uid in enumerate(ids):
    p = os.path.join(d, uid + ".png")
    if not os.path.exists(p):
        continue
    im = Image.open(p).convert("RGBA")
    f0 = im.crop((0, 0, FW, FH)).resize((FW*SC, FH*SC), Image.NEAREST)
    x = (i % cols)*FW*SC
    y = (i//cols)*FH*SC
    sheet.alpha_composite(f0, (x, y))
sheet.save(os.path.join(root, "build", "shots", "sheet_idle.png"))
print("sheet saved", sheet.size, "units:", len(ids))
