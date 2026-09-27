using System;
using System.Collections.Generic;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>HUD：顶部战况、左右双方信息、底部商店与技能、遗物选择弹窗。</summary>
    public partial class Hud : CanvasLayer
    {
        public Match match;
        public Main main;

        Label roundLabel, phaseLabel, timerLabel, leftLabel, rightLabel, myStats, foeStats, logLabel, hintLabel, deployLabel, myRelics, foeRelics;
        Label topSideL, topSideR, myTitle, foeTitle;
        Panel myPanel, foePanel, logPanel;
        Panel leftHpBar, rightHpBar, leftHpBack, rightHpBack, xpFill, xpBack;
        Button[] shopButtons = new Button[5];
        Button rerollBtn, xpBtn, readyBtn, autoDeployBtn, shopToggleBtn;
        Button[] skillButtons = new Button[4];
        Panel relicPanel;
        Button[] relicButtons = new Button[3];
        Label[] relicTexts = new Label[3];
        Panel gemPanel;
        Button[] gemButtons = new Button[3];
        Button[] gemSkillButtons = new Button[3];
        Label gemInfo;
        Control root;

        public PlayerState Me { get { return match.players[main != null ? main.localIndex : 0]; } }
        public PlayerState Foe { get { return match.players[1 - (main != null ? main.localIndex : 0)]; } }

        static readonly Color Gold = new Color(1f, 0.85f, 0.4f);
        static readonly Color Left = new Color(0.45f, 0.72f, 1f);
        static readonly Color Right = new Color(1f, 0.5f, 0.5f);
        static readonly Color Dim = new Color(0.68f, 0.72f, 0.8f);
        static readonly Color PanelBg = new Color(0.09f, 0.11f, 0.16f, 0.92f);

        public override void _Ready()
        {
            root = new Control();
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(root);

            var top = MakePanel(new Rect2(0, 0, 1600, 92), new Color(0.07f, 0.08f, 0.12f, 0.9f));
            roundLabel = MakeLabel(top, new Rect2(18, 8, 320, 34), "", 26, Gold);
            phaseLabel = MakeLabel(top, new Rect2(18, 46, 320, 26), "", 17, Dim);
            timerLabel = MakeLabel(top, new Rect2(1330, 10, 250, 40), "", 30, Colors.White);
            timerLabel.HorizontalAlignment = HorizontalAlignment.Right;

            topSideL = MakeLabel(top, new Rect2(320, 8, 460, 26), "左侧王国", 16, Left);
            leftHpBack = MakePanel(top, new Rect2(320, 36, 460, 30), new Color(0, 0, 0, 0.55f));
            leftHpBar = MakePanel(top, new Rect2(320, 36, 460, 30), Left);
            leftLabel = MakeLabel(top, new Rect2(320, 34, 460, 32), "", 17, Colors.Black);
            leftLabel.HorizontalAlignment = HorizontalAlignment.Center;
            leftLabel.VerticalAlignment = VerticalAlignment.Center;

            topSideR = MakeLabel(top, new Rect2(880, 8, 460, 26), "右侧王国", 16, Right);
            rightHpBack = MakePanel(top, new Rect2(880, 36, 460, 30), new Color(0, 0, 0, 0.55f));
            rightHpBar = MakePanel(top, new Rect2(880, 36, 460, 30), Right);
            rightLabel = MakeLabel(top, new Rect2(880, 34, 460, 32), "", 17, Colors.Black);
            rightLabel.HorizontalAlignment = HorizontalAlignment.Center;
            rightLabel.VerticalAlignment = VerticalAlignment.Center;

            // ---- 左侧玩家面板 ----
            var lp = MakePanel(new Rect2(14, 104, 300, 432), PanelBg);
            myPanel = lp;
            myTitle = MakeLabel(lp, new Rect2(12, 8, 280, 24), "我方王国", 18, Left);
            myStats = MakeLabel(lp, new Rect2(12, 34, 280, 200), "", 16, Colors.White);
            myStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            xpBack = MakePanel(lp, new Rect2(12, 236, 276, 12), new Color(0, 0, 0, 0.6f));
            xpFill = MakePanel(lp, new Rect2(12, 236, 0, 12), new Color(0.55f, 0.85f, 0.5f));
            myRelics = MakeLabel(lp, new Rect2(12, 258, 280, 166), "", 13, new Color(0.86f, 0.78f, 0.55f));
            myRelics.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            // ---- 右侧敌方面板 ----
            var rp = MakePanel(new Rect2(1286, 104, 300, 232), PanelBg);
            foePanel = rp;
            foeTitle = MakeLabel(rp, new Rect2(12, 8, 280, 24), "敌方王国", 18, Right);
            foeStats = MakeLabel(rp, new Rect2(12, 34, 280, 130), "", 16, Colors.White);
            foeStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            foeRelics = MakeLabel(rp, new Rect2(12, 168, 280, 44), "", 12, new Color(0.95f, 0.72f, 0.72f));
            foeRelics.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            var lg = MakePanel(new Rect2(1286, 348, 300, 188), PanelBg);
            logPanel = lg;
            MakeLabel(lg, new Rect2(12, 6, 280, 22), "战报", 16, Dim);
            logLabel = MakeLabel(lg, new Rect2(12, 30, 276, 150), "", 12, new Color(0.8f, 0.84f, 0.9f));
            logLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            // ---- 底部：商店与操作 ----
            var bottom = MakePanel(new Rect2(0, 640, 1600, 260), new Color(0.07f, 0.08f, 0.12f, 0.94f));
            MakeLabel(bottom, new Rect2(20, 6, 400, 24), "兵种商店（人口不足或金币不足时无法购买）", 15, Dim);
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                shopButtons[i] = MakeButton(bottom, new Rect2(20 + i * 208, 34, 196, 92), "", 15, () => main.OnBuyUnit(idx));
            }
            rerollBtn = MakeButton(bottom, new Rect2(1072, 34, 200, 40), "刷新（2 金）", 16, () => main.OnReroll());
            xpBtn = MakeButton(bottom, new Rect2(1072, 80, 200, 40), "买经验（4 金）", 16, () => main.OnBuyXp());
            readyBtn = MakeButton(bottom, new Rect2(1290, 34, 290, 86), "开始战斗", 22, () => main.OnReady());
            autoDeployBtn = MakeButton(bottom, new Rect2(1072, 126, 200, 30), "自动部署待上阵", 14, () => main.OnAutoDeploy());
            shopToggleBtn = MakeButton(bottom, new Rect2(1290, 126, 290, 30), "宝石商店 / 技能强化", 14, () => main.OnToggleGemShop());
            deployLabel = MakeLabel(bottom, new Rect2(20, 132, 1030, 22), "", 14, Gold);

            MakeLabel(bottom, new Rect2(20, 160, 400, 22), "技能（战斗中点击后，再点战场释放）", 14, Dim);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                skillButtons[i] = MakeButton(bottom, new Rect2(20 + i * 200, 186, 190, 56), "", 15, () => main.OnSkill(idx));
            }
            hintLabel = MakeLabel(bottom, new Rect2(840, 160, 740, 90), "", 15, Dim);
            hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            BuildRelicPanel();
            BuildGemPanel();
        }

        Panel MakePanel(Rect2 rect, Color color)
        {
            return MakePanel(root, rect, color);
        }

        Panel MakePanel(Control parent, Rect2 rect, Color color)
        {
            var p = new Panel();
            var sb = new StyleBoxFlat();
            sb.BgColor = color;
            sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 6;
            p.AddThemeStyleboxOverride("panel", sb);
            p.Position = rect.Position;
            p.Size = rect.Size;
            p.MouseFilter = Control.MouseFilterEnum.Ignore;
            parent.AddChild(p);
            return p;
        }

        Label MakeLabel(Control parent, Rect2 rect, string text, int size, Color color)
        {
            var l = new Label();
            l.Text = text;
            l.Position = rect.Position;
            l.Size = rect.Size;
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            l.MouseFilter = Control.MouseFilterEnum.Ignore;
            parent.AddChild(l);
            return l;
        }

        Button MakeButton(Control parent, Rect2 rect, string text, int size, Action onPress)
        {
            var b = new Button();
            b.Text = text;
            b.Position = rect.Position;
            b.Size = rect.Size;
            b.AddThemeFontSizeOverride("font_size", size);
            b.ClipText = true;
            b.Pressed += onPress;
            parent.AddChild(b);
            return b;
        }

        void BuildRelicPanel()
        {
            relicPanel = MakePanel(new Rect2(300, 200, 1000, 420), new Color(0.10f, 0.09f, 0.16f, 0.97f));
            relicPanel.MouseFilter = Control.MouseFilterEnum.Stop;
            var title = MakeLabel(relicPanel, new Rect2(24, 16, 952, 34), "肉鸽遗物 · 三选一（每 2 回合出现一次）", 24, Gold);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var card = MakePanel(relicPanel, new Rect2(20 + i * 322, 64, 306, 330), new Color(0.14f, 0.15f, 0.22f, 0.95f));
                card.MouseFilter = Control.MouseFilterEnum.Stop;
                relicTexts[i] = MakeLabel(card, new Rect2(14, 12, 278, 250), "", 15, Colors.White);
                relicTexts[i].AutowrapMode = TextServer.AutowrapMode.WordSmart;
                relicButtons[i] = MakeButton(card, new Rect2(14, 272, 278, 44), "选择", 18, () => main.OnPickRelic(idx));
            }
            relicPanel.Visible = false;
        }

        void BuildGemPanel()
        {
            gemPanel = MakePanel(new Rect2(340, 150, 920, 500), new Color(0.08f, 0.11f, 0.14f, 0.97f));
            gemPanel.MouseFilter = Control.MouseFilterEnum.Stop;
            var title = MakeLabel(gemPanel, new Rect2(20, 14, 880, 32), "宝石商店（购买后于下回合开始生效）", 22, new Color(0.6f, 0.9f, 1f));
            title.HorizontalAlignment = HorizontalAlignment.Center;
            gemInfo = MakeLabel(gemPanel, new Rect2(20, 50, 880, 26), "", 16, Gold);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                gemButtons[i] = MakeButton(gemPanel, new Rect2(20 + i * 300, 86, 286, 150), "", 15, () => main.OnBuyGemRelic(idx));
                gemButtons[i].ClipText = false;
            }
            MakeLabel(gemPanel, new Rect2(20, 250, 880, 26), "技能强化（每级提升效果，最高 3 级）", 18, new Color(0.8f, 0.75f, 1f));
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                gemSkillButtons[i] = MakeButton(gemPanel, new Rect2(20 + i * 300, 284, 286, 60), "", 15, () => main.OnUpgradeSkill(idx));
            }
            MakeButton(gemPanel, new Rect2(360, 370, 200, 50), "关闭", 18, () => main.OnToggleGemShop());
            var gemNote = MakeLabel(gemPanel, new Rect2(20, 380, 320, 80), "宝石来源：成长型遗物、放弃遗物（+3）", 14, Dim);
            gemNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            gemPanel.Visible = false;
        }

        public void Refresh()
        {
            if (match == null) return;
            var p0 = Me;
            var p1 = Foe;
            var rcfg = match.db.Balance.round;

            // 客户端（右侧玩家）时镜像两侧信息面板，保证「我方面板」总在自己那一侧
            bool meLeft = (main != null ? main.localIndex : 0) == 0;
            float myX = meLeft ? 14f : 1286f;
            float foeX = meLeft ? 1286f : 14f;
            myPanel.Position = new Vector2(myX, 104);
            foePanel.Position = new Vector2(foeX, 104);
            logPanel.Position = new Vector2(foeX, 348);
            myTitle.Text = (meLeft ? "左侧王国" : "右侧王国") + "（你）";
            foeTitle.Text = meLeft ? "右侧王国" : "左侧王国";
            topSideL.Text = "左侧王国" + (meLeft ? "（你）" : "");
            topSideR.Text = "右侧王国" + (meLeft ? "" : "（你）");

            roundLabel.Text = "第 " + match.round + " 回合";
            string phase = match.phase == MatchPhase.Prep ? "准备阶段" : match.phase == MatchPhase.Battle ? "战斗中" : match.phase == MatchPhase.Settle ? "结算中" : "游戏结束";
            phaseLabel.Text = phase + (match.phase == MatchPhase.Prep ? "  剩余 " + Mathf.Ceil(match.phaseTimer) + "s" : "");
            timerLabel.Text = match.phase == MatchPhase.Battle
                ? (match.sim.maxSeconds - match.sim.time).ToString("0.0") + "s"
                : (winnerText(match));

            float maxHp = Mathf.Max(1f, match.db.Balance.baseHp);
            leftHpBar.Size = new Vector2(460f * Mathf.Clamp(p0.baseHp / maxHp, 0f, 1f), 30);
            rightHpBar.Size = new Vector2(460f * Mathf.Clamp(p1.baseHp / maxHp, 0f, 1f), 30);
            leftLabel.Text = "城邦生命 " + Mathf.Ceil(p0.baseHp) + " / " + (int)maxHp;
            rightLabel.Text = "城邦生命 " + Mathf.Ceil(p1.baseHp) + " / " + (int)maxHp;

            int popCap = match.PopCap(p0);
            int popUsed = match.PopUsed(p0);
            int need = rcfg.XpToNext(p0.level);
            myStats.Text = "金币 " + (int)p0.gold + "   宝石 " + (int)p0.gems + "\n"
                + "等级 " + p0.level + " / " + rcfg.maxLevel + "   经验 " + (int)p0.xp + " / " + need + "\n"
                + "人口 " + popUsed + " / " + popCap + "\n"
                + "存活单位 " + match.sim.AliveCount(Me.Team) + "   杀戮值 " + (int)match.sim.KillValueSum(Me.Team) + "\n"
                + "待部署 " + p0.pendingDeploy.Count + "\n"
                + "连胜 " + p0.winStreak + "  连败 " + p0.lossStreak;
            myRelics.Text = "遗物（" + p0.relicIds.Count + "）：\n" + RelicList(p0, 6);
            float xpRatio = need > 0 ? Mathf.Clamp(p0.xp / need, 0f, 1f) : 1f;
            xpFill.Size = new Vector2(276f * xpRatio, 14);

            foeStats.Text = "金币 " + (int)p1.gold + "   宝石 " + (int)p1.gems + "\n"
                + "等级 " + p1.level + "   人口 " + match.PopUsed(p1) + " / " + match.PopCap(p1) + "\n"
                + "存活单位 " + match.sim.AliveCount(Foe.Team) + "   杀戮值 " + (int)match.sim.KillValueSum(Foe.Team);
            foeRelics.Text = "遗物（" + p1.relicIds.Count + "）：\n" + RelicList(p1, 2);

            var logs = match.log;
            string logText = "";
            int from = Mathf.Max(0, logs.Count - 9);
            for (int i = from; i < logs.Count; i++) logText += "· " + logs[i].text + "\n";
            logLabel.Text = logText;

            for (int i = 0; i < 5; i++)
            {
                var b = shopButtons[i];
                if (i >= p0.shop.Count) { b.Text = "—"; b.Disabled = true; continue; }
                var offer = p0.shop[i];
                var def = match.db.Unit(offer.unitId);
                if (def == null) { b.Text = "—"; b.Disabled = true; continue; }
                int price = match.UnitPrice(p0, def);
                bool canPop = match.PopUsed(p0) + def.pop <= popCap;
                b.Text = "T" + def.tier + " " + def.name + "\n" + price + " 金 · 人口 " + def.pop + (offer.sold ? "\n已购买" : "");
                b.Disabled = offer.sold || p0.gold < price || !canPop || match.phase != MatchPhase.Prep;
            }

            rerollBtn.Disabled = match.phase != MatchPhase.Prep || p0.gold < rcfg.rerollCost;
            xpBtn.Disabled = match.phase != MatchPhase.Prep || p0.gold < rcfg.xpBuyCost || p0.level >= rcfg.maxLevel;
            readyBtn.Disabled = match.phase != MatchPhase.Prep;
            readyBtn.Text = p0.ready ? "已准备（等待对手）" : "开始战斗";
            deployLabel.Text = p0.pendingDeploy.Count > 0
                ? "有待部署单位：" + p0.pendingDeploy.Count + " 个 —— 点击战场自己那半边放置，或点「自动部署待上阵」"
                : "";

            for (int i = 0; i < 4; i++)
            {
                var b = skillButtons[i];
                if (i >= p0.skills.Count) { b.Text = "未解锁"; b.Disabled = true; b.Modulate = new Color(1, 1, 1, 0.4f); continue; }
                string sid = p0.skills[i];
                var sd = match.db.Skill(sid);
                int lv = Mathf.Max(1, p0.SkillLevel(sid));
                bool active = main.selectedSkill == sid;
                b.Text = (sd != null ? sd.name : sid) + " Lv" + lv + (active ? "\n[释放中]" : "");
                b.Modulate = active ? new Color(1f, 0.9f, 0.5f) : Colors.White;
                b.Disabled = match.phase != MatchPhase.Battle;
            }

            hintLabel.Text = match.phase == MatchPhase.Prep
                ? "操作提示：\n· 点击商店购买兵种 → 点击战场" + (meLeft ? "左" : "右") + "半边部署\n· 人口受等级限制（当前 " + popUsed + "/" + popCap + "）；右键点击自己的兵可以出售换人口\n· 每 2 回合三选一遗物，宝石商店的东西下回合生效\n· 快捷键：空格=开始战斗  R=刷新  E=买经验  D=自动部署"
                : "战斗中：点击技能按钮 → 点击战场任意位置释放。\n战后按双方存活单位的杀戮值差值对敌方城邦造成伤害。";

            RefreshRelicPanel();
            RefreshGemPanel();
        }

        string RelicList(PlayerState p, int maxLines)
        {
            if (p.relicIds.Count == 0) return "  暂无\n";
            string s = "";
            int shown = Mathf.Min(maxLines, p.relicIds.Count);
            for (int i = 0; i < shown; i++)
            {
                var rd = match.db.Relic(p.relicIds[i]);
                if (rd != null) s += "  · " + rd.name + "\n";
            }
            if (p.relicIds.Count > shown) s += "  …… 还有 " + (p.relicIds.Count - shown) + " 个\n";
            return s;
        }

        static string winnerText(Match m)
        {
            if (m.phase == MatchPhase.GameOver) return m.winnerIndex >= 0 ? m.players[m.winnerIndex].name + " 获胜" : "平局";
            return "";
        }

        void RefreshRelicPanel()
        {
            var p = Me;
            bool show = !p.relicPicked && p.activeRelicOffers.Count > 0 && match.phase != MatchPhase.GameOver;
            relicPanel.Visible = show;
            if (!show) return;
            for (int i = 0; i < 3; i++)
            {
                if (i >= p.activeRelicOffers.Count) { relicTexts[i].Text = ""; relicButtons[i].Disabled = true; continue; }
                var rd = match.db.Relic(p.activeRelicOffers[i]);
                if (rd == null) continue;
                string cat;
                match.db.CategoryNames.TryGetValue(rd.category, out cat);
                relicTexts[i].Text = "【" + (cat ?? rd.category) + " · " + Rarity(rd.rarity) + "】\n" + rd.name + "\n\n" + rd.desc + "\n\n「" + rd.flavor + "」";
                relicButtons[i].Disabled = false;
            }
        }

        static string Rarity(string r)
        {
            if (r == "epic") return "史诗";
            if (r == "rare") return "稀有";
            return "普通";
        }

        void RefreshGemPanel()
        {
            gemPanel.Visible = main.gemShopOpen;
            if (!main.gemShopOpen) return;
            var p = Me;
            gemInfo.Text = "当前宝石：" + (int)p.gems + "   金币：" + (int)p.gold;
            for (int i = 0; i < 3; i++)
            {
                if (i >= p.gemShop.Count) { gemButtons[i].Text = "—"; gemButtons[i].Disabled = true; continue; }
                var rd = match.db.Relic(p.gemShop[i]);
                if (rd == null) { gemButtons[i].Disabled = true; continue; }
                int cost = match.db.GemCost(rd.rarity);
                gemButtons[i].Text = rd.name + "（" + Rarity(rd.rarity) + "）\n" + cost + " 宝石\n\n" + rd.desc;
                gemButtons[i].Disabled = p.gems < cost;
            }
            for (int i = 0; i < 3; i++)
            {
                if (i >= p.skills.Count) { gemSkillButtons[i].Text = "未解锁"; gemSkillButtons[i].Disabled = true; continue; }
                string sid = p.skills[i];
                var sd = match.db.Skill(sid);
                int lv = Mathf.Max(1, p.SkillLevel(sid));
                if (lv >= 3) { gemSkillButtons[i].Text = (sd != null ? sd.name : sid) + " 已满级"; gemSkillButtons[i].Disabled = true; continue; }
                int cost = 5 + lv * 4;
                gemSkillButtons[i].Text = "强化 " + (sd != null ? sd.name : sid) + " Lv" + lv + " → Lv" + (lv + 1) + "\n" + cost + " 宝石";
                gemSkillButtons[i].Disabled = p.gems < cost;
            }
        }

        public override void _Process(double delta)
        {
            Refresh();
        }
    }
}
