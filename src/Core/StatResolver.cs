using System;
using System.Collections.Generic;

namespace Milaqi.Core
{
    public struct ResolvedStats
    {
        public float hp, atk, matk, pdef, mdef, speed, atkSpeed, range, killValue;
        public float lifesteal, splash, cleave, cleaveRatio, antiCavalry, bonusVsBig;
        public float slow, slowTime;
        public bool targetBackline, dash;
        public string summonOnKill;
        public string deathSummonUnit;
        public float deathSummonChance;
    }

    public static class StatResolver
    {
        public sealed class BoardContext
        {
            public readonly Dictionary<string, int> TagCount = new Dictionary<string, int>();
            public int Total;
            public void Add(UnitDef d)
            {
                Total++;
                if (d?.tags == null) return;
                for (int i = 0; i < d.tags.Length; i++)
                {
                    int c;
                    TagCount.TryGetValue(d.tags[i], out c);
                    TagCount[d.tags[i]] = c + 1;
                }
            }
            public int Count(string tag)
            {
                if (tag == "any") return Total;
                int c;
                return TagCount.TryGetValue(tag, out c) ? c : 0;
            }
        }

        public static BoardContext BuildBoard(IReadOnlyList<UnitDef> defs)
        {
            var ctx = new BoardContext();
            if (defs != null) for (int i = 0; i < defs.Count; i++) ctx.Add(defs[i]);
            return ctx;
        }

        public static ResolvedStats Base(UnitDef d)
        {
            var s = new ResolvedStats
            {
                hp = d.hp, atk = d.atk, matk = d.matk, pdef = d.pdef, mdef = d.mdef,
                speed = d.speed, atkSpeed = d.atkSpeed, range = d.range, killValue = d.killValue,
                lifesteal = d.ExF("lifesteal"), splash = d.ExF("splash"), cleave = d.ExF("cleave"),
                cleaveRatio = d.ExF("cleaveRatio", 0.5f), antiCavalry = d.ExF("antiCavalry"),
                slow = d.ExF("slow"), slowTime = d.ExF("slowTime", 1.5f),
                targetBackline = d.ExB("targetBackline"), dash = d.ExB("dash"),
                summonOnKill = d.ExS("summonOnKill"),
            };
            return s;
        }

        static bool MatchesTarget(UnitDef d, string target)
        {
            if (string.IsNullOrEmpty(target) || target == "all") return true;
            if (target == "big") return d.pop >= 3;
            if (target == "pop1") return d.pop == 1;
            if (target == "notHero") return !d.HasTag("hero");
            return d.HasTag(target) || d.id == target;
        }

        // 把所有遗物效果结算成该兵种的最终属性
        public static ResolvedStats Resolve(GameDatabase db, IReadOnlyList<RelicDef> relics, BoardContext board, UnitDef d, out float costDelta)
        {
            var s = Base(d);
            costDelta = 0f;
            if (relics == null) return s;

            float hpFlat = 0, atkFlat = 0, matkFlat = 0, pdefFlat = 0, mdefFlat = 0, spdFlat = 0, asFlat = 0, rngFlat = 0, kvFlat = 0;
            float hpPct = 0, atkPct = 0, matkPct = 0, pdefPct = 0, mdefPct = 0, spdPct = 0, asPct = 0, rngPct = 0, kvPct = 0;

            for (int i = 0; i < relics.Count; i++)
            {
                var rel = relics[i];
                if (rel?.effects == null) continue;
                for (int j = 0; j < rel.effects.Length; j++)
                {
                    var e = rel.effects[j];
                    if (e == null) continue;
                    switch (e.type)
                    {
                        case "stat":
                            if (!MatchesTarget(d, e.target)) break;
                            Apply(s, e.stat, e.mode, e.value,
                                ref hpFlat, ref atkFlat, ref matkFlat, ref pdefFlat, ref mdefFlat,
                                ref spdFlat, ref asFlat, ref rngFlat, ref kvFlat,
                                ref hpPct, ref atkPct, ref matkPct, ref pdefPct, ref mdefPct,
                                ref spdPct, ref asPct, ref rngPct, ref kvPct);
                            break;
                        case "synergy":
                            {
                                int count = board != null ? board.Count(e.tag) : 0;
                                int layers = e.perCount <= 0 ? 0 : count / e.perCount;
                                if (e.max > 0 && layers > e.max) layers = e.max;
                                if (layers <= 0) break;
                                if (!MatchesTarget(d, string.IsNullOrEmpty(e.target) ? e.tag : e.target)) break;
                                Apply(s, e.stat, e.mode, e.value * layers,
                                    ref hpFlat, ref atkFlat, ref matkFlat, ref pdefFlat, ref mdefFlat,
                                    ref spdFlat, ref asFlat, ref rngFlat, ref kvFlat,
                                    ref hpPct, ref atkPct, ref matkPct, ref pdefPct, ref mdefPct,
                                    ref spdPct, ref asPct, ref rngPct, ref kvPct);
                                break;
                            }
                        case "costMod":
                            if (MatchesTarget(d, e.target)) costDelta += e.value;
                            break;
                        case "bonusVsBig":
                            if (string.IsNullOrEmpty(e.target) || d.HasTag(e.target)) s.bonusVsBig += e.value;
                            break;
                        case "onDeathSummon":
                            if (MatchesTarget(d, e.target))
                            {
                                s.deathSummonUnit = e.unit;
                                s.deathSummonChance = Math.Max(s.deathSummonChance, e.value > 0 ? e.value : e.chance);
                            }
                            break;
                    }
                }
            }

            s.hp = Math.Max(1f, (s.hp + hpFlat) * (1f + hpPct));
            s.atk = Math.Max(0f, (s.atk + atkFlat) * (1f + atkPct));
            s.matk = Math.Max(0f, (s.matk + matkFlat) * (1f + matkPct));
            s.pdef = Math.Max(0f, (s.pdef + pdefFlat) * (1f + pdefPct));
            s.mdef = Math.Max(0f, (s.mdef + mdefFlat) * (1f + mdefPct));
            s.speed = Math.Max(10f, (s.speed + spdFlat) * (1f + spdPct));
            s.atkSpeed = Math.Max(0.1f, (s.atkSpeed + asFlat) * (1f + asPct));
            s.range = Math.Max(12f, (s.range + rngFlat) * (1f + rngPct));
            s.killValue = Math.Max(0f, (s.killValue + kvFlat) * (1f + kvPct));
            return s;
        }

        static void Apply(ResolvedStats s, string stat, string mode, float value,
            ref float hpFlat, ref float atkFlat, ref float matkFlat, ref float pdefFlat, ref float mdefFlat,
            ref float spdFlat, ref float asFlat, ref float rngFlat, ref float kvFlat,
            ref float hpPct, ref float atkPct, ref float matkPct, ref float pdefPct, ref float mdefPct,
            ref float spdPct, ref float asPct, ref float rngPct, ref float kvPct)
        {
            bool pct = mode == "pct";
            if (stat == "all" || stat == "hp") { if (pct) hpPct += value; else hpFlat += value; }
            if (stat == "all" || stat == "atk") { if (pct) atkPct += value; else atkFlat += value; }
            if (stat == "all" || stat == "matk") { if (pct) matkPct += value; else matkFlat += value; }
            if (stat == "all" || stat == "pdef") { if (pct) pdefPct += value; else pdefFlat += value; }
            if (stat == "all" || stat == "mdef") { if (pct) mdefPct += value; else mdefFlat += value; }
            if (stat == "all" || stat == "speed") { if (pct) spdPct += value; else spdFlat += value; }
            if (stat == "all" || stat == "atkSpeed") { if (pct) asPct += value; else asFlat += value; }
            if (stat == "all" || stat == "range") { if (pct) rngPct += value; else rngFlat += value; }
            if (stat == "all" || stat == "killValue") { if (pct) kvPct += value; else kvFlat += value; }
        }
    }
}
