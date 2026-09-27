using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Milaqi.Core;

namespace Milaqi.Tests
{
    public static class Program
    {
        static string FindRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "data", "units.json"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new Exception("找不到仓库根目录（data/units.json）");
        }

        static GameDatabase LoadDb(string root)
        {
            return GameDatabase.Load(rel => File.ReadAllText(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))));
        }

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var root = FindRoot();
            var db = LoadDb(root);
            string mode = args.Length > 0 ? args[0] : "all";
            int fails = 0;

            if (mode == "all" || mode == "selftest") fails += SelfTest(db);
            if (mode == "all" || mode == "batch")
            {
                int n = args.Length > 1 ? int.Parse(args[1]) : 60;
                BatchSim(db, n);
            }
            if (mode == "battle")
            {
                int n = args.Length > 1 ? int.Parse(args[1]) : 300;
                PureBattleTest(db, n);
            }
            if (mode == "flip")
            {
                int n = args.Length > 1 ? int.Parse(args[1]) : 200;
                BatchSim(db, n, true);
            }
            if (mode == "matrix")
            {
                int n = args.Length > 1 ? int.Parse(args[1]) : 30;
                ArchetypeMatrix(db, n);
            }
            if (mode == "regress") { fails += Regression(db); }
            if (mode == "power")
            {
                // power [n]  全兵种实战战力体检：等人口 / 等金币 两种口径对照民兵
                int n = args.Length > 1 ? int.Parse(args[1]) : 100;
                PowerAudit(db, n);
            }
            if (mode == "cross")
            {
                // cross <A> <nA> <B> <nB> <局数>  —— 同人口跨兵种对抗，检验单兵强度是否平衡
                string a = args.Length > 1 ? args[1] : "archer";
                int na = args.Length > 2 ? int.Parse(args[2]) : 6;
                string b = args.Length > 3 ? args[3] : "militia";
                int nb = args.Length > 4 ? int.Parse(args[4]) : 6;
                int n = args.Length > 5 ? int.Parse(args[5]) : 400;
                CrossTest(db, a, na, b, nb, n);
            }
            if (mode == "diag")
            {
                string a = args.Length > 1 ? args[1] : "greed";
                string b = args.Length > 2 ? args[2] : "undead_swarm";
                DiagMatch(db, a, b);
            }
            if (mode == "ladder")
            {
                int n = args.Length > 1 ? int.Parse(args[1]) : 60;
                DifficultyLadder(db, n);
            }
            Console.WriteLine(fails == 0 ? "[OK] 全部自检通过" : "[FAIL] 有 " + fails + " 项自检失败");
            return fails == 0 ? 0 : 1;
        }

        static void Check(bool cond, string name, ref int fails)
        {
            if (cond) Console.WriteLine("  [PASS] " + name);
            else { Console.WriteLine("  [FAIL] " + name); fails++; }
        }

        static int SelfTest(GameDatabase db)
        {
            int fails = 0;
            Console.WriteLine("== 数据自检 ==");
            Check(db.Units.Length >= 30, "兵种数量 >= 30（实际 " + db.Units.Length + "）", ref fails);
            Check(db.Relics.Length >= 20, "遗物数量 >= 20（实际 " + db.Relics.Length + "）", ref fails);
            Check(db.Skills.Length >= 6, "技能数量 >= 6（实际 " + db.Skills.Length + "）", ref fails);
            Check(db.Balance.baseHp > 0 && db.Balance.round.levelXp.Length >= 10, "经济配置已加载", ref fails);
            int badTier = 0;
            for (int i = 0; i < db.Units.Length; i++) if (db.Units[i].tier < 1 || db.Units[i].tier > 5) badTier++;
            Check(badTier == 0, "所有兵种 tier 合法", ref fails);
            for (int tier = 1; tier <= 5; tier++)
            {
                int c = 0;
                for (int i = 0; i < db.Units.Length; i++) if (db.Units[i].tier == tier && db.Units[i].unlockRound < 900) c++;
                Check(c >= 5, "T" + tier + " 兵种数量 >= 5（实际 " + c + "）", ref fails);
            }

            Console.WriteLine("== 战斗自检 ==");
            var m = new Match(db, 7);
            m.Start();
            Check(m.round == 1 && m.phase == MatchPhase.Prep, "开局进入第 1 回合准备阶段", ref fails);
            var rcfg0 = db.Balance.round;
            Check(m.players[0].gold >= rcfg0.startGold && m.players[0].level >= rcfg0.startLevel && m.players[0].level <= rcfg0.startLevel + 3,
                "初始经济与等级正确（金币 " + (int)m.players[0].gold + "，等级 " + m.players[0].level + "）", ref fails);
            Check(rcfg0.incomePerPop > 0f && rcfg0.xpPerRound > 0f,
                "收入按人口成长、" + "经验每回合发放已配置", ref fails);
            Check(m.players[0].roster.Count >= 2, "初始部队已编入名册（" + m.players[0].roster.Count + " 个单位）", ref fails);
            Check(m.PopUsed(m.players[0]) <= m.PopCap(m.players[0]), "初始人口未超上限", ref fails);

            m.BeginBattle();
            float t = 0f;
            bool ended = false;
            while (t < 40f)
            {
                m.sim.Step(1f / 30f);
                t += 1f / 30f;
                if (m.sim.BattleOver) { ended = true; break; }
            }
            Check(ended, "战斗在限定时间内结束（" + t.ToString("0.0") + "s）", ref fails);
            Check(m.sim.AliveCount(Team.Left) > 0 || m.sim.AliveCount(Team.Right) > 0, "战斗结束后存在存活单位", ref fails);

            Console.WriteLine("== 经济与系统自检 ==");
            var m2 = new Match(db, 99);
            m2.Start();
            var p0 = m2.players[0];
            float before = p0.gold;
            int cap = m2.PopCap(p0);
            Check(cap == p0.level + 1, "人口上限 = 等级 + 1（" + cap + "）", ref fails);
            m2.AddXp(p0, 100f);
            Check(p0.level > 3 && m2.PopCap(p0) == p0.level + 1, "升级后人口上限成长", ref fails);
            Check(p0.level <= db.Balance.round.maxLevel, "等级不超过上限", ref fails);
            m2.BuildBoard(p0);
            bool gotRelic = false;
            for (int r = 0; r < 6 && !gotRelic; r++)
            {
                if (p0.activeRelicOffers.Count > 0) { gotRelic = m2.ChooseRelic(p0, p0.activeRelicOffers[0]); }
                else { m2.ForceStartBattle(); while (m2.phase == MatchPhase.Battle) m2.Tick(1f / 30f); while (m2.phase == MatchPhase.Settle) m2.Tick(0.5f); }
            }
            Check(gotRelic, "遗物系统可正常选择", ref fails);
            Check(p0.relicIds.Count >= 1, "选中遗物后生效", ref fails);

            var p1 = m2.players[0];
            p1.gems = 100;
            bool upgraded = m2.UpgradeSkill(p1, "meteor");
            Check(upgraded && p1.pendingSkillUpgrades.Count == 1, "宝石可强化技能（下回合生效）", ref fails);

            Console.WriteLine("== 解锁与商店自检 ==");
            int noLevel = 0;
            for (int i = 0; i < db.Units.Length; i++) if (db.Units[i].unlockLevel <= 0) noLevel++;
            Check(noLevel == 0, "所有兵种都配置了解锁等级", ref fails);
            var m4 = new Match(db, 88);
            m4.Start();
            var p4 = m4.players[0];
            m4.RollShop(p4);
            int nLow = p4.shop.Count;
            p4.level = db.Balance.round.maxLevel;
            m4.RollShop(p4);
            int nHigh = p4.shop.Count;
            Check(nLow >= 5 && nHigh > nLow, "商店随等级解锁更多兵种（Lv3 可见 " + nLow + " 种 → Lv12 可见 " + nHigh + " 种）", ref fails);
            bool lowTierLocked = true;
            for (int i = 0; i < p4.shop.Count; i++)
            {
                var ud = db.Unit(p4.shop[i].unitId);
                if (ud != null && ud.unlockLevel > p4.level) lowTierLocked = false;
            }
            Check(lowTierLocked, "高等级商店里没有超出等级的兵种", ref fails);

            Console.WriteLine("== 购买与结算自检 ==");
            var m3 = new Match(db, 321);
            m3.Start();
            var p3 = m3.players[0];
            int r0 = p3.roster.Count;
            float g0 = p3.gold;
            var d0 = db.Unit(p3.shop[0].unitId);
            int price0 = m3.UnitPrice(p3, d0);
            bool bought1 = m3.BuyUnit(p3, 0);
            Check(bought1 && p3.roster.Count == r0 + 1, "点一次只买 1 个（名册 " + r0 + " -> " + p3.roster.Count + "）", ref fails);
            Check(Math.Abs((g0 - p3.gold) - price0) < 0.01f, "点一次只扣 1 个的钱（" + (g0 - p3.gold) + " / 单价 " + price0 + "）", ref fails);
            Check(p3.roster[p3.roster.Count - 1].placed == false, "新买的兵进入待出战队列", ref fails);
            m3.BeginBattle();
            float t3 = 0f;
            while (!m3.sim.BattleOver && t3 < 40f) { m3.sim.Step(1f / 30f); t3 += 1f / 30f; }
            m3.Tick(0.02f);
            Check(m3.phase != MatchPhase.Battle && m3.sim.Units.Count == 0, "结算后战场清空（剩余 " + m3.sim.Units.Count + "）", ref fails);
            return fails;
        }

        /// <summary>跑一整局。双方 AI 每 tick 随机顺序更新，避免顺序带来的系统性优势。</summary>
        static Match PlayMatch(GameDatabase db, ArchetypeDef a, ArchetypeDef b, DifficultyDef da, DifficultyDef db_, Random rnd)
        {
            var m = new Match(db, rnd.Next(1, int.MaxValue));
            m.Start();
            var ai0 = new AiController(m, m.players[0], a, da);
            var ai1 = new AiController(m, m.players[1], b, db_);
            int guard = 0;
            while (m.phase != MatchPhase.GameOver && guard++ < 300000)
            {
                float dt = 1f / 30f;
                if (rnd.Next(2) == 0) { ai0.Update(dt); ai1.Update(dt); }
                else { ai1.Update(dt); ai0.Update(dt); }
                m.Tick(dt);
            }
            return m;
        }

        static string ArmyLine(GameDatabase db, Match m, int pi)
        {
            var p = m.players[pi];
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < p.roster.Count; i++)
            {
                var d = db.Unit(p.roster[i].id);
                if (d == null) continue;
                int c; counts.TryGetValue(d.name, out c); counts[d.name] = c + 1;
            }
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add(kv.Key + "x" + kv.Value);
            var relics = new List<string>();
            for (int i = 0; i < p.relicIds.Count; i++) { var rd = db.Relic(p.relicIds[i]); if (rd != null) relics.Add(rd.name); }
            return "Lv" + p.level + " 金" + (int)p.gold + " 人口" + m.PopUsed(p) + "/" + m.PopCap(p) + " 城邦" + (int)p.baseHp
                + " [" + string.Join(",", parts) + "] 遗物[" + string.Join(",", relics) + "]";
        }

        /// <summary>
        /// 回归测试：把这一轮修掉的公平性缺陷全部锁住，防止以后改回去。
        /// 覆盖：镜像布阵 / 结算清场 / 随机性存在 / 战斗对称 / 坐标无 NaN / 购买一次一只。
        /// </summary>
        static int Regression(GameDatabase db)
        {
            int fails = 0;
            Console.WriteLine();
            Console.WriteLine("== 公平性回归测试 ==");

            // 1) 布阵必须左右严格镜像
            var m = new Match(db, 777);
            m.Start();
            foreach (var p in m.players) for (int i = 0; i < 6; i++) p.roster.Add(new OwnedUnit { id = "swordsman" });
            foreach (var p in m.players) { foreach (var o in p.roster) o.placed = false; m.AutoDeployAll(p); }
            var left = m.players[0].roster; var right = m.players[1].roster;
            bool mirrored = left.Count == right.Count;
            float maxErr = 0f;
            for (int i = 0; i < left.Count && mirrored; i++)
            {
                float expect = m.sim.fieldWidth - left[i].x;
                float err = Math.Abs(right[i].x - expect);
                if (err > maxErr) maxErr = err;
                if (Math.Abs(right[i].y - left[i].y) > 0.01f) mirrored = false;
            }
            Check(mirrored && maxErr < 0.01f, "布阵左右严格镜像（最大误差 " + maxErr.ToString("0.000") + "）", ref fails);

            // 2) 结算后战场清空
            m.BeginBattle();
            float t = 0f;
            while (!m.sim.BattleOver && t < 40f) { m.sim.Step(1f / 30f); t += 1f / 30f; }
            m.Tick(0.02f);
            Check(m.sim.Units.Count == 0, "结算后战场清空（剩余 " + m.sim.Units.Count + "）", ref fails);
            Check(m.players[0].roster.Count == 0 && m.players[1].roster.Count == 0,
                "结算后名册也清空——部队不跨回合（左 " + m.players[0].roster.Count + " / 右 " + m.players[1].roster.Count + "）", ref fails);

            // 3) 每局必须有随机性（不能所有对局完全相同）
            string sig1 = BattleSignature(db, 1001), sig2 = BattleSignature(db, 1002), sig3 = BattleSignature(db, 1003);
            Check(!(sig1 == sig2 && sig2 == sig3), "不同种子产生不同的战斗过程（随机性存在）", ref fails);

            // 4) 坐标不能出现 NaN / 无穷
            var m2 = new Match(db, 2024);
            m2.Start();
            for (int i = 0; i < 3; i++)
            {
                m2.AutoDeployAll(m2.players[0]); m2.AutoDeployAll(m2.players[1]);
                m2.BeginBattle();
                float tt = 0f;
                while (!m2.sim.BattleOver && tt < 40f) { m2.sim.Step(1f / 30f); tt += 1f / 30f; }
                m2.Tick(0.02f);
                for (int k = 0; k < 400 && m2.phase == MatchPhase.Settle; k++) m2.Tick(0.02f);
            }
            bool finite = true;
            foreach (var u in m2.sim.Units)
                if (float.IsNaN(u.x) || float.IsNaN(u.y) || float.IsInfinity(u.x) || float.IsInfinity(u.y)) finite = false;
            Check(finite, "长时间模拟后坐标无 NaN/Inf", ref fails);

            // 5) 战斗对称性：镜像阵容下左右胜率应接近 50%
            int lw = 0, rw = 0;
            for (int i = 0; i < 600; i++)
            {
                var mm = new Match(db, 50000 + i * 13);
                mm.Start();
                mm.players[0].roster.Clear(); mm.players[1].roster.Clear();
                mm.sim.ClearUnits();
                for (int k = 0; k < 6; k++) { mm.players[0].roster.Add(new OwnedUnit { id = "swordsman" }); mm.players[1].roster.Add(new OwnedUnit { id = "swordsman" }); }
                mm.BeginBattle();
                float tt = 0f;
                while (!mm.sim.BattleOver && tt < 40f) { mm.sim.Step(1f / 30f); tt += 1f / 30f; }
                float l, r; mm.sim.Settle(out l, out r);
                if (l > r) lw++; else if (r > l) rw++;
            }
            double bias = (lw + rw) > 0 ? 100.0 * Math.Abs(lw - rw) / (lw + rw) : 0;
            Check(bias < 8.0, "镜像阵容左右胜率偏差 < 8%（实测 " + bias.ToString("0.0") + "%）", ref fails);
            return fails;
        }

        /// <summary>跑一组对抗，返回 (A 胜率, 平均杀戮值差)。</summary>
        static void Duel(GameDatabase db, string a, int na, string b, int nb, int n, out double winRate, out double avgKv)
        {
            int aw = 0, bw = 0; double kv = 0;
            for (int i = 0; i < n; i++)
            {
                var m = new Match(db, 81000 + i * 29);
                m.Start();
                m.players[0].roster.Clear(); m.players[1].roster.Clear();
                m.sim.ClearUnits();
                for (int k = 0; k < na; k++) m.players[0].roster.Add(new OwnedUnit { id = a });
                for (int k = 0; k < nb; k++) m.players[1].roster.Add(new OwnedUnit { id = b });
                m.BeginBattle();
                float t = 0f;
                while (!m.sim.BattleOver && t < 60f) { m.sim.Step(1f / 30f); t += 1f / 30f; }
                float l, r; m.sim.Settle(out l, out r);
                kv += l - r;
                if (l > r) aw++; else if (r > l) bw++;
            }
            winRate = (aw + bw) > 0 ? 100.0 * aw / (aw + bw) : 50.0;
            avgKv = kv / n;
        }

        /// <summary>
        /// 全兵种实战战力体检。
        /// 用两种口径对照 1 费民兵：等人口、等金币。用来发现「贵但打不过便宜货」的兵种。
        /// </summary>
        static void PowerAudit(GameDatabase db, int n)
        {
            const int popBudget = 6;
            const double goldBudget = 12;
            var rows = new List<(string name, int tier, int price, int pop, double eqPop, double eqGold, double oneVone, double kvGold)>();
            foreach (var u in db.Units)
            {
                if (u == null || u.tier < 1 || u.tier > 5) continue;
                if (u.unlockRound > 900) continue;
                if (u.id == "militia") continue;
                // 严格对等：先按预算决定「被测单位」的数量，再让民兵一方凑出相同的人口/金币
                int nPop = Math.Max(1, (int)Math.Floor((double)popBudget / u.pop));
                int militiaPop = nPop * u.pop;
                int nGold = Math.Max(1, (int)Math.Floor(goldBudget / Math.Max(1, u.price)));
                int militiaGold = Math.Max(1, (int)Math.Floor(nGold * (double)u.price / 2.0));
                double wp, wg, w1, kvg;
                Duel(db, u.id, nPop, "militia", militiaPop, n, out wp, out _);
                Duel(db, u.id, nGold, "militia", militiaGold, n, out wg, out kvg);
                Duel(db, u.id, 1, "militia", 1, n, out w1, out _);
                rows.Add((u.name, u.tier, u.price, u.pop, wp, wg, w1, kvg));
            }
            rows.Sort((a, b) => a.eqGold.CompareTo(b.eqGold));
            Console.WriteLine();
            Console.WriteLine("== 全兵种实战战力体检（对照 民兵，每组 " + n + " 局）==");
            Console.WriteLine("   等人口 = 双方同为整数个（被测方 floor(" + popBudget + "/人口) 个，民兵补足同人口）");
            Console.WriteLine("   等金币 = 双方同为整数个（被测方 floor(" + (int)goldBudget + "/价格) 个，民兵补足同金币）");
            Console.WriteLine("   胜率 <50% 说明同等资源下打不过最便宜的 T1 民兵");
            Console.WriteLine();
            Console.WriteLine("  兵种            层  价 人口 | 等人口 等金币  1v1 | 等金币杀戮差值");
            Console.WriteLine("  ------------------------------------------------------------------");
            foreach (var r in rows)
                Console.WriteLine("  " + Pad(r.name, 14) + " T" + r.tier + "  " + Pad(r.price.ToString(), 2) + "  " + Pad(r.pop.ToString(), 2)
                    + "  | " + Pad(r.eqPop.ToString("0") + "%", 6) + " " + Pad(r.eqGold.ToString("0") + "%", 6) + " " + Pad(r.oneVone.ToString("0") + "%", 5)
                    + " | " + r.kvGold.ToString("0.0"));
            int bad = 0;
            foreach (var r in rows) if (r.eqGold < 45.0) bad++;
            Console.WriteLine();
            Console.WriteLine("  等金币口径下胜率 <45% 的兵种数：" + bad + " / " + rows.Count);
        }

        /// <summary>同人口跨兵种对抗：A 方 na 个 vs B 方 nb 个，看谁赢。用于检验单兵强度。</summary>
        static void CrossTest(GameDatabase db, string a, int na, string b, int nb, int n)
        {
            var da = db.Unit(a); var dbb = db.Unit(b);
            if (da == null || dbb == null) { Console.WriteLine("兵种 id 错误"); return; }
            int popA = da.pop * na, popB = dbb.pop * nb;
            int aw = 0, bw = 0, tie = 0;
            double kv = 0;
            for (int i = 0; i < n; i++)
            {
                var m = new Match(db, 70000 + i * 17);
                m.Start();
                m.players[0].roster.Clear(); m.players[1].roster.Clear();
                m.sim.ClearUnits();
                for (int k = 0; k < na; k++) m.players[0].roster.Add(new OwnedUnit { id = a });
                for (int k = 0; k < nb; k++) m.players[1].roster.Add(new OwnedUnit { id = b });
                m.BeginBattle();
                float t = 0f;
                while (!m.sim.BattleOver && t < 50f) { m.sim.Step(1f / 30f); t += 1f / 30f; }
                float l, r; m.sim.Settle(out l, out r);
                kv += l - r;
                if (l > r) aw++; else if (r > l) bw++; else tie++;
            }
            Console.WriteLine();
            Console.WriteLine("== 同人口对抗： " + da.name + " x" + na + "（" + popA + " 人口） vs "
                + dbb.name + " x" + nb + "（" + popB + " 人口），" + n + " 局 ==");
            Console.WriteLine("  " + da.name + " 胜 " + (100.0 * aw / n).ToString("0.0") + "%   "
                + dbb.name + " 胜 " + (100.0 * bw / n).ToString("0.0") + "%   平 " + tie
                + "   平均杀戮值差 " + (kv / n).ToString("0.0"));
            Console.WriteLine("  （人口相等时应在 50% 附近才算平衡）");
        }

        /// <summary>把一场战斗的过程压成一个签名，用于检测「所有对局完全相同」。</summary>
        static string BattleSignature(GameDatabase db, int seed)
        {
            var m = new Match(db, seed);
            m.Start();
            m.players[0].roster.Clear(); m.players[1].roster.Clear();
            m.sim.ClearUnits();
            for (int k = 0; k < 6; k++) { m.players[0].roster.Add(new OwnedUnit { id = "swordsman" }); m.players[1].roster.Add(new OwnedUnit { id = "knight" }); }
            m.BeginBattle();
            int ticks = 0;
            while (!m.sim.BattleOver && ticks < 1200) { m.sim.Step(1f / 30f); ticks++; }
            float l, r; m.sim.Settle(out l, out r);
            // 用「战斗持续帧数 + 双方存活杀戮值」做签名，对随机性极其敏感
            return ticks + "|" + m.sim.AliveCount(Team.Left) + ":" + m.sim.AliveCount(Team.Right)
                 + "|" + (int)l + ":" + (int)r;
        }

        /// <summary>单局逐回合诊断：看清是经济、等级、兵力还是战斗导致胜负。</summary>
        static void DiagMatch(GameDatabase db, string idA, string idB)
        {
            var a = db.Archetype(idA);
            var b = db.Archetype(idB);
            var diff = db.Difficulty("hard");
            var rnd = new Random(555);
            var m = new Match(db, rnd.Next(1, int.MaxValue));
            m.Start();
            var ai0 = new AiController(m, m.players[0], a, diff);
            var ai1 = new AiController(m, m.players[1], b, diff);
            Console.WriteLine();
            Console.WriteLine("== 诊断：" + a.name + "（左） vs " + b.name + "（右） ==");
            Console.WriteLine("回合 | 左 Lv/金/单位/KV/城邦      | 右 Lv/金/单位/KV/城邦      | 结果");
            int lastRound = 0;
            int guard = 0;
            while (m.phase != MatchPhase.GameOver && guard++ < 300000)
            {
                float dt = 1f / 30f;
                if (rnd.Next(2) == 0) { ai0.Update(dt); ai1.Update(dt); } else { ai1.Update(dt); ai0.Update(dt); }
                m.Tick(dt);
                if (m.round != lastRound && m.phase == MatchPhase.Prep)
                {
                    lastRound = m.round;
                    Console.WriteLine("R" + Pad(m.round.ToString(), 3) + " " + ArmyLine(db, m, 0) + "   VS   " + ArmyLine(db, m, 1));
                }
            }
            Console.WriteLine("胜者：" + (m.winnerIndex == 0 ? a.name : b.name) + "  共 " + m.round + " 回合");
        }

        /// <summary>7 大流派互相对战的胜率矩阵，用于验证流派强度是否收敛。</summary>
        static void ArchetypeMatrix(GameDatabase db, int n)
        {
            var archs = db.Archetypes;
            var diff = db.Difficulty("hard");
            if (archs.Length == 0) { Console.WriteLine("没有流派数据"); return; }
            Console.WriteLine();
            Console.WriteLine("== 流派胜率矩阵（每格 " + n + " 局，双方同为「困难」难度）==");
            var rnd = new Random(20260202);
            int[,] wins = new int[archs.Length, archs.Length];
            int[] totalWins = new int[archs.Length], totalGames = new int[archs.Length];
            for (int a = 0; a < archs.Length; a++)
            {
                for (int b = a + 1; b < archs.Length; b++)
                {
                    int wa = 0, wb = 0;
                    for (int k = 0; k < n; k++)
                    {
                        var m = PlayMatch(db, archs[a], archs[b], diff, diff, rnd);
                        if (m.winnerIndex == 0) wa++; else if (m.winnerIndex == 1) wb++;
                    }
                    wins[a, b] += wa;
                    wins[b, a] += wb;
                    totalWins[a] += wa; totalGames[a] += n;
                    totalWins[b] += wb; totalGames[b] += n;
                }
            }
            Console.Write("  行=我方 列=对手   ");
            for (int b = 0; b < archs.Length; b++) Console.Write(Pad(archs[b].name, 7));
            Console.WriteLine();
            for (int a = 0; a < archs.Length; a++)
            {
                Console.Write("  " + Pad(archs[a].name, 10) + " ");
                for (int b = 0; b < archs.Length; b++)
                {
                    if (a == b) { Console.Write(Pad("—", 7)); continue; }
                    double wr = 100.0 * wins[a, b] / n;
                    Console.Write(Pad(wr.ToString("0") + "%", 7));
                }
                Console.WriteLine();
            }
            Console.WriteLine();
            var ranked = new List<KeyValuePair<string, double>>();
            for (int a = 0; a < archs.Length; a++)
            {
                double wr = totalGames[a] > 0 ? 100.0 * totalWins[a] / totalGames[a] : 0;
                ranked.Add(new KeyValuePair<string, double>(archs[a].name, wr));
                Console.WriteLine("  " + Pad(archs[a].name, 10) + " 综合胜率 " + wr.ToString("0.0") + "%   (" + totalWins[a] + "/" + totalGames[a] + ")");
            }
            double max = -1, min = 101, sum = 0;
            foreach (var kv in ranked) { if (kv.Value > max) max = kv.Value; if (kv.Value < min) min = kv.Value; sum += kv.Value; }
            Console.WriteLine("  极差 " + (max - min).ToString("0.0") + "%  (最高 " + max.ToString("0.0") + "% / 最低 " + min.ToString("0.0") + "% / 平均 " + (sum / ranked.Count).ToString("0.0") + "%)");
            Console.WriteLine("  >>> 收敛目标：每个流派综合胜率落在 45%~55%");
        }

        static string Pad(string s, int width)
        {
            int len = 0;
            foreach (var ch in s) len += ch > 255 ? 2 : 1;
            var sb = new StringBuilder(s);
            for (int i = len; i < width; i++) sb.Append(' ');
            return sb.ToString();
        }

        /// <summary>难度阶梯：同一流派下各难度互打的胜率。</summary>
        static void DifficultyLadder(GameDatabase db, int n)
        {
            var arch = db.Archetype("iron_wall") ?? (db.Archetypes.Length > 0 ? db.Archetypes[0] : null);
            if (arch == null) return;
            Console.WriteLine();
            Console.WriteLine("== AI 难度阶梯（同一流派「" + arch.name + "」，各 " + n + " 局）==");
            var rnd = new Random(31415);
            var baseline = db.Difficulty("normal");
            foreach (var d in db.Difficulties)
            {
                if (d.id == "normal") continue;
                int wNormal = 0, wDiff = 0;
                for (int k = 0; k < n; k++)
                {
                    var m = PlayMatch(db, arch, arch, baseline, d, rnd);
                    if (m.winnerIndex == 0) wNormal++; else if (m.winnerIndex == 1) wDiff++;
                }
                double wr = 100.0 * wDiff / (wNormal + wDiff);
                Console.WriteLine("  普通 vs " + Pad(d.name, 6) + " -> 该难度胜率 " + wr.ToString("0.0") + "%  (普通胜 " + wNormal + " / " + d.name + "胜 " + wDiff + ")");
            }
            Console.WriteLine("  >>> 期望：简单 < 35%，普通 ≈ 50%，困难 > 60%，噩梦 > 75%");
        }

        /// <summary>纯战斗隔离测试：双方阵容完全一致，检验战斗模拟本身是否存在左右偏差。</summary>
        static void PureBattleTest(GameDatabase db, int n)
        {
            Console.WriteLine();
            Console.WriteLine("== 纯战斗隔离测试（双方完全同阵容，" + n + " 局）==");
            string[] comps = { "militia", "archer", "swordsman", "knight" };
            foreach (var comp in comps)
            {
                int leftWin = 0, rightWin = 0, tie = 0;
                double kvSum = 0;
                int unitCount = comp == "militia" ? 8 : (comp == "knight" ? 4 : 6);
                for (int i = 0; i < n; i++)
                {
                    var m = new Match(db, 90000 + i * 37);
                    m.Start();
                    m.sim.ClearUnits();
                    m.players[0].roster.Clear();
                    m.players[1].roster.Clear();
                    for (int k = 0; k < unitCount; k++)
                    {
                        m.players[0].roster.Add(new OwnedUnit { id = comp });
                        m.players[1].roster.Add(new OwnedUnit { id = comp });
                    }
                    m.BeginBattle();
                    float t = 0f;
                    while (!m.sim.BattleOver && t < 40f) { m.sim.Step(1f / 30f); t += 1f / 30f; }
                    float l, rr;
                    m.sim.Settle(out l, out rr);
                    kvSum += l - rr;
                    if (l > rr) leftWin++; else if (rr > l) rightWin++; else tie++;
                }
                double bias = (leftWin + rightWin) > 0 ? 100.0 * Math.Abs(leftWin - rightWin) / (leftWin + rightWin) : 0;
                Console.WriteLine("  " + comp + " x" + unitCount + "x2 : 左胜 " + leftWin + " / 右胜 " + rightWin + " / 平 " + tie +
                    " 偏差 " + bias.ToString("0.0") + "% 平均杀戮值差 " + (kvSum / n).ToString("0.00"));
            }
        }

        static void BatchSim(GameDatabase db, int n) { BatchSim(db, n, false); }

        static void BatchSim(GameDatabase db, int n, bool flipOrder)
        {
            Console.WriteLine();
            Console.WriteLine("== 批量 AI 对战（" + n + " 局" + (flipOrder ? "，交替更新顺序" : "") + "）==");
            var roundCounts = new List<int>();
            int leftWin = 0, rightWin = 0, draw = 0, timeouts = 0;
            float totalBattleTime = 0f;
            int totalBattles = 0;
            var unitWins = new Dictionary<string, int>();
            var relicPicks = new Dictionary<string, int>();
            var rnd = new Random(20260101);

            for (int i = 0; i < n; i++)
            {
                var m = new Match(db, rnd.Next(1, int.MaxValue));
                m.Start();
                var ai0 = new AiController(m, m.players[0]);
                var ai1 = new AiController(m, m.players[1]);
                int guard = 0;
                while (m.phase != MatchPhase.GameOver && guard++ < 200000)
                {
                    float dt = 1f / 30f;
                    // 双方 AI 在同 tick 内顺序执行会引入系统偏差（后行动方占优），
                    // 因此每 tick 随机化更新顺序，模拟真实对局中双方操作天然交错的情况。
                    if (rnd.Next(2) == 0) { ai0.Update(dt); ai1.Update(dt); }
                    else { ai1.Update(dt); ai0.Update(dt); }
                    bool wasBattle = m.phase == MatchPhase.Battle;
                    m.Tick(dt);
                    if (wasBattle && m.phase != MatchPhase.Battle)
                    {
                        totalBattles++;
                        totalBattleTime += m.sim.time;
                        if (m.sim.time >= m.sim.maxSeconds - 0.05f) timeouts++;
                    }
                }
                if (m.phase != MatchPhase.GameOver) { draw++; continue; }
                roundCounts.Add(m.round);
                if (m.winnerIndex == 0) leftWin++;
                else if (m.winnerIndex == 1) rightWin++;
                else draw++;
                for (int pi = 0; pi < 2; pi++)
                    foreach (var rid in m.players[pi].relicIds)
                    {
                        int c; relicPicks.TryGetValue(rid, out c); relicPicks[rid] = c + 1;
                    }
                foreach (var u in m.sim.Units) { int c; unitWins.TryGetValue(u.def.id, out c); unitWins[u.def.id] = c + 1; }
            }

            double avgRounds = 0;
            for (int i = 0; i < roundCounts.Count; i++) avgRounds += roundCounts[i];
            if (roundCounts.Count > 0) avgRounds /= roundCounts.Count;
            roundCounts.Sort();
            int median = roundCounts.Count > 0 ? roundCounts[roundCounts.Count / 2] : 0;
            Console.WriteLine("  对局结果：左胜 " + leftWin + " / 右胜 " + rightWin + " / 平局 " + draw);
            Console.WriteLine("  平均回合数 " + avgRounds.ToString("0.0") + "，中位数 " + median + "，最快 " + (roundCounts.Count > 0 ? roundCounts[0].ToString() : "-") + "，最慢 " + (roundCounts.Count > 0 ? roundCounts[roundCounts.Count - 1].ToString() : "-"));
            Console.WriteLine("  战斗次数 " + totalBattles + "，平均时长 " + (totalBattles > 0 ? (totalBattleTime / totalBattles).ToString("0.0") : "-") + "s，超时率 " + (totalBattles > 0 ? (100.0 * timeouts / totalBattles).ToString("0.0") : "-") + "%");
            double bias = (leftWin + rightWin) > 0 ? 100.0 * Math.Abs(leftWin - rightWin) / (leftWin + rightWin) : 0;
            Console.WriteLine("  左右胜率偏差 " + bias.ToString("0.0") + "%（越接近 0 越平衡）");
            var list = new List<KeyValuePair<string, int>>(relicPicks);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            Console.Write("  热门遗物: ");
            for (int i = 0; i < Math.Min(6, list.Count); i++) Console.Write(list[i].Key + "(" + list[i].Value + ") ");
            Console.WriteLine();
        }
    }
}
