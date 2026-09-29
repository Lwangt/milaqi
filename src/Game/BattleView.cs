using System;
using System.Collections.Generic;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>
    /// 战场渲染层：单节点批绘制，逻辑与表现完全分离。
    /// 单位优先使用 art/units/&lt;id&gt;.svg 贴图（按阵营染色），缺失时回退到程序化几何。
    /// </summary>
    public partial class BattleView : Control
    {
        public Match match;
        public Main main;
        public string selectedSkill;
        public bool showDebug;
        /// <summary>为 true 时战场图形交给 BattleWorld3D，这里只保留 2D 覆盖层。</summary>
        public bool Use3D;
        /// <summary>战场在屏幕上的矩形（3D 相机据此取景）。</summary>
        public Rect2 FieldRect { get { return _field; } }

        Rect2 _field = new Rect2(100, 185, 1400, 420);
        float _scale = 1f;
        float _vtime;
        int _vfxJitter;

        /// <summary>单位动作状态（待机/行走/攻击），按单位 id 缓存。</summary>
        sealed class UnitAnimState
        {
            public float px, py, prevT;
            public bool init;
            public float wt, it, at;
            public UnitSprite.Anim anim = UnitSprite.Anim.Idle;
        }
        readonly Dictionary<int, UnitAnimState> _uanim = new();
        Vector2 _curShake;

        UnitAnimState AnimOf(SimUnit u)
        {
            if (!_uanim.TryGetValue(u.id, out var a))
            {
                a = new UnitAnimState();
                _uanim[u.id] = a;
            }
            return a;
        }

        // 屏幕震动：受击、阵亡、城邦掉血时抖一下，战斗才有打击感
        float _shake;
        public void AddShake(float amount) { if (amount > _shake) _shake = Math.Min(26f, amount); }

        // 回合结算横幅
        string _banner = "";
        float _bannerT, _bannerMax = 2.8f;
        Color _bannerColor = Colors.White;
        float _seenP0Hp = -1f, _seenP1Hp = -1f;

        // 回合开始 / 阶段切换的过场横幅
        string _roundBanner = "";
        string _roundSub = "";
        float _roundBannerT, _roundBannerMax = 2.4f;
        Color _roundColor = UiTheme.Gold;
        int _seenRound = -1;
        MatchPhase _seenPhase = MatchPhase.Prep;
        float _flashT;

        sealed class FloatText { public Vector2 pos; public string text; public float life, max; public Color color; public float size; }
        sealed class Shot { public Vector2 from, to, cur; public float t, dur; public Color color; public Color core; public int kind; public float size; }
        sealed class Slash { public Vector2 from, to; public float life, max; public Color color; public int kind; public float width; }
        sealed class Puff { public Vector2 pos; public float life, max; public Color color; public float radius; public int kind; }

        readonly List<FloatText> _floats = new List<FloatText>();
        readonly List<Shot> _shots = new List<Shot>();
        readonly List<Slash> _slashes = new List<Slash>();
        readonly List<Puff> _puffs = new List<Puff>();

        // ---------------------------------------------------------------- 攻击特效分类
        // 0 挥砍 1 箭矢 2 奥术 3 龙息 4 炮击 5 暗影 6 冲击波 7 亡灵 8 自然
        static int VfxKind(UnitDef d)
        {
            if (d == null) return 0;
            if (d.HasTag("dragon")) return 3;
            if (d.HasTag("demon")) return 5;
            if (d.HasTag("elemental")) return 2;
            if (d.HasTag("plant")) return 8;
            if (d.HasTag("undead")) return 7;
            if (d.HasTag("machine")) return 4;
            if (d.HasTag("assassin")) return 5;
            if (d.HasTag("giant")) return 6;
            if (d.range > 60f) return 1;
            return 0;
        }

        static readonly Color[] VfxCore = {
            new Color(0.96f, 0.96f, 1.00f),   // 挥砍
            new Color(0.86f, 0.92f, 1.00f),   // 箭矢
            new Color(0.74f, 0.56f, 1.00f),   // 奥术
            new Color(1.00f, 0.52f, 0.20f),   // 龙息
            new Color(0.92f, 0.48f, 0.22f),   // 炮击
            new Color(0.58f, 0.32f, 0.86f),   // 暗影
            new Color(1.00f, 0.86f, 0.42f),   // 冲击波
            new Color(0.52f, 1.00f, 0.58f),   // 亡灵
            new Color(0.46f, 0.88f, 0.36f),   // 自然
        };

        static Color VfxOf(int kind, Team team)
        {
            var c = VfxCore[kind < 0 || kind >= VfxCore.Length ? 0 : kind];
            // 轻微混入阵营色，方便在混战里分辨是谁打的
            var t = team == Team.Left ? new Color(0.40f, 0.72f, 1f) : new Color(1f, 0.46f, 0.44f);
            return c.Lerp(t, 0.22f);
        }

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.All;
        }

        public void Layout()
        {
            var sz = Size;
            // 左右两侧留给资源面板，上方留给顶栏，下方留给商店区
            float sideL = 320f, sideR = 320f, top = 112f, bottom = 266f;
            float availW = Math.Max(200f, sz.X - sideL - sideR);
            float availH = Math.Max(120f, sz.Y - top - bottom);
            float s = Math.Min(availW / match.sim.fieldWidth, availH / match.sim.fieldHeight);
            _scale = s;
            float w = match.sim.fieldWidth * s;
            float h = match.sim.fieldHeight * s;
            float x = sideL + (availW - w) * 0.5f;
            float y = top + (availH - h) * 0.5f;
            _field = new Rect2(x, y, w, h);
        }

        public Vector2 ToScreen(float fx, float fy)
        {
            return new Vector2(_field.Position.X + fx * _scale, _field.Position.Y + fy * _scale);
        }

        public Vector2 ToField(Vector2 screen)
        {
            return new Vector2((screen.X - _field.Position.X) / _scale, (screen.Y - _field.Position.Y) / _scale);
        }

        static readonly Color BgTop = new Color(0.07f, 0.09f, 0.13f);
        static readonly Color BgBottom = new Color(0.11f, 0.10f, 0.15f);
        static readonly Color LeftColor = new Color(0.38f, 0.70f, 1.0f);
        static readonly Color RightColor = new Color(1.0f, 0.44f, 0.46f);

        // ------------------------------------------------------------------ 特效收集
        public override void _Process(double delta)
        {
            if (match == null) return;
            float dt = (float)delta;
            if (dt > 0.1f) dt = 0.1f;
            _vtime += dt;
            if (_shake > 0f) _shake = Mathf.Max(0f, _shake - dt * 52f);
            if (_bannerT > 0f) _bannerT -= dt;
            if (_roundBannerT > 0f) _roundBannerT -= dt;
            if (_flashT > 0f) _flashT -= dt;

            // 回合 / 阶段切换 → 过场横幅 + 闪光
            if (match.round != _seenRound)
            {
                _seenRound = match.round;
                _roundBanner = "第 " + match.round + " 回合";
                _roundSub = "备战开始 · 人口上限 " + match.PopCap(match.players[main != null ? main.localIndex : 0]);
                _roundColor = UiTheme.Gold;
                _roundBannerT = _roundBannerMax;
                _flashT = 0.35f;
            }
            if (match.phase != _seenPhase)
            {
                _seenPhase = match.phase;
                if (match.phase == MatchPhase.Battle)
                {
                    _roundBanner = "交战开始";
                    _roundSub = "双方部队出击";
                    _roundColor = new Color(1f, 0.72f, 0.4f);
                    _roundBannerT = 1.6f;
                    AddShake(5f);
                }
            }

            // 城邦掉血 → 结算横幅 + 强烈震动
            if (main != null)
            {
                var q0 = match.players[0]; var q1 = match.players[1];
                if (_seenP0Hp < 0f) { _seenP0Hp = q0.baseHp; _seenP1Hp = q1.baseHp; }
                else if (q0.baseHp != _seenP0Hp || q1.baseHp != _seenP1Hp)
                {
                    float dealt = _seenP1Hp - q1.baseHp;
                    float taken = _seenP0Hp - q0.baseHp;
                    bool leftIsMe = main.localIndex == 0;
                    float myDealt = leftIsMe ? dealt : taken;
                    float myTaken = leftIsMe ? taken : dealt;
                    _seenP0Hp = q0.baseHp; _seenP1Hp = q1.baseHp;
                    if (myDealt > 0f || myTaken > 0f)
                    {
                        _banner = "本回合　造成 " + (int)myDealt + " 伤害　承受 " + (int)myTaken + " 伤害";
                        _bannerColor = myDealt > myTaken ? new Color(0.55f, 1f, 0.65f) : (myDealt < myTaken ? new Color(1f, 0.6f, 0.6f) : UiTheme.Parchment);
                        _bannerT = _bannerMax;
                        AddShake(8f + Mathf.Min(14f, myTaken));
                    }
                }
            }

            var events = match.sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                switch (e.kind)
                {
                    case SimEventKind.Hit:
                    case SimEventKind.Shot:
                        {
                            if (Use3D)
                            {
                                // 3D 世界负责弹道与命中特效，这里只负责伤害数字
                                if (e.value >= 1f && _floats.Count < 80)
                                    _floats.Add(new FloatText
                                    {
                                        pos = ToScreen(e.x2, e.y2) + new Vector2((float)(_vfxJitter++ % 3 - 1) * 7f, -14f),
                                        text = ((int)e.value).ToString(), life = 0.75f, max = 0.75f,
                                        color = new Color(1f, 0.92f, 0.58f), size = 13f
                                    });
                                break;
                            }
                            var ad = match.db.Unit(e.label);
                            int kind = VfxKind(ad);
                            var col = VfxOf(kind, e.team);
                            var pFrom = ToScreen(e.x, e.y);
                            var pTo = ToScreen(e.x2, e.y2);
                            float dist = pFrom.DistanceTo(pTo);
                            float size = ad != null ? Mathf.Clamp(9f + ad.pop * 1.5f + (ad.tier - 1) * 1.6f, 9f, 26f) : 12f;

                            if (e.kind == SimEventKind.Hit)
                            {
                                // 近战：挥砍弧线（重击/巨型单位更宽更慢）
                                if (_slashes.Count < 70)
                                    _slashes.Add(new Slash
                                    {
                                        from = pFrom, to = pTo, life = 0.26f + (kind == 6 ? 0.14f : 0f), max = 0.26f + (kind == 6 ? 0.14f : 0f),
                                        color = col, kind = kind, width = size * (kind == 6 ? 0.75f : 0.46f)
                                    });
                            }
                            else
                            {
                                // 远程：按兵种播放不同弹道
                                if (_shots.Count < 140)
                                    _shots.Add(new Shot
                                    {
                                        from = pFrom, to = pTo, cur = pFrom, t = 0f,
                                        dur = Mathf.Clamp(dist / (kind == 4 ? 900f : kind == 3 ? 1300f : 1500f), 0.06f, 0.30f),
                                        color = col, core = col.Lightened(0.35f), kind = kind, size = size
                                    });
                            }

                            // 命中特效
                            if (_puffs.Count < 180)
                                _puffs.Add(new Puff
                                {
                                    pos = pTo, life = kind == 6 ? 0.36f : 0.20f, max = kind == 6 ? 0.36f : 0.20f,
                                    color = new Color(col.R, col.G, col.B, 0.85f),
                                    radius = kind == 6 ? 36f : (kind == 3 ? 26f : 14f), kind = kind
                                });
                            if ((kind == 2 || kind == 3 || kind == 7 || kind == 8) && _puffs.Count < 180)
                                _puffs.Add(new Puff
                                {
                                    pos = pTo, life = 0.42f, max = 0.42f,
                                    color = new Color(col.R, col.G, col.B, 0.4f),
                                    radius = kind == 3 ? 34f : 18f, kind = kind
                                });

                            if (e.value >= 1f && _floats.Count < 80)
                                _floats.Add(new FloatText
                                {
                                    pos = new Vector2(pTo.X + (float)(_vfxJitter++ % 3 - 1) * 7f, pTo.Y - 12f),
                                    text = ((int)e.value).ToString(),
                                    life = 0.7f, max = 0.7f,
                                    color = kind == 2 || kind == 7 ? new Color(0.88f, 0.78f, 1f) : new Color(1f, 0.90f, 0.55f),
                                    size = 12f
                                });
                        }
                        break;
                    case SimEventKind.Death:
                        AddShake(4f + e.value * 1.2f);
                        _puffs.Add(new Puff { pos = ToScreen(e.x, e.y), life = 0.5f, max = 0.5f, color = e.team == Team.Left ? new Color(0.5f, 0.7f, 1f, 0.6f) : new Color(1f, 0.55f, 0.55f, 0.6f), radius = 26f, kind = -1 });
                        for (int s = 0; s < 5; s++)
                        {
                            float ang = (s / 5f) * Mathf.Tau + (float)(_vfxJitter % 7) * 0.31f;
                            var pp = ToScreen(e.x, e.y) + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 10f;
                            _puffs.Add(new Puff { pos = pp, life = 0.35f, max = 0.35f, color = e.team == Team.Left ? new Color(0.6f, 0.8f, 1f, 0.5f) : new Color(1f, 0.6f, 0.6f, 0.5f), radius = 7f, kind = -1 });
                        }
                        _vfxJitter++;
                        break;
                    case SimEventKind.Heal:
                        if (_floats.Count < 80)
                            _floats.Add(new FloatText { pos = ToScreen(e.x, e.y - 14f), text = "+" + (int)e.value, life = 0.7f, max = 0.7f, color = new Color(0.5f, 1f, 0.6f), size = 12f });
                        break;
                    case SimEventKind.Cast:
                        _puffs.Add(new Puff { pos = ToScreen(e.x, e.y), life = 0.45f, max = 0.45f, color = new Color(0.75f, 0.6f, 1f, 0.55f), radius = Math.Max(20f, e.radius * _scale) });
                        break;
                }
            }
            match.sim.ClearEvents();

            for (int i = _floats.Count - 1; i >= 0; i--)
            {
                var f = _floats[i];
                f.life -= dt;
                f.pos = new Vector2(f.pos.X, f.pos.Y - 26f * dt);
                if (f.life <= 0f) _floats.RemoveAt(i);
            }
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var s = _shots[i];
                s.t += dt;
                float k = Math.Min(1f, s.t / s.dur);
                s.cur = s.from.Lerp(s.to, k);
                if (k >= 1f) _shots.RemoveAt(i);
            }
            for (int i = _slashes.Count - 1; i >= 0; i--)
            {
                var s = _slashes[i];
                s.life -= dt;
                if (s.life <= 0f) _slashes.RemoveAt(i);
            }
            for (int i = _puffs.Count - 1; i >= 0; i--)
            {
                var p = _puffs[i];
                p.life -= dt;
                if (p.life <= 0f) _puffs.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ 绘制
        public override void _Draw()
        {
            if (match == null) return;
            var sim = match.sim;
            var sz = Size;

            // 屏幕震动：整体偏移绘制（不影响布局与命中判定）
            _curShake = _shake > 0.2f
                ? new Vector2((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0)) * _shake
                : Vector2.Zero;
            DrawSetTransform(_curShake, 0f, Vector2.One);

            if (!Use3D)
            {
            DrawRect(new Rect2(Vector2.Zero, sz), BgTop);
            DrawRect(new Rect2(0, sz.Y * 0.55f, sz.X, sz.Y * 0.45f), BgBottom);

            DrawGround();

            DrawBase(true, match.players[0].baseHp / Math.Max(1f, match.db.Balance.baseHp));
            DrawBase(false, match.players[1].baseHp / Math.Max(1f, match.db.Balance.baseHp));

            float midX = _field.Position.X + _field.Size.X * 0.5f;
            DrawLine(new Vector2(midX, _field.Position.Y), new Vector2(midX, _field.Position.Y + _field.Size.Y), new Color(1, 1, 1, 0.07f), 2f);

            int li = main != null ? main.localIndex : 0;
            if (match.phase == MatchPhase.Prep && match.players[li].pendingDeploy.Count > 0)
            {
                bool leftSide = li == 0;
                var zone = leftSide
                    ? new Rect2(_field.Position, new Vector2(_field.Size.X * 0.5f, _field.Size.Y))
                    : new Rect2(_field.Position + new Vector2(_field.Size.X * 0.5f, 0f), new Vector2(_field.Size.X * 0.5f, _field.Size.Y));
                DrawRect(zone, leftSide ? new Color(0.36f, 0.68f, 1.0f, 0.06f) : new Color(1.0f, 0.42f, 0.45f, 0.06f));
            }

            // 地面先画影子，再画单位
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var u = sim.Units[i];
                if (!u.alive) continue;
                var p = ToScreen(u.x, u.y);
                bool lft = u.team == Team.Left;
                // 阵营光环 + 投影，让单位在大场面里更容易分辨敌我
                DrawCircle(new Vector2(p.X, p.Y + u.radius * _scale * 0.5f), u.radius * _scale * 1.15f,
                    lft ? new Color(0.35f, 0.62f, 1f, 0.18f) : new Color(1f, 0.42f, 0.42f, 0.18f));
                DrawCircle(new Vector2(p.X, p.Y + u.radius * _scale * 0.55f), u.radius * _scale * 0.8f, new Color(0, 0, 0, 0.28f));
            }
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var u = sim.Units[i];
                if (!u.alive) continue;
                DrawUnit(u);
            }

            // 近战挥砍
            for (int i = 0; i < _slashes.Count; i++)
            {
                var s = _slashes[i];
                float k = 1f - s.life / s.max;          // 0..1 播放进度
                float a = (1f - k) * 0.95f;
                var dir = (s.to - s.from);
                float len = dir.Length();
                if (len < 0.01f) continue;
                dir /= len;
                var nrm = new Vector2(-dir.Y, dir.X);
                // 弧线：起点沿法线偏移，终点收到目标上，形成挥砍轨迹
                float sweep = (1f - k) * len * 0.55f;
                var p0 = s.from + dir * (len * 0.25f) + nrm * sweep;
                var pm = s.from + dir * (len * 0.7f) + nrm * (sweep * 0.45f);
                var p1 = s.to;
                var outer = new Color(s.color.R, s.color.G, s.color.B, a * 0.55f);
                var inner = new Color(1f, 1f, 1f, a * 0.85f);
                DrawLine(p0, pm, outer, s.width * (1.15f - k * 0.35f));
                DrawLine(pm, p1, outer, s.width * (1.0f - k * 0.4f));
                DrawLine(p0.Lerp(pm, 0.35f), pm.Lerp(p1, 0.25f), inner, Math.Max(1.4f, s.width * 0.3f));
                if (s.kind == 6) // 巨兽/冲击波：额外震地圆环
                    DrawArc(s.to, s.width * (1.4f + k * 2.2f), 0, Mathf.Tau, 26, new Color(s.color.R, s.color.G, s.color.B, a * 0.5f), 2f);
            }

            // 远程弹道
            for (int i = 0; i < _shots.Count; i++)
            {
                var s = _shots[i];
                float k = Mathf.Clamp(s.t / s.dur, 0f, 1f);
                var dir = (s.to - s.from);
                float len = dir.Length();
                if (len < 0.01f) continue;
                dir /= len;
                var tail = s.cur - dir * Mathf.Min(len * 0.55f, s.size * (s.kind == 3 ? 4.5f : s.kind == 2 ? 3.0f : 2.0f));
                var body = new Color(s.color.R, s.color.G, s.color.B, 0.9f);
                var glow = new Color(s.color.R, s.color.G, s.color.B, 0.30f);

                switch (s.kind)
                {
                    case 3: // 龙息：锥形吐息
                        {
                            float w0 = s.size * 0.5f, w1 = s.size * 1.6f;
                            var n = new Vector2(-dir.Y, dir.X);
                            var poly = new Vector2[] {
                                s.cur - n * w0 - dir * s.size * 2.6f,
                                s.cur + n * w0 - dir * s.size * 2.6f,
                                s.cur + n * w1, s.cur - n * w1
                            };
                            DrawColoredPolygon(poly, new Color(s.color.R, s.color.G, s.color.B, 0.45f));
                            DrawCircle(s.cur, w1 * 0.85f, new Color(1f, 0.72f, 0.30f, 0.55f));
                            DrawCircle(s.cur, w0 * 0.7f, new Color(1f, 0.95f, 0.7f, 0.85f));
                        }
                        break;
                    case 2: case 7: case 8: // 奥术 / 亡灵 / 自然：发光法球 + 拖尾
                        DrawLine(tail, s.cur, glow, s.size * 0.75f);
                        DrawCircle(s.cur, s.size * 0.42f, body);
                        DrawCircle(s.cur, s.size * 0.22f, new Color(1f, 1f, 1f, 0.9f));
                        break;
                    case 4: // 炮击：实心弹 + 烟迹
                        DrawLine(tail, s.cur, new Color(0.35f, 0.30f, 0.28f, 0.45f), s.size * 0.55f);
                        DrawCircle(s.cur, s.size * 0.36f, new Color(0.30f, 0.26f, 0.24f, 0.95f));
                        DrawCircle(s.cur - dir * s.size * 0.4f, s.size * 0.30f, new Color(1f, 0.62f, 0.24f, 0.85f));
                        break;
                    case 5: // 暗影：断续残影
                        for (int g = 1; g <= 4; g++)
                            DrawCircle(s.cur - dir * s.size * 0.42f * g, s.size * (0.30f - g * 0.045f), new Color(s.color.R, s.color.G, s.color.B, 0.42f - g * 0.08f));
                        DrawCircle(s.cur, s.size * 0.26f, new Color(0.95f, 0.85f, 1f, 0.9f));
                        break;
                    case 6: // 冲击波：环状推进
                        DrawArc(s.cur, s.size * (0.7f + k * 0.8f), 0, Mathf.Tau, 22, body, 2.5f);
                        DrawCircle(s.cur, s.size * 0.30f, new Color(1f, 0.95f, 0.75f, 0.85f));
                        break;
                    default: // 箭矢 / 弩矢：细线 + 箭头
                        DrawLine(tail, s.cur, glow, 2.2f);
                        {
                            var n = new Vector2(-dir.Y, dir.X);
                            var tip = s.cur + dir * s.size * 0.40f;
                            var poly = new Vector2[] { tip, s.cur - n * s.size * 0.20f, s.cur + n * s.size * 0.20f };
                            DrawColoredPolygon(poly, new Color(1f, 1f, 1f, 0.92f));
                        }
                        break;
                }
            }

            // 命中与死亡特效
            for (int i = 0; i < _puffs.Count; i++)
            {
                var p = _puffs[i];
                float k = 1f - p.life / p.max;
                var c = p.color; c.A *= (1f - k);
                if (p.kind == 6)
                {
                    // 冲击波：扩散圆环
                    DrawArc(p.pos, p.radius * (0.3f + k * 1.5f), 0, Mathf.Tau, 30, c, 2.5f);
                    DrawCircle(p.pos, p.radius * (0.3f + k * 0.7f), new Color(c.R, c.G, c.B, c.A * 0.45f));
                }
                else if (p.kind >= 0 && p.kind != 4)
                {
                    // 命中火花：放射状短线
                    DrawCircle(p.pos, p.radius * (0.35f + k * 0.85f), c);
                    int rays = 4 + (p.kind == 3 ? 3 : 0);
                    for (int r = 0; r < rays; r++)
                    {
                        float ang = r * Mathf.Tau / rays + k * 1.1f;
                        var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                        DrawLine(p.pos + d * p.radius * 0.35f, p.pos + d * p.radius * (0.7f + k * 0.9f),
                            new Color(c.R, c.G, c.B, c.A * 0.8f), 1.8f);
                    }
                }
                else
                    DrawCircle(p.pos, p.radius * (0.4f + k * 0.9f), c);
            }
            }   // end !Use3D
            // 伤害数字
            var font = GetThemeDefaultFont();
            if (font != null)
            {
                for (int i = 0; i < _floats.Count; i++)
                {
                    var f = _floats[i];
                    var c = f.color; c.A = Mathf.Clamp(f.life / f.max, 0f, 1f);
                    DrawString(font, f.pos, f.text, HorizontalAlignment.Center, 0f, (int)f.size, c);
                }
            }

            if (!string.IsNullOrEmpty(selectedSkill) && match.phase == MatchPhase.Battle)
            {
                var mouse = GetLocalMousePosition();
                var sd = match.db.Skill(selectedSkill);
                float r = sd != null ? sd.LvF("radius", 1, 100f) : 100f;
                DrawCircle(mouse, r * _scale, new Color(1f, 0.6f, 0.3f, 0.14f));
                DrawArc(mouse, r * _scale, 0, Mathf.Tau, 48, new Color(1f, 0.75f, 0.4f, 0.9f), 2f);
                // 预览：这个范围能覆盖几个敌人
                int hit = 0;
                var units0 = match.sim.Units;
                var center = ToField(mouse);
                for (int i = 0; i < units0.Count; i++)
                {
                    var u0 = units0[i];
                    if (!u0.alive) continue;
                    float dx = u0.x - center.X, dy = u0.y - center.Y;
                    if (dx * dx + dy * dy <= r * r) hit++;
                }
                var f0 = GetThemeDefaultFont();
                if (f0 != null)
                {
                    var tp = mouse + new Vector2(0, -r * _scale - 20f);
                    DrawString(f0, tp, "可命中 " + hit + " 个单位", HorizontalAlignment.Center, 0f, 15, new Color(1f, 0.86f, 0.5f));
                }
            }

            DrawHoverInfo();
            DrawBanner();
            if (_shake > 0.2f) DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }

        /// <summary>鼠标悬停在某个单位上时，就地显示它的战况信息。</summary>
        void DrawHoverInfo()
        {
            if (match == null || main == null) return;
            if (match.phase != MatchPhase.Battle && match.phase != MatchPhase.Prep) return;
            var mp = GetLocalMousePosition();
            if (!_field.Grow(30f).HasPoint(mp)) return;
            SimUnit best = null;
            float bestD = 30f * 30f;
            var units = match.sim.Units;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.alive) continue;
                var sp = ToScreen(u.x, u.y);
                float d = sp.DistanceSquaredTo(mp);
                if (d < bestD) { bestD = d; best = u; }
            }
            if (best == null) return;
            var font = GetThemeDefaultFont();
            if (font == null) return;

            var d0 = best.def;
            bool me = main.localIndex == (best.team == Team.Left ? 0 : 1);
            string[] lines = {
                d0.name + "  T" + d0.tier + (me ? "（我方）" : "（敌方）"),
                "生命 " + (int)Mathf.Max(0f, best.hp) + " / " + (int)best.maxHp,
                "物攻 " + (int)best.st.atk + "　魔攻 " + (int)best.st.matk,
                "物防 " + (int)best.st.pdef + "　魔防 " + (int)best.st.mdef,
                "射程 " + (int)best.st.range + "　攻速 " + best.st.atkSpeed.ToString("0.00"),
            };
            float w = 210f, h = 22f + lines.Length * 19f;
            var pos = new Vector2(mp.X + 18f, mp.Y - h - 6f);
            if (pos.X + w > Size.X) pos.X = mp.X - w - 18f;
            if (pos.Y < 4f) pos.Y = mp.Y + 18f;
            DrawRect(new Rect2(pos, new Vector2(w, h)), new Color(0.09f, 0.08f, 0.13f, 0.95f));
            DrawRect(new Rect2(pos, new Vector2(w, h)), new Color(0f, 0f, 0f, 0f), false, 2f, false);
            DrawLine(pos, pos + new Vector2(w, 0), new Color(0.62f, 0.50f, 0.26f), 2f);
            DrawLine(pos + new Vector2(0, h), pos + new Vector2(w, h), new Color(0.62f, 0.50f, 0.26f), 2f);
            DrawLine(pos, pos + new Vector2(0, h), new Color(0.62f, 0.50f, 0.26f), 2f);
            DrawLine(pos + new Vector2(w, 0), pos + new Vector2(w, h), new Color(0.62f, 0.50f, 0.26f), 2f);
            for (int i = 0; i < lines.Length; i++)
                DrawString(font, pos + new Vector2(10, 18 + i * 19), lines[i], HorizontalAlignment.Left, 0f, i == 0 ? 15 : 13,
                    i == 0 ? (me ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.68f, 0.68f)) : UiTheme.Parchment);
        }

        /// <summary>回合过场与结算横幅（都带淡入淡出与缩放动画）。</summary>
        void DrawBanner()
        {
            var font = GetThemeDefaultFont();
            if (font == null) return;

            // 过场闪光：整屏一闪
            if (_flashT > 0f)
            {
                float fa = Mathf.Clamp(_flashT / 0.35f, 0f, 1f) * 0.22f;
                DrawRect(new Rect2(Vector2.Zero, Size), new Color(1f, 0.92f, 0.72f, fa));
            }

            // ---- 回合开始 / 交战开始 ----
            if (_roundBannerT > 0f && !string.IsNullOrEmpty(_roundBanner))
            {
                float k = 1f - _roundBannerT / _roundBannerMax;        // 0→1 进度
                float a = Mathf.Min(1f, k / 0.12f) * Mathf.Min(1f, _roundBannerT / 0.5f);
                float pop = 1f + 0.35f * Mathf.Max(0f, 1f - k / 0.25f); // 进场放大回落
                int fs = (int)(44 * pop);
                var cc = _roundColor; cc.A = a;
                float cy = Size.Y * 0.40f;
                DrawRect(new Rect2(0, cy - 58f, Size.X, 116f), new Color(0.06f, 0.05f, 0.10f, 0.72f * a));
                DrawLine(new Vector2(Size.X * 0.22f, cy - 58f), new Vector2(Size.X * 0.78f, cy - 58f), new Color(0.62f, 0.50f, 0.26f, a), 2f);
                DrawLine(new Vector2(Size.X * 0.22f, cy + 58f), new Vector2(Size.X * 0.78f, cy + 58f), new Color(0.62f, 0.50f, 0.26f, a), 2f);
                DrawString(font, new Vector2(Size.X * 0.5f, cy + 8f), _roundBanner, HorizontalAlignment.Center, -1f, fs, cc);
                var sc = UiTheme.ParchDim; sc.A = a;
                DrawString(font, new Vector2(Size.X * 0.5f, cy + 40f), _roundSub, HorizontalAlignment.Center, -1f, 17, sc);
            }

            // ---- 回合结算结果 ----
            if (_bannerT > 0f && !string.IsNullOrEmpty(_banner))
            {
                float k = 1f - _bannerT / _bannerMax;
                float a = Mathf.Min(1f, k / 0.10f) * Mathf.Min(1f, _bannerT / 0.7f);
                float slide = (1f - Mathf.Min(1f, k / 0.18f)) * 26f;   // 从上方滑入
                var c = _bannerColor; c.A = a;
                float w = 560f, h = 48f;
                var pos = new Vector2((Size.X - w) * 0.5f, _field.Position.Y - 86f - slide);
                DrawRect(new Rect2(pos, new Vector2(w, h)), new Color(0.08f, 0.07f, 0.12f, 0.92f * a));
                DrawLine(pos, pos + new Vector2(w, 0), new Color(0.62f, 0.50f, 0.26f, a), 2f);
                DrawLine(pos + new Vector2(0, h), pos + new Vector2(w, h), new Color(0.62f, 0.50f, 0.26f, a), 2f);
                DrawString(font, pos + new Vector2(w * 0.5f, 33f), _banner, HorizontalAlignment.Center, -1f, 20, c);
            }
        }

        void DrawGround()
        {
            // 金属外框 + 金边
            var outer = new Rect2(_field.Position - new Vector2(8, 8), _field.Size + new Vector2(16, 16));
            DrawRect(outer, new Color(0.16f, 0.13f, 0.12f));
            DrawRect(new Rect2(outer.Position + new Vector2(3, 3), outer.Size - new Vector2(6, 6)), new Color(0.42f, 0.34f, 0.22f));

            // 草地：整张程序化贴图，无接缝，一次绘制
            var arena = FieldArt.Arena();
            if (arena != null) DrawTextureRect(arena, _field, false);
            else DrawRect(_field, new Color(0.35f, 0.57f, 0.24f));

            // 中线：极淡的踩踏痕迹，帮忙判断距离
            float midX = _field.Position.X + _field.Size.X * 0.5f;
            DrawLine(new Vector2(midX, _field.Position.Y + 2), new Vector2(midX, _field.Position.Y + _field.Size.Y - 2),
                     new Color(1f, 1f, 1f, 0.055f), 2f);

            // 上下金色饰线
            var gold = new Color(0.55f, 0.44f, 0.26f, 0.5f);
            DrawLine(new Vector2(_field.Position.X, _field.Position.Y), new Vector2(_field.Position.X + _field.Size.X, _field.Position.Y), gold, 2f);
            DrawLine(new Vector2(_field.Position.X, _field.Position.Y + _field.Size.Y), new Vector2(_field.Position.X + _field.Size.X, _field.Position.Y + _field.Size.Y), gold, 2f);

            // 上下内阴影，给战场一点纵深
            DrawRect(new Rect2(_field.Position, new Vector2(_field.Size.X, 10f)), new Color(0f, 0f, 0f, 0.10f));
            DrawRect(new Rect2(_field.Position + new Vector2(0f, _field.Size.Y - 10f), new Vector2(_field.Size.X, 10f)), new Color(0f, 0f, 0f, 0.10f));
        }

        void DrawUnit(SimUnit u)
        {
            bool left = u.team == Team.Left;
            var tex = UnitSprite.Get(u.def.id);

            // ---- 动作状态机：攻击 > 行走 > 待机 ----
            float period = 1f / Mathf.Max(0.05f, u.st.atkSpeed);
            float since = period - u.attackCd;
            bool attacking = since >= 0f && since < 0.46f;

            var a = AnimOf(u);
            float dt = Mathf.Max(0.0005f, _vtime - a.prevT);
            float ddx = u.x - a.px, ddy = u.y - a.py;
            float spd = a.init ? Mathf.Sqrt(ddx * ddx + ddy * ddy) / dt : 0f;
            a.px = u.x; a.py = u.y; a.prevT = _vtime; a.init = true;

            if (attacking) { a.anim = UnitSprite.Anim.Attack; a.at = since; }
            else if (spd > 4f)
            {
                a.anim = UnitSprite.Anim.Walk;
                a.wt += dt * Mathf.Clamp(spd / 40f, 0.55f, 1.7f);
            }
            else { a.anim = UnitSprite.Anim.Idle; a.it += dt; }

            float t = a.anim == UnitSprite.Anim.Attack ? a.at
                    : a.anim == UnitSprite.Anim.Walk ? a.wt : a.it;
            int frame = UnitSprite.FrameOf(a.anim, t);

            float r = Mathf.Max(5f, u.radius * _scale * 0.95f);
            var p = ToScreen(u.x, u.y);

            if (tex != null)
            {
                float h = Mathf.Max(34f, u.radius * _scale * 6.4f);
                float w = h;
                float foot = h * UnitSprite.FootRatio;
                Color tint;
                if (u.hitUntil > match.sim.time) tint = new Color(1.9f, 1.2f, 1.2f, 1f);
                else if (left) tint = new Color(0.95f, 0.99f, 1f, 1f);
                else tint = new Color(1f, 0.95f, 0.93f, 1f);
                DrawSetTransform(_curShake + p, 0f, new Vector2(left ? 1f : -1f, 1f));
                DrawTextureRectRegion(tex, new Rect2(-w * 0.5f, -foot, w, h), UnitSprite.SrcRect(frame), tint);
                DrawSetTransform(_curShake, 0f, Vector2.One);
            }
            else
            {
                DrawCircle(p, r, left ? LeftColor : RightColor);
                DrawArc(p, r, 0f, Mathf.Tau, 22, new Color(0, 0, 0, 0.55f), 1.5f);
            }

            if (u.st.range > 60f) DrawArc(p, r + 2.5f, 0f, Mathf.Tau, 20, new Color(1, 1, 1, 0.18f), 1.2f);
            if (u.def.tier >= 4) DrawArc(p, r + 5f, 0f, Mathf.Tau, 24, new Color(1f, 0.85f, 0.35f, 0.6f), 1.8f);
            if (u.shield > 0f) DrawArc(p, r + 7f, 0f, Mathf.Tau, 24, new Color(0.6f, 0.9f, 1f, 0.7f), 2f);
            if (u.slowPct > 0f && u.slowUntil > match.sim.time) DrawArc(p, r + 9f, 0f, Mathf.Tau, 24, new Color(0.5f, 0.85f, 1f, 0.5f), 1.5f);

            float bw = Mathf.Max(16f, r * 2.3f);
            float bh = 3.5f;
            var bpos = new Vector2(p.X - bw * 0.5f, p.Y - r - 11f);
            DrawRect(new Rect2(bpos, new Vector2(bw, bh)), new Color(0, 0, 0, 0.65f));
            float ratio = u.maxHp > 0 ? Mathf.Clamp(u.hp / u.maxHp, 0f, 1f) : 0f;
            var hpColor = ratio > 0.5f ? new Color(0.4f, 0.9f, 0.4f) : (ratio > 0.25f ? new Color(0.95f, 0.8f, 0.3f) : new Color(0.95f, 0.35f, 0.35f));
            DrawRect(new Rect2(bpos, new Vector2(bw * ratio, bh)), hpColor);
        }

        void DrawBase(bool left, float hpRatio)
        {
            var color = left ? UiTheme.TeamLeft : UiTheme.TeamRight;
            float w = 46f;
            // 城堡画在战场框内（之前画在框外，被左右资源面板挡住看不见）
            float x = left ? _field.Position.X + 4 : _field.Position.X + _field.Size.X - w - 4;
            float y = _field.Position.Y - 14;
            float h = _field.Size.Y + 28;
            var tower = new Rect2(x, y, w, h);

            // 塔身
            DrawRect(tower, new Color(0.22f, 0.20f, 0.22f));
            DrawRect(new Rect2(tower.Position + new Vector2(3, 3), tower.Size - new Vector2(6, 6)), new Color(0.15f, 0.14f, 0.17f));
            // 石缝
            for (int i = 1; i < 6; i++)
            {
                float ly = tower.Position.Y + tower.Size.Y * i / 6f;
                DrawLine(new Vector2(tower.Position.X + 3, ly), new Vector2(tower.Position.X + tower.Size.X - 3, ly), new Color(0, 0, 0, 0.35f), 1f);
            }
            // 血量填充
            float fillH = tower.Size.Y * Mathf.Clamp(hpRatio, 0f, 1f);
            var hpCol = hpRatio > 0.5f ? color : (hpRatio > 0.25f ? new Color(1f, 0.78f, 0.3f) : new Color(1f, 0.4f, 0.4f));
            DrawRect(new Rect2(tower.Position.X + 3, tower.Position.Y + tower.Size.Y - fillH - 3, tower.Size.X - 6, fillH), new Color(hpCol.R, hpCol.G, hpCol.B, 0.55f));
            DrawRect(tower, new Color(0, 0, 0, 0));
            DrawLine(new Vector2(tower.Position.X, tower.Position.Y), new Vector2(tower.Position.X, tower.Position.Y + tower.Size.Y), new Color(0.55f, 0.44f, 0.26f), 2f);
            DrawLine(new Vector2(tower.Position.X + tower.Size.X, tower.Position.Y), new Vector2(tower.Position.X + tower.Size.X, tower.Position.Y + tower.Size.Y), new Color(0.55f, 0.44f, 0.26f), 2f);

            // 城垛
            DrawRect(new Rect2(tower.Position.X - 5, tower.Position.Y - 12, tower.Size.X + 10, 14), new Color(0.26f, 0.23f, 0.25f));
            for (int i = 0; i < 4; i++)
                DrawRect(new Rect2(tower.Position.X - 3 + i * (tower.Size.X + 6) / 4f, tower.Position.Y - 22, 11, 11), color.Darkened(0.35f));

            // 城门
            float gateW = tower.Size.X * 0.6f;
            var gate = new Rect2(tower.Position.X + (tower.Size.X - gateW) * 0.5f, tower.Position.Y + tower.Size.Y - 46, gateW, 46);
            DrawRect(gate, new Color(0.30f, 0.20f, 0.12f));
            DrawArc(new Vector2(gate.Position.X + gate.Size.X * 0.5f, gate.Position.Y + 8), gate.Size.X * 0.5f, Mathf.Pi, Mathf.Tau, 20, new Color(0.30f, 0.20f, 0.12f), 16f);
            DrawRect(new Rect2(gate.Position.X + gate.Size.X * 0.5f - 1.5f, gate.Position.Y, 3, gate.Size.Y), new Color(0, 0, 0, 0.5f));

            // 旗帜（血量越低越下垂）
            float bx = tower.Position.X + tower.Size.X * 0.5f;
            float by = tower.Position.Y - 22;
            float fh = 30f + 18f * Mathf.Clamp(hpRatio, 0f, 1f);
            float wave = Mathf.Sin(_vtime * 2.2f) * 3f;
            DrawLine(new Vector2(bx, by), new Vector2(bx, by - fh), new Color(0.5f, 0.42f, 0.3f), 2.5f);
            var flag = new Vector2[] {
                new Vector2(bx, by - fh),
                new Vector2(bx + (left ? 26 : -26), by - fh + 6 + wave),
                new Vector2(bx + (left ? 24 : -24), by - fh + 20 + wave),
                new Vector2(bx, by - fh + 22),
            };
            DrawColoredPolygon(flag, new Color(color.R, color.G, color.B, 0.9f));
        }

        static Color ClassTint(UnitDef d)
        {
            if (d.HasTag("undead")) return new Color(0.72f, 0.62f, 0.95f);
            if (d.HasTag("machine")) return new Color(0.82f, 0.86f, 0.92f);
            if (d.HasTag("magic")) return new Color(0.78f, 0.6f, 1.0f);
            if (d.HasTag("beast")) return new Color(0.85f, 0.8f, 0.6f);
            if (d.HasTag("hero")) return new Color(1f, 0.88f, 0.5f);
            if (d.HasTag("assassin")) return new Color(0.85f, 0.65f, 0.9f);
            return Colors.White;
        }

        public override void _Input(InputEvent @event)
        {
            if (match == null || main == null) return;
            if (main.gemShopOpen) return;
            if (match.phase != MatchPhase.Battle && match.phase != MatchPhase.Prep) return;
            var mb = @event as InputEventMouseButton;
            if (mb == null || !mb.Pressed) return;
            if (mb.ButtonIndex != MouseButton.Left && mb.ButtonIndex != MouseButton.Right) return;
            if (main.IsPointerOverUi(mb.Position)) return;
            // 战场矩形稍微内缩，避免和两侧面板的边缘重叠
            var r = new Rect2(_field.Position + new Vector2(5, 5), _field.Size - new Vector2(10, 10));
            if (main.clickTest)
                GD.Print("CLICKTEST _Input pos=" + mb.Position + " inField=" + r.HasPoint(mb.Position)
                    + " field=" + _field + " overUi=" + main.IsPointerOverUi(mb.Position));
            if (!r.HasPoint(mb.Position)) return;
            var f = ToField(mb.Position);
            if (mb.ButtonIndex == MouseButton.Left) main.OnFieldClick(f, !string.IsNullOrEmpty(selectedSkill));
            else main.OnFieldRightClick(f);
            GetViewport().SetInputAsHandled();
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (match == null || main == null) return;
            // 正常情况下点击已由 _Input 处理（_GuiInput 会被 HUD 的 CanvasLayer 拦掉，实际很少走到这里）
            if (@event is InputEventMouseButton) return;
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                var f = ToField(mb.Position);
                bool inside = f.X >= 0 && f.X <= match.sim.fieldWidth && f.Y >= 0 && f.Y <= match.sim.fieldHeight;
                if (!inside) return;
                main.OnFieldClick(f, !string.IsNullOrEmpty(selectedSkill));
                AcceptEvent();
            }
            else if (@event is InputEventMouseButton rb && rb.Pressed && rb.ButtonIndex == MouseButton.Right)
            {
                var f = ToField(rb.Position);
                if (f.X >= 0 && f.X <= match.sim.fieldWidth && f.Y >= 0 && f.Y <= match.sim.fieldHeight)
                {
                    main.OnFieldRightClick(f);
                    AcceptEvent();
                }
            }
        }
    }
}
