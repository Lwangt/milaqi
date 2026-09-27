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
            Check(m.players[0].gold > 0 && m.players[0].level == 3, "初始经济与等级正确", ref fails);
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
