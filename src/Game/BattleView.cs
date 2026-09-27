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

        Rect2 _field = new Rect2(100, 185, 1400, 420);
        float _scale = 1f;
        float _vtime;

        sealed class FloatText { public Vector2 pos; public string text; public float life, max; public Color color; public float size; }
        sealed class Shot { public Vector2 from, to, cur; public float t, dur; public Color color; public bool magic; }
        sealed class Puff { public Vector2 pos; public float life, max; public Color color; public float radius; }

        readonly List<FloatText> _floats = new List<FloatText>();
        readonly List<Shot> _shots = new List<Shot>();
        readonly List<Puff> _puffs = new List<Puff>();

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.All;
        }

        public void Layout()
        {
            var sz = Size;
            float margin = 90f;
            float top = 150f;
            float bottom = 270f;
            float availW = Math.Max(200f, sz.X - margin * 2f);
            float availH = Math.Max(120f, sz.Y - top - bottom);
            float s = Math.Min(availW / match.sim.fieldWidth, availH / match.sim.fieldHeight);
            _scale = s;
            float w = match.sim.fieldWidth * s;
            float h = match.sim.fieldHeight * s;
            float x = (sz.X - w) * 0.5f;
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

            var events = match.sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                switch (e.kind)
                {
                    case SimEventKind.Hit:
                        if (e.value >= 1f && _floats.Count < 80)
                            _floats.Add(new FloatText { pos = ToScreen(e.x2, e.y2 - 12f), text = ((int)e.value).ToString(), life = 0.7f, max = 0.7f, color = new Color(1f, 0.92f, 0.55f), size = 12f });
                        _puffs.Add(new Puff { pos = ToScreen(e.x2, e.y2), life = 0.18f, max = 0.18f, color = new Color(1f, 0.95f, 0.7f, 0.7f), radius = 7f });
                        break;
                    case SimEventKind.Shot:
                        _shots.Add(new Shot
                        {
                            from = ToScreen(e.x, e.y), to = ToScreen(e.x2, e.y2), cur = ToScreen(e.x, e.y),
                            t = 0f, dur = 0.16f,
                            color = e.team == Team.Left ? new Color(0.75f, 0.9f, 1f) : new Color(1f, 0.8f, 0.8f),
                            magic = e.value > 0f && e.value < 0f
                        });
                        if (e.value >= 1f && _floats.Count < 80)
                            _floats.Add(new FloatText { pos = ToScreen(e.x2, e.y2 - 12f), text = ((int)e.value).ToString(), life = 0.7f, max = 0.7f, color = new Color(1f, 0.85f, 0.5f), size = 12f });
                        break;
                    case SimEventKind.Death:
                        _puffs.Add(new Puff { pos = ToScreen(e.x, e.y), life = 0.5f, max = 0.5f, color = e.team == Team.Left ? new Color(0.5f, 0.7f, 1f, 0.6f) : new Color(1f, 0.55f, 0.55f, 0.6f), radius = 26f });
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
                DrawCircle(new Vector2(p.X, p.Y + u.radius * _scale * 0.55f), u.radius * _scale * 0.85f, new Color(0, 0, 0, 0.25f));
            }
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var u = sim.Units[i];
                if (!u.alive) continue;
                DrawUnit(u);
            }

            // 弹道
            for (int i = 0; i < _shots.Count; i++)
            {
                var s = _shots[i];
                DrawLine(s.cur, s.to, new Color(s.color.R, s.color.G, s.color.B, 0.35f), 1.4f);
                DrawCircle(s.cur, 3.2f, s.color);
            }
            // 爆点
            for (int i = 0; i < _puffs.Count; i++)
            {
                var p = _puffs[i];
                float k = 1f - p.life / p.max;
                var c = p.color; c.A *= (1f - k);
                DrawCircle(p.pos, p.radius * (0.4f + k * 0.9f), c);
            }
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
            }
        }

        void DrawGround()
        {
            DrawRect(_field, new Color(0.13f, 0.15f, 0.20f));
            var inner = new Rect2(_field.Position + new Vector2(2, 2), _field.Size - new Vector2(4, 4));
            DrawRect(inner, new Color(0.095f, 0.11f, 0.145f));
            int lanes = 5;
            for (int i = 1; i < lanes; i++)
            {
                float y = _field.Position.Y + _field.Size.Y * i / lanes;
                DrawLine(new Vector2(_field.Position.X, y), new Vector2(_field.Position.X + _field.Size.X, y), new Color(1, 1, 1, 0.035f), 1f);
            }
            DrawRect(new Rect2(_field.Position, _field.Size), new Color(0, 0, 0, 0));
            DrawLine(new Vector2(_field.Position.X, _field.Position.Y), new Vector2(_field.Position.X + _field.Size.X, _field.Position.Y), new Color(0.4f, 0.5f, 0.7f, 0.35f), 2f);
            DrawLine(new Vector2(_field.Position.X, _field.Position.Y + _field.Size.Y), new Vector2(_field.Position.X + _field.Size.X, _field.Position.Y + _field.Size.Y), new Color(0.4f, 0.5f, 0.7f, 0.35f), 2f);
        }

        void DrawBase(bool left, float hpRatio)
        {
            var color = left ? LeftColor : RightColor;
            float w = 30f;
            float x = left ? _field.Position.X - w : _field.Position.X + _field.Size.X;
            var rect = new Rect2(x, _field.Position.Y, w, _field.Size.Y);

            DrawRect(rect, new Color(0.16f, 0.17f, 0.22f));
            float h = rect.Size.Y * Mathf.Clamp(hpRatio, 0f, 1f);
            DrawRect(new Rect2(rect.Position.X, rect.Position.Y + rect.Size.Y - h, rect.Size.X, h), color.Darkened(0.25f));

            int blocks = 5;
            for (int i = 0; i < blocks; i++)
            {
                float by = rect.Position.Y + rect.Size.Y * i / blocks;
                float bh = rect.Size.Y / blocks - 4f;
                float fill = Mathf.Clamp(hpRatio * blocks - i, 0f, 1f);
                DrawRect(new Rect2(rect.Position.X + 3, by + 2, rect.Size.X - 6, bh), new Color(0, 0, 0, 0.45f));
                DrawRect(new Rect2(rect.Position.X + 3, by + 2, (rect.Size.X - 6) * fill, bh), hpRatio > 0.5f ? color : (hpRatio > 0.25f ? new Color(1f, 0.78f, 0.3f) : new Color(1f, 0.4f, 0.4f)));
            }
            // 城垛
            DrawRect(new Rect2(rect.Position.X - 4, _field.Position.Y - 12, rect.Size.X + 8, 12), color.Darkened(0.4f));
            for (int i = 0; i < 3; i++)
                DrawRect(new Rect2(rect.Position.X - 2 + i * (rect.Size.X + 4) / 3f, _field.Position.Y - 20, 10, 9), color.Darkened(0.3f));
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

        void DrawUnit(SimUnit u)
        {
            bool left = u.team == Team.Left;
            var baseCol = left ? LeftColor : RightColor;
            var tint = baseCol.Lerp(ClassTint(u.def), 0.45f);
            if (u.hitUntil > match.sim.time) tint = tint.Lerp(Colors.White, 0.7f);

            float r = Math.Max(5f, u.radius * _scale * 0.95f);
            float bob = Mathf.Sin(_vtime * 7f + u.id * 1.7f) * (_vtime > 0 ? 1.6f : 0f);
            float period = 1f / Mathf.Max(0.05f, u.st.atkSpeed);
            float since = period - u.attackCd;
            float lunge = since >= 0f && since < 0.16f ? (1f - since / 0.16f) * 5f : 0f;
            float dir = left ? 1f : -1f;

            var p = ToScreen(u.x, u.y) + new Vector2(lunge * dir, bob);

            var tex = UnitArt.Get(u.def.id);
            if (tex != null)
            {
                float size = r * 2.9f;
                DrawSetTransform(p, 0f, new Vector2(left ? 1f : -1f, 1f));
                DrawTextureRect(tex, new Rect2(-size * 0.5f, -size * 0.5f, size, size), false, tint);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }
            else
            {
                DrawCircle(p, r, tint);
                DrawArc(p, r, 0, Mathf.Tau, 22, new Color(0, 0, 0, 0.55f), 1.5f);
            }

            if (u.st.range > 60f) DrawArc(p, r + 2.5f, 0, Mathf.Tau, 20, new Color(1, 1, 1, 0.2f), 1.2f);
            if (u.def.tier >= 4) DrawArc(p, r + 5f, 0, Mathf.Tau, 24, new Color(1f, 0.85f, 0.35f, 0.6f), 1.8f);
            if (u.shield > 0f) DrawArc(p, r + 7f, 0, Mathf.Tau, 24, new Color(0.6f, 0.9f, 1f, 0.7f), 2f);
            if (u.slowPct > 0f && u.slowUntil > match.sim.time) DrawArc(p, r + 9f, 0, Mathf.Tau, 24, new Color(0.5f, 0.85f, 1f, 0.5f), 1.5f);

            float bw = Mathf.Max(16f, r * 2.3f);
            float bh = 3.5f;
            var bpos = new Vector2(p.X - bw * 0.5f, p.Y - r - 11f);
            DrawRect(new Rect2(bpos, new Vector2(bw, bh)), new Color(0, 0, 0, 0.65f));
            float ratio = u.maxHp > 0 ? Mathf.Clamp(u.hp / u.maxHp, 0f, 1f) : 0f;
            var hpColor = ratio > 0.5f ? new Color(0.4f, 0.9f, 0.4f) : (ratio > 0.25f ? new Color(0.95f, 0.8f, 0.3f) : new Color(0.95f, 0.35f, 0.35f));
            DrawRect(new Rect2(bpos, new Vector2(bw * ratio, bh)), hpColor);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (match == null || main == null) return;
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
