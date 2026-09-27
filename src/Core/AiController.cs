using System;
using System.Collections.Generic;

namespace Milaqi.Core
{
    /// <summary>
    /// 流派化 AI：按 ArchetypeDef 决定买什么兵、拿什么遗物、怎么运营，
    /// 按 DifficultyDef 决定反应速度、决策质量与针对性强度。
    /// 同时作为批量平衡验证的执行体。
    /// </summary>
    public sealed class AiController
    {
        readonly Match match;
        readonly GameDatabase db;
        public readonly PlayerState player;
        public ArchetypeDef archetype;
        public DifficultyDef difficulty;

        float thinkTimer;
        float skillTimer;
        int lastRound = -1;
        int xpBuysThisRound;
        int deployIndex;
        readonly Dictionary<string, int> deployCycle = new Dictionary<string, int>();

        public AiController(Match m, PlayerState p, ArchetypeDef arch = null, DifficultyDef diff = null)
        {
            match = m;
            db = m.db;
            player = p;
            archetype = arch ?? (db.Archetypes.Length > 0 ? db.Archetypes[0] : new ArchetypeDef { id = "default", name = "默认" });
            difficulty = diff ?? db.DifficultyOr(1);
            player.isAI = true;
            player.archetypeId = archetype.id;
            player.difficultyId = difficulty.id;
            if (arch != null) player.name = arch.name + "（AI）";
        }

        public void Update(float dt)
        {
            if (match.round != lastRound)
            {
                lastRound = match.round;
                thinkTimer = difficulty.thinkInterval;
                skillTimer = 1.5f;
                deployIndex = 0;
                xpBuysThisRound = 0;
            }
            if (match.phase == MatchPhase.Prep)
            {
                thinkTimer -= dt;
                if (thinkTimer <= 0f)
                {
                    thinkTimer = difficulty.thinkInterval;
                    Think();
                }
            }
            else if (match.phase == MatchPhase.Battle)
            {
                skillTimer -= dt;
                if (skillTimer <= 0f)
                {
                    skillTimer = 3.2f - difficulty.deployQuality * 1.4f;
                    if (difficulty.useSkills) BattleThink();
                }
            }
        }

        // ------------------------------------------------------------ 评分
        static int RarityScore(string rarity)
        {
            if (rarity == "epic") return 3;
            if (rarity == "rare") return 2;
            return 1;
        }

        int[] EnemyTagCounts()
        {
            var counts = new int[16];
            var units = match.sim.Units;
            Team foe = player.Team == Team.Left ? Team.Right : Team.Left;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.alive || u.team != foe) continue;
                counts[0]++;
                if (u.def.HasTag("shield")) counts[1]++;
                if (u.def.pop >= 4) counts[2]++;
                if (u.def.HasTag("ranged")) counts[3]++;
                if (u.def.HasTag("magic")) counts[4]++;
                if (u.def.HasTag("cavalry")) counts[5]++;
                if (u.def.HasTag("assassin")) counts[6]++;
                if (u.def.HasTag("giant")) counts[7]++;
            }
            return counts;
        }

        float CounterScore(UnitDef d, int[] foe)
        {
            if (difficulty.counterPick <= 0f) return 0f;
            float s = 0f;
            if (d.HasTag("magic") && (foe[1] >= 2 || foe[2] >= 2)) s += 12f;          // 破盾/破巨兽
            if (d.HasTag("assassin") && (foe[3] + foe[4]) >= 3) s += 12f;              // 切后排
            if (d.ExF("antiCavalry") > 0f && foe[5] >= 2) s += 12f;                    // 反骑
            if (d.ExF("splash") > 0f && foe[0] >= 5) s += 10f;                         // 清群
            if (d.HasTag("shield") && (foe[3] + foe[4]) >= 3) s += 8f;                 // 抗远程
            if (d.HasTag("ranged") && foe[7] >= 1) s += 6f;                            // 消耗巨兽
            return s;
        }

        public float UnitScore(UnitDef d, int[] foe)
        {
            float s = d.tier * (12f + archetype.style.tierBias);
            if (d.tags != null)
                for (int i = 0; i < d.tags.Length; i++) s += 15f * archetype.TagW(d.tags[i]);
            float uw = archetype.UnitW(d.id);
            s += 18f * uw;
            // 流派如果定义了兵源白名单，名单外的兵重罚——否则 AI 会跑去买不属于本流派的超模单位，
            // 流派身份会被「哪个标签的高费兵更强」淹没（实测会让胜率极差冲到 60%+）
            if (archetype.unitWeight != null && archetype.unitWeight.Count > 0 && uw <= 0f) s -= 55f;
            s -= d.price * (1f + archetype.style.pricePenalty) * 0.55f;
            s += d.pop * archetype.style.popWeight * 1.2f;
            s += CounterScore(d, foe) * difficulty.counterPick;
            s += CompositionScore(d);
            return s;
        }

        /// <summary>
        /// 阵容结构分 —— 这是流派平衡的关键机制。
        /// 之前 AI 只按「单体评分」贪心买兵，结果各流派都退化成「堆当前最强的那一类兵」，
        /// 流派胜率完全由「哪个标签的高费兵更强」决定，怎么调印记都会阶跃式摆动。
        /// 现在强制保证三条结构性约束：
        ///   ① 前排配额：前排人口占比低于目标时，前排兵获得大幅加分（保证站得住）
        ///   ② 后排纪律：前排不足时不再堆后排
        ///   ③ 多样性：同一兵种买太多会递减收益（防止 7 个骷髅兵刷屏）
        /// </summary>
        float CompositionScore(UnitDef d)
        {
            float totalPop = 0f, frontPop = 0f;
            int sameCount = 0;
            for (int i = 0; i < player.roster.Count; i++)
            {
                var o = player.roster[i];
                var u = db.Unit(o.id);
                if (u == null) continue;
                totalPop += u.pop;
                if (o.id == d.id) sameCount++;
                if (!IsBackline(u)) frontPop += u.pop;
            }
            float targetFront = archetype.style.frontlineTarget > 0f ? archetype.style.frontlineTarget : 0.38f;
            float curFront = totalPop > 1f ? frontPop / totalPop : 0f;
            bool dBack = IsBackline(d);
            float s = 0f;

            if (!dBack)
            {
                if (curFront < targetFront) s += (targetFront - curFront) * 190f;
                else s -= (curFront - targetFront) * 45f;
            }
            else
            {
                if (curFront < targetFront * 0.65f) s -= 55f;
                else if (curFront >= targetFront) s += 14f;
            }
            // 多样性：第 4 个同兵种开始递减
            if (sameCount >= 3) s -= (sameCount - 2) * 16f;
            return s;
        }

        static bool IsBackline(UnitDef u)
        {
            // 只看射程：刺客/骑兵是近战单位，一样要顶在前面吸收伤害。
            // （之前把刺客也算后排，导致刺客流被自己的结构约束惩罚，被迫买盾兵，胜率掉到 5%）
            return u.range >= 60;
        }

        float RelicScore(RelicDef r)
        {
            float s = archetype.RelicScore(r.id) * 100f + RarityScore(r.rarity) * 12f;
            if (r.effects != null)
            {
                for (int i = 0; i < r.effects.Length; i++)
                {
                    var e = r.effects[i];
                    if (e == null) continue;
                    if (e.type == "stat" && !string.IsNullOrEmpty(e.target))
                    {
                        if (e.target == "all" || e.target == "big" || e.target == "pop1") s += 3f;
                        else s += 8f * archetype.TagW(e.target);
                    }
                    else if (e.type == "synergy") s += 9f * archetype.TagW(e.tag);
                    else if (e.type == "income" || e.type == "interest" || e.type == "compoundInterest" || e.type == "instantGems")
                        s += archetype.id == "greed" ? 14f : 4f;
                }
            }
            return s;
        }

        float PickRandom(float best, float worst)
        {
            // quality=1 一定选最优；quality=0 近似随机
            float noise = (match.Rand(1000) / 1000f - 0.5f) * 2f * (1.05f - difficulty.relicPickQuality) * Math.Max(40f, Math.Abs(best - worst) + 40f);
            return noise;
        }

        // ------------------------------------------------------------ 准备阶段
        void Think()
        {
            if (!player.relicPicked && player.activeRelicOffers.Count > 0)
            {
                string best = null;
                float bestScore = float.MinValue, worstScore = float.MaxValue;
                float[] scores = new float[player.activeRelicOffers.Count];
                for (int i = 0; i < player.activeRelicOffers.Count; i++)
                {
                    var r = db.Relic(player.activeRelicOffers[i]);
                    float sc = r != null ? RelicScore(r) + PickRandom(0f, 0f) : 0f;
                    scores[i] = sc;
                    if (sc > bestScore) { bestScore = sc; best = r?.id; }
                    if (sc < worstScore) worstScore = sc;
                }
                if (best != null) match.ChooseRelic(player, best); else match.SkipRelic(player);
                return;
            }

            // 宝石消费：优先流派关键遗物，其次技能强化
            if (player.gems >= 4)
            {
                string bestGem = null;
                float bestGemScore = -1f;
                for (int i = 0; i < player.gemShop.Count; i++)
                {
                    var r = db.Relic(player.gemShop[i]);
                    if (r == null) continue;
                    if (player.gems < db.GemCost(r.rarity)) continue;
                    float sc = RelicScore(r) - db.GemCost(r.rarity) * 2f;
                    if (sc > bestGemScore) { bestGemScore = sc; bestGem = r.id; }
                }
                if (bestGem != null && bestGemScore > 20f) { match.BuyGemRelic(player, bestGem); return; }
                if (difficulty.relicPickQuality > 0.5f)
                {
                    for (int i = 0; i < player.skills.Count; i++)
                    {
                        int lv = player.SkillLevel(player.skills[i]);
                        if (lv > 0 && lv < 3 && player.gems >= 5 + lv * 4 + 6) { match.UpgradeSkill(player, player.skills[i]); return; }
                    }
                }
            }

            // 升级：每回合最多投入 2~3 次，且必须留出买兵的钱（否则会把金币全烧在经验上）
            float xpCost2 = db.Balance.round.xpBuyCost;
            int maxXpBuys = Math.Max(1, (int)Math.Round(2f * difficulty.xpBias * archetype.style.xpBias));
            int xpTarget = archetype.id == "greed" ? 11 : (archetype.style.xpBias > 1.1f ? 10 : 9);
            maxXpBuys = Math.Max(maxXpBuys, player.level >= 8 ? 4 : maxXpBuys);
            if (xpBuysThisRound < maxXpBuys && player.level < xpTarget && player.gold >= xpCost2 + 10f + archetype.style.saveBuffer)
            {
                xpBuysThisRound++;
                match.BuyXp(player);
                return;
            }

            // 买兵
            int[] foe = EnemyTagCounts();
            var scored = new List<KeyValuePair<int, float>>();
            for (int i = 0; i < player.shop.Count; i++)
            {
                var o = player.shop[i];
                if (o == null || o.sold) continue;
                var d = db.Unit(o.unitId);
                if (d == null || d.unlockLevel > player.level) continue;
                scored.Add(new KeyValuePair<int, float>(i, UnitScore(d, foe)));
            }
            scored.Sort((a, b) => b.Value.CompareTo(a.Value));

            // 保留金：只为「下一档利息」留最多 2 金，其余全部换成兵力
            // （利息上限只有 5 金，囤钱远不如把人口填满）
            float keep = 0f;
            int nextInterest = ((int)(player.gold / 10f) + 1) * 10;
            float gap = nextInterest - player.gold;
            if (gap <= 2f && nextInterest <= (archetype.id == "greed" ? 40 : 20)) keep = gap;
            bool bought = false;
            if (difficulty.buyRandomness > 0.5f && scored.Count > 1)
            {
                // 低难度：在能买得起的选项里随机挑，不再按最优解买
                var affordable = new List<KeyValuePair<int, float>>();
                for (int i = 0; i < scored.Count; i++)
                {
                    var od = db.Unit(player.shop[scored[i].Key].unitId);
                    if (od == null) continue;
                    if (match.PopUsed(player) + od.pop > match.PopCap(player)) continue;
                    if (player.gold - match.UnitPrice(player, od) < keep) continue;
                    affordable.Add(scored[i]);
                }
                if (affordable.Count > 0)
                {
                    int pick = match.Rand(affordable.Count);
                    bought = match.BuyUnit(player, affordable[pick].Key);
                }
            }
            else
            {
                for (int i = 0; i < scored.Count; i++)
                {
                    var o = player.shop[scored[i].Key];
                    var d = db.Unit(o.unitId);
                    if (match.PopUsed(player) + d.pop > match.PopCap(player)) continue;
                    int price = match.UnitPrice(player, d);
                    if (player.gold - price < keep) continue;
                    if (match.BuyUnit(player, scored[i].Key)) bought = true;
                }
            }

            // 商店已包含全部已解锁兵种，不需要刷新
            bool popRoom = match.PopUsed(player) + 1 <= match.PopCap(player);
            bool noRoom = match.PopUsed(player) >= match.PopCap(player);
            bool upgraded = difficulty.allowUpgrade && TryUpgrade(scored, foe);
            bought |= upgraded;
            if (player.gold < 4f || (noRoom && !upgraded) || !popRoom)
            {
                DeployArmy();
                match.SetReady(player, true);
            }
        }

        /// <summary>
        /// 人口已满时的换血：按「最弱优先」批量卖掉若干部队，腾出人口与金币换更强的兵。
        /// 这是人类玩 auto-battler 的核心操作，缺了它 AI 会攒一堆金币却只有一堆杂兵。
        /// </summary>
        bool TryUpgrade(List<KeyValuePair<int, float>> scored, int[] foe)
        {
            if (scored.Count == 0 || player.roster.Count == 0) return false;
            int cap = match.PopCap(player);
            int used = match.PopUsed(player);

            // 已有部队按战力升序
            var owned = new List<KeyValuePair<int, float>>();
            for (int i = 0; i < player.roster.Count; i++)
            {
                var d = db.Unit(player.roster[i].id);
                if (d == null) continue;
                owned.Add(new KeyValuePair<int, float>(i, UnitScore(d, foe)));
            }
            owned.Sort((x, y) => x.Value.CompareTo(y.Value));

            for (int s = 0; s < scored.Count; s++)
            {
                var nd = db.Unit(player.shop[scored[s].Key].unitId);
                if (nd == null) continue;
                int price = match.UnitPrice(player, nd);
                int freeNow = cap - used;
                int need = nd.pop - freeNow;
                if (need <= 0) continue;                       // 本来就能直接买
                var sellIdx = new List<int>();
                int accPop = 0, refund = 0;
                float bestSoldScore = float.MinValue;
                for (int k = 0; k < owned.Count && accPop < need; k++)
                {
                    var od = db.Unit(player.roster[owned[k].Key].id);
                    if (od == null) continue;
                    accPop += od.pop;
                    refund += Math.Max(1, (int)Math.Floor(match.UnitPrice(player, od) * db.Balance.round.sellRefundRatio));
                    sellIdx.Add(owned[k].Key);
                    if (owned[k].Value > bestSoldScore) bestSoldScore = owned[k].Value;
                }
                if (accPop < need) continue;
                if (scored[s].Value < bestSoldScore + 20f) continue;   // 换完必须明显更强
                if (player.gold + refund < price) continue;
                sellIdx.Sort((x, y) => y.CompareTo(x));
                for (int k = 0; k < sellIdx.Count; k++) match.SellUnit(player, sellIdx[k]);
                if (match.BuyUnit(player, scored[s].Key)) return true;
                return false;
            }
            return false;
        }

        /// <summary>按流派与难度部署：近战靠前、远程靠后，高质量难度排成整齐阵型。</summary>
        void DeployArmy()
        {
            var sim = match.sim;
            int guard = 0;
            while (guard++ < 64)
            {
                OwnedUnit target = null;
                for (int i = 0; i < player.roster.Count; i++) if (!player.roster[i].placed) { target = player.roster[i]; break; }
                if (target == null) break;
                string id = target.id;
                var def = db.Unit(id);
                if (def == null) { target.placed = true; continue; }
                bool ranged = def.range > 60f;
                bool back = ranged;
                if (archetype.id == "shadow") back = false;
                if (archetype.id == "volley") back = true;

                float lane = 0.5f + ((deployIndex % 5) - 2) * 0.16f;
                float depth = back ? 0.07f : 0.14f;
                if (def.HasTag("assassin"))
                {
                    // 刺客走侧翼通道，绕过敌方前排；其余部队正常列阵（否则会被各个击破）
                    lane = 0.5f + (deployIndex % 2 == 0 ? 0.36f : -0.36f);
                    depth = 0.11f;
                }
                if (difficulty.deployQuality < 1f)
                    lane += (match.Rand(100) / 100f - 0.5f) * 0.5f * (1f - difficulty.deployQuality);
                lane = Math.Max(0.06f, Math.Min(0.94f, lane));
                if (difficulty.deployQuality < 0.6f) depth += match.Rand(60) / 1000f;

                float x = player.Team == Team.Left
                    ? sim.fieldWidth * depth + (deployIndex / 5) * 24f
                    : sim.fieldWidth * (1f - depth) - (deployIndex / 5) * 24f;
                float y = sim.fieldHeight * lane;
                target.placed = true; target.x = x; target.y = y;
                deployIndex++;   // AI 保留自己的战术布阵（走侧翼/前后排差异）
            }
        }

        // ------------------------------------------------------------ 战斗阶段
        void BattleThink()
        {
            var units = match.sim.Units;
            var mySkills = player.skills;
            if (mySkills.Count == 0) return;

            for (int si = 0; si < mySkills.Count; si++)
            {
                string sid = mySkills[si];
                if (player.SkillLevel(sid) <= 0) continue;
                var sd = db.Skill(sid);
                if (sd == null) continue;
                if (IsPreferredSkill(sid) == false && si > 0) continue;

                float x, y;
                bool ok;
                string kind = sd.kind;
                if (kind == "support") ok = FindAllySpot(sid, out x, out y);
                else if (kind == "summon") ok = FindSummonSpot(out x, out y);
                else ok = FindEnemySpot(out x, out y);
                if (!ok) continue;
                if (match.CastSkill(player, sid, x, y)) return;
            }
        }

        bool IsPreferredSkill(string sid)
        {
            var list = archetype.style.skills;
            if (list == null || list.Length == 0) return true;
            for (int i = 0; i < list.Length; i++) if (list[i] == sid) return true;
            return false;
        }

        bool FindEnemySpot(out float bx, out float by)
        {
            var units = match.sim.Units;
            Team foe = player.Team == Team.Left ? Team.Right : Team.Left;
            int need = difficulty.deployQuality > 0.7f ? 2 : 3;
            return BestCluster(units, foe, need, out bx, out by);
        }

        bool FindAllySpot(string skillId, out float bx, out float by)
        {
            var units = match.sim.Units;
            bool heal = skillId == "heal";
            float bestScore = -1f; bx = 0f; by = 0f;
            int need = difficulty.deployQuality > 0.7f ? 2 : 3;
            for (int i = 0; i < units.Count; i++)
            {
                var c = units[i];
                if (!c.alive || c.team != player.Team) continue;
                float score = 0f; int n = 0;
                for (int j = 0; j < units.Count; j++)
                {
                    var o = units[j];
                    if (!o.alive || o.team != player.Team) continue;
                    float dx = o.x - c.x, dy = o.y - c.y;
                    if (dx * dx + dy * dy > 130f * 130f) continue;
                    float w = heal ? (1f - o.hp / Math.Max(1f, o.maxHp)) : 1f;
                    float tierW = o.def.tier * 0.4f + 0.6f;
                    score += w * tierW; n++;
                }
                if (n < need) continue;
                if (score > bestScore) { bestScore = score; bx = c.x; by = c.y; }
            }
            return bestScore > (heal ? 1.6f : 2.0f);
        }

        bool FindSummonSpot(out float bx, out float by)
        {
            var units = match.sim.Units;
            Team foe = player.Team == Team.Left ? Team.Right : Team.Left;
            float ex = 0f, ey = 0f; int n = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.alive || u.team != foe) continue;
                ex += u.x; ey += u.y; n++;
            }
            if (n == 0) { bx = by = 0f; return false; }
            ex /= n; ey /= n;
            float dir = player.Team == Team.Left ? -1f : 1f;
            bx = Math.Max(40f, Math.Min(match.sim.fieldWidth - 40f, ex + dir * 140f));
            by = ey;
            return true;
        }

        bool BestCluster(IReadOnlyList<SimUnit> units, Team target, int need, out float bx, out float by)
        {
            float bestScore = -1f; bx = 0f; by = 0f;
            float radius = 120f;
            for (int i = 0; i < units.Count; i++)
            {
                var c = units[i];
                if (!c.alive || c.team != target) continue;
                float score = 0f; int n = 0;
                for (int j = 0; j < units.Count; j++)
                {
                    var o = units[j];
                    if (!o.alive || o.team != target) continue;
                    float dx = o.x - c.x, dy = o.y - c.y;
                    if (dx * dx + dy * dy > radius * radius) continue;
                    score += o.def.tier * 0.5f + 1f;
                    n++;
                }
                if (n < need) continue;
                if (score > bestScore) { bestScore = score; bx = c.x; by = c.y; }
            }
            return bestScore > 0f;
        }
    }
}
