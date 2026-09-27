using Godot;

namespace Milaqi.Game
{
    /// <summary>
    /// 全局视觉主题：中世纪魔法城邦风格。
    /// 羊皮纸面板 + 錾金边框 + 深色魔法石底，统一间距与圆角。
    /// </summary>
    public static class UiTheme
    {
        // ---- 主色 ----
        public static readonly Color Ink = new Color(0.09f, 0.08f, 0.12f);            // 最底
        public static readonly Color PanelDeep = new Color(0.12f, 0.11f, 0.16f, 0.96f);
        public static readonly Color PanelStone = new Color(0.16f, 0.15f, 0.21f, 0.96f);
        public static readonly Color PanelParch = new Color(0.20f, 0.18f, 0.22f, 0.97f);
        public static readonly Color PanelRaised = new Color(0.24f, 0.21f, 0.26f, 0.98f);

        public static readonly Color Gold = new Color(0.92f, 0.76f, 0.38f);
        public static readonly Color GoldDim = new Color(0.62f, 0.50f, 0.26f);
        public static readonly Color Parchment = new Color(0.90f, 0.86f, 0.76f);
        public static readonly Color ParchDim = new Color(0.66f, 0.62f, 0.56f);
        public static readonly Color Arcane = new Color(0.55f, 0.72f, 1f);
        public static readonly Color ArcaneDeep = new Color(0.30f, 0.40f, 0.72f);
        public static readonly Color Crimson = new Color(0.90f, 0.42f, 0.42f);
        public static readonly Color Emerald = new Color(0.42f, 0.85f, 0.52f);
        public static readonly Color Shadow = new Color(0.62f, 0.42f, 0.78f);

        public static readonly Color TeamLeft = new Color(0.45f, 0.72f, 1f);
        public static readonly Color TeamRight = new Color(1f, 0.46f, 0.48f);

        public static readonly Color[] TierColor = {
            new Color(0.62f, 0.64f, 0.70f),   // T1 铅灰
            new Color(0.44f, 0.80f, 0.52f),   // T2 翠绿
            new Color(0.38f, 0.66f, 1f),      // T3 秘蓝
            new Color(0.74f, 0.50f, 1f),      // T4 紫晶
            new Color(1f, 0.72f, 0.28f),      // T5 鎏金
        };

        // ---- 构件 ----
        public static StyleBoxFlat Panel(Color bg, Color border, int borderW = 2, int radius = 10, float shadow = 0f)
        {
            var sb = new StyleBoxFlat();
            sb.BgColor = bg;
            sb.BorderColor = border;
            sb.BorderWidthTop = sb.BorderWidthBottom = sb.BorderWidthLeft = sb.BorderWidthRight = borderW;
            sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = radius;
            if (shadow > 0f) { sb.ShadowColor = new Color(0, 0, 0, 0.45f); sb.ShadowSize = (int)shadow; sb.ShadowOffset = new Vector2(0, 3); }
            return sb;
        }

        public static StyleBoxFlat Stone(float alpha = 0.96f)
            { return Panel(new Color(PanelStone.R, PanelStone.G, PanelStone.B, alpha), GoldDim, 2, 10); }

        public static StyleBoxFlat Card(Color tier, float bgAlpha, int borderW, float radius = 9f)
            { return Panel(new Color(PanelParch.R, PanelParch.G, PanelParch.B, bgAlpha), tier, borderW, (int)radius); }

        /// <summary>给面板加四角金饰（用四个小方块模拟铆钉）。</summary>
        public static void AddCornerRivets(Control parent, float w, float h, Color? color = null)
        {
            var c = color ?? GoldDim;
            float[,] pts = { { 4, 4 }, { w - 12, 4 }, { 4, h - 12 }, { w - 12, h - 12 } };
            for (int i = 0; i < 4; i++)
            {
                var d = new Panel();
                var sb = new StyleBoxFlat();
                sb.BgColor = c;
                sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 4;
                d.AddThemeStyleboxOverride("panel", sb);
                d.Position = new Vector2(pts[i, 0], pts[i, 1]);
                d.Size = new Vector2(8, 8);
                d.MouseFilter = Control.MouseFilterEnum.Ignore;
                parent.AddChild(d);
            }
        }
    }
}
