using System;
using System.Collections.Generic;

namespace Milaqi.Core
{
    public enum MatchPhase { Prep, Battle, Settle, GameOver }

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
        public readonly List<string> pendingDeploy = new List<string>();
        public readonly List<string> pendingRelics = new List<string>();
        public readonly Dictionary<string, int> pendingSkillUpgrades = new Dictionary<string, int>();
        public readonly List<string> gemShop = new List<string>();
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

        public int PopUsed(PlayerState p)
        {
            int used = 0;
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var u = sim.Units[i];
                if (u.alive && u.team == p.Team) used += u.def.pop;
            }
            for (int i = 0; i < p.pendingDeploy.Count; i++)
            {
                var d = db.Unit(p.pendingDeploy[i]);
                if (d != null) used += d.pop;
            }
            return used;
        }

        public int UnitPrice(PlayerState p, UnitDef d)
        {
            float delta;
            StatResolver.Resolve(db, p.RelicDefs(db), BuildBoard(p), d, out delta);
            return Math.Max(1, (int)Math.Round(d.price + delta));
        }

        public StatResolver.BoardContext BuildBoard(PlayerState p)
        {
            var defs = new List<UnitDef>();
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var u = sim.Units[i];
                if (u.alive && u.team == p.Team) defs.Add(u.def);
            }
            for (int i = 0; i < p.pendingDeploy.Count; i++)
            {
                var d = db.Unit(p.pendingDeploy[i]);
                if (d != null) defs.Add(d);
            }
            return StatResolver.BuildBoard(defs);
        }

        public void RecalcStats(PlayerState p)
        {
            var relics = p.RelicDefs(db);
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
                    for (int n = 0; n < kv.Value; n++) players[i].pendingDeploy.Add(kv.Key);
            }
            for (int i = 0; i < 2; i++) AutoDeployAll(players[i]);
            NextRound(true);
        }

        void NextRound(bool first)
        {
            round++;
            var rcfg = db.Balance.round;
            foreach (var p in players)
            {
                ApplyPendingPurchases(p, first);
                float goldGain = rcfg.baseIncome;
                float gemGain = 0f;
                float xpGain = 0f;
                var relics = p.RelicDefs(db);
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
                // 受伤单位每回合恢复 35% 生命
                for (int i = 0; i < sim.Units.Count; i++)
                {
                    var u = sim.Units[i];
                    if (u.alive && u.team == p.Team) u.hp = Math.Min(u.maxHp, u.hp + u.maxHp * 0.35f);
                }
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
        public void RollShop(PlayerState p)
        {
            p.shop.Clear();
            var odds = db.Balance.round.OddsFor(p.level);
            var poolByTier = new List<UnitDef>[5];
            for (int t = 0; t < 5; t++) poolByTier[t] = new List<UnitDef>();
            for (int i = 0; i < db.Units.Length; i++)
            {
                var u = db.Units[i];
                if (u == null || u.unlockRound > round || u.tier < 1 || u.tier > 5) continue;
                poolByTier[u.tier - 1].Add(u);
            }
            for (int s = 0; s < db.Balance.round.shopSize; s++)
            {
                int roll = Rand(100), acc = 0, tier = 0;
                for (int t = 0; t < 5; t++)
                {
                    acc += odds[t];
                    if (roll < acc) { tier = t; break; }
                }
                var list = poolByTier[tier];
                if (list.Count == 0)
                {
                    for (int t = tier - 1; t >= 0 && list.Count == 0; t--) list = poolByTier[t];
                }
                if (list.Count == 0) continue;
                var pick = list[Rand(list.Count)];
                p.shop.Add(new ShopOffer { unitId = pick.id, price = UnitPrice(p, pick) });
            }
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
            if (offer.sold) return false;
            var def = db.Unit(offer.unitId);
            if (def == null) return false;
            int price = UnitPrice(p, def);
            if (p.gold < price) return false;
            if (PopUsed(p) + def.pop > PopCap(p)) return false;
            p.gold -= price;
            offer.sold = true;
            p.pendingDeploy.Add(def.id);
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

        public bool Reroll(PlayerState p)
        {
            if (phase != MatchPhase.Prep) return false;
            int cost = db.Balance.round.rerollCost;
            if (p.gold < cost) return false;
            p.gold -= cost;
            RollShop(p);
            return true;
        }

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
        public SimUnit Deploy(PlayerState p, string unitId, float x, float y)
        {
            var def = db.Unit(unitId);
            if (def == null) return null;
            int idx = p.pendingDeploy.IndexOf(unitId);
            if (idx < 0) return null;
            p.pendingDeploy.RemoveAt(idx);
            float delta;
            var st = StatResolver.Resolve(db, p.RelicDefs(db), BuildBoard(p), def, out delta);
            return sim.Spawn(def, p.Team, x, y, st);
        }

        public void AutoDeployAll(PlayerState p)
        {
            while (p.pendingDeploy.Count > 0)
            {
                var id = p.pendingDeploy[0];
                var def = db.Unit(id);
                if (def == null) { p.pendingDeploy.RemoveAt(0); continue; }
                float lane = (float)(Rand(1000) / 1000.0) * 2f - 1f;
                float y = sim.fieldHeight * 0.5f + lane * sim.fieldHeight * 0.32f;
                float x = p.Team == Team.Left
                    ? db.Balance.combat.spawnOffset + Rand(90)
                    : sim.fieldWidth - db.Balance.combat.spawnOffset - Rand(90);
                Deploy(p, id, x, y);
            }
        }

        public void BeginBattle()
        {
            for (int i = 0; i < 2; i++)
            {
                AutoDeployAll(players[i]);
                RecalcStats(players[i]);
            }
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
            phase = MatchPhase.Settle;
            phaseTimer = 3.5f;
        }

        // ---------------------------------------------------------------- 技能
        public int SkillMaxCharges(PlayerState p, string skillId)
        {
            var sd = db.Skill(skillId);
            int baseCharges = sd != null ? Math.Max(1, 3 - sd.cooldown + 1) : 1;
            baseCharges = 1;
            var relics = p.RelicDefs(db);
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
            var relics = p.RelicDefs(db);
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
