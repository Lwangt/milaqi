using System;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>战场渲染层：单节点 _Draw 批绘制所有单位，逻辑与表现完全分离。</summary>
    public partial class BattleView : Control
    {
        public Match match;
        public Main main;
        public string selectedSkill;
        public bool showDebug;

        Rect2 _field = new Rect2(100, 185, 1400, 420);
        float _scale = 1f;

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
        static readonly Color LeftColor = new Color(0.36f, 0.68f, 1.0f);
        static readonly Color RightColor = new Color(1.0f, 0.42f, 0.45f);
        static readonly Color LeftDark = new Color(0.16f, 0.34f, 0.58f);
        static readonly Color RightDark = new Color(0.58f, 0.20f, 0.24f);

        public override void _Draw()
        {
            if (match == null) return;
            var sim = match.sim;
            var sz = Size;

            DrawRect(new Rect2(Vector2.Zero, sz), BgTop);
            DrawRect(new Rect2(0, sz.Y * 0.55f, sz.X, sz.Y * 0.45f), BgBottom);

            // 战场地面
            DrawRect(_field, new Color(0.13f, 0.15f, 0.20f));
            var inner = new Rect2(_field.Position + new Vector2(2, 2), _field.Size - new Vector2(4, 4));
            DrawRect(inner, new Color(0.09f, 0.105f, 0.14f));

            // 左右基地
            float baseW = 26f * _scale;
            var leftBase = new Rect2(_field.Position.X - baseW, _field.Position.Y, baseW, _field.Size.Y);
            var rightBase = new Rect2(_field.Position.X + _field.Size.X, _field.Position.Y, baseW, _field.Size.Y);
            DrawBase(leftBase, LeftColor, match.players[0].baseHp / Math.Max(1f, match.db.Balance.baseHp));
            DrawBase(rightBase, RightColor, match.players[1].baseHp / Math.Max(1f, match.db.Balance.baseHp));

            // 中线
            float midX = _field.Position.X + _field.Size.X * 0.5f;
            DrawLine(new Vector2(midX, _field.Position.Y), new Vector2(midX, _field.Position.Y + _field.Size.Y),
                new Color(1, 1, 1, 0.08f), 2f);

            // 敌方半场提示（部署时）
            int li = main != null ? main.localIndex : 0;
            if (match.phase == MatchPhase.Prep && match.players[li].pendingDeploy.Count > 0)
            {
                bool leftSide = li == 0;
                var zone = leftSide
                    ? new Rect2(_field.Position, new Vector2(_field.Size.X * 0.5f, _field.Size.Y))
                    : new Rect2(_field.Position + new Vector2(_field.Size.X * 0.5f, 0f), new Vector2(_field.Size.X * 0.5f, _field.Size.Y));
                DrawRect(zone, leftSide ? new Color(0.36f, 0.68f, 1.0f, 0.06f) : new Color(1.0f, 0.42f, 0.45f, 0.06f));
            }

            // 单位
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var u = sim.Units[i];
                if (!u.alive) continue;
                DrawUnit(u);
            }

            // 特效事件
            for (int i = 0; i < sim.Events.Count; i++)
            {
                var e = sim.Events[i];
                switch (e.kind)
                {
                    case SimEventKind.Shot:
                        {
                            var a = ToScreen(e.x, e.y);
                            var b = ToScreen(e.x2, e.y2);
                            var c = e.team == Team.Left ? new Color(0.7f, 0.9f, 1f, 0.5f) : new Color(1f, 0.8f, 0.8f, 0.5f);
                            DrawLine(a, b, c, 1.6f);
                            break;
                        }
                    case SimEventKind.Hit:
                        {
                            var p = ToScreen(e.x2, e.y2);
                            DrawCircle(p, 4.5f * _scale, new Color(1f, 0.95f, 0.6f, 0.65f));
                            break;
                        }
                    case SimEventKind.Cast:
                        {
                            var p = ToScreen(e.x, e.y);
                            DrawCircle(p, e.radius * _scale, new Color(0.7f, 0.5f, 1f, 0.16f));
                            DrawArc(p, e.radius * _scale, 0, Mathf.Tau, 48, new Color(0.8f, 0.7f, 1f, 0.75f), 2f);
                            break;
                        }
                    case SimEventKind.Heal:
                        {
                            var p = ToScreen(e.x, e.y);
                            DrawCircle(p, 6f * _scale, new Color(0.4f, 1f, 0.5f, 0.5f));
                            break;
                        }
                }
            }
            sim.ClearEvents();

            // 技能瞄准
            if (!string.IsNullOrEmpty(selectedSkill) && match.phase == MatchPhase.Battle)
            {
                var mouse = GetLocalMousePosition();
                var sd = match.db.Skill(selectedSkill);
                float r = sd != null ? sd.LvF("radius", 1, 100f) : 100f;
                DrawCircle(mouse, r * _scale, new Color(1f, 0.6f, 0.3f, 0.14f));
                DrawArc(mouse, r * _scale, 0, Mathf.Tau, 48, new Color(1f, 0.75f, 0.4f, 0.9f), 2f);
            }
        }

        void DrawBase(Rect2 rect, Color color, float hpRatio)
        {
            DrawRect(rect, color.Darkened(0.45f));
            float h = rect.Size.Y * Mathf.Clamp(hpRatio, 0f, 1f);
            DrawRect(new Rect2(rect.Position.X, rect.Position.Y + rect.Size.Y - h, rect.Size.X, h), color);
        }

        void DrawUnit(SimUnit u)
        {
            var p = ToScreen(u.x, u.y);
            float r = Math.Max(5f, u.radius * _scale * 0.92f);
            bool left = u.team == Team.Left;
            var body = left ? LeftColor : RightColor;
            if (u.def.tier >= 4) body = left ? new Color(0.55f, 0.85f, 1f) : new Color(1f, 0.65f, 0.55f);
            if (u.def.HasTag("undead")) body = body.Lerp(new Color(0.55f, 0.45f, 0.75f), 0.45f);
            if (u.def.HasTag("machine")) body = body.Lerp(new Color(0.7f, 0.7f, 0.7f), 0.35f);
            if (u.def.HasTag("magic")) body = body.Lerp(new Color(0.7f, 0.45f, 1f), 0.4f);
            if (!left) body = body.Darkened(0.05f);

            if (u.hitUntil > match.sim.time) body = body.Lerp(Colors.White, 0.65f);

            DrawCircle(p, r, body);
            DrawArc(p, r, 0, Mathf.Tau, 22, new Color(0, 0, 0, 0.55f), 1.5f);

            // 远程单位加一圈细环表示射程类型
            if (u.st.range > 60f) DrawArc(p, r + 2.5f, 0, Mathf.Tau, 20, new Color(1, 1, 1, 0.22f), 1.2f);
            if (u.def.tier >= 4) DrawArc(p, r + 4.5f, 0, Mathf.Tau, 24, new Color(1f, 0.85f, 0.35f, 0.55f), 1.6f);
            if (u.shield > 0f) DrawArc(p, r + 6.5f, 0, Mathf.Tau, 24, new Color(0.6f, 0.9f, 1f, 0.7f), 2f);
            if (u.slowPct > 0f && u.slowUntil > match.sim.time) DrawArc(p, r + 8.5f, 0, Mathf.Tau, 24, new Color(0.5f, 0.85f, 1f, 0.5f), 1.5f);

            // 血条
            float bw = r * 2.2f;
            float bh = 3.5f;
            var bpos = new Vector2(p.X - bw * 0.5f, p.Y - r - 9f);
            DrawRect(new Rect2(bpos, new Vector2(bw, bh)), new Color(0, 0, 0, 0.6f));
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
        }
    }
}
