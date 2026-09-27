using System.Collections.Generic;
using Godot;

namespace Milaqi.Game
{
    /// <summary>
    /// 兵种美术资源：优先加载 art/units/&lt;id&gt;.svg（白色剪影，运行时按阵营染色），
    /// 缺失时返回 null，渲染层自动回退到程序化几何绘制。
    /// 替换正式美术只需把同名文件丢进 art/units/，不需要改任何代码。
    /// </summary>
    public static class UnitArt
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        static readonly HashSet<string> Missing = new HashSet<string>();

        public static Texture2D Get(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return null;
            Texture2D t;
            if (Cache.TryGetValue(unitId, out t)) return t;
            if (Missing.Contains(unitId)) return null;
            t = ResourceLoader.Load<Texture2D>("res://art/units/" + unitId + ".svg");
            if (t == null) t = ResourceLoader.Load<Texture2D>("res://art/units/" + unitId + ".png");
            if (t == null) { Missing.Add(unitId); return null; }
            Cache[unitId] = t;
            return t;
        }
    }
}
