using Godot;
using System;
using System.Collections.Generic;

namespace Milaqi;

/// <summary>
/// 兵种精灵：程序化生成的动作帧带（每帧 128x128，共 20 帧）。
/// 布局固定：0-5 待机 / 6-13 行走 / 14-19 攻击。
/// </summary>
public static class UnitSprite
{
    public const int FW = 128;
    public const int FH = 128;
    public const int IdleStart = 0, IdleCount = 6;
    public const int WalkStart = 6, WalkCount = 8;
    public const int AtkStart = 14, AtkCount = 6;
    public const int TotalFrames = 20;

    /// <summary>角色脚底在帧内的高度比例（用于把脚对齐到地面）。</summary>
    public const float FootRatio = 124f / 128f;
    /// <summary>角色实际占据的帧高比例（用于按体型换算显示尺寸）。</summary>
    public const float BodyRatio = 112f / 128f;

    static readonly Dictionary<string, Texture2D> _cache = new();
    static bool _warned;

    public static Texture2D Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_cache.TryGetValue(id, out var t)) return t;
        var path = "res://art/units2/" + id + ".png";
        Texture2D tex = null;
        if (ResourceLoader.Exists(path)) tex = GD.Load<Texture2D>(path);
        if (tex == null && !_warned)
        {
            _warned = true;
            GD.PrintErr("[UnitSprite] 找不到精灵: " + path);
        }
        _cache[id] = tex;
        return tex;
    }

    public enum Anim { Idle, Walk, Attack }

    public static int FrameOf(Anim a, float t, float speedScale = 1f)
    {
        switch (a)
        {
            case Anim.Walk:
                return WalkStart + (int)(t * 11f * speedScale) % WalkCount;
            case Anim.Attack:
                return AtkStart + Mathf.Min(AtkCount - 1, (int)(t * 17f));
            default:
                return IdleStart + (int)(t * 6.5f) % IdleCount;
        }
    }

    static readonly Dictionary<string, AtlasTexture> _icons = new();

    /// <summary>卡牌/头像用图：从帧带裁出角色主体（头肩特写）。</summary>
    public static Texture2D Icon(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_icons.TryGetValue(id, out var cached)) return cached;
        var sheet = Get(id);
        AtlasTexture at = null;
        if (sheet != null)
        {
            at = new AtlasTexture();
            at.Atlas = sheet;
            at.Region = new Rect2(28, 8, 72, 72);
            at.FilterClip = true;
        }
        _icons[id] = at;
        return at;
    }

    public static Rect2 SrcRect(int frame)
    {
        int f = ((frame % TotalFrames) + TotalFrames) % TotalFrames;
        return new Rect2(f * FW, 0, FW, FH);
    }
}
