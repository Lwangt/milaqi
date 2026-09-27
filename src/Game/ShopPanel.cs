using System;
using System.Collections.Generic;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>
    /// 兵种商店：列出当前回合全部已解锁兵种，左右翻页；每格上方是兵种立绘，
    /// 下方是「金币图标 + 价格」和「人口图标 + 人口」；鼠标悬浮弹出完整属性详情。
    /// </summary>
    public partial class ShopPanel : Control
    {
        public Main main;
        public Match match;

        const int Cols = 12;
        const int CardW = 86, CardH = 108, StepX = 90;

        readonly List<Button> _cards = new List<Button>();
        readonly List<TextureRect> _cardIcons = new List<TextureRect>();
        readonly List<TextureRect> _cardGold = new List<TextureRect>();
        readonly List<TextureRect> _cardPop = new List<TextureRect>();
        readonly List<Label> _cardPrice = new List<Label>();
        readonly List<Label> _cardPopNum = new List<Label>();

        Button _prev, _next, _ready;
        Label _pageLabel, _info;
        Panel _tip;
        TextureRect _tipIcon;
        Label _tipTitle, _tipTags, _tipDesc;
        readonly List<TextureRect> _tipStatIcons = new List<TextureRect>();
        readonly List<Label> _tipStatValues = new List<Label>();

        int _page;
        int _hover = -1;
        public bool forceTooltip;
        static readonly string[] StatIcons = { UiArt.Hp, UiArt.Atk, UiArt.Matk, UiArt.Pdef, UiArt.Mdef, UiArt.Speed, UiArt.AtkSpeed, UiArt.Range, UiArt.Kill };

        static readonly Color Txt = new Color(0.92f, 0.94f, 0.98f);
        static readonly Color Gold = new Color(1f, 0.85f, 0.4f);
        static readonly Color Dim = new Color(0.62f, 0.68f, 0.78f);

        static readonly Color[] TierColors = {
            new Color(0.62f, 0.66f, 0.72f),   // T1 灰
            new Color(0.42f, 0.82f, 0.5f),    // T2 绿
            new Color(0.38f, 0.66f, 1f),      // T3 蓝
            new Color(0.72f, 0.5f, 1f),       // T4 紫
            new Color(1f, 0.72f, 0.28f),      // T5 金
        };

        static StyleBoxFlat CardStyle(Color border, float bgAlpha, float borderW)
        {
            var sb = new StyleBoxFlat();
            sb.BgColor = new Color(0.13f, 0.15f, 0.21f, bgAlpha);
            sb.BorderColor = border;
            sb.BorderWidthTop = sb.BorderWidthBottom = sb.BorderWidthLeft = sb.BorderWidthRight = (int)borderW;
            sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 6;
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

                var icon = Tex(b, new Rect2(15, 4, 56, 60), null);
                _cardIcons.Add(icon);
                _cardGold.Add(Tex(b, new Rect2(7, 68, 16, 16), UiArt.Get(UiArt.Gold)));
                var pl = Lbl(b, new Rect2(25, 64, 58, 22), "", 15, Gold, HorizontalAlignment.Left);
                _cardPrice.Add(pl);
                _cardPop.Add(Tex(b, new Rect2(7, 88, 16, 16), UiArt.Get(UiArt.Pop)));
                var pn = Lbl(b, new Rect2(25, 84, 58, 22), "", 15, Txt, HorizontalAlignment.Left);
                _cardPopNum.Add(pn);
            }

            _prev = Btn(new Rect2(1096, 0, 50, 40), UiArt.ArrowL, () => { _page = Math.Max(0, _page - 1); Refresh(); });
            _next = Btn(new Rect2(1200, 0, 50, 40), UiArt.ArrowR, () => { _page++; Refresh(); });
            _pageLabel = Lbl(this, new Rect2(1150, 0, 46, 40), "1/1", 16, Txt, HorizontalAlignment.Center);
            _pageLabel.VerticalAlignment = VerticalAlignment.Center;
            _info = Lbl(this, new Rect2(1090, 46, 170, 62), "", 13, Dim, HorizontalAlignment.Left);
            _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            _ready = new Button();
            _ready.Position = new Vector2(1290, 0);
            _ready.Size = new Vector2(290, CardH);
            _ready.AddThemeFontSizeOverride("font_size", 24);
            _ready.FocusMode = FocusModeEnum.None;
            _ready.Pressed += () => main.OnReady();
            AddChild(_ready);

            BuildTooltip();
        }

        TextureRect Tex(Control parent, Rect2 rect, Texture2D t)
        {
            var r = new TextureRect();
            r.Texture = t;
            r.Position = rect.Position;
            r.Size = rect.Size;
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

        Button Btn(Rect2 rect, string iconName, Action onPress)
        {
            var b = new Button();
            b.Position = rect.Position;
            b.Size = rect.Size;
            b.FocusMode = FocusModeEnum.None;
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
            sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 8;
            _tip.AddThemeStyleboxOverride("panel", sb);
            _tip.Size = new Vector2(430, 274);
            _tip.Visible = false;
            _tip.MouseFilter = MouseFilterEnum.Ignore;
            _tip.ZIndex = 50;
            AddChild(_tip);

            _tipTitle = Lbl(_tip, new Rect2(14, 8, 402, 30), "", 20, Gold, HorizontalAlignment.Left);
            _tipIcon = Tex(_tip, new Rect2(16, 46, 96, 96), null);
            _tipTags = Lbl(_tip, new Rect2(16, 150, 96, 60), "", 12, Dim, HorizontalAlignment.Left);
            _tipTags.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            for (int i = 0; i < StatIcons.Length; i++)
            {
                float col = i % 2;
                float row = i / 2;
                float x = 124 + col * 154;
                float y = 46 + row * 25;
                _tipStatIcons.Add(Tex(_tip, new Rect2(x, y, 20, 20), UiArt.Get(StatIcons[i])));
                _tipStatValues.Add(Lbl(_tip, new Rect2(x + 24, y - 2, 128, 24), "", 15, Txt, HorizontalAlignment.Left));
            }
            _tipDesc = Lbl(_tip, new Rect2(14, 200, 402, 64), "", 13, new Color(0.82f, 0.86f, 0.94f), HorizontalAlignment.Left);
            _tipDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
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
            if (_page >= pages) _page = pages - 1;
            if (_page < 0) _page = 0;
            _pageLabel.Text = (_page + 1) + "/" + pages;
            _prev.Disabled = _page <= 0;
            _next.Disabled = _page >= pages - 1;

            bool canBuyPhase = match.phase == MatchPhase.Prep;
            int popUsed = match.PopUsed(p), popCap = match.PopCap(p);

            for (int i = 0; i < Cols; i++)
            {
                int slot = _page * Cols + i;
                var b = _cards[i];
                if (slot >= total)
                {
                    b.Visible = false;
                    continue;
                }
                b.Visible = true;
                var def = match.db.Unit(p.shop[slot].unitId);
                if (def == null) { b.Visible = false; continue; }
                int price = match.UnitPrice(p, def);
                bool canPop = popUsed + def.pop <= popCap;
                var tc = TierColors[Mathf.Clamp(def.tier - 1, 0, 4)];
                b.AddThemeStyleboxOverride("normal", CardStyle(tc, 0.85f, 2));
                b.AddThemeStyleboxOverride("hover", CardStyle(tc.Lightened(0.35f), 1f, 3));
                b.AddThemeStyleboxOverride("disabled", CardStyle(tc.Darkened(0.45f), 0.55f, 2));
                _cardIcons[i].Texture = UnitArt.Get(def.id);
                _cardPrice[i].Text = price.ToString();
                _cardPopNum[i].Text = def.pop.ToString();
                bool afford = p.gold >= price;
                b.Disabled = !canBuyPhase || !afford || !canPop;
                b.Modulate = (afford && canPop) ? Colors.White : new Color(1f, 1f, 1f, 0.45f);
                _cardIcons[i].Modulate = b.Modulate;
                _cardPrice[i].Modulate = b.Modulate;
                _cardPopNum[i].Modulate = b.Modulate;
            }

            float price2 = 0;
            _info.Text = "兵种 " + total + " 种\n人口 " + popUsed + "/" + popCap + "\n先点兵种购买，再点战场自己那半边布阵";
            _ready.Disabled = !canBuyPhase;
            _ready.Text = p.ready ? "已准备\n等待对手" : "开始战斗";

            RefreshTooltip(p);
        }

        void RefreshTooltip(PlayerState p)
        {
            int hover = forceTooltip ? 3 : _hover;
            bool show = hover >= 0 && match.phase != MatchPhase.GameOver;
            int slot = _page * Cols + hover;
            if (show && (slot < 0 || slot >= p.shop.Count)) show = false;
            _tip.Visible = show;
            if (!show) return;

            var def = match.db.Unit(p.shop[slot].unitId);
            if (def == null) { _tip.Visible = false; return; }
            int price = match.UnitPrice(p, def);
            var cat = new List<string>();
            if (def.tags != null) for (int i = 0; i < def.tags.Length; i++)
            {
                string tn;
                cat.Add(match.db.TagNames.TryGetValue(def.tags[i], out tn) ? tn : def.tags[i]);
            }
            _tipTitle.Text = def.name + "   T" + def.tier + "   " + price + "金 / " + def.pop + "人口";
            _tipIcon.Texture = UnitArt.Get(def.id);
            _tipTags.Text = string.Join(" · ", cat);
            string[] vals = {
                ((int)def.hp).ToString(),
                def.atk.ToString(),
                def.matk.ToString(),
                def.pdef.ToString(),
                def.mdef.ToString(),
                def.speed.ToString(),
                def.atkSpeed.ToString("0.00") + "/s",
                def.range.ToString(),
                def.killValue.ToString(),
            };
            for (int i = 0; i < vals.Length; i++) _tipStatValues[i].Text = vals[i];
            _tipDesc.Text = def.desc + "\n解锁回合：" + def.unlockRound;
        }

        public override void _Process(double delta)
        {
            Refresh();
            if (_tip.Visible)
            {
                // 鼠标是全局坐标，而 _tip.Position 是相对本面板的局部坐标，必须换算
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
