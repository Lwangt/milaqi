using System;
using System.Collections.Generic;
using System.IO;

namespace Milaqi.Core
{
    /// <summary>主机权威模式下的状态快照序列化（纯 C#，字节流）。</summary>
    public static class Snapshot
    {
        public const byte Version = 1;

        public static byte[] Write(Match m, GameDatabase db)
        {
            using (var ms = new MemoryStream(8192))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Version);
                w.Write(m.round);
                w.Write((byte)m.phase);
                w.Write(m.phaseTimer);
                w.Write(m.battleTimer);
                w.Write(m.sim.time);
                w.Write((short)m.winnerIndex);

                for (int i = 0; i < 2; i++) WritePlayer(w, m.players[i], db);

                var units = m.sim.Units;
                int alive = 0;
                for (int i = 0; i < units.Count; i++) if (units[i].alive) alive++;
                w.Write((short)alive);
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (!u.alive) continue;
                    w.Write((short)db.UnitIndex(u.def.id));
                    w.Write((byte)u.team);
                    w.Write(u.x);
                    w.Write(u.y);
                    w.Write((short)Math.Max(0, Math.Min(32000, u.hp)));
                    w.Write((short)Math.Max(0, Math.Min(32000, u.maxHp)));
                    w.Write((short)Math.Max(0, Math.Min(32000, u.shield)));
                    byte flags = 0;
                    if (u.slowUntil > m.sim.time && u.slowPct > 0f) flags |= 1;
                    if (u.buffAtkUntil > m.sim.time) flags |= 2;
                    if (u.buffSpdUntil > m.sim.time) flags |= 4;
                    if (u.hitUntil > m.sim.time) flags |= 8;
                    w.Write(flags);
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        static void WritePlayer(BinaryWriter w, PlayerState p, GameDatabase db)
        {
            w.Write(p.gold);
            w.Write(p.gems);
            w.Write((short)p.level);
            w.Write((short)p.xp);
            w.Write(p.baseHp);
            w.Write((short)p.winStreak);
            w.Write((short)p.lossStreak);
            w.Write(p.ready);
            w.Write(p.relicPicked);
            w.Write(p.lastIncomeGold);
            w.Write(p.lastIncomeGems);

            w.Write((short)p.relicIds.Count);
            for (int i = 0; i < p.relicIds.Count; i++) w.Write(p.relicIds[i]);

            w.Write((short)p.shop.Count);
            for (int i = 0; i < p.shop.Count; i++) { w.Write(p.shop[i].unitId); w.Write(p.shop[i].sold); }

            w.Write((short)p.skills.Count);
            for (int i = 0; i < p.skills.Count; i++) { w.Write(p.skills[i]); w.Write((short)p.SkillLevel(p.skills[i])); }

            w.Write((short)p.roster.Count);
            for (int i = 0; i < p.roster.Count; i++)
            {
                w.Write(p.roster[i].id);
                w.Write(p.roster[i].x);
                w.Write(p.roster[i].y);
                w.Write(p.roster[i].placed);
            }

            w.Write((short)p.activeRelicOffers.Count);
            for (int i = 0; i < p.activeRelicOffers.Count; i++) w.Write(p.activeRelicOffers[i]);

            w.Write((short)p.gemShop.Count);
            for (int i = 0; i < p.gemShop.Count; i++) w.Write(p.gemShop[i]);

            w.Write((short)p.pendingRelics.Count);
            for (int i = 0; i < p.pendingRelics.Count; i++) w.Write(p.pendingRelics[i]);
        }

        public static void Apply(Match m, byte[] data, GameDatabase db)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                byte ver = r.ReadByte();
                if (ver != Version) return;
                m.round = r.ReadInt32();
                m.phase = (MatchPhase)r.ReadByte();
                m.phaseTimer = r.ReadSingle();
                m.battleTimer = r.ReadSingle();
                m.sim.time = r.ReadSingle();
                m.winnerIndex = r.ReadInt16();

                for (int i = 0; i < 2; i++) ReadPlayer(r, m.players[i]);

                m.sim.ClearUnits();
                m.sim.ClearEvents();
                int count = r.ReadInt16();
                for (int i = 0; i < count; i++)
                {
                    int defIdx = r.ReadInt16();
                    var team = (Team)r.ReadByte();
                    float x = r.ReadSingle();
                    float y = r.ReadSingle();
                    float hp = r.ReadInt16();
                    float maxHp = r.ReadInt16();
                    float shield = r.ReadInt16();
                    r.ReadByte();
                    var def = db.UnitByIndex(defIdx);
                    if (def == null) continue;
                    m.sim.SpawnRaw(def, team, x, y, hp, maxHp, shield);
                }
            }
        }

        static void ReadPlayer(BinaryReader r, PlayerState p)
        {
            p.gold = r.ReadSingle();
            p.gems = r.ReadSingle();
            p.level = r.ReadInt16();
            p.xp = r.ReadInt16();
            p.baseHp = r.ReadSingle();
            p.winStreak = r.ReadInt16();
            p.lossStreak = r.ReadInt16();
            p.ready = r.ReadBoolean();
            p.relicPicked = r.ReadBoolean();
            p.lastIncomeGold = r.ReadInt32();
            p.lastIncomeGems = r.ReadInt32();

            p.relicIds.Clear();
            int n = r.ReadInt16();
            for (int i = 0; i < n; i++) p.relicIds.Add(r.ReadString());

            p.shop.Clear();
            n = r.ReadInt16();
            for (int i = 0; i < n; i++) { var id = r.ReadString(); var sold = r.ReadBoolean(); p.shop.Add(new ShopOffer { unitId = id, sold = sold }); }

            p.skills.Clear();
            p.skillLevels.Clear();
            n = r.ReadInt16();
            for (int i = 0; i < n; i++) { var id = r.ReadString(); int lv = r.ReadInt16(); p.skills.Add(id); p.skillLevels[id] = lv; }

            p.roster.Clear();
            n = r.ReadInt16();
            for (int i = 0; i < n; i++)
            {
                var id = r.ReadString();
                float ox = r.ReadSingle(), oy = r.ReadSingle();
                bool placed = r.ReadBoolean();
                p.roster.Add(new OwnedUnit { id = id, x = ox, y = oy, placed = placed });
            }

            p.activeRelicOffers.Clear();
            n = r.ReadInt16();
            for (int i = 0; i < n; i++) p.activeRelicOffers.Add(r.ReadString());

            p.gemShop.Clear();
            n = r.ReadInt16();
            for (int i = 0; i < n; i++) p.gemShop.Add(r.ReadString());

            p.pendingRelics.Clear();
            n = r.ReadInt16();
            for (int i = 0; i < n; i++) p.pendingRelics.Add(r.ReadString());
        }
    }
}
