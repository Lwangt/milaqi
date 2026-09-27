using System;
using System.Collections.Generic;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>
    /// HUD：顶部战况 + 双方资源面板（全部使用小图标表示）+ 底部兵种商店。
    /// </summary>
    public partial class Hud : CanvasLayer
    {
        public Match match;
        public Main main;
        public ShopPanel shop;

        Label roundLabel, phaseLabel, timerLabel, leftHpText, rightHpText, hintLabel, logLabel;
        Label myTitle, foeTitle, topSideL, topSideR;
        Label gGold, gGem, gLevel, gXp, gPop, gHp, gKill, gStreak, gRelics;
        Label eGold, eGem, eLevel, ePop, eKill, eRelics;
        Panel leftHpBar, rightHpBar, xpFill, myPanel, foePanel, logPanel;
        Button[] skillButtons = new Button[4];
        Panel relicPanel;
        Button[] relicButtons = new Button[3];
        Label[] relicTexts = new Label[3];
        Panel gemPanel;
        Button[] gemButtons = new Button[3];
        Button[] gemSkillButtons = new Button[3];
        Label gemInfo;
        Control root;

        static readonly Color Gold = new Color(1f, 0.85f, 0.4f);
        static readonly Color Left = new Color(0.45f, 0.72f, 1f);
        static readonly Color Right = new Color(1f, 0.5f, 0.5f);
        static readonly Color Dim = new Color(0.66f, 0.72f, 0.82f);
        static readonly Color Txt = new Color(0.92f, 0.94f, 0.98f);
        static readonly Color PanelBg = new Color(0.09f, 0.11f, 0.16f, 0.92f);

        public PlayerState Me { get { return match.players[main != null ? main.localIndex : 0]; } }
        public PlayerState Foe { get { return match.players[1 - (main != null ? main.localIndex : 0)]; } }

        public override void _Ready()
        {
            root = new Control();
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(root);

            BuildTop();
            BuildSidePanels();
            BuildBottom();
            BuildRelicPanel();
            BuildGemPanel();
        }

        // ---------------------------------------------------------------- 构件
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

        TextureRect MakeIcon(Control parent, Rect2 rect, string icon)
        {
            var t = new TextureRect();
            t.Texture = UiArt.Get(icon);
            t.Position = rect.Position;
            t.Size = rect.Size;
            // 关键：默认 ExpandMode=KeepSize 会把控件最小尺寸顶成贴图尺寸，导致图标撑大压住旁边的文字
            t.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            t.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            t.MouseFilter = Control.MouseFilterEnum.Ignore;
            parent.AddChild(t);
            return t;
        }

        /// <summary>一行：小图标 + 数字/文本</summary>
        Label IconRow(Control parent, float x, float y, string icon, float w = 150f, int fontSize = 16, Color? color = null)
        {
            MakeIcon(parent, new Rect2(x, y, 18, 18), icon);
            return MakeLabel(parent, new Rect2(x + 22, y - 3, w, 24), "", fontSize, color ?? Txt);
        }

        Button MakeButton(Control parent, Rect2 rect, string text, int size, Action onPress)
        {
            var b = new Button();
            b.Text = text;
            b.Position = rect.Position;
            b.Size = rect.Size;
            b.AddThemeFontSizeOverride("font_size", size);
            b.ClipText = true;
            b.FocusMode = Control.FocusModeEnum.None;
            b.Pressed += onPress;
            parent.AddChild(b);
            return b;
        }

        // ---------------------------------------------------------------- 顶部
        void BuildTop()
        {
            var top = MakePanel(root, new Rect2(0, 0, 1600, 92), new Color(0.07f, 0.08f, 0.12f, 0.92f));
            roundLabel = MakeLabel(top, new Rect2(18, 8, 300, 34), "", 26, Gold);
            phaseLabel = MakeLabel(top, new Rect2(18, 46, 300, 26), "", 17, Dim);
            timerLabel = MakeLabel(top, new Rect2(1330, 10, 250, 40), "", 30, Colors.White);
            timerLabel.HorizontalAlignment = HorizontalAlignment.Right;

            topSideL = MakeLabel(top, new Rect2(320, 8, 460, 24), "", 15, Left);
            MakePanel(top, new Rect2(320, 34, 460, 30), new Color(0, 0, 0, 0.55f));
            leftHpBar = MakePanel(top, new Rect2(320, 34, 460, 30), Left);
            MakeIcon(top, new Rect2(326, 39, 20, 20), UiArt.Hp);
            leftHpText = MakeLabel(top, new Rect2(350, 34, 430, 30), "", 16, Colors.Black);
            leftHpText.VerticalAlignment = VerticalAlignment.Center;

            topSideR = MakeLabel(top, new Rect2(880, 8, 460, 24), "", 15, Right);
            MakePanel(top, new Rect2(880, 34, 460, 30), new Color(0, 0, 0, 0.55f));
            rightHpBar = MakePanel(top, new Rect2(880, 34, 460, 30), Right);
            MakeIcon(top, new Rect2(886, 39, 20, 20), UiArt.Hp);
            rightHpText = MakeLabel(top, new Rect2(910, 34, 430, 30), "", 16, Colors.Black);
            rightHpText.VerticalAlignment = VerticalAlignment.Center;
        }

        // ---------------------------------------------------------------- 左右信息面板
        void BuildSidePanels()
        {
            myPanel = MakePanel(root, new Rect2(14, 104, 300, 430), PanelBg);
            myTitle = MakeLabel(myPanel, new Rect2(12, 8, 276, 24), "", 18, Left);

            gGold = IconRow(myPanel, 12, 40, UiArt.Gold);
            gGem = IconRow(myPanel, 158, 40, UiArt.Gem);
            gLevel = IconRow(myPanel, 12, 68, UiArt.Level);
            gXp = IconRow(myPanel, 158, 68, UiArt.Xp);
            gPop = IconRow(myPanel, 12, 96, UiArt.Pop, 276f);
            gHp = IconRow(myPanel, 12, 124, UiArt.Hp);
            gKill = IconRow(myPanel, 158, 124, UiArt.Kill);
            gStreak = IconRow(myPanel, 12, 152, UiArt.Flame, 276f, 16, Dim);
            MakePanel(myPanel, new Rect2(12, 182, 276, 12), new Color(0, 0, 0, 0.6f));
            xpFill = MakePanel(myPanel, new Rect2(12, 182, 0, 12), new Color(0.55f, 0.85f, 0.5f));
            gRelics = MakeLabel(myPanel, new Rect2(12, 202, 276, 220), "", 13, new Color(0.88f, 0.8f, 0.58f));
            gRelics.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            foePanel = MakePanel(root, new Rect2(1286, 104, 300, 236), PanelBg);
            foeTitle = MakeLabel(foePanel, new Rect2(12, 8, 276, 24), "", 18, Right);
            eGold = IconRow(foePanel, 12, 40, UiArt.Gold);
            eGem = IconRow(foePanel, 158, 40, UiArt.Gem);
            eLevel = IconRow(foePanel, 12, 68, UiArt.Level);
            ePop = IconRow(foePanel, 158, 68, UiArt.Pop);
            eKill = IconRow(foePanel, 12, 96, UiArt.Kill);
            eRelics = MakeLabel(foePanel, new Rect2(12, 128, 276, 100), "", 13, new Color(0.95f, 0.72f, 0.72f));
            eRelics.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            logPanel = MakePanel(root, new Rect2(1286, 352, 300, 184), PanelBg);
            MakeLabel(logPanel, new Rect2(12, 6, 276, 22), "战报", 15, Dim);
            logLabel = MakeLabel(logPanel, new Rect2(12, 30, 276, 146), "", 12, new Color(0.8f, 0.84f, 0.9f));
            logLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }

        // ---------------------------------------------------------------- 底部
        void BuildBottom()
        {
            MakePanel(root, new Rect2(0, 640, 1600, 260), new Color(0.07f, 0.08f, 0.12f, 0.95f));

            bool tipDebug = false, detDebug = false;
            foreach (var a in OS.GetCmdlineArgs()) { if (a == "--tipdebug") tipDebug = true; if (a == "--detaildebug") detDebug = true; }
            foreach (var a in OS.GetCmdlineUserArgs()) { if (a == "--tipdebug") tipDebug = true; if (a == "--detaildebug") detDebug = true; }
            shop = new ShopPanel();
            shop.forceTooltip = tipDebug;
            shop.forceDetail = detDebug;
            shop.main = main;
            shop.Position = new Vector2(0, 646);
            shop.Size = new Vector2(1600, 112);
            root.AddChild(shop);

            BuildQueue();

            MakeIcon(root, new Rect2(20, 812, 18, 18), UiArt.Matk);
            MakeLabel(root, new Rect2(42, 806, 260, 24), "技能", 14, Dim);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                skillButtons[i] = MakeButton(root, new Rect2(20 + i * 200, 834, 190, 50), "", 15, () => main.OnSkill(idx));
            }
            hintLabel = MakeLabel(root, new Rect2(840, 812, 740, 82), "", 13, Dim);
            hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }

        // ---------------------------------------------------------------- 待出战队列
        const int QueueChips = 16;
        Panel queuePanel;
        readonly List<Button> queueChips = new List<Button>();
        readonly List<TextureRect> queueIcons = new List<TextureRect>();
        readonly List<Label> queuePops = new List<Label>();
        Label queueInfo;

        void BuildQueue()
        {
            queuePanel = MakePanel(root, new Rect2(14, 752, 1088, 52), PanelBg);
            MakeLabel(queuePanel, new Rect2(8, 15, 44, 24), "部队", 14, Dim);
            for (int i = 0; i < QueueChips; i++)
            {
                int idx = i;
                var b = new Button();
                b.Position = new Vector2(52 + i * 48, 4);
                b.Size = new Vector2(44, 44);
                b.FocusMode = Control.FocusModeEnum.None;
                var sb = new StyleBoxFlat();
                sb.BgColor = new Color(0.15f, 0.17f, 0.24f, 0.9f);
                sb.BorderColor = new Color(0.36f, 0.44f, 0.6f);
                sb.BorderWidthTop = sb.BorderWidthBottom = sb.BorderWidthLeft = sb.BorderWidthRight = 2;
                sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 7;
                b.AddThemeStyleboxOverride("normal", sb);
                var hov = (StyleBoxFlat)sb.Duplicate();
                hov.BorderColor = new Color(1f, 0.88f, 0.5f);
                b.AddThemeStyleboxOverride("hover", hov);
                b.Pressed += () => main.OnQueueChip(idx);
                queuePanel.AddChild(b);
                queueChips.Add(b);
                queueIcons.Add(MakeIcon(b, new Rect2(2, 1, 40, 40), null));
                // 人口数字加深色底，避免和立绘糊在一起
                var bg = new Panel();
                var bsb = new StyleBoxFlat();
                bsb.BgColor = new Color(0f, 0f, 0f, 0.72f);
                bsb.CornerRadiusTopLeft = bsb.CornerRadiusTopRight = bsb.CornerRadiusBottomLeft = bsb.CornerRadiusBottomRight = 6;
                bg.AddThemeStyleboxOverride("panel", bsb);
                bg.Position = new Vector2(26, 27);
                bg.Size = new Vector2(16, 15);
                bg.MouseFilter = Control.MouseFilterEnum.Ignore;
                b.AddChild(bg);
                queuePops.Add(MakeLabel(b, new Rect2(26, 25, 16, 18), "", 11, Gold));
            }
            queueInfo = MakeLabel(queuePanel, new Rect2(824, 6, 256, 42), "", 11, Dim);
            queueInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            MakeButton(root, new Rect2(1296, 754, 140, 24), "自动布阵", 13, () => main.OnAutoDeploy());
            MakeButton(root, new Rect2(1296, 782, 140, 24), "撤回全部", 13, () => main.OnUnplaceAll());
            MakeButton(root, new Rect2(1444, 754, 140, 50), "取消选中", 13, () => main.OnQueueSelect(null));
        }

        void RefreshQueue()
        {
            if (match == null || main == null) return;
            var p = Me;
            int placed = 0, pending = 0;
            for (int i = 0; i < QueueChips; i++)
            {
                var b = queueChips[i];
                if (i >= p.roster.Count) { b.Visible = false; continue; }
                var o = p.roster[i];
                var def = match.db.Unit(o.id);
                b.Visible = true;
                queueIcons[i].Texture = UnitPortrait.Get(o.id);
                queuePops[i].Text = def != null ? def.pop.ToString() : "";
                if (o.placed) placed++; else pending++;

                bool sel = !o.placed && main.queueSelected == o.id;
                // 已上场：正常亮度 + 绿色描边；待部署：半透明 + 黄色描边；选中：高亮放大
                b.Modulate = o.placed ? Colors.White : new Color(1f, 1f, 1f, sel ? 1f : 0.55f);
                b.Scale = sel ? new Vector2(1.1f, 1.1f) : Vector2.One;
                b.AddThemeStyleboxOverride("normal", ChipStyle(o.placed, sel));
                b.AddThemeStyleboxOverride("hover", ChipStyle(o.placed, true));
                b.TooltipText = (def != null ? def.name : o.id) + (o.placed ? "（已上场，点击撤回）" : "（待部署，点击选中）");
            }
            queueInfo.Text = "已上场 " + placed + "  待部署 " + pending + "  人口 " + match.PopUsed(p) + "/" + match.PopCap(p)
                + (pending > 0 ? "\n选中后点战场放下" : "\n点已上场的可撤回");
        }

        static StyleBoxFlat ChipStyle(bool placed, bool highlight)
        {
            var sb = new StyleBoxFlat();
            sb.BgColor = new Color(0.15f, 0.17f, 0.24f, placed ? 0.95f : 0.6f);
            sb.BorderColor = highlight ? new Color(1f, 0.88f, 0.5f)
                                      : (placed ? new Color(0.38f, 0.85f, 0.5f) : new Color(0.55f, 0.5f, 0.35f));
            sb.BorderWidthTop = sb.BorderWidthBottom = sb.BorderWidthLeft = sb.BorderWidthRight = highlight ? 3 : 2;
            sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 7;
            return sb;
        }

        // ---------------------------------------------------------------- 遗物 / 宝石弹窗
        void BuildRelicPanel()
        {
            relicPanel = MakePanel(root, new Rect2(300, 200, 1000, 420), new Color(0.10f, 0.09f, 0.16f, 0.97f));
            relicPanel.MouseFilter = Control.MouseFilterEnum.Stop;
            var title = MakeLabel(relicPanel, new Rect2(24, 16, 952, 34), "遗物 · 三选一", 24, Gold);
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
            gemPanel = MakePanel(root, new Rect2(340, 150, 920, 500), new Color(0.08f, 0.11f, 0.14f, 0.97f));
            gemPanel.MouseFilter = Control.MouseFilterEnum.Stop;
            var title = MakeLabel(gemPanel, new Rect2(20, 14, 880, 32), "宝石商店（下回合生效）", 22, new Color(0.6f, 0.9f, 1f));
            title.HorizontalAlignment = HorizontalAlignment.Center;
            gemInfo = MakeLabel(gemPanel, new Rect2(20, 50, 880, 26), "", 16, Gold);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                gemButtons[i] = MakeButton(gemPanel, new Rect2(20 + i * 300, 86, 286, 150), "", 15, () => main.OnBuyGemRelic(idx));
                gemButtons[i].ClipText = false;
            }
            MakeLabel(gemPanel, new Rect2(20, 250, 880, 26), "技能强化（最高 3 级）", 18, new Color(0.8f, 0.75f, 1f));
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                gemSkillButtons[i] = MakeButton(gemPanel, new Rect2(20 + i * 300, 284, 286, 60), "", 15, () => main.OnUpgradeSkill(idx));
            }
            MakeButton(gemPanel, new Rect2(360, 370, 200, 50), "关闭", 18, () => main.OnToggleGemShop());
            var note = MakeLabel(gemPanel, new Rect2(20, 380, 320, 80), "宝石来源：成长型遗物、放弃遗物（+3）", 14, Dim);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            gemPanel.Visible = false;
        }

        static string Rarity(string r)
        {
            if (r == "epic") return "史诗";
            if (r == "rare") return "稀有";
            return "普通";
        }

        string Line(string icon, string text) { return text; }

        // ---------------------------------------------------------------- 刷新
        public void Refresh()
        {
            if (match == null || main == null) return;
            var p0 = Me;
            var p1 = Foe;
            var rcfg = match.db.Balance.round;

            bool meLeft = main.localIndex == 0;
            float myX = meLeft ? 14f : 1286f;
            float foeX = meLeft ? 1286f : 14f;
            myPanel.Position = new Vector2(myX, 104);
            foePanel.Position = new Vector2(foeX, 104);
            logPanel.Position = new Vector2(foeX, 352);
            myTitle.Text = meLeft ? "左侧王国" : "右侧王国";
            foeTitle.Text = meLeft ? "右侧王国" : "左侧王国";
            topSideL.Text = "左侧王国" + (meLeft ? "（你）" : "");
            topSideR.Text = "右侧王国" + (meLeft ? "" : "（你）");

            roundLabel.Text = "第 " + match.round + " 回合";
            string phase = match.phase == MatchPhase.Prep ? "准备" : match.phase == MatchPhase.Battle ? "战斗中" : match.phase == MatchPhase.Settle ? "结算" : "结束";
            phaseLabel.Text = phase;
            timerLabel.Text = match.phase == MatchPhase.Battle
                ? (match.sim.maxSeconds - match.sim.time).ToString("0.0") + "s"
                : (match.phase == MatchPhase.Prep ? Mathf.Ceil(match.phaseTimer) + "s" : (match.phase == MatchPhase.GameOver ? (match.winnerIndex >= 0 ? match.players[match.winnerIndex].name + " 胜" : "平局") : ""));

            float maxHp = Mathf.Max(1f, match.db.Balance.baseHp);
            leftHpBar.Size = new Vector2(460f * Mathf.Clamp(p0.baseHp / maxHp, 0f, 1f), 30);
            rightHpBar.Size = new Vector2(460f * Mathf.Clamp(p1.baseHp / maxHp, 0f, 1f), 30);
            leftHpText.Text = "   " + Mathf.Ceil(p0.baseHp) + " / " + (int)maxHp;
            rightHpText.Text = "   " + Mathf.Ceil(p1.baseHp) + " / " + (int)maxHp;

            int popCap = match.PopCap(p0), popUsed = match.PopUsed(p0);
            int need = rcfg.XpToNext(p0.level);
            gGold.Text = ((int)p0.gold).ToString();
            gGem.Text = ((int)p0.gems).ToString();
            gLevel.Text = p0.level.ToString();
            gXp.Text = (int)p0.xp + " / " + need;
            gPop.Text = popUsed + " / " + popCap;
            gHp.Text = Mathf.Ceil(p0.baseHp) + "";
            gKill.Text = ((int)match.sim.KillValueSum(Me.Team)).ToString();
            gStreak.Text = p0.winStreak > 0 ? ("连胜 " + p0.winStreak) : (p0.lossStreak > 0 ? ("连败 " + p0.lossStreak) : "—");
            xpFill.Size = new Vector2(276f * (need > 0 ? Mathf.Clamp(p0.xp / need, 0f, 1f) : 1f), 12);
            gRelics.Text = "遗物（" + p0.relicIds.Count + "）\n" + RelicList(p0, 7);

            eGold.Text = ((int)p1.gold).ToString();
            eGem.Text = ((int)p1.gems).ToString();
            eLevel.Text = p1.level.ToString();
            ePop.Text = match.PopUsed(p1) + " / " + match.PopCap(p1);
            eKill.Text = ((int)match.sim.KillValueSum(Foe.Team)).ToString();
            eRelics.Text = "遗物（" + p1.relicIds.Count + "）\n" + RelicList(p1, 4);

            var logs = match.log;
            string logText = "";
            int from = Mathf.Max(0, logs.Count - 8);
            for (int i = from; i < logs.Count; i++) logText += "· " + logs[i].text + "\n";
            logLabel.Text = logText;

            if (shop != null) { shop.match = match; }
            RefreshQueue();

            for (int i = 0; i < 4; i++)
            {
                var b = skillButtons[i];
                if (i >= p0.skills.Count) { b.Text = "未解锁"; b.Disabled = true; b.Modulate = new Color(1, 1, 1, 0.35f); continue; }
                string sid = p0.skills[i];
                var sd = match.db.Skill(sid);
                int lv = Mathf.Max(1, p0.SkillLevel(sid));
                bool active = main.selectedSkill == sid;
                b.Text = (sd != null ? sd.name : sid) + " Lv" + lv + (active ? "  ◀释放中" : "");
                b.Modulate = active ? new Color(1f, 0.9f, 0.5f) : Colors.White;
                b.Disabled = match.phase != MatchPhase.Battle;
            }

            hintLabel.Text = match.phase == MatchPhase.Prep
                ? "① 点兵种卡片购买（一次一只，进入待出战队列）\n② 在队列里选中一个，再点战场自己那半边放下\n③ 点「开始战斗」派兵出击\n右键点战场上的兵可出售（返 60% 金币）"
                : "战斗中：点技能 → 点战场释放\n回合结束按双方存活单位的杀戮值差值扣敌方城邦生命，然后清空战场";

            RefreshRelicPanel();
            RefreshGemPanel();
        }

        string RelicList(PlayerState p, int maxLines)
        {
            if (p.relicIds.Count == 0) return "  暂无";
            string s = "";
            int shown = Mathf.Min(maxLines, p.relicIds.Count);
            for (int i = 0; i < shown; i++)
            {
                var rd = match.db.Relic(p.relicIds[i]);
                if (rd != null) s += "  · " + rd.name + "\n";
            }
            if (p.relicIds.Count > shown) s += "  …… 还有 " + (p.relicIds.Count - shown) + " 个";
            return s;
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

        void RefreshGemPanel()
        {
            gemPanel.Visible = main.gemShopOpen;
            if (!main.gemShopOpen) return;
            var p = Me;
            gemInfo.Text = "宝石 " + (int)p.gems + "   金币 " + (int)p.gold;
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
                gemSkillButtons[i].Text = "强化 " + (sd != null ? sd.name : sid) + " Lv" + lv + " → " + (lv + 1) + "\n" + cost + " 宝石";
                gemSkillButtons[i].Disabled = p.gems < cost;
            }
        }

        public override void _Process(double delta)
        {
            Refresh();
        }
    }
}
