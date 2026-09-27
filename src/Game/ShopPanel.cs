using System;
using System.Collections.Generic;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>
    /// 兵种商店：全部已解锁兵种铺成卡片、左右翻页。
    /// 卡片 = 3D 兵种立绘 + 金币价 + 人口；悬浮出属性卡；点右上角 ⓘ 打开 3D 详情。
    /// </summary>
    public partial class ShopPanel : Control
    {
        public Main main;
        public Match match;

        const int Cols = 11;
        const int CardW = 96, CardH = 104, StepX = 100;

        readonly List<Button> _cards = new List<Button>();
        readonly List<TextureRect> _cardIcons = new List<TextureRect>();
        readonly List<TextureRect> _cardGold = new List<TextureRect>();
        readonly List<TextureRect> _cardPop = new List<TextureRect>();
        readonly List<Label> _cardPrice = new List<Label>();
        readonly List<Label> _cardPopNum = new List<Label>();
        readonly List<Button> _cardInfo = new List<Button>();

        Button _prev, _next, _ready;
        Label _pageLabel, _info;
        Panel _tip;
        TextureRect _tipIcon;
        Label _tipTitle, _tipTags, _tipDesc;
        readonly List<TextureRect> _tipStatIcons = new List<TextureRect>();
        readonly List<Label> _tipStatValues = new List<Label>();

        // 3D 详情
        Panel _detail;
        SubViewport _detailVp;
        Node3D _detailSlot;
        Camera3D _detailCam;
        Label _detailName, _detailStats, _detailDesc;
        Button _detailBuy, _detailClose;
        string _detailUnit;
        float _spin;

        int _page;
        int _hover = -1;
        public bool forceTooltip;
        public bool forceDetail;

        static readonly Color[] TierColors = {
            new Color(0.62f, 0.66f, 0.72f),
            new Color(0.42f, 0.82f, 0.5f),
            new Color(0.38f, 0.66f, 1f),
            new Color(0.72f, 0.5f, 1f),
            new Color(1f, 0.72f, 0.28f),
        };
        static readonly string[] StatIcons = { UiArt.Hp, UiArt.Atk, UiArt.Matk, UiArt.Pdef, UiArt.Mdef, UiArt.Speed, UiArt.AtkSpeed, UiArt.Range, UiArt.Kill };

        static readonly Color Txt = new Color(0.92f, 0.94f, 0.98f);
        static readonly Color Gold = new Color(1f, 0.85f, 0.4f);
        static readonly Color Dim = new Color(0.62f, 0.68f, 0.78f);

        static StyleBoxFlat CardStyle(Color border, float bgAlpha, int borderW, float radius = 9f)
        {
            var sb = new StyleBoxFlat();
            sb.BgColor = new Color(0.13f, 0.15f, 0.21f, bgAlpha);
            sb.BorderColor = border;
            sb.BorderWidthTop = sb.BorderWidthBottom = sb.BorderWidthLeft = sb.BorderWidthRight = borderW;
            sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = (int)radius;
            return sb;
        }

        public override void _Ready()
        {
            for (int i = 0; i < Cols; i++)
            {
                int idx = i;
                var b = new Button();
                b.Position = new Vector2(i * StepX, 0);
                b.Size = new Vector2(CardW, CardH);
                b.FocusMode = FocusModeEnum.None;
                b.AddThemeStyleboxOverride("normal", CardStyle(new Color(0.42f, 0.5f, 0.64f), 0.85f, 2));
                b.AddThemeStyleboxOverride("hover", CardStyle(new Color(1f, 0.88f, 0.5f), 1f, 3));
                b.AddThemeStyleboxOverride("pressed", CardStyle(new Color(1f, 0.78f, 0.3f), 1f, 3));
                b.AddThemeStyleboxOverride("disabled", CardStyle(new Color(0.25f, 0.28f, 0.34f), 0.6f, 2));
                b.Pressed += () => OnCardPressed(idx);
                b.MouseEntered += () => { _hover = idx; };
                b.MouseExited += () => { if (_hover == idx) _hover = -1; };
                AddChild(b);
                _cards.Add(b);

                _cardIcons.Add(Tex(b, new Rect2(18, 1, 60, 56), null));
                _cardGold.Add(Tex(b, new Rect2(10, 61, 15, 15), UiArt.Get(UiArt.Gold)));
                _cardPrice.Add(Lbl(b, new Rect2(28, 57, 60, 22), "", 15, Gold, HorizontalAlignment.Left));
                _cardPop.Add(Tex(b, new Rect2(10, 82, 15, 15), UiArt.Get(UiArt.Pop)));
                _cardPopNum.Add(Lbl(b, new Rect2(28, 78, 60, 22), "", 15, Txt, HorizontalAlignment.Left));

                var info = new Button();
                info.Text = "i";
                info.Position = new Vector2(CardW - 21, 3);
                info.Size = new Vector2(18, 18);
                info.FocusMode = FocusModeEnum.None;
                info.AddThemeFontSizeOverride("font_size", 12);
                info.AddThemeStyleboxOverride("normal", CardStyle(new Color(0.4f, 0.55f, 0.75f), 0.75f, 1, 9f));
                info.AddThemeStyleboxOverride("hover", CardStyle(new Color(0.55f, 0.72f, 1f), 1f, 2, 9f));
                info.Pressed += () => OpenDetail(idx);
                b.AddChild(info);
                _cardInfo.Add(info);
            }

            _prev = IconBtn(new Rect2(1126, 4, 46, 42), UiArt.ArrowL, () => { _page = Math.Max(0, _page - 1); });
            _next = IconBtn(new Rect2(1230, 4, 46, 42), UiArt.ArrowR, () => { _page++; });
            _pageLabel = Lbl(this, new Rect2(1176, 4, 50, 42), "1/1", 16, Txt, HorizontalAlignment.Center);
            _pageLabel.VerticalAlignment = VerticalAlignment.Center;
            _info = Lbl(this, new Rect2(1120, 50, 160, 52), "", 12, Dim, HorizontalAlignment.Left);
            _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            _ready = new Button();
            _ready.Position = new Vector2(1296, 2);
            _ready.Size = new Vector2(288, CardH);
            _ready.AddThemeFontSizeOverride("font_size", 23);
            _ready.FocusMode = FocusModeEnum.None;
            _ready.AddThemeStyleboxOverride("normal", CardStyle(new Color(0.35f, 0.6f, 0.4f), 0.85f, 2, 10f));
            _ready.AddThemeStyleboxOverride("hover", CardStyle(new Color(0.5f, 0.85f, 0.55f), 1f, 3, 10f));
            _ready.Pressed += () => main.OnReady();
            AddChild(_ready);

            BuildTooltip();
            BuildDetail();
        }

        TextureRect Tex(Control parent, Rect2 rect, Texture2D t)
        {
            var r = new TextureRect();
            r.Texture = t;
            r.Position = rect.Position;
            r.Size = rect.Size;
            r.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            r.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            r.MouseFilter = MouseFilterEnum.Ignore;
            parent.AddChild(r);
            return r;
        }

        Label Lbl(Control parent, Rect2 rect, string text, int size, Color color, HorizontalAlignment align)
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
            b.AddThemeStyleboxOverride("normal", CardStyle(new Color(0.4f, 0.5f, 0.66f), 0.8f, 2));
            b.AddThemeStyleboxOverride("hover", CardStyle(new Color(0.7f, 0.82f, 1f), 1f, 2));
            b.AddThemeStyleboxOverride("disabled", CardStyle(new Color(0.24f, 0.26f, 0.32f), 0.5f, 2));
            b.Pressed += onPress;
            AddChild(b);
            Tex(b, new Rect2(rect.Size.X * 0.5f - 11, rect.Size.Y * 0.5f - 11, 22, 22), UiArt.Get(iconName));
            return b;
        }

        void BuildTooltip()
        {
            _tip = new Panel();
            var sb = new StyleBoxFlat();
            sb.BgColor = new Color(0.07f, 0.09f, 0.14f, 0.98f);
            sb.BorderColor = new Color(0.35f, 0.45f, 0.65f);
            sb.BorderWidthTop = sb.BorderWidthBottom = sb.BorderWidthLeft = sb.BorderWidthRight = 2;
            sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 10;
            _tip.AddThemeStyleboxOverride("panel", sb);
            _tip.Size = new Vector2(436, 268);
            _tip.Visible = false;
            _tip.MouseFilter = MouseFilterEnum.Ignore;
            _tip.ZIndex = 60;
            AddChild(_tip);
            _tipTitle = Lbl(_tip, new Rect2(16, 10, 404, 30), "", 20, Gold, HorizontalAlignment.Left);
            _tipIcon = Tex(_tip, new Rect2(16, 46, 100, 100), null);
            _tipTags = Lbl(_tip, new Rect2(16, 152, 100, 62), "", 12, Dim, HorizontalAlignment.Left);
            _tipTags.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            for (int i = 0; i < StatIcons.Length; i++)
            {
                float x = 130 + (i % 2) * 154;
                float y = 48 + (i / 2) * 25;
                _tipStatIcons.Add(Tex(_tip, new Rect2(x, y, 20, 20), UiArt.Get(StatIcons[i])));
                _tipStatValues.Add(Lbl(_tip, new Rect2(x + 25, y - 2, 128, 24), "", 15, Txt, HorizontalAlignment.Left));
            }
            _tipDesc = Lbl(_tip, new Rect2(16, 200, 404, 60), "", 13, new Color(0.82f, 0.86f, 0.94f), HorizontalAlignment.Left);
            _tipDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }

        void BuildDetail()
        {
            _detail = new Panel();
            var sb = new StyleBoxFlat();
            sb.BgColor = new Color(0.06f, 0.08f, 0.13f, 0.985f);
            sb.BorderColor = new Color(0.4f, 0.52f, 0.78f);
            sb.BorderWidthTop = sb.BorderWidthBottom = sb.BorderWidthLeft = sb.BorderWidthRight = 2;
            sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 14;
            _detail.AddThemeStyleboxOverride("panel", sb);
            _detail.Size = new Vector2(880, 430);
            _detail.Position = new Vector2(360, 240);   // 稍后在 _Process 里换算成相对本面板的坐标
            _detail.Visible = false;
            _detail.MouseFilter = MouseFilterEnum.Stop;
            _detail.ZIndex = 70;
            AddChild(_detail);

            _detailName = Lbl(_detail, new Rect2(24, 12, 832, 34), "", 24, Gold, HorizontalAlignment.Left);
            var close = new Button();
            close.Text = "关闭";
            close.Position = new Vector2(760, 12);
            close.Size = new Vector2(96, 34);
            close.FocusMode = FocusModeEnum.None;
            close.AddThemeFontSizeOverride("font_size", 15);
            close.Pressed += () => { _detail.Visible = false; };
            _detail.AddChild(close);
            _detailClose = close;

            var vpc = new SubViewportContainer();
            vpc.Position = new Vector2(24, 58);
            vpc.Size = new Vector2(400, 300);
            vpc.Stretch = true;
            vpc.MouseFilter = MouseFilterEnum.Ignore;
            _detail.AddChild(vpc);
            _detailVp = new SubViewport();
            _detailVp.Size = new Vector2I(400, 300);
            _detailVp.TransparentBg = true;
            _detailVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            _detailVp.Msaa3D = Viewport.Msaa.Msaa4X;
            vpc.AddChild(_detailVp);

            var w = _detailVp.FindWorld3D();
            w.Environment = new Godot.Environment();
            w.Environment.BackgroundMode = Godot.Environment.BGMode.Color;
            w.Environment.BackgroundColor = new Color(0.10f, 0.12f, 0.18f, 1f);
            w.Environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            w.Environment.AmbientLightColor = new Color(0.66f, 0.72f, 0.86f);
            w.Environment.AmbientLightEnergy = 0.9f;
            w.Environment.TonemapMode = Godot.Environment.ToneMapper.Aces;
            var k = new DirectionalLight3D();
            k.RotationDegrees = new Vector3(-42, 34, 0);
            k.LightEnergy = 2.0f;
            _detailVp.AddChild(k);
            var r = new DirectionalLight3D();
            r.RotationDegrees = new Vector3(-12, -126, 0);
            r.LightEnergy = 1.0f;
            r.LightColor = new Color(0.6f, 0.78f, 1f);
            _detailVp.AddChild(r);
            _detailCam = new Camera3D();
            _detailCam.Fov = 32f;
            _detailVp.AddChild(_detailCam);

            _detailStats = Lbl(_detail, new Rect2(444, 62, 412, 250), "", 16, Txt, HorizontalAlignment.Left);
            _detailStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _detailDesc = Lbl(_detail, new Rect2(444, 300, 412, 56), "", 13, Dim, HorizontalAlignment.Left);
            _detailDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            _detailBuy = new Button();
            _detailBuy.Position = new Vector2(444, 362);
            _detailBuy.Size = new Vector2(200, 44);
            _detailBuy.AddThemeFontSizeOverride("font_size", 18);
            _detailBuy.FocusMode = FocusModeEnum.None;
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
            _detailName.Text = def.name + "   T" + def.tier;

            if (_detailSlot != null) { _detailSlot.QueueFree(); _detailSlot = null; }
            _detailSlot = new Node3D();
            var model = UnitModel.Build(def, null);
            _detailSlot.AddChild(model);
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
            int price = match.UnitPrice(match.players[main.localIndex], def);
            _detailStats.Text =
                "价格 " + price + " 金    人口 " + def.pop + "    杀戮值 " + def.killValue + "\n"
                + "生命 " + def.hp + "    物攻 " + def.atk + "    魔攻 " + def.matk + "\n"
                + "物防 " + def.pdef + "    魔防 " + def.mdef + "\n"
                + "移速 " + def.speed + "    攻速 " + def.atkSpeed.ToString("0.00") + "/s    射程 " + def.range + "\n"
                + "标签 " + string.Join(" · ", tn);
            _detailDesc.Text = def.desc + "\n解锁回合 " + def.unlockRound;
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

                var tc = TierColors[Mathf.Clamp(def.tier - 1, 0, 4)];
                b.AddThemeStyleboxOverride("normal", CardStyle(tc, 0.85f, 2));
                b.AddThemeStyleboxOverride("hover", CardStyle(tc.Lightened(0.4f), 1f, 3));
                b.AddThemeStyleboxOverride("disabled", CardStyle(tc.Darkened(0.5f), 0.5f, 2));

                _cardIcons[i].Texture = UnitPortrait.Get(def.id);
                _cardPrice[i].Text = price.ToString();
                _cardPopNum[i].Text = def.pop.ToString();
                b.Disabled = !ok;
                float a = ok ? 1f : 0.42f;
                _cardIcons[i].Modulate = new Color(1, 1, 1, a);
                _cardPrice[i].Modulate = new Color(1, 1, 1, a);
                _cardPopNum[i].Modulate = new Color(1, 1, 1, a);
                _cardGold[i].Modulate = new Color(1, 1, 1, a);
                _cardPop[i].Modulate = new Color(1, 1, 1, a);
                _cardInfo[i].Visible = true;
            }

            _info.Text = "兵种 " + total + " 种\n人口 " + popUsed + " / " + popCap + "\n点击卡片购买\n点右上 ⓘ 看 3D 详情";
            _ready.Disabled = !canBuy;
            _ready.Text = p.ready ? "已准备\n等待对手" : "开始战斗";

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
            _tipTitle.Text = def.name + "   T" + def.tier + "   " + price + "金 / " + def.pop + "人口";
            _tipIcon.Texture = UnitPortrait.Get(def.id);
            _tipTags.Text = string.Join(" · ", tn);
            string[] vals = {
                ((int)def.hp).ToString(), def.atk.ToString(), def.matk.ToString(),
                def.pdef.ToString(), def.mdef.ToString(), def.speed.ToString(),
                def.atkSpeed.ToString("0.00") + "/s", def.range.ToString(), def.killValue.ToString(),
            };
            for (int i = 0; i < vals.Length; i++) _tipStatValues[i].Text = vals[i];
            _tipDesc.Text = def.desc + "\n解锁回合：" + def.unlockRound;
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
                // 弹窗要居中显示在屏幕，本面板在屏幕下方，必须换算坐标
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
                var g = forceTooltip ? new Vector2(560, 300) : GetGlobalMousePosition();
                var want = g + new Vector2(26, 20);
                var vp = GetViewportRect().Size;
                if (want.X + _tip.Size.X > vp.X) want.X = g.X - _tip.Size.X - 22;
                if (want.Y + _tip.Size.Y > vp.Y) want.Y = Mathf.Max(4, vp.Y - _tip.Size.Y - 4);
                _tip.Position = want - GlobalPosition;
            }
        }
    }
}
