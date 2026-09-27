using System;
using System.Collections.Generic;

namespace Milaqi.Core
{
    /// <summary>简单 AI 对手：用于单机对战与批模拟平衡测试。</summary>
    public sealed class AiController
    {
        readonly Match match;
        readonly GameDatabase db;
        public readonly PlayerState player;
        float thinkTimer;
        float skillTimer;
        int lastRound = -1;

        public AiController(Match m, PlayerState p)
        {
            match = m;
            db = m.db;
            player = p;
            player.isAI = true;
        }

        public void Update(float dt)
        {
            if (match.round != lastRound) { lastRound = match.round; thinkTimer = 0.4f; skillTimer = 2.5f; }
            if (match.phase == MatchPhase.Prep)
            {
                thinkTimer -= dt;
                if (thinkTimer <= 0f) { thinkTimer = 0.25f; Think(); }
            }
            else if (match.phase == MatchPhase.Battle)
            {
                skillTimer -= dt;
                if (skillTimer <= 0f) { skillTimer = 3.0f; BattleThink(); }
            }
        }

        static int RarityScore(string rarity)
        {
            if (rarity == "epic") return 3;
            if (rarity == "rare") return 2;
            return 1;
        }

        void Think()
        {
            if (!player.relicPicked && player.activeRelicOffers.Count > 0)
            {
                string best = null;
                int bestScore = -1;
                for (int i = 0; i < player.activeRelicOffers.Count; i++)
                {
                    var r = db.Relic(player.activeRelicOffers[i]);
                    if (r == null) continue;
                    int sc = RarityScore(r.rarity) * 10 + match.Rand(12);
                    if (sc > bestScore) { bestScore = sc; best = r.id; }
                }
                if (best != null) match.ChooseRelic(player, best); else match.SkipRelic(player);
                return;
            }

            if (player.gems >= 8)
            {
                for (int i = 0; i < player.gemShop.Count; i++)
                {
                    var r = db.Relic(player.gemShop[i]);
                    if (r == null) continue;
                    if (player.gems >= db.GemCost(r.rarity) + 6) { match.BuyGemRelic(player, r.id); return; }
                }
            }

            float xpCost = db.Balance.round.xpBuyCost;
            if (player.level < 9 && player.gold >= xpCost + 16f) { match.BuyXp(player); return; }

            bool bought = false;
            for (int i = 0; i < player.shop.Count; i++)
            {
                var o = player.shop[i];
                if (o == null || o.sold) continue;
                var d = db.Unit(o.unitId);
                if (d == null) continue;
                int price = match.UnitPrice(player, d);
                if (player.gold - price < 4f) continue;
                if (match.PopUsed(player) + d.pop > match.PopCap(player)) continue;
                if (match.BuyUnit(player, i)) bought = true;
            }

            if (!bought && player.gold >= 14f && match.PopUsed(player) < match.PopCap(player))
            {
                match.Reroll(player);
                return;
            }

            if (player.gold < 10f || match.PopUsed(player) >= match.PopCap(player))
                match.SetReady(player, true);
        }

        void BattleThink()
        {
            var units = match.sim.Units;
            float ex = 0f, ey = 0f; int ec = 0;
            float hx = 0f, hy = 0f; int hc = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.alive) continue;
                if (u.team == player.Team)
                {
                    if (u.hp / u.maxHp < 0.55f) { hx += u.x; hy += u.y; hc++; }
                }
                else { ex += u.x; ey += u.y; ec++; }
            }
            if (ec == 0) return;
            ex /= ec; ey /= ec;
            if (hc >= 2 && player.SkillLevel("heal") > 0)
            {
                match.CastSkill(player, "heal", hx / hc, hy / hc);
                return;
            }
            if (player.SkillLevel("meteor") > 0) { match.CastSkill(player, "meteor", ex, ey); return; }
            if (player.SkillLevel("arcane_blast") > 0) { match.CastSkill(player, "arcane_blast", ex, ey); return; }
        }
    }
}
