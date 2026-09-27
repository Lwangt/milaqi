using System;
using System.Collections.Generic;

namespace Milaqi.Core
{
    public enum MatchPhase { Prep, Battle, Settle, GameOver }

    /// <summary>玩家拥有的一支部队：买了就永久属于你，每回合满血重新上阵。</summary>
    public sealed class OwnedUnit
    {
        public string id;
        public float x, y;
        public bool placed;
    }

    public sealed class ShopOffer
    {
        public string unitId;
        public bool sold;
        public int price;
    }

    public sealed class PlayerState
    {
        public int index;
        public string name = "玩家";
        public bool isAI;
        public float gold;
        public float gems;
        public int level = 3;
        public float xp;
        public float baseHp;
        public int winStreak, lossStreak;
        public bool ready;
        public bool relicPicked;

        public readonly List<string> relicIds = new List<string>();
        public readonly List<ShopOffer> shop = new List<ShopOffer>();
        public readonly List<string> skills = new List<string>();
        public readonly Dictionary<string, int> skillLevels = new Dictionary<string, int>();
        public readonly List<OwnedUnit> roster = new List<OwnedUnit>();

        /// <summary>尚未落位的部队（兼容旧接口，只读使用）</summary>
        readonly List<string> _pendingCache = new List<string>();
        public List<string> pendingDeploy
        {
            get
            {
                _pendingCache.Clear();
                for (int i = 0; i < roster.Count; i++) if (!roster[i].placed) _pendingCache.Add(roster[i].id);
                return _pendingCache;
            }
        }
        public int OwnedPop(GameDatabase db)
        {
            int n = 0;
            for (int i = 0; i < roster.Count; i++) { var d = db.Unit(roster[i].id); if (d != null) n += d.pop; }
            return n;
        }
        public readonly List<string> pendingRelics = new List<string>();
        public readonly Dictionary<string, int> pendingSkillUpgrades = new Dictionary<string, int>();
        public readonly List<string> gemShop = new List<string>();
        public string archetypeId;   // 该玩家使用的流派（决定流派印记）
        public readonly Dictionary<string, int> skillCharges = new Dictionary<string, int>();
        public readonly List<string> activeRelicOffers = new List<string>();
        public int lastIncomeGold, lastIncomeGems;

        public Team Team { get { return index == 0 ? Team.Left : Team.Right; } }
        public List<RelicDef> RelicDefs(GameDatabase db)
        {
            var list = new List<RelicDef>(relicIds.Count);
            for (int i = 0; i < relicIds.Count; i++) { var r = db.Relic(relicIds[i]); if (r != null) list.Add(r); }
            return list;
        }
        public int SkillLevel(string id)
        {
            int lv;
            return skillLevels.TryGetValue(id, out lv) ? lv : 0;
        }
    }

    public sealed class LogEntry
    {
        public int round;
        public string text;
        public LogEntry(int r, string t) { round = r; text = t; }
    }

    /// <summary>整局比赛状态机：准备 -> 战斗 -> 结算 -> 下一回合</summary>
    public sealed class Match
    {
        public readonly GameDatabase db;
        public readonly BattleSim sim;
        public readonly PlayerState[] players = new PlayerState[2];
        public readonly List<LogEntry> log = new List<LogEntry>();
        public MatchPhase phase = MatchPhase.Prep;
        public int round;
        public float phaseTimer;
        public float battleTimer;
        public int winnerIndex = -1;
        public int rngSeed = 12345;
        int _rngState;

        public event Action<string> OnLog;

        public Match(GameDatabase db, int seed = 12345)
        {
            this.db = db;
            rngSeed = seed;
            _rngState = seed == 0 ? 1 : seed;
            sim = new BattleSim(db);
            for (int i = 0; i < 2; i++)
            {
                players[i] = new PlayerState { index = i, name = i == 0 ? "左侧王国" : "右侧王国", baseHp = db.Balance.baseHp };
            }
        }

        public int Rand(int maxExclusive)
        {
            unchecked
            {
                _rngState ^= _rngState << 13;
                _rngState ^= _rngState >> 17;
                _rngState ^= _rngState << 5;
            }
            int v = _rngState & 0x7FFFFFFF;
            return maxExclusive <= 0 ? 0 : v % maxExclusive;
        }

        public float Randf() { return Rand(100000) / 100000f; }

        void Say(string s)
        {
            log.Add(new LogEntry(round, s));
            if (OnLog != null) OnLog(s);
        }

        public int PopCap(PlayerState p) { return p.level + 1; }

        public int PopUsed(PlayerState p) { return p.OwnedPop(db); }

        public int UnitPrice(PlayerState p, UnitDef d)
        {
            float delta;
            StatResolver.Resolve(db, EffectiveRelics(p), BuildBoard(p), d, out delta);
            return Math.Max(1, (int)Math.Round(d.price + delta));
        }

        /// <summary>玩家的有效遗物 = 已获得遗物 + 流派印记（固有加成）。</summary>
        public List<RelicDef> EffectiveRelics(PlayerState p)
        {
            var list = p.RelicDefs(db);
            var a = db.Archetype(p.archetypeId);
            if (a != null && a.style != null && a.style.factionBonus != null && a.style.factionBonus.Length > 0)
                list.Add(new RelicDef { id = "__faction", name = "流派印记", category = "faction", rarity = "rare", effects = a.style.factionBonus });
            return list;
        }

        public StatResolver.BoardContext BuildBoard(PlayerState p)
        {
            var defs = new List<UnitDef>();
            for (int i = 0; i < p.roster.Count; i++)
            {
                var d = db.Unit(p.roster[i].id);
                if (d != null) defs.Add(d);
            }
            for (int i = 0; i < p.roster.Count; i++)
            {
                var d = db.Unit(p.roster[i].id);
                if (d != null) defs.Add(d);
            }
            return StatResolver.BuildBoard(defs);
        }

        public void RecalcStats(PlayerState p)
        {
            var relics = EffectiveRelics(p);
            var board = BuildBoard(p);
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var u = sim.Units[i];
                if (!u.alive || u.team != p.Team) continue;
                float delta;
                var st = StatResolver.Resolve(db, relics, board, u.def, out delta);
                float ratio = u.maxHp > 0.01f ? u.hp / u.maxHp : 1f;
                u.st = st;
                u.maxHp = st.hp;
                u.hp = Math.Max(1f, st.hp * ratio);
            }
        }

        // ---------------------------------------------------------------- 开局
        public void Start()
        {
            round = 0;
            var rcfg = db.Balance.round;
            for (int i = 0; i < 2; i++)
            {
                var p = players[i];
                p.gold = rcfg.startGold;
                p.level = rcfg.startLevel;
                p.xp = 0;
                p.baseHp = db.Balance.baseHp;
                p.gems = 0;
                p.skills.Clear();
                p.skills.Add("meteor");
                p.skills.Add("heal");
                p.skillLevels["meteor"] = 1;
                p.skillLevels["heal"] = 1;
            }
            // 初始兵种
            foreach (var kv in db.Balance.startingUnits)
            {
                var def = db.Unit(kv.Key);
                if (def == null) continue;
                for (int i = 0; i < 2; i++)
                    for (int n = 0; n < kv.Value; n++) players[i].roster.Add(new OwnedUnit { id = kv.Key });
            }
            NextRound(true);
        }

        void NextRound(bool first)
        {
            round++;
            var rcfg = db.Balance.round;
            // 每回合交替结算顺序：双方共用一条随机数流，固定让 0 号先抽会积累出先手优势
            int firstIdx = (round & 1) == 0 ? 0 : 1;
            for (int pi = 0; pi < 2; pi++)
            {
                var p = players[(firstIdx + pi) & 1];
                ApplyPendingPurchases(p, first);
                float goldGain = rcfg.baseIncome;
                float gemGain = 0f;
                float xpGain = 0f;
                var relics = EffectiveRelics(p);
                int capAdd = 0; float addPct = 0f;
                for (int i = 0; i < relics.Count; i++)
                {
                    var rel = relics[i];
                    if (rel.effects == null) continue;
                    for (int j = 0; j < rel.effects.Length; j++)
                    {
                        var e = rel.effects[j];
                        if (e == null) continue;
                        bool active = e.fromRound <= 0 || round >= e.fromRound;
                        switch (e.type)
                        {
                            case "income":
                                if (!active) break;
                                if (e.everyRounds > 0 && round % e.everyRounds != 0) break;
                                goldGain += e.gold; gemGain += e.gems; xpGain += e.xp;
                                break;
                            case "interest": addPct += e.addPct; capAdd += e.capAdd; break;
                            case "compoundInterest":
                                goldGain += Math.Min(e.cap, p.gold * e.pct);
                                break;
                            case "instantGems": gemGain += e.gems > 0 ? e.gems : e.value; break;
                        }
                    }
                }
                if (!first)
                {
                    float rate = 1f / Math.Max(1, rcfg.interestPer) + addPct;
                    int interest = (int)Math.Floor(p.gold * rate);
                    int cap = rcfg.interestCap + capAdd;
                    if (interest > cap) interest = cap;
                    goldGain += interest;
                    int streak = Math.Max(p.winStreak, p.lossStreak);
                    int bonus;
                    if (rcfg.streakBonus.TryGetValue(streak.ToString(), out bonus)) goldGain += bonus;
                }
                p.gold += goldGain;
                p.gems += gemGain;
                p.lastIncomeGold = (int)goldGain;
                p.lastIncomeGems = (int)gemGain;
                if (xpGain > 0f) AddXp(p, xpGain);
                // 技能解锁
                if (round >= 4 && p.skills.Count <= 2) { p.skills.Add("frost"); p.skillLevels["frost"] = 1; }
                if (round >= 8 && p.skills.Count <= 3) { p.skills.Add("arcane_blast"); p.skillLevels["arcane_blast"] = 1; }
                if (round >= 12 && p.skills.Count <= 4) { p.skills.Add("thorn"); p.skillLevels["thorn"] = 1; }
                RollShop(p);
                BuildGemShop(p);
                p.ready = false;
                p.relicPicked = false;
                p.skillCharges.Clear();
                p.activeRelicOffers.Clear();
                if (round % rcfg.relicEveryRounds == 0 || first)
                {
                    GrantRelicOffer(p, first);
                }
            }
            // 准备阶段把双方的部队按阵容摆回战场（满血），方便玩家调整站位
            for (int i = 0; i < 2; i++) RebuildTeam(players[i]);
            sim.ClearEvents();

            phase = MatchPhase.Prep;
            phaseTimer = db.Balance.prepSeconds;
            Say("第 " + round + " 回合开始，双方获得经济。");
        }

        void ApplyPendingPurchases(PlayerState p, bool first)
        {
            for (int i = 0; i < p.pendingRelics.Count; i++)
            {
                var id = p.pendingRelics[i];
                if (!p.relicIds.Contains(id)) { p.relicIds.Add(id); var rd = db.Relic(id); if (rd != null) Say(p.name + " 的宝石商店生效：" + rd.name); }
            }
            p.pendingRelics.Clear();
            foreach (var kv in p.pendingSkillUpgrades)
            {
                if (!p.skills.Contains(kv.Key)) p.skills.Add(kv.Key);
                int cur = p.SkillLevel(kv.Key);
                p.skillLevels[kv.Key] = Math.Min(3, Math.Max(cur, kv.Value));
            }
            p.pendingSkillUpgrades.Clear();
        }

        void GrantRelicOffer(PlayerState p, bool first)
        {
            p.activeRelicOffers.Clear();
            int want = db.Balance.round.relicChoices;
            var pool = new List<RelicDef>();
            for (int i = 0; i < db.Relics.Length; i++)
            {
                var r = db.Relics[i];
                if (r == null) continue;
                if (p.relicIds.Contains(r.id)) continue;
                if (p.activeRelicOffers.Contains(r.id)) continue;
                pool.Add(r);
            }
            for (int i = 0; i < want && pool.Count > 0; i++)
            {
                int idx = Rand(pool.Count);
                p.activeRelicOffers.Add(pool[idx].id);
                pool.RemoveAt(idx);
            }
        }

        public bool ChooseRelic(PlayerState p, string relicId)
        {
            if (p.relicPicked || !p.activeRelicOffers.Contains(relicId)) return false;
            var rd = db.Relic(relicId);
            if (rd == null) return false;
            p.relicIds.Add(relicId);
            p.relicPicked = true;
            p.activeRelicOffers.Clear();
            Say(p.name + " 选择了遗物：" + rd.name);
            RecalcStats(p);
            return true;
        }

        public void SkipRelic(PlayerState p)
        {
            if (p.relicPicked) return;
            p.relicPicked = true;
            p.activeRelicOffers.Clear();
            p.gems += 3;
            Say(p.name + " 放弃了遗物，换取 3 颗宝石。");
        }

        // ---------------------------------------------------------------- 商店
        /// <summary>
        /// 兵种商店 = 当前回合已解锁的全部兵种（不再随机抽卡，也不需要刷新）。
        /// 玩家的选择空间只受金币与人口限制。
        /// </summary>
        public void RollShop(PlayerState p)
        {
            p.shop.Clear();
            var list = new List<UnitDef>();
            for (int i = 0; i < db.Units.Length; i++)
            {
                var u = db.Units[i];
                if (u == null || u.tier < 1 || u.tier > 5) continue;
                if (u.unlockRound > round) continue;
                list.Add(u);
            }
            list.Sort((a, b) => a.tier != b.tier ? a.tier.CompareTo(b.tier) : (a.price != b.price ? a.price.CompareTo(b.price) : string.CompareOrdinal(a.id, b.id)));
            for (int i = 0; i < list.Count; i++) p.shop.Add(new ShopOffer { unitId = list[i].id, price = UnitPrice(p, list[i]) });
        }

        void BuildGemShop(PlayerState p)
        {
            p.gemShop.Clear();
            var pool = new List<RelicDef>();
            for (int i = 0; i < db.Relics.Length; i++)
            {
                var r = db.Relics[i];
                if (r == null || p.relicIds.Contains(r.id) || p.pendingRelics.Contains(r.id)) continue;
                pool.Add(r);
            }
            for (int i = 0; i < 3 && pool.Count > 0; i++)
            {
                int idx = Rand(pool.Count);
                p.gemShop.Add(pool[idx].id);
                pool.RemoveAt(idx);
            }
        }

        public bool BuyUnit(PlayerState p, int slotIndex)
        {
            if (phase != MatchPhase.Prep || slotIndex < 0 || slotIndex >= p.shop.Count) return false;
            var offer = p.shop[slotIndex];
            var def = db.Unit(offer.unitId);
            if (def == null) return false;
            int price = UnitPrice(p, def);
            if (p.gold < price) return false;
            if (PopUsed(p) + def.pop > PopCap(p)) return false;
            p.gold -= price;
            p.roster.Add(new OwnedUnit { id = def.id });
            return true;
        }

        public bool BuyXp(PlayerState p)
        {
            if (phase != MatchPhase.Prep) return false;
            var rcfg = db.Balance.round;
            if (p.level >= rcfg.maxLevel) return false;
            if (p.gold < rcfg.xpBuyCost) return false;
            p.gold -= rcfg.xpBuyCost;
            AddXp(p, rcfg.xpPerBuy);
            RollShop(p);
            return true;
        }

        public void AddXp(PlayerState p, float amount)
        {
            var rcfg = db.Balance.round;
            p.xp += amount;
            while (p.level < rcfg.maxLevel && p.xp >= rcfg.XpToNext(p.level))
            {
                p.xp -= rcfg.XpToNext(p.level);
                p.level++;
                Say(p.name + " 升到 " + p.level + " 级，人口上限 " + PopCap(p));
                if (p.level == 4 || p.level == 8 || p.level == 12) RollShopAfterLevel(p);
            }
            if (p.level >= rcfg.maxLevel) p.xp = 0;
        }

        void RollShopAfterLevel(PlayerState p) { if (p.isAI) return; }

        /// <summary>刷新功能已移除（商店直接列出全部已解锁兵种）。保留接口给旧存档/协议兼容。</summary>
        public bool Reroll(PlayerState p) { return false; }

        public bool BuyGemRelic(PlayerState p, string relicId)
        {
            var rd = db.Relic(relicId);
            if (rd == null) return false;
            int cost = db.GemCost(rd.rarity);
            if (p.gems < cost) return false;
            p.gems -= cost;
            p.gemShop.Remove(relicId);
            p.pendingRelics.Add(relicId);
            Say(p.name + " 用 " + cost + " 宝石预定了遗物 " + rd.name + "（下回合生效）");
            return true;
        }

        public bool UpgradeSkill(PlayerState p, string skillId)
        {
            int lv = p.SkillLevel(skillId);
            if (lv <= 0 || lv >= 3) return false;
            int cost = 5 + lv * 4;
            if (p.gems < cost) return false;
            p.gems -= cost;
            p.pendingSkillUpgrades[skillId] = lv + 1;
            Say(p.name + " 花费 " + cost + " 宝石强化技能（下回合生效）");
            return true;
        }

        // ---------------------------------------------------------------- 部署与战斗
        /// <summary>登记一个单位的站位（准备阶段的布阵）。</summary>
        public bool Place(PlayerState p, string unitId, float x, float y)
        {
            for (int i = 0; i < p.roster.Count; i++)
            {
                var o = p.roster[i];
                if (o.placed || o.id != unitId) continue;
                o.placed = true; o.x = x; o.y = y;
                return true;
            }
            return false;
        }

        /// <summary>出售一个部队（返还 60% 金币），用于腾出人口。</summary>
        public bool SellAt(PlayerState p, float x, float y, float radius)
        {
            int best = -1;
            float bestD2 = radius * radius;
            for (int i = 0; i < p.roster.Count; i++)
            {
                var o = p.roster[i];
                if (!o.placed) continue;
                float dx = o.x - x, dy = o.y - y;
                float d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = i; }
            }
            if (best < 0) return false;
            var def = db.Unit(p.roster[best].id);
            if (def != null) p.gold += Math.Max(1, (int)Math.Floor(UnitPrice(p, def) * db.Balance.round.sellRefundRatio));
            p.roster.RemoveAt(best);
            return true;
        }

        /// <summary>按索引出售部队（AI 换血用）。</summary>
        public bool SellUnit(PlayerState p, int rosterIndex)
        {
            if (rosterIndex < 0 || rosterIndex >= p.roster.Count) return false;
            var def = db.Unit(p.roster[rosterIndex].id);
            if (def != null) p.gold += Math.Max(1, (int)Math.Floor(UnitPrice(p, def) * db.Balance.round.sellRefundRatio));
            p.roster.RemoveAt(rosterIndex);
            return true;
        }

        /// <summary>把所有部队撤回待出战队列（清空战场布阵）。</summary>
        public void UnplaceAll(PlayerState p)
        {
            for (int i = 0; i < p.roster.Count; i++) p.roster[i].placed = false;
            sim.RemoveTeam(p.Team);
        }

        public void AutoDeployAll(PlayerState p)
        {
            int guard = 0;
            while (guard++ < 128)
            {
                OwnedUnit target = null;
                for (int i = 0; i < p.roster.Count; i++) if (!p.roster[i].placed) { target = p.roster[i]; break; }
                if (target == null) break;
                var def = db.Unit(target.id);
                if (def == null) { target.placed = true; continue; }
                float lane = (float)(Rand(1000) / 1000.0) * 2f - 1f;
                float y = sim.fieldHeight * 0.5f + lane * sim.fieldHeight * 0.32f;
                float x = p.Team == Team.Left
                    ? db.Balance.combat.spawnOffset + Rand(90)
                    : sim.fieldWidth - db.Balance.combat.spawnOffset - Rand(90);
                target.placed = true; target.x = x; target.y = y;
            }
        }

        /// <summary>每回合开始时把整支部队满血重新部署（云顶之弈模型）。</summary>
        public void RebuildTeam(PlayerState p)
        {
            sim.RemoveTeam(p.Team);
            AutoDeployAll(p);
            var relics = EffectiveRelics(p);
            var board = BuildBoard(p);
            for (int i = 0; i < p.roster.Count; i++)
            {
                var o = p.roster[i];
                var def = db.Unit(o.id);
                if (def == null) continue;
                float delta;
                var st = StatResolver.Resolve(db, relics, board, def, out delta);
                sim.Spawn(def, p.Team, o.x, o.y, st);
            }
        }

        public void BeginBattle()
        {
            for (int i = 0; i < 2; i++) RebuildTeam(players[i]);
            sim.ClearEvents();
            sim.time = 0f;
            battleTimer = 0f;
            phase = MatchPhase.Battle;
            Say("第 " + round + " 回合战斗开始！");
        }

        public void Tick(float dt)
        {
            if (phase == MatchPhase.Prep)
            {
                phaseTimer -= dt;
                if (phaseTimer <= 0f) BeginBattle();
            }
            else if (phase == MatchPhase.Battle)
            {
                battleTimer += dt;
                sim.Step(dt);
                if (sim.BattleOver) SettleRound();
            }
            else if (phase == MatchPhase.Settle)
            {
                phaseTimer -= dt;
                if (phaseTimer <= 0f) NextRound(false);
            }
        }

        void SettleRound()
        {
            float lkv, rkv;
            sim.Settle(out lkv, out rkv);
            float dmgToRight = Math.Max(0f, lkv - rkv);
            float dmgToLeft = Math.Max(0f, rkv - lkv);
            players[1].baseHp -= dmgToRight;
            players[0].baseHp -= dmgToLeft;

            if (dmgToRight > 0 || dmgToLeft > 0)
                Say("结算：左方杀戮值 " + (int)lkv + "，右方 " + (int)rkv + "。左方城邦受伤 " + (int)dmgToLeft + "，右方城邦受伤 " + (int)dmgToRight);
            else
                Say("结算：双方势均力敌，城邦未受伤害。");

            for (int i = 0; i < 2; i++)
            {
                var me = players[i];
                var foe = players[1 - i];
                float my = i == 0 ? lkv : rkv;
                float other = i == 0 ? rkv : lkv;
                if (my > other) { me.winStreak++; me.lossStreak = 0; me.gold += db.Balance.round.battleBonusGoldWin; }
                else if (my < other) { me.lossStreak++; me.winStreak = 0; }
            }

            if (players[0].baseHp <= 0f || players[1].baseHp <= 0f)
            {
                phase = MatchPhase.GameOver;
                winnerIndex = players[0].baseHp <= 0f ? (players[1].baseHp <= 0f ? -1 : 1) : 0;
                Say("游戏结束！" + (winnerIndex >= 0 ? players[winnerIndex].name + " 获胜！" : "双方同归于尽。"));
                return;
            }
            // 结算完毕：清空战场，部队不跨回合残留（下回合准备阶段再按阵容重新摆出）
            sim.ClearUnits();
            sim.ClearEvents();

            phase = MatchPhase.Settle;
            phaseTimer = 3.5f;
        }

        // ---------------------------------------------------------------- 技能
        public int SkillMaxCharges(PlayerState p, string skillId)
        {
            var sd = db.Skill(skillId);
            int baseCharges = sd != null ? Math.Max(1, 3 - sd.cooldown + 1) : 1;
            baseCharges = 1;
            var relics = EffectiveRelics(p);
            for (int i = 0; i < relics.Count; i++)
            {
                if (relics[i].effects == null) continue;
                for (int j = 0; j < relics[i].effects.Length; j++)
                {
                    var e = relics[i].effects[j];
                    if (e != null && e.type == "skillMod" && e.stat == "charges") baseCharges += (int)e.value;
                }
            }
            return baseCharges;
        }

        public float SkillModValue(PlayerState p, string stat, float fallback)
        {
            float pct = 0f, flat = 0f;
            var relics = EffectiveRelics(p);
            for (int i = 0; i < relics.Count; i++)
            {
                if (relics[i].effects == null) continue;
                for (int j = 0; j < relics[i].effects.Length; j++)
                {
                    var e = relics[i].effects[j];
                    if (e == null || e.type != "skillMod" || e.stat != stat) continue;
                    if (e.mode == "pct") pct += e.value; else flat += e.value;
                }
            }
            return (fallback + flat) * (1f + pct);
        }

        public bool CastSkill(PlayerState p, string skillId, float x, float y)
        {
            if (phase != MatchPhase.Battle) return false;
            var sd = db.Skill(skillId);
            if (sd == null) return false;
            var chargesUsed = p.skillCharges;
            int used;
            chargesUsed.TryGetValue(skillId, out used);
            if (used >= SkillMaxCharges(p, skillId)) return false;
            int lv = Math.Max(1, p.SkillLevel(skillId));
            float radius = SkillModValue(p, "radius", sd.LvF("radius", lv, 100f));
            float dmgMul = SkillModValue(p, "damage", 1f);
            float dmg = sd.LvF("value", lv, 0f) * dmgMul;
            switch (skillId)
            {
                case "meteor": sim.AreaDamage(x, y, radius, p.Team, dmg, true, "meteor"); break;
                case "heal": sim.AreaHeal(x, y, radius, p.Team, dmg, "heal"); break;
                case "frost": sim.AreaSlow(x, y, radius, p.Team, sd.LvF("slow", lv, 0.5f), sd.LvF("slowTime", lv, 4f), dmg, "frost"); break;
                case "horn": sim.AreaBuff(x, y, radius, p.Team, sd.LvF("atk", lv, 0.35f), sd.LvF("atkSpeed", lv, 0.3f), sd.LvF("duration", lv, 8f), 0f, 0f, "horn"); break;
                case "summon": sim.SummonAt(x, y, (int)sd.LvF("count", lv, 3f), p.Team, "reinforcement", "summon"); break;
                case "arcane_blast":
                    {
                        float dir = p.Team == Team.Left ? 1f : -1f;
                        sim.LineDamage(x, y, dir, 520f, radius, p.Team, dmg, "arcane_blast");
                        break;
                    }
                case "thorn": sim.AreaBuff(x, y, radius, p.Team, 0f, 0f, sd.LvF("duration", lv, 8f), sd.LvF("shield", lv, 130f) * dmgMul, sd.LvF("thorn", lv, 0.35f), "thorn"); break;
                case "death_gaze": sim.DeathGaze(x, y, radius, p.Team, dmg, sd.LvF("threshold", lv, 0.4f), "death_gaze"); break;
                default: return false;
            }
            chargesUsed[skillId] = used + 1;
            Say(p.name + " 释放了 " + sd.name);
            return true;
        }

        public void SetReady(PlayerState p, bool value)
        {
            p.ready = value;
            if (players[0].ready && players[1].ready && phase == MatchPhase.Prep) BeginBattle();
        }

        public void ForceStartBattle() { if (phase == MatchPhase.Prep) BeginBattle(); }
    }
}
