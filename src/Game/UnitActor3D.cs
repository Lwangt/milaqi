using System;
using System.Collections.Generic;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>
    /// 战场上的 3D 单位：带骨骼层级（胯-躯干-头-四肢-武器）与程序化动作。
    /// 动作由代码驱动，不需要美术资源：
    ///   待机 = 呼吸起伏 + 轻微摆动
    ///   行走 = 摆腿摆臂 + 身体上下起伏 + 前倾
    ///   攻击 = 抬手蓄力 → 挥下 + 前冲 + 轻微扭转
    ///   受击 = 后仰 + 闪白
    ///   阵亡 = 倒地 + 沉入地面
    /// </summary>
    public partial class UnitActor3D : Node3D
    {
        public SimUnit unit;
        public Node3D Root;          // 整体（用于前冲/倒地）
        public Node3D Hips;
        public Node3D Torso;
        public Node3D Head;
        public Node3D ArmL, ArmR, ForeL, ForeR;
        public Node3D LegL, LegR, ShinL, ShinR;
        public Node3D Weapon;
        public Node3D Cape;
        public Node3D Ring;

        readonly List<StandardMaterial3D> _mats = new List<StandardMaterial3D>();
        readonly List<Color> _baseCols = new List<Color>();

        float _phase;
        float _attackT;
        float _attackDur = 0.32f;
        float _hitT;
        float _deathT = -1f;
        float _lungeT;
        public float AnimSpeed = 1f;
        public bool IsBeast;
        public bool IsFlying;
        public bool IsRobed;
        float _bodyH = 1.8f;
        float _groundY;

        // ------------------------------------------------------------------ 构建
        public void Setup(SimUnit u, bool leftTeam)
        {
            unit = u;
            var d = u.def;
            var pal = UnitModel.PaletteOf(d);
            var plan = UnitModel.PlanOf(d);

            IsBeast = plan == UnitModel.Plan.Beast || plan == UnitModel.Plan.Spider;
            IsFlying = plan == UnitModel.Plan.Flyer || plan == UnitModel.Plan.Dragon;
            IsRobed = plan == UnitModel.Plan.Robed;

            float s = (0.86f + (d.tier - 1) * 0.06f) * (0.85f + u.radius * 0.16f);
            _bodyH = 1.75f * s;

            Root = new Node3D();
            AddChild(Root);
            Root.Scale = Vector3.One * s;

            // 阵营色：整体压一层微色，并加脚下光环
            var teamCol = leftTeam ? new Color(0.42f, 0.68f, 1f) : new Color(1f, 0.48f, 0.46f);

            Hips = new Node3D(); Hips.Position = new Vector3(0, 0.86f, 0); Root.AddChild(Hips);
            Torso = new Node3D(); Torso.Position = new Vector3(0, 0.30f, 0); Hips.AddChild(Torso);
            Head = new Node3D(); Head.Position = new Vector3(0, 0.62f, 0); Torso.AddChild(Head);

            BuildLegs(d, pal, s);
            BuildTorso(d, pal, plan);
            BuildArms(d, pal, plan);
            BuildHead(d, pal, plan);
            BuildWeapon(d, pal, plan);

            // 脚下阵营光环
            Ring = new Node3D();
            var ringMesh = new TorusMesh(); ringMesh.InnerRadius = 0.42f; ringMesh.OuterRadius = 0.52f;
            var ringMat = new StandardMaterial3D();
            ringMat.AlbedoColor = new Color(teamCol.R, teamCol.G, teamCol.B, 0.55f);
            ringMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            ringMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            ringMat.EmissionEnabled = true; ringMat.Emission = teamCol; ringMat.EmissionEnergyMultiplier = 1.4f;
            var rm = new MeshInstance3D(); rm.Mesh = ringMesh; rm.MaterialOverride = ringMat;
            rm.Position = new Vector3(0, 0.03f, 0);
            rm.RotationDegrees = new Vector3(90, 0, 0);
            Ring.AddChild(rm);
            Ring.Scale = Vector3.One / s * (u.radius * 0.62f);
            AddChild(Ring);

            _groundY = 0f;
        }

        MeshInstance3D Part(Node3D parent, Mesh mesh, Color c, Vector3 pos, Vector3 rot = default, float metallic = 0.05f, float rough = 0.8f, bool emissive = false)
        {
            var mat = new StandardMaterial3D();
            mat.AlbedoColor = c;
            mat.Metallic = metallic;
            mat.Roughness = rough;
            if (emissive) { mat.EmissionEnabled = true; mat.Emission = c; mat.EmissionEnergyMultiplier = 1.2f; }
            _mats.Add(mat); _baseCols.Add(c);
            var mi = new MeshInstance3D();
            mi.Mesh = mesh; mi.MaterialOverride = mat;
            mi.Position = pos;
            if (rot != default) mi.RotationDegrees = rot;
            parent.AddChild(mi);
            return mi;
        }

        static BoxMesh Box(float x, float y, float z) { var b = new BoxMesh(); b.Size = new Vector3(x, y, z); return b; }
        static SphereMesh Ball(float r) { var m = new SphereMesh(); m.Radius = r; m.Height = r * 2f; m.RadialSegments = 14; m.Rings = 8; return m; }
        static CapsuleMesh Cap(float r, float h) { var m = new CapsuleMesh(); m.Radius = r; m.Height = h; m.RadialSegments = 12; return m; }
        static CylinderMesh Cyl(float rt, float rb, float h) { var m = new CylinderMesh(); m.TopRadius = rt; m.BottomRadius = rb; m.Height = h; m.RadialSegments = 12; return m; }

        void BuildLegs(UnitDef d, UnitModel.Palette pal, float s)
        {
            if (IsBeast) return;   // 兽形用四条腿，另行处理
            LegL = new Node3D(); LegL.Position = new Vector3(-0.14f, 0, 0); Hips.AddChild(LegL);
            LegR = new Node3D(); LegR.Position = new Vector3(0.14f, 0, 0); Hips.AddChild(LegR);
            foreach (var leg in new[] { LegL, LegR })
            {
                Part(leg, Cap(0.075f, 0.42f), IsRobed ? pal.Cloth : pal.Main, new Vector3(0, -0.20f, 0));
                var shin = new Node3D(); shin.Position = new Vector3(0, -0.42f, 0); leg.AddChild(shin);
                Part(shin, Cap(0.065f, 0.38f), pal.Cloth, new Vector3(0, -0.18f, 0));
                Part(shin, Box(0.14f, 0.07f, 0.24f), pal.Metal, new Vector3(0, -0.38f, 0.04f), default, 0.5f, 0.45f);
                if (leg == LegL) ShinL = shin; else ShinR = shin;
            }
        }

        void BuildTorso(UnitDef d, UnitModel.Palette pal, UnitModel.Plan plan)
        {
            float wide = plan == UnitModel.Plan.Bulky ? 0.62f : (plan == UnitModel.Plan.Giant ? 0.72f : 0.50f);
            float tall = plan == UnitModel.Plan.Giant ? 0.72f : 0.60f;
            Part(Torso, Box(wide, tall, 0.34f), pal.Main, new Vector3(0, tall * 0.5f - 0.06f, 0));
            if (plan == UnitModel.Plan.Bulky || plan == UnitModel.Plan.Giant)
                Part(Torso, Box(wide * 1.08f, tall * 0.55f, 0.40f), pal.Metal, new Vector3(0, tall * 0.62f, 0), default, 0.7f, 0.35f);
            Part(Torso, Box(wide * 0.9f, 0.14f, 0.36f), pal.Accent, new Vector3(0, 0.08f, 0));
            // 披风
            Cape = new Node3D(); Cape.Position = new Vector3(0, tall * 0.85f, -0.17f); Torso.AddChild(Cape);
            Part(Cape, Box(wide * 0.95f, tall * 1.15f, 0.06f), pal.Cloth, new Vector3(0, -tall * 0.55f, -0.03f));
            // 肩甲
            Part(Torso, Ball(wide * 0.30f), pal.Metal, new Vector3(-wide * 0.62f, tall * 0.86f, 0), default, 0.7f, 0.35f);
            Part(Torso, Ball(wide * 0.30f), pal.Metal, new Vector3(wide * 0.62f, tall * 0.86f, 0), default, 0.7f, 0.35f);
        }

        void BuildArms(UnitDef d, UnitModel.Palette pal, UnitModel.Plan plan)
        {
            if (IsBeast) return;
            float wide = plan == UnitModel.Plan.Bulky ? 0.62f : (plan == UnitModel.Plan.Giant ? 0.72f : 0.50f);
            ArmL = new Node3D(); ArmL.Position = new Vector3(-wide * 0.60f, 0.46f, 0); Torso.AddChild(ArmL);
            ArmR = new Node3D(); ArmR.Position = new Vector3(wide * 0.60f, 0.46f, 0); Torso.AddChild(ArmR);
            foreach (var arm in new[] { ArmL, ArmR })
            {
                Part(arm, Cap(0.07f, 0.34f), pal.Main, new Vector3(0, -0.17f, 0));
                var fore = new Node3D(); fore.Position = new Vector3(0, -0.34f, 0); arm.AddChild(fore);
                Part(fore, Cap(0.062f, 0.30f), pal.Cloth, new Vector3(0, -0.15f, 0));
                Part(fore, Ball(0.075f), pal.Metal, new Vector3(0, -0.31f, 0), default, 0.35f, 0.6f);
                if (arm == ArmL) ForeL = fore; else ForeR = fore;
            }
        }

        void BuildHead(UnitDef d, UnitModel.Palette pal, UnitModel.Plan plan)
        {
            float r = plan == UnitModel.Plan.Giant ? 0.20f : 0.155f;
            Part(Head, Ball(r), pal.Main, new Vector3(0, r * 0.9f, 0));
            Part(Head, Box(r * 1.5f, r * 0.55f, r * 1.2f), pal.Metal, new Vector3(0, r * 0.55f, 0.02f), default, 0.6f, 0.4f);
            // 眼睛（发光）
            Part(Head, Ball(r * 0.22f), pal.Glow, new Vector3(-r * 0.38f, r * 1.0f, r * 0.78f), default, 0f, 1f, true);
            Part(Head, Ball(r * 0.22f), pal.Glow, new Vector3(r * 0.38f, r * 1.0f, r * 0.78f), default, 0f, 1f, true);
            if (d.HasTag("undead") || d.HasTag("demon") || d.HasTag("dragon"))
            {
                Part(Head, Cyl(0f, 0.05f, 0.22f), pal.Accent, new Vector3(-r * 0.85f, r * 1.5f, 0), new Vector3(0, 0, -32));
                Part(Head, Cyl(0f, 0.05f, 0.22f), pal.Accent, new Vector3(r * 0.85f, r * 1.5f, 0), new Vector3(0, 0, 32));
            }
            if (IsRobed)
                Part(Head, Cyl(0f, 0.34f, 0.62f), pal.Cloth, new Vector3(0, r * 1.1f, 0));   // 兜帽
        }

        void BuildWeapon(UnitDef d, UnitModel.Palette pal, UnitModel.Plan plan)
        {
            Weapon = new Node3D();
            if (ForeR != null) { Weapon.Position = new Vector3(0, -0.34f, 0.02f); ForeR.AddChild(Weapon); }
            else { Weapon.Position = new Vector3(0, 0.9f, 0.3f); Torso.AddChild(Weapon); }

            bool ranged = d.range > 60f;
            bool magic = d.HasTag("magic") || d.HasTag("elemental");
            if (d.HasTag("machine"))
            {
                Part(Weapon, Cyl(0.10f, 0.12f, 0.66f), pal.Metal, new Vector3(0, -0.18f, 0.24f), new Vector3(80, 0, 0), 0.85f, 0.3f);
                Part(Weapon, Cyl(0.13f, 0.13f, 0.10f), pal.Accent, new Vector3(0, -0.24f, 0.54f), new Vector3(80, 0, 0), 0.6f, 0.4f);
            }
            else if (magic)
            {
                Part(Weapon, Cyl(0.022f, 0.022f, 0.95f), pal.Cloth, new Vector3(0, -0.2f, 0.05f));
                Part(Weapon, Ball(0.10f), pal.Glow, new Vector3(0, 0.28f, 0.05f), default, 0f, 0.6f, true);
                Part(Weapon, Ball(0.05f), pal.Glow, new Vector3(0, 0.28f, 0.05f), default, 0f, 0.4f, true).Scale = Vector3.One * 1.8f;
            }
            else if (ranged)
            {
                Part(Weapon, TorusMesh0(0.22f, 0.03f), pal.Cloth, new Vector3(0, -0.1f, 0.12f), new Vector3(90, 0, 0));
                Part(Weapon, Box(0.012f, 0.44f, 0.012f), pal.Metal, new Vector3(0, -0.1f, 0.12f), new Vector3(0, 0, 90));
            }
            else
            {
                bool twoHand = plan == UnitModel.Plan.Giant || plan == UnitModel.Plan.Bulky;
                float len = twoHand ? 0.95f : 0.72f;
                Part(Weapon, Cyl(0.025f, 0.03f, 0.20f), pal.Cloth, new Vector3(0, -0.06f, 0.03f));
                Part(Weapon, Box(0.075f, len, 0.022f), pal.Metal, new Vector3(0, -0.06f + len * 0.5f + 0.10f, 0.03f), default, 0.9f, 0.22f);
                Part(Weapon, Box(0.10f, 0.055f, 0.05f), pal.Accent, new Vector3(0, -0.06f + len + 0.12f, 0.03f), default, 0.6f, 0.4f);
            }
        }

        static TorusMesh TorusMesh0(float inner, float outer)
        {
            var t = new TorusMesh(); t.InnerRadius = inner; t.OuterRadius = outer; return t;
        }

        // ------------------------------------------------------------------ 动作
        public void Tick(float dt)
        {
            if (unit == null) return;
            bool moving = _lastPos.DistanceSquaredTo(new Vector3(unit.x, 0, unit.y)) > 0.0004f;
            _lastPos = new Vector3(unit.x, 0, unit.y);

            if (!unit.alive)
            {
                if (_deathT < 0f) _deathT = 0f;
                _deathT += dt;
                float k = Mathf.Min(1f, _deathT / 0.55f);
                Root.RotationDegrees = new Vector3(-92f * Ease(k), Root.RotationDegrees.Y, 0);
                Root.Position = new Vector3(0, -0.35f * k, 0);
                SetFade(1f - Mathf.Min(0.85f, _deathT / 1.4f));
                return;
            }

            if (_attackT > 0f) _attackT -= dt;
            if (_hitT > 0f) _hitT -= dt;
            if (_lungeT > 0f) _lungeT -= dt;

            if (moving) _phase += dt * 9.5f * AnimSpeed;
            else _phase += dt * 1.6f;

            Vector3 look = _lastLook;
            if (look.LengthSquared() > 0.001f) Root.Rotation = new Vector3(0, Mathf.Atan2(look.X, look.Z), 0);

            float atkK = _attackT > 0f ? 1f - _attackT / _attackDur : 0f;   // 0→1
            float hitK = _hitT > 0f ? _hitT / 0.18f : 0f;

            if (moving)
            {
                float sw = Mathf.Sin(_phase);
                if (LegL != null) LegL.RotationDegrees = new Vector3(sw * 46f, 0, 0);
                if (LegR != null) LegR.RotationDegrees = new Vector3(-sw * 46f, 0, 0);
                if (ShinL != null) ShinL.RotationDegrees = new Vector3(Mathf.Max(0f, -sw) * 52f, 0, 0);
                if (ShinR != null) ShinR.RotationDegrees = new Vector3(Mathf.Max(0f, sw) * 52f, 0, 0);
                if (ArmL != null) ArmL.RotationDegrees = new Vector3(-sw * 34f, 0, 6f);
                if (ArmR != null) ArmR.RotationDegrees = new Vector3(sw * 34f - atkK * 130f, 0, -6f);
                Hips.Position = new Vector3(0, 0.86f + Mathf.Abs(Mathf.Sin(_phase)) * 0.05f, 0);
                Torso.RotationDegrees = new Vector3(6f + Mathf.Sin(_phase * 2f) * 2.5f, 0, Mathf.Sin(_phase) * 3f);
            }
            else
            {
                float br = Mathf.Sin(_phase * 1.3f);
                if (LegL != null) LegL.RotationDegrees = new Vector3(0, 0, 0);
                if (LegR != null) LegR.RotationDegrees = new Vector3(0, 0, 0);
                if (ShinL != null) ShinL.RotationDegrees = Vector3.Zero;
                if (ShinR != null) ShinR.RotationDegrees = Vector3.Zero;
                if (ArmL != null) ArmL.RotationDegrees = new Vector3(br * 4f - atkK * 40f, 0, 7f);
                if (ArmR != null) ArmR.RotationDegrees = new Vector3(-br * 4f - atkK * 135f, 0, -7f);
                Hips.Position = new Vector3(0, 0.86f + br * 0.022f, 0);
                Torso.RotationDegrees = new Vector3(2f + br * 1.6f, 0, 0);
            }

            // 攻击：抬手蓄力 → 挥下 + 前冲
            if (_attackT > 0f)
            {
                float wind = atkK < 0.35f ? atkK / 0.35f : 1f;
                float swing = atkK < 0.35f ? 0f : (atkK - 0.35f) / 0.65f;
                if (ForeR != null) ForeR.RotationDegrees = new Vector3(-40f * wind + 70f * swing, 0, 0);
                if (Torso != null) Torso.RotationDegrees += new Vector3(-8f * wind + 16f * swing, -18f * wind + 26f * swing, 0);
                _lungeT = 0.18f;
            }
            else if (ForeR != null) ForeR.RotationDegrees = new Vector3(Mathf.Lerp(ForeR.RotationDegrees.X, 0f, 8f * dt), 0, 0);

            float lunge = _lungeT > 0f ? 0.22f * (_lungeT / 0.18f) : 0f;
            Root.Position = new Vector3(0, -hitK * 0.06f, lunge);
            if (Cape != null) Cape.RotationDegrees = new Vector3(-Mathf.Abs(Mathf.Sin(_phase * 0.9f)) * 9f + lunge * 60f, 0, 0);

            // 受击闪白
            if (hitK > 0f) SetTint(new Color(1f, 0.75f, 0.75f).Lerp(Colors.White, 1f - hitK));
            else if (_tinted) SetTint(Colors.White);
        }

        Vector3 _lastPos, _lastLook;
        bool _tinted;

        public void FaceTo(Vector3 target)
        {
            _lastLook = (target - new Vector3(unit.x, 0, unit.y));
            _lastLook.Y = 0f;
        }

        public void TriggerAttack()
        {
            if (_attackT > 0f) return;
            _attackT = _attackDur;
        }

        public void TriggerHit() { _hitT = 0.18f; }

        static float Ease(float t) { return 1f - (1f - t) * (1f - t); }

        void SetTint(Color mul)
        {
            _tinted = mul != Colors.White;
            for (int i = 0; i < _mats.Count; i++)
            {
                var c = _baseCols[i];
                _mats[i].AlbedoColor = new Color(c.R * mul.R, c.G * mul.G, c.B * mul.B, c.A);
            }
        }

        void SetFade(float a)
        {
            for (int i = 0; i < _mats.Count; i++)
            {
                var c = _baseCols[i];
                _mats[i].AlbedoColor = new Color(c.R, c.G, c.B, a);
                _mats[i].Transparency = a >= 0.99f ? BaseMaterial3D.TransparencyEnum.Disabled : BaseMaterial3D.TransparencyEnum.Alpha;
            }
        }

        public void SetWorldPos(float fieldX, float fieldZ, float scale)
        {
            Position = new Vector3(fieldX * scale - _ox, 0, fieldZ * scale - _oz);
        }
        float _ox, _oz;
        public void SetOrigin(float ox, float oz) { _ox = ox; _oz = oz; }
    }
}
