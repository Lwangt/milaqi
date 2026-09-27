using System;
using System.Collections.Generic;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>
    /// HUD 全局布局（1600x900 画布坐标，均已核算不重叠）：
    ///   顶部 (0,0,1600,104)  回合徽章 / 双方城邦 / 倒计时
    ///   左侧 (10,116,300,428) 我方资源与遗物
    ///   右侧 (1270,116,320,232) 敌方 + (1270,356,320,188) 战报
    ///   战场 (320,186,960,288)
    ///   底部 (0,640,1600,260) 商店卡片(648) / 待出战队列(758) / 技能(820)
    /// </summary>
    public partial class Hud : CanvasLayer
    {
        public Match match;
        public Main main;
        public ShopPanel shop;

        Label roundLabel, roundSuffix, phaseLabel, timerLabel, leftHpText, rightHpText, hintLabel, logLabel;
        Label myTitle, foeTitle, topSideL, topSideR;
        Label gGold, gGem, gLevel, gXp, gPop, gHp, gKill, gStreak, gRelics;
        Button xpButton, gemButton;
        Label eGold, eGem, eLevel, ePop, eKill, eRelics;
        Panel leftHpBar, rightHpBar, xpFill, myPanel, foePanel, logPanel;
        Button[] skillButtons = new Button[4];
        Panel relicPanel;
        Button[] relicButtons = new Button[3];
        Label[] relicTexts = new Label[3];
        Panel gemPanel;
        Button[] gemButtons = new Button[4];
        Button[] gemSkillButtons = new Button[3];
        Button gemReroll;
        Label gemInfo;
        Control root;

        const int QueueChips = 16;
        Panel queuePanel;
        readonly List<Button> queueChips = new List<Button>();
        readonly List<TextureRect> queueIcons = new List<TextureRect>();
        readonly List<Label> queuePops = new List<Label>();
        Label queueInfo;

        /// <summary>
        /// 该屏幕坐标是否被 HUD 的交互区域挡住。
        /// BattleView 在 _Input 里判定战场点击时用它来区分「点战场」和「点界面」。
        /// </summary>
        public bool BlocksPoint(Vector2 pos)
        {
            if (relicPanel != null && relicPanel.Visible && new Rect2(relicPanel.GlobalPosition, relicPanel.Size).HasPoint(pos)) return true;
            if (gemPanel != null && gemPanel.Visible && new Rect2(gemPanel.GlobalPosition, gemPanel.Size).HasPoint(pos)) return true;
            if (new Rect2(0, 0, 1600, 104).HasPoint(pos)) return true;      // 顶部信息条
            if (new Rect2(0, 634, 1600, 266).HasPoint(pos)) return true;    // 底部商店/队列/技能
            if (myPanel != null && new Rect2(myPanel.GlobalPosition, myPanel.Size).HasPoint(pos)) return true;
            if (foePanel != null && new Rect2(foePanel.GlobalPosition, foePanel.Size).HasPoint(pos)) return true;
            if (logPanel != null && new Rect2(logPanel.GlobalPosition, logPanel.Size).HasPoint(pos)) return true;
            return false;
        }

        public PlayerState Me { get { return match.players[main != null ? main.localIndex : 0]; } }
        public PlayerState Foe { get { return match.players[1 - (main != null ? main.localIndex : 0)]; } }

        public override void _Ready()
        {
            root = new Control();
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            // 必须设为 Ignore：Control 默认是 Stop，这个全屏 root 会把整屏鼠标事件都吃掉，
            // 导致战场收不到点击（技能点不出来、队列也放不下去）。子节点仍然正常响应。
            root.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(root);
            BuildBackdrop();
            BuildTop();
            BuildSidePanels();
            BuildBottom();
            BuildRelicPanel();
            BuildGemPanel();
        }

        // ---------------------------------------------------------------- 构件
        Panel PanelAt(Control parent, Rect2 rect, StyleBoxFlat sb, bool rivets = false)
        {
            var p = new Panel();
            p.AddThemeStyleboxOverride("panel", sb);
            p.Position = rect.Position;
            p.Size = rect.Size;
            p.MouseFilter = Control.MouseFilterEnum.Ignore;
            parent.AddChild(p);
            if (rivets) UiTheme.AddCornerRivets(p, rect.Size.X, rect.Size.Y);
            return p;
        }

        Label Lbl(Control parent, Rect2 rect, string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var l = new Label();
            l.Text = text;
            l.Position = rect.Position;
            l.Size = rect.Size;
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            l.HorizontalAlignment = align;
            l.MouseFilter = Control.MouseFilterEnum.Ignore;
            parent.AddChild(l);
            return l;
        }

        TextureRect Icon(Control parent, Rect2 rect, string icon)
        {
            var t = new TextureRect();
            if (icon != null) t.Texture = UiArt.Get(icon);
            t.Position = rect.Position;
            t.Size = rect.Size;
            t.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            t.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            t.MouseFilter = Control.MouseFilterEnum.Ignore;
            parent.AddChild(t);
            return t;
        }

        Label IconRow(Control parent, float x, float y, string icon, float w = 140f, int fontSize = 16, Color? color = null)
        {
            Icon(parent, new Rect2(x, y, 20, 20), icon);
            return Lbl(parent, new Rect2(x + 26, y - 3, w, 26), "", fontSize, color ?? UiTheme.Parchment);
        }

        Button Btn(Control parent, Rect2 rect, string text, int size, Action onPress, bool accent = false)
        {
            var b = new Button();
            b.Text = text;
            b.Position = rect.Position;
            b.Size = rect.Size;
            b.AddThemeFontSizeOverride("font_size", size);
            b.AddThemeColorOverride("font_color", UiTheme.Parchment);
            b.AddThemeColorOverride("font_hover_color", Colors.White);
            b.FocusMode = Control.FocusModeEnum.None;
            b.ClipText = true;
            var baseCol = accent ? UiTheme.ArcaneDeep : UiTheme.PanelRaised;
            b.AddThemeStyleboxOverride("normal", UiTheme.Panel(baseCol, UiTheme.GoldDim, 2, 8));
            b.AddThemeStyleboxOverride("hover", UiTheme.Panel(baseCol.Lightened(0.18f), UiTheme.Gold, 2, 8));
            b.AddThemeStyleboxOverride("pressed", UiTheme.Panel(baseCol.Darkened(0.2f), UiTheme.Gold, 3, 8));
            b.AddThemeStyleboxOverride("disabled", UiTheme.Panel(new Color(0.13f, 0.13f, 0.17f, 0.7f), new Color(0.3f, 0.28f, 0.32f), 2, 8));
            b.Pressed += onPress;
            parent.AddChild(b);
            return b;
        }

        // ---------------------------------------------------------------- 背景
        /// <summary>
        /// 背景必须放在更低的 CanvasLayer 上。
        /// Hud 本身是 CanvasLayer，永远画在战场的 CanvasItem 之上——
        /// 之前把全屏背景直接加在 root 里，结果把整个战场盖住了。
        /// </summary>
        void BuildBackdrop()
        {
            var bgLayer = new CanvasLayer();
            bgLayer.Layer = -1;
            AddChild(bgLayer);

            var bg = new ColorRect();
            bg.Color = UiTheme.Ink;
            bg.Position = Vector2.Zero;
            bg.Size = new Vector2(1600, 900);
            bg.MouseFilter = Control.MouseFilterEnum.Ignore;
            bgLayer.AddChild(bg);

            var top = new ColorRect();
            top.Color = new Color(0.15f, 0.12f, 0.22f, 0.85f);
            top.Position = Vector2.Zero;
            top.Size = new Vector2(1600, 104);
            top.MouseFilter = Control.MouseFilterEnum.Ignore;
            bgLayer.AddChild(top);

            var bot = new ColorRect();
            bot.Color = new Color(0.09f, 0.08f, 0.13f, 0.95f);
            bot.Position = new Vector2(0, 634);
            bot.Size = new Vector2(1600, 266);
            bot.MouseFilter = Control.MouseFilterEnum.Ignore;
            bgLayer.AddChild(bot);
        }

        // ---------------------------------------------------------------- 顶部
        void BuildTop()
        {
            var top = PanelAt(root, new Rect2(0, 0, 1600, 104), UiTheme.Panel(new Color(0.10f, 0.09f, 0.14f, 0.97f), UiTheme.GoldDim, 0, 0));
            PanelAt(root, new Rect2(0, 102, 1600, 3), UiTheme.Panel(UiTheme.GoldDim, UiTheme.GoldDim, 0, 0));

            // 回合徽章
            var badge = PanelAt(top, new Rect2(14, 12, 104, 80), UiTheme.Panel(new Color(0.20f, 0.16f, 0.24f), UiTheme.Gold, 3, 12), true);
            roundLabel = Lbl(badge, new Rect2(0, 6, 104, 46), "1", 38, UiTheme.Gold, HorizontalAlignment.Center);
            roundSuffix = Lbl(badge, new Rect2(0, 50, 104, 22), "回合", 13, UiTheme.ParchDim, HorizontalAlignment.Center);

            phaseLabel = Lbl(top, new Rect2(128, 14, 170, 30), "", 19, UiTheme.Arcane);
            timerLabel = Lbl(top, new Rect2(128, 44, 170, 44), "", 34, UiTheme.Parchment);

            // 我方城邦
            topSideL = Lbl(top, new Rect2(310, 12, 300, 24), "", 15, UiTheme.TeamLeft);
            PanelAt(top, new Rect2(310, 40, 440, 36), UiTheme.Panel(new Color(0, 0, 0, 0.55f), UiTheme.TeamLeft, 2, 8));
            leftHpBar = PanelAt(top, new Rect2(310, 40, 440, 36), UiTheme.Panel(UiTheme.TeamLeft, UiTheme.TeamLeft, 0, 8));
            Icon(top, new Rect2(318, 46, 24, 24), UiArt.Hp);
            leftHpText = Lbl(top, new Rect2(348, 40, 396, 36), "", 17, Colors.Black, HorizontalAlignment.Left);
            leftHpText.VerticalAlignment = VerticalAlignment.Center;

            // 敌方城邦
            topSideR = Lbl(top, new Rect2(860, 12, 300, 24), "", 15, UiTheme.TeamRight);
            PanelAt(top, new Rect2(860, 40, 440, 36), UiTheme.Panel(new Color(0, 0, 0, 0.55f), UiTheme.TeamRight, 2, 8));
            rightHpBar = PanelAt(top, new Rect2(860, 40, 440, 36), UiTheme.Panel(UiTheme.TeamRight, UiTheme.TeamRight, 0, 8));
            Icon(top, new Rect2(868, 46, 24, 24), UiArt.Hp);
            rightHpText = Lbl(top, new Rect2(898, 40, 396, 36), "", 17, Colors.Black, HorizontalAlignment.Left);
            rightHpText.VerticalAlignment = VerticalAlignment.Center;

            // 右上角魔法纹章
            var crest = PanelAt(top, new Rect2(1320, 16, 264, 72), UiTheme.Panel(new Color(0.16f, 0.14f, 0.22f), UiTheme.Shadow, 2, 10), true);
            Lbl(crest, new Rect2(8, 8, 248, 24), "维斯特兰纪元", 14, UiTheme.Shadow, HorizontalAlignment.Center);
            Lbl(crest, new Rect2(8, 34, 248, 28), "", 12, UiTheme.ParchDim, HorizontalAlignment.Center);
        }

        // ---------------------------------------------------------------- 侧栏
        void BuildSidePanels()
        {
            myPanel = PanelAt(root, new Rect2(10, 116, 300, 428), UiTheme.Stone(), true);
            myTitle = Lbl(myPanel, new Rect2(14, 8, 272, 26), "", 18, UiTheme.TeamLeft);
            PanelAt(myPanel, new Rect2(14, 38, 272, 2), UiTheme.Panel(UiTheme.GoldDim, UiTheme.GoldDim, 0, 0));

            gGold = IconRow(myPanel, 14, 50, UiArt.Gold);
            gGem = IconRow(myPanel, 158, 50, UiArt.Gem);
            gLevel = IconRow(myPanel, 14, 80, UiArt.Level);
            gXp = IconRow(myPanel, 158, 80, UiArt.Xp);
            gPop = IconRow(myPanel, 14, 110, UiArt.Pop, 272f);
            gHp = IconRow(myPanel, 14, 140, UiArt.Hp);
            gKill = IconRow(myPanel, 158, 140, UiArt.Kill);
            gStreak = IconRow(myPanel, 14, 170, UiArt.Flame, 272f, 15, UiTheme.ParchDim);

            PanelAt(myPanel, new Rect2(14, 200, 272, 12), UiTheme.Panel(new Color(0, 0, 0, 0.6f), UiTheme.GoldDim, 0, 6));
            xpFill = PanelAt(myPanel, new Rect2(14, 200, 0, 12), UiTheme.Panel(UiTheme.Emerald, UiTheme.Emerald, 0, 6));

            Lbl(myPanel, new Rect2(14, 220, 272, 22), "遗物", 14, UiTheme.GoldDim);
            gRelics = Lbl(myPanel, new Rect2(14, 244, 272, 104), "", 13, new Color(0.86f, 0.80f, 0.62f));
            gRelics.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            // 买经验 / 宝石商店：之前只有快捷键 E 和 G，没有按钮入口
            xpButton = Btn(myPanel, new Rect2(14, 356, 272, 32), "", 14, () => main.OnBuyXp(), true);
            gemButton = Btn(myPanel, new Rect2(14, 394, 272, 32), "", 14, () => main.OnToggleGemShop());

            foePanel = PanelAt(root, new Rect2(1270, 116, 320, 232), UiTheme.Stone(), true);
            foeTitle = Lbl(foePanel, new Rect2(14, 8, 292, 26), "", 18, UiTheme.TeamRight);
            PanelAt(foePanel, new Rect2(14, 38, 292, 2), UiTheme.Panel(UiTheme.GoldDim, UiTheme.GoldDim, 0, 0));
            eGold = IconRow(foePanel, 14, 50, UiArt.Gold);
            eGem = IconRow(foePanel, 168, 50, UiArt.Gem);
            eLevel = IconRow(foePanel, 14, 80, UiArt.Level);
            ePop = IconRow(foePanel, 168, 80, UiArt.Pop);
            eKill = IconRow(foePanel, 14, 110, UiArt.Kill);
            Lbl(foePanel, new Rect2(14, 140, 292, 22), "遗物", 14, UiTheme.GoldDim);
            eRelics = Lbl(foePanel, new Rect2(14, 162, 292, 62), "", 12, new Color(0.92f, 0.70f, 0.70f));
            eRelics.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            logPanel = PanelAt(root, new Rect2(1270, 356, 320, 188), UiTheme.Stone(), true);
            Lbl(logPanel, new Rect2(14, 8, 292, 22), "战报", 14, UiTheme.GoldDim);
            logLabel = Lbl(logPanel, new Rect2(14, 32, 292, 146), "", 12, new Color(0.84f, 0.84f, 0.88f));
            logLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }

        // ---------------------------------------------------------------- 底部
        void BuildBottom()
        {
            PanelAt(root, new Rect2(0, 634, 1600, 266), UiTheme.Panel(new Color(0.10f, 0.09f, 0.14f, 0.97f), UiTheme.GoldDim, 0, 0));
            PanelAt(root, new Rect2(0, 634, 1600, 3), UiTheme.Panel(UiTheme.GoldDim, UiTheme.GoldDim, 0, 0));

            shop = new ShopPanel();
            shop.main = main;
            bool tipDebug = false, detDebug = false;
            foreach (var a in OS.GetCmdlineArgs()) { if (a == "--tipdebug") tipDebug = true; if (a == "--detaildebug") detDebug = true; }
            foreach (var a in OS.GetCmdlineUserArgs()) { if (a == "--tipdebug") tipDebug = true; if (a == "--detaildebug") detDebug = true; }
            shop.forceTooltip = tipDebug;
            shop.forceDetail = detDebug;
            shop.Position = new Vector2(0, 644);
            shop.Size = new Vector2(1600, 112);
            root.AddChild(shop);

            BuildQueue();

            Icon(root, new Rect2(12, 826, 20, 20), UiArt.Matk);
            Lbl(root, new Rect2(38, 820, 200, 24), "技能", 14, UiTheme.GoldDim);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                skillButtons[i] = Btn(root, new Rect2(10 + i * 198, 846, 190, 44), "", 15, () => main.OnSkill(idx), true);
            }
            hintLabel = Lbl(root, new Rect2(812, 820, 778, 72), "", 12, UiTheme.ParchDim);
            hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }

        void BuildQueue()
        {
            queuePanel = PanelAt(root, new Rect2(8, 758, 1092, 54), UiTheme.Panel(new Color(0.15f, 0.14f, 0.19f, 0.96f), UiTheme.GoldDim, 2, 10), false);
            Lbl(queuePanel, new Rect2(10, 17, 40, 24), "部队", 14, UiTheme.GoldDim);
            for (int i = 0; i < QueueChips; i++)
            {
                int idx = i;
                var b = new Button();
                b.Position = new Vector2(54 + i * 46, 5);
                b.Size = new Vector2(44, 44);
                b.FocusMode = Control.FocusModeEnum.None;
                b.AddThemeStyleboxOverride("normal", ChipStyle(true, false));
                b.AddThemeStyleboxOverride("hover", ChipStyle(true, true));
                b.Pressed += () => main.OnQueueChip(idx);
                queuePanel.AddChild(b);
                queueChips.Add(b);
                queueIcons.Add(Icon(b, new Rect2(2, 1, 40, 40), null));
                var bg = new Panel();
                var bsb = new StyleBoxFlat();
                bsb.BgColor = new Color(0f, 0f, 0f, 0.75f);
                bsb.CornerRadiusTopLeft = bsb.CornerRadiusTopRight = bsb.CornerRadiusBottomLeft = bsb.CornerRadiusBottomRight = 5;
                bg.AddThemeStyleboxOverride("panel", bsb);
                bg.Position = new Vector2(27, 28);
                bg.Size = new Vector2(15, 14);
                bg.MouseFilter = Control.MouseFilterEnum.Ignore;
                b.AddChild(bg);
                queuePops.Add(Lbl(b, new Rect2(27, 26, 15, 16), "", 10, UiTheme.Gold, HorizontalAlignment.Center));
            }
            queueInfo = Lbl(queuePanel, new Rect2(792, 7, 290, 40), "", 11, UiTheme.ParchDim);
            queueInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            Btn(root, new Rect2(1108, 758, 132, 25), "自动布阵", 13, () => main.OnAutoDeploy());
            Btn(root, new Rect2(1108, 787, 132, 25), "撤回全部", 13, () => main.OnUnplaceAll());
            Btn(root, new Rect2(1246, 758, 132, 54), "取消选中", 13, () => main.OnQueueSelect(null));
        }

        static StyleBoxFlat ChipStyle(bool placed, bool highlight)
        {
            var border = highlight ? UiTheme.Gold : (placed ? UiTheme.Emerald : new Color(0.66f, 0.56f, 0.30f));
            return UiTheme.Panel(new Color(0.19f, 0.17f, 0.23f, placed ? 0.98f : 0.6f), border, highlight ? 3 : 2, 8);
        }

        // ---------------------------------------------------------------- 遗物 / 宝石弹窗
        void BuildRelicPanel()
        {
            relicPanel = PanelAt(root, new Rect2(300, 190, 1000, 440), UiTheme.Panel(new Color(0.13f, 0.11f, 0.18f, 0.985f), UiTheme.Shadow, 3, 16), true);
            relicPanel.MouseFilter = Control.MouseFilterEnum.Stop;
            var title = Lbl(relicPanel, new Rect2(24, 16, 952, 36), "遗物 · 三选一", 26, UiTheme.Gold, HorizontalAlignment.Center);
            Lbl(relicPanel, new Rect2(24, 50, 952, 22), "每 2 回合出现一次；放弃可以换 3 颗宝石", 13, UiTheme.ParchDim, HorizontalAlignment.Center);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var card = PanelAt(relicPanel, new Rect2(20 + i * 322, 80, 306, 336), UiTheme.Panel(UiTheme.PanelRaised, UiTheme.GoldDim, 2, 12), true);
                card.MouseFilter = Control.MouseFilterEnum.Stop;
                relicTexts[i] = Lbl(card, new Rect2(16, 14, 274, 250), "", 15, UiTheme.Parchment);
                relicTexts[i].AutowrapMode = TextServer.AutowrapMode.WordSmart;
                relicButtons[i] = Btn(card, new Rect2(16, 278, 274, 44), "选择", 18, () => main.OnPickRelic(idx), true);
            }
            relicPanel.Visible = false;
        }

        void BuildGemPanel()
        {
            gemPanel = PanelAt(root, new Rect2(340, 130, 920, 540), UiTheme.Panel(new Color(0.10f, 0.13f, 0.18f, 0.985f), UiTheme.Arcane, 3, 16), true);
            gemPanel.MouseFilter = Control.MouseFilterEnum.Stop;
            var title = Lbl(gemPanel, new Rect2(20, 14, 880, 34), "宝石商店", 24, UiTheme.Arcane, HorizontalAlignment.Center);
            Lbl(gemPanel, new Rect2(20, 48, 880, 22), "购买后于下回合开始生效", 13, UiTheme.ParchDim, HorizontalAlignment.Center);
            gemInfo = Lbl(gemPanel, new Rect2(20, 72, 880, 26), "", 17, UiTheme.Gold, HorizontalAlignment.Center);
            // 4 个遗物货位
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                gemButtons[i] = Btn(gemPanel, new Rect2(18 + i * 222, 104, 210, 152), "", 13, () => main.OnBuyGemRelic(idx));
                gemButtons[i].ClipText = false;
            }
            // 刷新货架
            gemReroll = Btn(gemPanel, new Rect2(18, 264, 210, 34), "", 14, () => main.OnRerollGemShop());
            Lbl(gemPanel, new Rect2(240, 264, 660, 34), "商店每回合自动补货；花 2 宝石可以立刻换一批", 13, UiTheme.ParchDim);
            Lbl(gemPanel, new Rect2(20, 306, 880, 24), "技能强化（最高 3 级）", 17, UiTheme.Shadow);
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                gemSkillButtons[i] = Btn(gemPanel, new Rect2(18 + i * 298, 336, 286, 56), "", 14, () => main.OnUpgradeSkill(idx));
            }
            Btn(gemPanel, new Rect2(360, 408, 200, 46), "关闭", 18, () => main.OnToggleGemShop(), true);
            var note = Lbl(gemPanel, new Rect2(18, 406, 330, 90), "宝石来源：每回合发放、每 3 回合额外 +1、放弃遗物 +5、成长型遗物", 13, UiTheme.ParchDim);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            gemPanel.Visible = false;
        }

        static string Rarity(string r)
        {
            if (r == "epic") return "史诗";
            if (r == "rare") return "稀有";
            return "普通";
        }

        // ---------------------------------------------------------------- 刷新
        public void Refresh()
        {
            if (match == null || main == null) return;
            var p0 = Me;
            var p1 = Foe;
            var rcfg = match.db.Balance.round;

            bool meLeft = main.localIndex == 0;
            float myX = meLeft ? 10f : 1270f;
            float foeX = meLeft ? 1270f : 10f;
            myPanel.Position = new Vector2(myX, 116);
            foePanel.Position = new Vector2(foeX, 116);
            logPanel.Position = new Vector2(foeX, 356);
            myTitle.Text = meLeft ? "左侧城邦" : "右侧城邦";
            foeTitle.Text = meLeft ? "右侧城邦" : "左侧城邦";
            topSideL.Text = "左侧城邦" + (meLeft ? "（我方）" : "（敌方）");
            topSideR.Text = "右侧城邦" + (meLeft ? "（敌方）" : "（我方）");

            roundLabel.Text = match.round.ToString();
            string phase = match.phase == MatchPhase.Prep ? "备战阶段" : match.phase == MatchPhase.Battle ? "交战中" : match.phase == MatchPhase.Settle ? "结算中" : "战局终了";
            phaseLabel.Text = phase;
            timerLabel.Text = match.phase == MatchPhase.Battle
                ? (match.sim.maxSeconds - match.sim.time).ToString("0.0")
                : (match.phase == MatchPhase.Prep ? Mathf.Ceil(match.phaseTimer).ToString()
                    : (match.phase == MatchPhase.GameOver ? (match.winnerIndex >= 0 ? "胜" : "和") : ""));

            float maxHp = Mathf.Max(1f, match.db.Balance.baseHp);
            leftHpBar.Size = new Vector2(440f * Mathf.Clamp(p0.baseHp / maxHp, 0f, 1f), 36);
            rightHpBar.Size = new Vector2(440f * Mathf.Clamp(p1.baseHp / maxHp, 0f, 1f), 36);
            leftHpText.Text = "   " + Mathf.Ceil(p0.baseHp) + " / " + (int)maxHp;
            rightHpText.Text = "   " + Mathf.Ceil(p1.baseHp) + " / " + (int)maxHp;

            int popCap = match.PopCap(p0), popUsed = match.PopUsed(p0);
            int need = rcfg.XpToNext(p0.level);
            gGold.Text = ((int)p0.gold).ToString();
            gGem.Text = ((int)p0.gems).ToString();
            gLevel.Text = "Lv " + p0.level + " / " + rcfg.maxLevel;
            gXp.Text = (int)p0.xp + " / " + need;
            gPop.Text = popUsed + " / " + popCap;
            gHp.Text = Mathf.Ceil(p0.baseHp) + "";
            gKill.Text = ((int)match.sim.KillValueSum(Me.Team)).ToString();
            gStreak.Text = p0.winStreak > 0 ? ("连胜 " + p0.winStreak) : (p0.lossStreak > 0 ? ("连败 " + p0.lossStreak) : "势均力敌");
            xpFill.Size = new Vector2(272f * (need > 0 ? Mathf.Clamp(p0.xp / need, 0f, 1f) : 1f), 12);
            gRelics.Text = RelicList(p0, 5);

            // 买经验：等级未满且金币够时可点
            int xpCost = rcfg.xpBuyCost;
            bool canXp = match.phase == MatchPhase.Prep && p0.level < rcfg.maxLevel && p0.gold >= xpCost;
            xpButton.Disabled = !canXp;
            xpButton.Text = p0.level >= rcfg.maxLevel
                ? "已满级 Lv " + rcfg.maxLevel
                : "买经验  " + xpCost + " 金 → +" + rcfg.xpPerBuy + " 经验   (E)";
            xpButton.Modulate = canXp ? Colors.White : new Color(1f, 1f, 1f, 0.5f);

            // 宝石商店：随时可开，按钮上直接显示宝石数
            gemButton.Text = "宝石商店   宝石 " + (int)p0.gems + "   (G)";
            gemButton.Modulate = p0.gems > 0 ? Colors.White : new Color(1f, 1f, 1f, 0.6f);

            eGold.Text = ((int)p1.gold).ToString();
            eGem.Text = ((int)p1.gems).ToString();
            eLevel.Text = "Lv " + p1.level;
            ePop.Text = match.PopUsed(p1) + " / " + match.PopCap(p1);
            eKill.Text = ((int)match.sim.KillValueSum(Foe.Team)).ToString();
            eRelics.Text = RelicList(p1, 3);

            var logs = match.log;
            string logText = "";
            int from = Mathf.Max(0, logs.Count - 8);
            for (int i = from; i < logs.Count; i++) logText += "· " + logs[i].text + "\n";
            logLabel.Text = logText;

            if (shop != null) shop.match = match;
            RefreshQueue();

            for (int i = 0; i < 4; i++)
            {
                var b = skillButtons[i];
                if (i >= p0.skills.Count) { b.Text = "未解锁"; b.Disabled = true; b.Modulate = new Color(1, 1, 1, 0.4f); continue; }
                string sid = p0.skills[i];
                var sd = match.db.Skill(sid);
                int lv = Mathf.Max(1, p0.SkillLevel(sid));
                bool active = main.selectedSkill == sid;
                b.Text = (sd != null ? sd.name : sid) + "  Lv" + lv + (active ? "  ◀ 释放中" : "");
                b.Modulate = active ? new Color(1f, 0.92f, 0.55f) : Colors.White;
                b.Disabled = match.phase != MatchPhase.Battle;
            }

            hintLabel.Text = match.phase == MatchPhase.Prep
                ? "① 点上方兵种卡片购买（一次一只，进入待出战队列）\n② 队列里选中一个再点战场放下（不点也会自动布阵）  右键点场上的兵可出售\n③ 点「开始战斗」派兵出击\n★ 回合结束部队全部清空 —— 本轮有多少金币就打多少兵，金币与等级跨回合保留"
                : "战斗中：点技能 → 点战场释放\n回合结束按双方存活单位的杀戮值差值扣敌方城邦生命，然后清空战场与部队";

            RefreshRelicPanel();
            RefreshGemPanel();
        }

        string RelicList(PlayerState p, int maxLines)
        {
            if (p.relicIds.Count == 0) return "暂无";
            string s = "";
            int shown = Mathf.Min(maxLines, p.relicIds.Count);
            for (int i = 0; i < shown; i++)
            {
                var rd = match.db.Relic(p.relicIds[i]);
                if (rd != null) s += "· " + rd.name + "\n";
            }
            if (p.relicIds.Count > shown) s += "…… 还有 " + (p.relicIds.Count - shown) + " 个";
            return s;
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
                b.Modulate = o.placed ? Colors.White : new Color(1f, 1f, 1f, sel ? 1f : 0.62f);
                b.Scale = sel ? new Vector2(1.1f, 1.1f) : Vector2.One;
                b.AddThemeStyleboxOverride("normal", ChipStyle(o.placed, sel));
                b.TooltipText = (def != null ? def.name : o.id) + (o.placed ? "（已上场，点击撤回）" : "（待部署，点击选中）");
            }
            queueInfo.Text = "已上场 " + placed + "   待部署 " + pending + "   人口 " + match.PopUsed(p) + "/" + match.PopCap(p)
                + (pending > 0 ? "\n选中一个，再点战场放下" : "\n本回合部队 · 战后清空");
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
            gemInfo.Text = "宝石 " + (int)p.gems + "　　金币 " + (int)p.gold;
            for (int i = 0; i < 4; i++)
            {
                if (i >= p.gemShop.Count) { gemButtons[i].Text = "—"; gemButtons[i].Disabled = true; continue; }
                var rd = match.db.Relic(p.gemShop[i]);
                if (rd == null) { gemButtons[i].Disabled = true; continue; }
                int cost = match.db.GemCost(rd.rarity);
                gemButtons[i].Text = rd.name + "（" + Rarity(rd.rarity) + "）\n" + cost + " 宝石\n\n" + rd.desc;
                gemButtons[i].Disabled = p.gems < cost;
                gemButtons[i].Modulate = p.gems >= cost ? Colors.White : new Color(1f, 1f, 1f, 0.6f);
            }
            if (gemReroll != null)
            {
                gemReroll.Text = "刷新货架  2 宝石";
                gemReroll.Disabled = p.gems < Match.GemShopRerollCost;
            }
            for (int i = 0; i < 3; i++)
            {
                if (i >= p.skills.Count) { gemSkillButtons[i].Text = "未解锁"; gemSkillButtons[i].Disabled = true; continue; }
                string sid = p.skills[i];
                var sd = match.db.Skill(sid);
                int lv = Mathf.Max(1, p.SkillLevel(sid));
                if (lv >= 3) { gemSkillButtons[i].Text = (sd != null ? sd.name : sid) + " 已满级"; gemSkillButtons[i].Disabled = true; continue; }
                int cost = 5 + lv * 4;
                gemSkillButtons[i].Text = "强化 " + (sd != null ? sd.name : sid) + "  Lv" + lv + " → " + (lv + 1) + "\n" + cost + " 宝石";
                gemSkillButtons[i].Disabled = p.gems < cost;
            }
        }

        public override void _Process(double delta)
        {
            Refresh();
        }
    }
}
