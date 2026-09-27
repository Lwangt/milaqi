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
            // 外框：暗色魔法石
            var outer = new Rect2(_field.Position - new Vector2(8, 8), _field.Size + new Vector2(16, 16));
            DrawRect(outer, new Color(0.16f, 0.13f, 0.12f));
            DrawRect(new Rect2(outer.Position + new Vector2(3, 3), outer.Size - new Vector2(6, 6)), new Color(0.42f, 0.34f, 0.22f));
            DrawRect(_field, new Color(0.11f, 0.10f, 0.13f));

            // 石板纹理
            float tile = _field.Size.Y / 5f;
            int cols = Mathf.CeilToInt(_field.Size.X / tile);
            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float x = _field.Position.X + c * tile;
                    float y = _field.Position.Y + r * tile;
                    var shade = ((r * 31 + c * 17) & 3) == 0 ? 0.185f : 0.150f;
                    DrawRect(new Rect2(x + 1, y + 1, tile - 2, tile - 2), new Color(shade, shade * 0.94f, shade * 1.18f));
                }
            }
            // 中央大道
            float midY = _field.Position.Y + _field.Size.Y * 0.5f;
            DrawRect(new Rect2(_field.Position.X, midY - _field.Size.Y * 0.16f, _field.Size.X, _field.Size.Y * 0.32f), new Color(0.16f, 0.15f, 0.20f, 0.75f));
            for (int i = 0; i < 5; i++)
            {
                float y = _field.Position.Y + _field.Size.Y * (i + 1) / 6f;
                DrawLine(new Vector2(_field.Position.X, y), new Vector2(_field.Position.X + _field.Size.X, y), new Color(1, 1, 1, 0.03f), 1f);
            }
            DrawLine(new Vector2(_field.Position.X, _field.Position.Y), new Vector2(_field.Position.X + _field.Size.X, _field.Position.Y), new Color(0.55f, 0.44f, 0.26f, 0.55f), 2f);
            DrawLine(new Vector2(_field.Position.X, _field.Position.Y + _field.Size.Y), new Vector2(_field.Position.X + _field.Size.X, _field.Position.Y + _field.Size.Y), new Color(0.55f, 0.44f, 0.26f, 0.55f), 2f);

            // 中央符文法阵
            float cx = _field.Position.X + _field.Size.X * 0.5f;
            var arc = UiTheme.Arcane;
            float pulse = 0.5f + 0.5f * Mathf.Sin(_vtime * 1.2f);
            DrawCircle(new Vector2(cx, midY), _field.Size.Y * 0.40f, new Color(arc.R, arc.G, arc.B, 0.05f + 0.03f * pulse));
            DrawArc(new Vector2(cx, midY), _field.Size.Y * 0.40f, 0, Mathf.Tau, 64, new Color(arc.R, arc.G, arc.B, 0.30f + 0.15f * pulse), 2f);
            DrawArc(new Vector2(cx, midY), _field.Size.Y * 0.30f, _vtime * 0.4f, _vtime * 0.4f + 2.2f, 40, new Color(arc.R, arc.G, arc.B, 0.22f), 2f);
            DrawArc(new Vector2(cx, midY), _field.Size.Y * 0.30f, _vtime * 0.4f + 3.14f, _vtime * 0.4f + 5.34f, 40, new Color(arc.R, arc.G, arc.B, 0.22f), 2f);
            for (int i = 0; i < 8; i++)
            {
                float a = _vtime * 0.25f + i * Mathf.Pi / 4f;
                var p = new Vector2(cx + Mathf.Cos(a) * _field.Size.Y * 0.40f, midY + Mathf.Sin(a) * _field.Size.Y * 0.40f);
                DrawCircle(p, 2.5f, new Color(UiTheme.Gold.R, UiTheme.Gold.G, UiTheme.Gold.B, 0.55f));
            }
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

            var tex = UnitPortrait.Get(u.def.id);
            if (tex != null)
            {
                float size = r * 4.0f;
                // 3D 立绘本身已是类别配色，这里只叠加一层阵营色调区分敌我
                var teamTint = Colors.White.Lerp(left ? new Color(0.62f, 0.82f, 1f) : new Color(1f, 0.66f, 0.68f), 0.55f);
                DrawSetTransform(p, 0f, new Vector2(left ? 1f : -1f, 1f));
                DrawTextureRect(tex, new Rect2(-size * 0.5f, -size * 0.5f, size, size), false, teamTint);
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
