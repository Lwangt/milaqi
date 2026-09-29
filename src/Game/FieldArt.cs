using Godot;

namespace Milaqi.Game
{
    /// <summary>战场地面贴图（程序化生成的整张草地，无接缝）。</summary>
    public static class FieldArt
    {
        static Texture2D _arena;
        public static Texture2D Arena()
        {
            if (_arena == null)
            {
                const string p = "res://art/field/arena.png";
                if (ResourceLoader.Exists(p)) _arena = GD.Load<Texture2D>(p);
            }
            return _arena;
        }
    }
}
