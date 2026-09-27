using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Milaqi.Core
{
    public sealed class UnitDef
    {
        public string id;
        public string name;
        public int tier;
        public int price;
        public int pop;
        public int unlockRound;
        public string[] tags = Array.Empty<string>();
        public float hp;
        public float atk;
        public float matk;
        public float pdef;
        public float mdef;
        public float speed;
        public float atkSpeed;
        public float range;
        public float killValue;
        public string desc;
        public Dictionary<string, JsonElement> extra;

        public bool HasTag(string t)
        {
            if (tags == null) return false;
            for (int i = 0; i < tags.Length; i++) if (tags[i] == t) return true;
            return false;
        }
        public float ExF(string k, float dv = 0f)
        {
            JsonElement e;
            if (extra != null && extra.TryGetValue(k, out e) && e.ValueKind == JsonValueKind.Number) return (float)e.GetDouble();
            return dv;
        }
        public bool ExB(string k)
        {
            JsonElement e;
            if (extra != null && extra.TryGetValue(k, out e)) return e.ValueKind == JsonValueKind.True;
            return false;
        }
        public string ExS(string k)
        {
            JsonElement e;
            if (extra != null && extra.TryGetValue(k, out e) && e.ValueKind == JsonValueKind.String) return e.GetString();
            return null;
        }
    }

    public sealed class EffectDef
    {
        public string type;
        public string target;
        public string stat;
        public string mode;
        public float value;
        public int max;
        public string tag;
        public int perCount;
        public int count;
        public float chance;
        public string unit;
        public int everyRounds;
        public int fromRound;
        public float pct;
        public float cap;
        public int capAdd;
        public float addPct;
        public int gold;
        public int gems;
        public int xp;
    }

    public sealed class RelicDef
    {
        public string id;
        public string name;
        public string category;
        public string rarity;
        public string desc;
        public string flavor;
        public EffectDef[] effects = Array.Empty<EffectDef>();
    }

    public sealed class SkillDef
    {
        public string id;
        public string name;
        public string kind;
        public string desc;
        public int cooldown;
        public Dictionary<string, JsonElement> levels;
        public float LvF(string key, int level, float dv = 0f)
        {
            if (levels == null) return dv;
            JsonElement e;
            if (!levels.TryGetValue(key, out e)) return dv;
            if (e.ValueKind == JsonValueKind.Number) return (float)e.GetDouble();
            if (e.ValueKind == JsonValueKind.Array)
            {
                var arr = new List<JsonElement>();
                foreach (var item in e.EnumerateArray()) arr.Add(item);
                int idx = Math.Max(0, Math.Min(level, arr.Count));
                if (idx == 0) return dv;
                float total = 0f;
                for (int i = 0; i < idx; i++) total += (float)arr[i].GetDouble();
                return total;
            }
            return dv;
        }
    }

    public sealed class RoundConfig
    {
        public int startGold = 10;
        public int startLevel = 3;
        public int baseIncome = 5;
        public int interestPer = 10;
        public int interestCap = 5;
        public Dictionary<string, int> streakBonus = new Dictionary<string, int>();
        public int[] levelXp = new int[] { 0, 2, 2, 6, 10, 20, 36, 56, 80, 100, 120, 150 };
        public int maxLevel = 12;
        public int xpBuyCost = 4;
        public int xpPerBuy = 4;
        public string popFormula = "level+1";
        public int shopSize = 5;
        public int rerollCost = 2;
        public int relicEveryRounds = 2;
        public int relicChoices = 3;
        public Dictionary<string, int[]> shopOdds = new Dictionary<string, int[]>();
        public int unitsPerShopSlot = 1;
        public float sellRefundRatio = 0.6f;
        public int battleBonusGoldWin = 1;

        public int[] OddsFor(int level)
        {
            int[] v;
            if (shopOdds != null && shopOdds.TryGetValue(level.ToString(), out v) && v != null && v.Length == 5) return v;
            return new int[] { 100, 0, 0, 0, 0 };
        }
        public int XpToNext(int level)
        {
            if (levelXp == null) return 10;
            if (level < 1) level = 1;
            if (level >= levelXp.Length) return levelXp[levelXp.Length - 1];
            return levelXp[level];
        }
    }

    public sealed class CombatConfig
    {
        public int tickRate = 30;
        public int laneCount = 1;
        public float fieldWidth = 1400f;
        public float fieldHeight = 420f;
        public float spawnOffset = 90f;
        public float defConstant = 50f;
        public float minDamageRatio = 0.20f;
        public float acquireRangeExtra = 40f;
        public float settleTimeoutSeconds = 30f;
    }

    public sealed class BalanceConfig
    {
        public float baseHp = 300f;
        public float prepSeconds = 35f;
        public float battleMaxSeconds = 30f;
        public RoundConfig round = new RoundConfig();
        public CombatConfig combat = new CombatConfig();
        public Dictionary<string, int> startingUnits = new Dictionary<string, int>();
    }

    public sealed class GameDatabase
    {
        public UnitDef[] Units = Array.Empty<UnitDef>();
        public RelicDef[] Relics = Array.Empty<RelicDef>();
        public SkillDef[] Skills = Array.Empty<SkillDef>();
        public BalanceConfig Balance = new BalanceConfig();
        public ArchetypeDef[] Archetypes = Array.Empty<ArchetypeDef>();
        public DifficultyDef[] Difficulties = Array.Empty<DifficultyDef>();
        public Dictionary<string, string> TagNames = new Dictionary<string, string>();
        public Dictionary<string, string> CategoryNames = new Dictionary<string, string>();
        public Dictionary<string, int> RarityGemCost = new Dictionary<string, int>();

        readonly Dictionary<string, UnitDef> _byId = new Dictionary<string, UnitDef>();
        readonly Dictionary<string, RelicDef> _relicById = new Dictionary<string, RelicDef>();
        readonly Dictionary<string, SkillDef> _skillById = new Dictionary<string, SkillDef>();

        readonly Dictionary<string, short> _indexById = new Dictionary<string, short>();
        readonly Dictionary<string, ArchetypeDef> _archById = new Dictionary<string, ArchetypeDef>();
        readonly Dictionary<string, DifficultyDef> _diffById = new Dictionary<string, DifficultyDef>();

        public ArchetypeDef Archetype(string id)
        {
            ArchetypeDef a;
            return id != null && _archById.TryGetValue(id, out a) ? a : null;
        }
        public DifficultyDef Difficulty(string id)
        {
            DifficultyDef d;
            return id != null && _diffById.TryGetValue(id, out d) ? d : null;
        }
        public DifficultyDef DifficultyOr(int index)
        {
            if (Difficulties.Length == 0) return new DifficultyDef();
            return Difficulties[Math.Max(0, Math.Min(Difficulties.Length - 1, index))];
        }

        public short UnitIndex(string id)
        {
            short i;
            if (id != null && _indexById.TryGetValue(id, out i)) return i;
            return -1;
        }
        public UnitDef UnitByIndex(int index)
        {
            if (index < 0 || index >= Units.Length) return null;
            return Units[index];
        }

        public UnitDef Unit(string id)
        {
            UnitDef d;
            return id != null && _byId.TryGetValue(id, out d) ? d : null;
        }
        public RelicDef Relic(string id)
        {
            RelicDef d;
            return id != null && _relicById.TryGetValue(id, out d) ? d : null;
        }
        public SkillDef Skill(string id)
        {
            SkillDef d;
            return id != null && _skillById.TryGetValue(id, out d) ? d : null;
        }
        public int TierIndex(UnitDef d) { return Math.Max(0, Math.Min(4, d.tier - 1)); }
        public int GemCost(string rarity)
        {
            int c;
            if (RarityGemCost != null && RarityGemCost.TryGetValue(rarity ?? "rare", out c)) return c;
            return 8;
        }

        static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static GameDatabase Load(Func<string, string> read)
        {
            var db = new GameDatabase();
            var unitsRoot = JsonSerializer.Deserialize<UnitsRoot>(read("data/units.json"), Opts);
            db.Units = unitsRoot?.units ?? Array.Empty<UnitDef>();
            if (unitsRoot != null && unitsRoot.tagNames != null) db.TagNames = unitsRoot.tagNames;

            var relicsRoot = JsonSerializer.Deserialize<RelicRoot>(read("data/relics.json"), Opts);
            db.Relics = relicsRoot?.relics ?? Array.Empty<RelicDef>();
            if (relicsRoot != null)
            {
                if (relicsRoot.rarityGemCost != null) db.RarityGemCost = relicsRoot.rarityGemCost;
                if (relicsRoot.categoryNames != null) db.CategoryNames = relicsRoot.categoryNames;
            }

            var skillsRoot = JsonSerializer.Deserialize<SkillRoot>(read("data/skills.json"), Opts);
            db.Skills = skillsRoot?.skills ?? Array.Empty<SkillDef>();

            db.Balance = JsonSerializer.Deserialize<BalanceConfig>(read("data/balance.json"), Opts) ?? new BalanceConfig();

            var archRoot = JsonSerializer.Deserialize<ArchetypeRoot>(read("data/archetypes.json"), Opts);
            db.Archetypes = archRoot?.archetypes ?? Array.Empty<ArchetypeDef>();
            db.Difficulties = archRoot?.difficulties ?? Array.Empty<DifficultyDef>();
            for (int i = 0; i < db.Archetypes.Length; i++)
            {
                var a = db.Archetypes[i];
                if (a == null || a.id == null) continue;
                db._archById[a.id] = a;
                if (a.tagWeight == null) a.tagWeight = new Dictionary<string, float>();
                if (a.unitWeight == null) a.unitWeight = new Dictionary<string, float>();
                if (a.style == null) a.style = new ArchetypeStyle();
            }
            for (int i = 0; i < db.Difficulties.Length; i++)
            {
                var d = db.Difficulties[i];
                if (d?.id != null) db._diffById[d.id] = d;
            }

            for (int i = 0; i < db.Units.Length; i++)
            {
                var u = db.Units[i];
                if (u == null || u.id == null) continue;
                db._byId[u.id] = u;
                db._indexById[u.id] = (short)i;
            }
            foreach (var r in db.Relics) if (r != null && r.id != null) db._relicById[r.id] = r;
            foreach (var s in db.Skills) if (s != null && s.id != null) db._skillById[s.id] = s;
            return db;
        }

        sealed class UnitsRoot { public UnitDef[] units; public Dictionary<string, string> tagNames; }
        sealed class RelicRoot { public RelicDef[] relics; public Dictionary<string, int> rarityGemCost; public Dictionary<string, string> categoryNames; }
        sealed class SkillRoot { public SkillDef[] skills; public int maxLevel; }
    }
}
