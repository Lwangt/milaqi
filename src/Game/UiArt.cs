using System.Collections.Generic;
using Godot;

namespace Milaqi.Game
{
    /// <summary>UI 图标（金币/宝石/人口/属性等），资源位于 art/ui/*.svg。</summary>
    public static class UiArt
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public const string Gold = "gold";
        public const string Gem = "gem";
        public const string Pop = "pop";
        public const string Level = "level";
        public const string Xp = "xp";
        public const string Hp = "hp";
        public const string Atk = "atk";
        public const string Matk = "matk";
        public const string Pdef = "pdef";
        public const string Mdef = "mdef";
        public const string Speed = "speed";
        public const string AtkSpeed = "atkspeed";
        public const string Range = "range";
        public const string Kill = "kill";
        public const string Flame = "flame";
        public const string Tier = "tier";
        public const string ArrowL = "arrowL";
        public const string ArrowR = "arrowR";

        public static Texture2D Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Texture2D t;
            if (Cache.TryGetValue(name, out t)) return t;
            t = ResourceLoader.Load<Texture2D>("res://art/ui/" + name + ".svg");
            Cache[name] = t;
            return t;
        }
    }
}
