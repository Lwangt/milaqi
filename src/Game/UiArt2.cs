using Godot;
using System.Collections.Generic;

namespace Milaqi.Game
{
    /// <summary>程序化生成的华丽 UI 贴图（錾金九宫格面板、圆形头像框、卡片框、阵营徽记）。</summary>
    public static class UiArt2
    {
        static readonly Dictionary<string, Texture2D> _cache = new();

        public static Texture2D Get(string name)
        {
            if (_cache.TryGetValue(name, out var t)) return t;
            var p = "res://art/ui2/" + name + ".png";
            t = ResourceLoader.Exists(p) ? GD.Load<Texture2D>(p) : null;
            _cache[name] = t;
            return t;
        }

        /// <summary>錾金九宫格面板；贴图缺失时返回 null，调用方回退到纯色样式。</summary>
        public static StyleBoxTexture Panel(int margin = 30, float contentPad = 10f)
        {
            var tex = Get("panel");
            if (tex == null) return null;
            var sb = new StyleBoxTexture();
            sb.Texture = tex;
            sb.SetTextureMargin(Side.Left, margin);
            sb.SetTextureMargin(Side.Top, margin);
            sb.SetTextureMargin(Side.Right, margin);
            sb.SetTextureMargin(Side.Bottom, margin);
            sb.SetContentMargin(Side.Left, contentPad);
            sb.SetContentMargin(Side.Top, contentPad * 0.5f);
            sb.SetContentMargin(Side.Right, contentPad);
            sb.SetContentMargin(Side.Bottom, contentPad * 0.5f);
            return sb;
        }

        public static StyleBoxTexture Card(int margin = 28)
        {
            var tex = Get("card");
            if (tex == null) return null;
            var sb = new StyleBoxTexture();
            sb.Texture = tex;
            sb.SetTextureMargin(Side.Left, margin);
            sb.SetTextureMargin(Side.Top, margin);
            sb.SetTextureMargin(Side.Right, margin);
            sb.SetTextureMargin(Side.Bottom, margin);
            return sb;
        }
    }
}
