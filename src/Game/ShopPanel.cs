using System;
using System.Collections.Generic;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>
    /// 兵种商店（中世纪魔法风格）：
    /// 卡片 = 层级徽记 + 3D 立绘 + 金币价 + 人口；悬浮出属性卡；点右上 ⓘ 打开可旋转 3D 详情。
    /// 布局：10 列 x 1 行，卡片 100x104，起点 (8,4)，步长 106；翻页与开始战斗在右侧。
    /// </summary>
    public partial class ShopPanel : Control
    {
        public Main main;
        public Match match;

        const int Cols = 10;
        const int CardW = 100, CardH = 104, StepX = 106;

        readonly List<Button> _cards = new List<Button>();
        readonly List<TextureRect> _cardIcons = new List<TextureRect>();
        readonly List<TextureRect> _cardGold = new List<TextureRect>();
        readonly List<TextureRect> _cardPop = new List<TextureRect>();
        readonly List<Label> _cardPrice = new List<Label>();
        readonly List<Label> _cardPopNum = new List<Label>();
        readonly List<Label> _cardTier = new List<Label>();
        readonly List<Button> _cardInfo = new List<Button>();

        Button _prev, _next, _ready;
        Label _pageLabel, _info;
        Panel _tip;
        TextureRect _tipIcon;
        Label _tipTitle, _tipTags, _tipDesc;
        readonly List<TextureRect> _tipStatIcons = new List<TextureRect>();
        readonly List<Label> _tipStatValues = new List<Label>();

        Panel _detail;
        SubViewport _detailVp;
        Node3D _detailSlot;
        Camera3D _detailCam;
        Label _detailName, _detailStats, _detailDesc, _detailUnlock;
        Button _detailBuy;
        string _detailUnit;
        float _spin;

        int _page, _hover = -1;
        public bool forceTooltip, forceDetail;

        static readonly string[] StatIcons = { UiArt.Hp, UiArt.Atk, UiArt.Matk, UiArt.Pdef, UiArt.Mdef, UiArt.Speed, UiArt.AtkSpeed, UiArt.Range, UiArt.Kill };

        public override void _Ready()
        {
            for (int i = 0; i < Cols; i++)
            {
                int idx = i;
                var b = new Button();
                b.Position = new Vector2(8 + i * StepX, 4);
                b.Size = new Vector2(CardW, CardH);
                b.FocusMode = FocusModeEnum.None;
                ApplyCardStyle(b, UiTheme.TierColor[0], true);
                b.Pressed += () => OnCardPressed(idx);
                b.MouseEntered += () => { _hover = idx; };
                b.MouseExited += () => { if (_hover == idx) _hover = -1; };
                AddChild(b);
                _cards.Add(b);

                _cardTier.Add(Lbl(b, new Rect2(5, 1, 30, 18), "", 12, UiTheme.Gold));
                _cardIcons.Add(Tex(b, new Rect2(20, 4, 60, 56), null));
                // 金币与人口改成左右并排（之前上下叠放，看起来像重复了）
                _cardGold.Add(Tex(b, new Rect2(7, 78, 14, 14), UiArt.Get(UiArt.Gold)));
                _cardPrice.Add(Lbl(b, new Rect2(23, 74, 26, 20), "", 15, UiTheme.Gold));
                _cardPop.Add(Tex(b, new Rect2(53, 78, 14, 14), UiArt.Get(UiArt.Pop)));
                _cardPopNum.Add(Lbl(b, new Rect2(69, 74, 26, 20), "", 15, UiTheme.Parchment));

                var info = new Button();
                info.Text = "i";
                info.Position = new Vector2(CardW - 21, 3);
                info.Size = new Vector2(18, 18);
                info.FocusMode = FocusModeEnum.None;
                info.AddThemeFontSizeOverride("font_size", 12);
                info.AddThemeColorOverride("font_color", UiTheme.Arcane);
                info.AddThemeStyleboxOverride("normal", UiTheme.Panel(new Color(0.16f, 0.18f, 0.26f), UiTheme.ArcaneDeep, 1, 9));
                info.AddThemeStyleboxOverride("hover", UiTheme.Panel(new Color(0.24f, 0.30f, 0.44f), UiTheme.Arcane, 2, 9));
                info.Pressed += () => OpenDetail(idx);
                b.AddChild(info);
                _cardInfo.Add(info);
            }

            _prev = IconBtn(new Rect2(1072, 6, 46, 42), UiArt.ArrowL, () => { _page = Math.Max(0, _page - 1); });
            _next = IconBtn(new Rect2(1172, 6, 46, 42), UiArt.ArrowR, () => { _page++; });
            _pageLabel = Lbl(this, new Rect2(1120, 6, 50, 42), "1/1", 16, UiTheme.Parchment, HorizontalAlignment.Center);
            _pageLabel.VerticalAlignment = VerticalAlignment.Center;
            _info = Lbl(this, new Rect2(1072, 54, 152, 52), "", 12, UiTheme.ParchDim);
            _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            _ready = new Button();
            _ready.Position = new Vector2(1236, 4);
            _ready.Size = new Vector2(356, CardH);
            _ready.AddThemeFontSizeOverride("font_size", 24);
            _ready.AddThemeColorOverride("font_color", UiTheme.Parchment);
            _ready.FocusMode = FocusModeEnum.None;
            _ready.AddThemeStyleboxOverride("normal", UiTheme.Panel(new Color(0.16f, 0.26f, 0.20f), UiTheme.Emerald, 3, 12));
            _ready.AddThemeStyleboxOverride("hover", UiTheme.Panel(new Color(0.22f, 0.36f, 0.26f), new Color(0.6f, 1f, 0.7f), 3, 12));
            _ready.AddThemeStyleboxOverride("disabled", UiTheme.Panel(new Color(0.12f, 0.12f, 0.16f), UiTheme.GoldDim, 2, 12));
            _ready.Pressed += () => main.OnReady();
            AddChild(_ready);
            UiTheme.AddCornerRivets(_ready, 356, CardH);

            BuildTooltip();
            BuildDetail();
        }

        static void ApplyCardStyle(Button b, Color tier, bool enabled)
        {
            b.AddThemeStyleboxOverride("normal", UiTheme.Card(tier, enabled ? 0.92f : 0.55f, 2));
            b.AddThemeStyleboxOverride("hover", UiTheme.Card(tier.Lightened(0.35f), 1f, 3));
            b.AddThemeStyleboxOverride("pressed", UiTheme.Card(UiTheme.Gold, 1f, 3));
            b.AddThemeStyleboxOverride("disabled", UiTheme.Card(tier.Darkened(0.5f), 0.5f, 2));
        }

        TextureRect Tex(Control parent, Rect2 rect, Texture2D t)
        {
            var r = new TextureRect();
            // 顺序很重要：先关掉「跟随贴图尺寸」，再赋 Texture，
            // 否则 Size 会被贴图原始尺寸（图标 96x96）撑开，图标会溢出卡片。
            r.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            r.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            r.Texture = t;
            r.Position = rect.Position;
            r.Size = rect.Size;
            r.MouseFilter = MouseFilterEnum.Ignore;
            parent.AddChild(r);
            return r;
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
            l.MouseFilter = MouseFilterEnum.Ignore;
            parent.AddChild(l);
            return l;
        }

        Button IconBtn(Rect2 rect, string iconName, Action onPress)
        {
            var b = new Button();
            b.Position = rect.Position;
            b.Size = rect.Size;
            b.FocusMode = FocusModeEnum.None;
            b.AddThemeStyleboxOverride("normal", UiTheme.Panel(UiTheme.PanelRaised, UiTheme.GoldDim, 2, 8));
            b.AddThemeStyleboxOverride("hover", UiTheme.Panel(UiTheme.PanelRaised.Lightened(0.2f), UiTheme.Gold, 2, 8));
            b.AddThemeStyleboxOverride("disabled", UiTheme.Panel(new Color(0.13f, 0.13f, 0.17f, 0.7f), new Color(0.28f, 0.26f, 0.3f), 2, 8));
            b.Pressed += onPress;
            AddChild(b);
            Tex(b, new Rect2(rect.Size.X * 0.5f - 11, rect.Size.Y * 0.5f - 11, 22, 22), UiArt.Get(iconName));
            return b;
        }

        void BuildTooltip()
        {
            _tip = new Panel();
            _tip.AddThemeStyleboxOverride("panel", UiTheme.Panel(new Color(0.10f, 0.09f, 0.14f, 0.985f), UiTheme.Gold, 2, 12));
            _tip.Size = new Vector2(452, 282);
            _tip.Visible = false;
            _tip.MouseFilter = MouseFilterEnum.Ignore;
            _tip.ZIndex = 60;
            AddChild(_tip);
            _tipTitle = Lbl(_tip, new Rect2(16, 10, 420, 30), "", 20, UiTheme.Gold);
            _tipIcon = Tex(_tip, new Rect2(18, 48, 100, 100), null);
            _tipTags = Lbl(_tip, new Rect2(18, 154, 100, 60), "", 12, UiTheme.ParchDim);
            _tipTags.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            for (int i = 0; i < StatIcons.Length; i++)
            {
                float x = 136 + (i % 2) * 158;
                float y = 50 + (i / 2) * 25;
                _tipStatIcons.Add(Tex(_tip, new Rect2(x, y, 20, 20), UiArt.Get(StatIcons[i])));
                _tipStatValues.Add(Lbl(_tip, new Rect2(x + 25, y - 2, 130, 24), "", 15, UiTheme.Parchment));
            }
            _tipDesc = Lbl(_tip, new Rect2(16, 208, 420, 62), "", 13, UiTheme.ParchDim);
            _tipDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }

        void BuildDetail()
        {
            _detail = new Panel();
            _detail.AddThemeStyleboxOverride("panel", UiTheme.Panel(new Color(0.09f, 0.08f, 0.13f, 0.99f), UiTheme.Gold, 3, 16));
            _detail.Size = new Vector2(900, 452);
            _detail.Visible = false;
            _detail.MouseFilter = MouseFilterEnum.Stop;
            _detail.ZIndex = 70;
            AddChild(_detail);
            UiTheme.AddCornerRivets(_detail, 900, 452, UiTheme.Gold);

            _detailName = Lbl(_detail, new Rect2(26, 14, 700, 36), "", 26, UiTheme.Gold);
            var close = new Button();
            close.Text = "关闭";
            close.Position = new Vector2(778, 14);
            close.Size = new Vector2(98, 36);
            close.AddThemeFontSizeOverride("font_size", 15);
            close.AddThemeColorOverride("font_color", UiTheme.Parchment);
            close.FocusMode = FocusModeEnum.None;
            close.AddThemeStyleboxOverride("normal", UiTheme.Panel(UiTheme.PanelRaised, UiTheme.GoldDim, 2, 8));
            close.AddThemeStyleboxOverride("hover", UiTheme.Panel(UiTheme.PanelRaised.Lightened(0.2f), UiTheme.Gold, 2, 8));
            close.Pressed += () => { _detail.Visible = false; };
            _detail.AddChild(close);

            var vpc = new SubViewportContainer();
            vpc.Position = new Vector2(26, 62);
            vpc.Size = new Vector2(420, 320);
            vpc.Stretch = true;
            vpc.MouseFilter = MouseFilterEnum.Ignore;
            _detail.AddChild(vpc);
            var frame = new Panel();
            frame.AddThemeStyleboxOverride("panel", UiTheme.Panel(new Color(0.12f, 0.11f, 0.17f), UiTheme.GoldDim, 2, 10));
            frame.Position = new Vector2(22, 58);
            frame.Size = new Vector2(428, 328);
            frame.MouseFilter = MouseFilterEnum.Ignore;
            _detail.AddChild(frame);
            _detail.MoveChild(frame, _detail.GetChildCount() - 2);
            _detailVp = new SubViewport();
            _detailVp.Size = new Vector2I(420, 320);
            _detailVp.TransparentBg = true;
            _detailVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            _detailVp.Msaa3D = Viewport.Msaa.Msaa4X;
            vpc.AddChild(_detailVp);

            var w = _detailVp.FindWorld3D();
            w.Environment = new Godot.Environment();
            w.Environment.BackgroundMode = Godot.Environment.BGMode.Color;
            w.Environment.BackgroundColor = new Color(0.10f, 0.10f, 0.16f, 1f);
            w.Environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            w.Environment.AmbientLightColor = new Color(0.68f, 0.72f, 0.86f);
            w.Environment.AmbientLightEnergy = 0.9f;
            w.Environment.TonemapMode = Godot.Environment.ToneMapper.Aces;
            var k = new DirectionalLight3D();
            k.RotationDegrees = new Vector3(-42, 34, 0);
            k.LightEnergy = 2.0f;
            _detailVp.AddChild(k);
            var r = new DirectionalLight3D();
            r.RotationDegrees = new Vector3(-12, -126, 0);
            r.LightEnergy = 1.0f;
            r.LightColor = new Color(0.62f, 0.78f, 1f);
            _detailVp.AddChild(r);
            _detailCam = new Camera3D();
            _detailCam.Fov = 32f;
            _detailVp.AddChild(_detailCam);

            Lbl(_detail, new Rect2(470, 62, 410, 24), "属性", 15, UiTheme.GoldDim);
            _detailStats = Lbl(_detail, new Rect2(470, 90, 410, 220), "", 16, UiTheme.Parchment);
            _detailStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _detailUnlock = Lbl(_detail, new Rect2(470, 312, 410, 26), "", 15, UiTheme.Arcane);
            _detailDesc = Lbl(_detail, new Rect2(470, 340, 410, 56), "", 13, UiTheme.ParchDim);
            _detailDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            _detailBuy = new Button();
            _detailBuy.Position = new Vector2(470, 400);
            _detailBuy.Size = new Vector2(220, 42);
            _detailBuy.AddThemeFontSizeOverride("font_size", 17);
            _detailBuy.AddThemeColorOverride("font_color", UiTheme.Parchment);
            _detailBuy.FocusMode = FocusModeEnum.None;
            _detailBuy.AddThemeStyleboxOverride("normal", UiTheme.Panel(new Color(0.16f, 0.26f, 0.20f), UiTheme.Emerald, 2, 8));
            _detailBuy.AddThemeStyleboxOverride("hover", UiTheme.Panel(new Color(0.22f, 0.36f, 0.26f), new Color(0.6f, 1f, 0.7f), 2, 8));
            _detailBuy.AddThemeStyleboxOverride("disabled", UiTheme.Panel(new Color(0.13f, 0.13f, 0.17f), UiTheme.GoldDim, 2, 8));
            _detailBuy.Pressed += () => { if (_detailUnit != null) main.OnBuyUnit(match.players[main.localIndex].shop.FindIndex(o => o.unitId == _detailUnit)); };
            _detail.AddChild(_detailBuy);
        }

        void OpenDetail(int cardIndex)
        {
            int slot = _page * Cols + cardIndex;
            var p = match.players[main.localIndex];
            if (slot < 0 || slot >= p.shop.Count) return;
            ShowDetail(p.shop[slot].unitId);
        }

        void ShowDetail(string unitId)
        {
            var def = match.db.Unit(unitId);
            if (def == null) return;
            _detailUnit = unitId;
            _detail.Visible = true;
            _detailName.Text = def.name + "    T" + def.tier;

            if (_detailSlot != null) { _detailSlot.QueueFree(); _detailSlot = null; }
            _detailSlot = new Node3D();
            _detailSlot.AddChild(UnitModel.Build(def, null));
            _detailVp.AddChild(_detailSlot);
            float h = UnitModel.ApproxHeight(def);
            float dist = h * 1.8f + 0.6f;
            _detailCam.Position = new Vector3(0, h * 0.55f, dist);
            _detailCam.LookAt(new Vector3(0, h * 0.5f, 0));

            var tn = new List<string>();
            if (def.tags != null) for (int i = 0; i < def.tags.Length; i++)
            {
                string t;
                tn.Add(match.db.TagNames.TryGetValue(def.tags[i], out t) ? t : def.tags[i]);
            }
            var p = match.players[main.localIndex];
            int price = match.UnitPrice(p, def);
            _detailStats.Text =
                "价格 " + price + " 金　　人口 " + def.pop + "　　杀戮值 " + def.killValue + "\n"
                + "生命 " + def.hp + "　　物攻 " + def.atk + "　　魔攻 " + def.matk + "\n"
                + "物防 " + def.pdef + "　　魔防 " + def.mdef + "\n"
                + "移速 " + def.speed + "　　攻速 " + def.atkSpeed.ToString("0.00") + "/s　　射程 " + def.range + "\n"
                + "标签 " + string.Join(" · ", tn);
            int lvNeed = def.unlockLevel > 0 ? def.unlockLevel : (def.tier + 1);
            bool unlocked = p.level >= lvNeed;
            _detailUnlock.Text = unlocked
                ? "解锁等级：Lv " + lvNeed + "（已解锁）"
                : "解锁等级：Lv " + lvNeed + "（当前 Lv " + p.level + "，还需 " + (lvNeed - p.level) + " 级）";
            _detailDesc.Text = def.desc;
            _detailBuy.Text = "购买  " + price + " 金";
        }

        void OnCardPressed(int cardIndex)
        {
            int slot = _page * Cols + cardIndex;
            if (match == null) return;
            var p = match.players[main.localIndex];
            if (slot < 0 || slot >= p.shop.Count) return;
            main.OnBuyUnit(slot);
        }

        public void Refresh()
        {
            if (match == null || main == null) return;
            var p = match.players[main.localIndex];
            int total = p.shop.Count;
            int pages = Math.Max(1, (total + Cols - 1) / Cols);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageLabel.Text = (_page + 1) + " / " + pages;
            _prev.Disabled = _page <= 0;
            _next.Disabled = _page >= pages - 1;

            bool canBuy = match.phase == MatchPhase.Prep;
            int popUsed = match.PopUsed(p), popCap = match.PopCap(p);

            for (int i = 0; i < Cols; i++)
            {
                int slot = _page * Cols + i;
                var b = _cards[i];
                if (slot >= total) { b.Visible = false; continue; }
                var def = match.db.Unit(p.shop[slot].unitId);
                if (def == null) { b.Visible = false; continue; }
                b.Visible = true;
                int price = match.UnitPrice(p, def);
                bool canPop = popUsed + def.pop <= popCap;
                bool afford = p.gold >= price;
                bool ok = canBuy && afford && canPop;

                var tc = UiTheme.TierColor[Mathf.Clamp(def.tier - 1, 0, 4)];
                ApplyCardStyle(b, tc, ok);
                _cardIcons[i].Texture = UnitPortrait.Get(def.id);
                _cardPrice[i].Text = price.ToString();
                _cardPopNum[i].Text = def.pop.ToString();
                _cardTier[i].Text = "T" + def.tier;
                _cardTier[i].AddThemeColorOverride("font_color", tc);
                b.Disabled = !ok;
                float a = ok ? 1f : 0.45f;
                _cardIcons[i].Modulate = new Color(1, 1, 1, a);
                _cardPrice[i].Modulate = new Color(1, 1, 1, a);
                _cardPopNum[i].Modulate = new Color(1, 1, 1, a);
                _cardGold[i].Modulate = new Color(1, 1, 1, a);
                _cardPop[i].Modulate = new Color(1, 1, 1, a);
                _cardTier[i].Modulate = new Color(1, 1, 1, a);
                _cardInfo[i].Visible = true;
            }

            _info.Text = "兵种 " + total + " 种\n人口 " + popUsed + " / " + popCap;
            _ready.Disabled = !canBuy;
            _ready.Text = p.ready ? "已就绪\n等待对手" : "开始战斗";

            if (forceDetail && !_detail.Visible && p.shop.Count > 3) ShowDetail(p.shop[Math.Min(6, p.shop.Count - 1)].unitId);
            RefreshTooltip(p);
            RefreshDetail(p);
        }

        void RefreshTooltip(PlayerState p)
        {
            int hover = forceTooltip ? 3 : _hover;
            bool show = hover >= 0 && match.phase != MatchPhase.GameOver && !_detail.Visible;
            int slot = _page * Cols + hover;
            if (show && (slot < 0 || slot >= p.shop.Count)) show = false;
            _tip.Visible = show;
            if (!show) return;
            var def = match.db.Unit(p.shop[slot].unitId);
            if (def == null) { _tip.Visible = false; return; }
            int price = match.UnitPrice(p, def);
            var tn = new List<string>();
            if (def.tags != null) for (int i = 0; i < def.tags.Length; i++)
            {
                string t;
                tn.Add(match.db.TagNames.TryGetValue(def.tags[i], out t) ? t : def.tags[i]);
            }
            _tipTitle.Text = def.name + "    T" + def.tier + "    " + price + "金 / " + def.pop + "人口";
            _tipIcon.Texture = UnitPortrait.Get(def.id);
            _tipTags.Text = string.Join(" · ", tn);
            string[] vals = {
                ((int)def.hp).ToString(), def.atk.ToString(), def.matk.ToString(),
                def.pdef.ToString(), def.mdef.ToString(), def.speed.ToString(),
                def.atkSpeed.ToString("0.00"), def.range.ToString(), def.killValue.ToString(),
            };
            for (int i = 0; i < vals.Length; i++) _tipStatValues[i].Text = vals[i];
            int lvNeed = def.unlockLevel > 0 ? def.unlockLevel : (def.tier + 1);
            _tipDesc.Text = def.desc + "\n解锁等级 Lv " + lvNeed;
        }

        void RefreshDetail(PlayerState p)
        {
            if (!_detail.Visible || _detailUnit == null) return;
            var def = match.db.Unit(_detailUnit);
            if (def == null) { _detail.Visible = false; return; }
            int price = match.UnitPrice(p, def);
            bool ok = match.phase == MatchPhase.Prep && p.gold >= price
                && match.PopUsed(p) + def.pop <= match.PopCap(p);
            _detailBuy.Disabled = !ok;
            _detailBuy.Text = "购买  " + price + " 金" + (ok ? "" : "（金币或人口不足）");
        }

        public override void _Process(double delta)
        {
            Refresh();
            if (_detail.Visible)
            {
                var vpSize = GetViewportRect().Size;
                _detail.Position = new Vector2((vpSize.X - _detail.Size.X) * 0.5f, (vpSize.Y - _detail.Size.Y) * 0.5f) - GlobalPosition;
                if (_detailSlot != null)
                {
                    _spin += (float)delta * 0.7f;
                    _detailSlot.Rotation = new Vector3(0, _spin, 0);
                }
            }
            if (_tip.Visible)
            {
                var g = forceTooltip ? new Vector2(520, 300) : GetGlobalMousePosition();
                var want = g + new Vector2(26, 20);
                var vp = GetViewportRect().Size;
                if (want.X + _tip.Size.X > vp.X) want.X = g.X - _tip.Size.X - 22;
                if (want.Y + _tip.Size.Y > vp.Y) want.Y = Mathf.Max(4, vp.Y - _tip.Size.Y - 4);
                _tip.Position = want - GlobalPosition;
            }
        }
    }
}
