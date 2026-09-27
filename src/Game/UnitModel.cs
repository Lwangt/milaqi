using System;
using System.Collections.Generic;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>
    /// 程序化 3D 兵种模型。
    /// 材质分五层：主色(布甲) / 金属(护甲) / 点缀 / 布料(披风) / 发光(眼睛·法球)，
    /// 配色按兵种类别决定（亡灵=骨白紫、机械=钢灰铜、法师=紫金…），阵营色只作为叠加色调。
    /// </summary>
    public static class UnitModel
    {
        public enum Plan { Humanoid, Bulky, Robed, Beast, Machine, Giant, Flyer }

        public struct Palette
        {
            public Color Main, Metal, Accent, Cloth, Glow;
        }

        public static Plan PlanOf(UnitDef d)
        {
            if (d.id == "gargoyle") return Plan.Flyer;
            if (d.HasTag("machine")) return Plan.Machine;
            if (d.HasTag("giant")) return Plan.Giant;
            if (d.HasTag("beast")) return Plan.Beast;
            if (d.HasTag("magic") && d.range > 100) return Plan.Robed;
            if (d.HasTag("shield") || d.pop >= 4) return Plan.Bulky;
            return Plan.Humanoid;
        }

        public static Palette PaletteOf(UnitDef d)
        {
            var p = new Palette();
            if (d.HasTag("undead"))
            {
                p.Main = new Color(0.80f, 0.82f, 0.86f); p.Metal = new Color(0.52f, 0.50f, 0.62f);
                p.Accent = new Color(0.42f, 0.28f, 0.62f); p.Cloth = new Color(0.22f, 0.16f, 0.34f);
                p.Glow = new Color(0.55f, 1f, 0.75f);
            }
            else if (d.HasTag("machine"))
            {
                p.Main = new Color(0.46f, 0.49f, 0.55f); p.Metal = new Color(0.68f, 0.71f, 0.78f);
                p.Accent = new Color(0.78f, 0.45f, 0.20f); p.Cloth = new Color(0.26f, 0.28f, 0.33f);
                p.Glow = new Color(1f, 0.62f, 0.22f);
            }
            else if (d.HasTag("giant"))
            {
                p.Main = new Color(0.44f, 0.44f, 0.42f); p.Metal = new Color(0.58f, 0.58f, 0.56f);
                p.Accent = new Color(0.36f, 0.44f, 0.30f); p.Cloth = new Color(0.28f, 0.26f, 0.24f);
                p.Glow = new Color(1f, 0.55f, 0.25f);
            }
            else if (d.HasTag("magic"))
            {
                p.Main = new Color(0.40f, 0.34f, 0.62f); p.Metal = new Color(0.72f, 0.64f, 0.34f);
                p.Accent = new Color(0.58f, 0.40f, 0.86f); p.Cloth = new Color(0.30f, 0.24f, 0.48f);
                p.Glow = new Color(0.72f, 0.55f, 1f);
            }
            else if (d.HasTag("hero"))
            {
                p.Main = new Color(0.62f, 0.20f, 0.20f); p.Metal = new Color(0.86f, 0.72f, 0.36f);
                p.Accent = new Color(0.92f, 0.78f, 0.40f); p.Cloth = new Color(0.52f, 0.14f, 0.16f);
                p.Glow = new Color(1f, 0.85f, 0.45f);
            }
            else if (d.HasTag("assassin"))
            {
                p.Main = new Color(0.22f, 0.24f, 0.34f); p.Metal = new Color(0.62f, 0.66f, 0.74f);
                p.Accent = new Color(0.28f, 0.58f, 0.56f); p.Cloth = new Color(0.16f, 0.18f, 0.26f);
                p.Glow = new Color(0.45f, 0.95f, 0.85f);
            }
            else if (d.HasTag("beast"))
            {
                p.Main = new Color(0.38f, 0.32f, 0.24f); p.Metal = new Color(0.55f, 0.50f, 0.42f);
                p.Accent = new Color(0.60f, 0.48f, 0.28f); p.Cloth = new Color(0.26f, 0.22f, 0.16f);
                p.Glow = new Color(1f, 0.72f, 0.30f);
            }
            else
            {
                p.Main = new Color(0.40f, 0.44f, 0.52f); p.Metal = new Color(0.74f, 0.78f, 0.84f);
                p.Accent = new Color(0.62f, 0.32f, 0.26f); p.Cloth = new Color(0.30f, 0.34f, 0.42f);
                p.Glow = new Color(1f, 0.80f, 0.45f);
            }
            if (d.tier >= 4) { p.Metal = p.Metal.Lerp(new Color(1f, 0.86f, 0.5f), 0.35f); p.Accent = p.Accent.Lerp(new Color(1f, 0.9f, 0.6f), 0.25f); }
            return p;
        }

        /// <summary>模型大致高度，用于相机取景。</summary>
        public static float ApproxHeight(UnitDef d)
        {
            var plan = PlanOf(d);
            float s = 0.92f + (d.tier - 1) * 0.05f;
            switch (plan)
            {
                case Plan.Giant: return 2.4f * s;
                case Plan.Bulky: return 1.85f * s * 1.16f;
                case Plan.Machine: return 1.5f * s;
                case Plan.Beast: return 1.5f * s;
                case Plan.Flyer: return 1.7f * s;
                case Plan.Robed: return 2.3f * s;
                default: return 1.8f * s;
            }
        }

        static StandardMaterial3D Mat(Color c, float metallic, float rough, float emission = 0f)
        {
            var m = new StandardMaterial3D();
            m.AlbedoColor = c;
            m.Metallic = metallic;
            m.Roughness = rough;
            if (emission > 0f) { m.EmissionEnabled = true; m.Emission = c; m.EmissionEnergyMultiplier = emission; }
            return m;
        }

        static MeshInstance3D P(Mesh mesh, Material mat, Vector3 pos, Vector3 rot, Vector3 scale)
        {
            var mi = new MeshInstance3D();
            mi.Mesh = mesh;
            mi.MaterialOverride = mat;
            mi.Position = pos;
            mi.RotationDegrees = rot;
            mi.Scale = scale;
            return mi;
        }

        static BoxMesh Box(float x, float y, float z) { var b = new BoxMesh(); b.Size = new Vector3(x, y, z); return b; }
        static SphereMesh Ball(float r, int ring = 10, int seg = 14)
        { var s = new SphereMesh(); s.Radius = r; s.Height = r * 2f; s.RadialSegments = seg; s.Rings = ring; return s; }
        static CapsuleMesh Cap(float r, float h, int seg = 10)
        { var c = new CapsuleMesh(); c.Radius = r; c.Height = Mathf.Max(h, r * 2.05f); c.RadialSegments = seg; return c; }
        static CylinderMesh Cyl(float top, float bottom, float h, int seg = 12)
        { var c = new CylinderMesh(); c.TopRadius = top; c.BottomRadius = bottom; c.Height = h; c.RadialSegments = seg; return c; }
        static TorusMesh Ring(float inner, float outer) { var t = new TorusMesh(); t.InnerRadius = inner; t.OuterRadius = outer; return t; }
        static PrismMesh Prism(float w, float h, float d) { var p = new PrismMesh(); p.Size = new Vector3(w, h, d); return p; }

        /// <summary>构建模型。teamTint 为 null 时使用兵种类别原色（商店/图鉴），否则叠加阵营色（战场）。</summary>
        public static Node3D Build(UnitDef d, Color? teamTint)
        {
            var pal = PaletteOf(d);
            if (teamTint.HasValue)
            {
                var t = teamTint.Value;
                pal.Main = pal.Main.Lerp(t, 0.45f);
                pal.Cloth = pal.Cloth.Lerp(t, 0.35f);
                pal.Accent = pal.Accent.Lerp(t, 0.30f);
                pal.Metal = pal.Metal.Lerp(t.Lightened(0.35f), 0.22f);
            }

            var root = new Node3D();
            root.Name = "Unit_" + d.id;
            var plan = PlanOf(d);
            float s = 0.92f + (d.tier - 1) * 0.05f;
            if (plan == Plan.Giant) s *= 1.85f;
            else if (plan == Plan.Bulky) s *= 1.16f;
            else if (plan == Plan.Machine) s *= 1.10f;

            var mMain = Mat(pal.Main, 0.08f, 0.68f);
            var mMetal = Mat(pal.Metal, 0.82f, 0.26f);
            var mAccent = Mat(pal.Accent, 0.35f, 0.45f);
            var mCloth = Mat(pal.Cloth, 0.0f, 0.92f);
            var mGlow = Mat(pal.Glow, 0.0f, 0.2f, 2.4f);
            var mSkin = Mat(d.HasTag("undead") ? new Color(0.86f, 0.88f, 0.84f) : new Color(0.80f, 0.62f, 0.48f), 0f, 0.72f);
            var mDark = Mat(pal.Cloth.Darkened(0.25f), 0.3f, 0.5f);

            switch (plan)
            {
                case Plan.Humanoid: Humanoid(root, d, s, mMain, mMetal, mAccent, mCloth, mGlow, mSkin, mDark, 1f); break;
                case Plan.Bulky: Humanoid(root, d, s, mMain, mMetal, mAccent, mCloth, mGlow, mSkin, mDark, 1.32f); break;
                case Plan.Giant: Humanoid(root, d, s, mMain, mMetal, mAccent, mCloth, mGlow, mSkin, mDark, 1.55f); break;
                case Plan.Robed: Robed(root, d, s, mMain, mMetal, mAccent, mCloth, mGlow, mSkin, mDark); break;
                case Plan.Beast: Beast(root, d, s, mMain, mAccent, mCloth, mGlow, mDark); break;
                case Plan.Machine: Machine(root, d, s, mMetal, mAccent, mDark, mGlow); break;
                case Plan.Flyer: Flyer(root, d, s, mMetal, mAccent, mCloth, mGlow, mDark); break;
            }
            return root;
        }

        // ================================================================ 人形
        static void Humanoid(Node3D root, UnitDef d, float s, Material main, Material metal, Material accent,
            Material cloth, Material glow, Material skin, Material dark, float bulk)
        {
            bool undead = d.HasTag("undead");
            float hipY = 0.72f, shoulderY = 1.30f, headY = 1.60f;
            float legX = 0.115f * bulk;

            // ---- 腿 ----
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -legX : legX);
                root.AddChild(P(Cyl(0.085f * bulk, 0.075f * bulk, 0.72f), undead ? skin : dark, new Vector3(x, 0.38f, 0), Vector3.Zero, Vector3.One * s));
                // 护胫
                root.AddChild(P(Box(0.17f * bulk, 0.26f, 0.16f * bulk), metal, new Vector3(x, 0.30f, 0.02f), Vector3.Zero, Vector3.One * s));
                // 靴
                root.AddChild(P(Box(0.18f * bulk, 0.13f, 0.30f * bulk), dark, new Vector3(x, 0.07f, 0.05f), Vector3.Zero, Vector3.One * s));
            }
            // ---- 腰 + 胸 ----
            root.AddChild(P(Box(0.42f * bulk, 0.20f, 0.30f * bulk), dark, new Vector3(0, hipY + 0.08f, 0), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Box(0.30f * bulk, 0.09f, 0.32f), accent, new Vector3(0, hipY + 0.09f, 0.01f), Vector3.Zero, Vector3.One * s));   // 腰带
            float chestH = shoulderY - hipY - 0.14f;
            root.AddChild(P(Box(0.50f * bulk, chestH, 0.34f * bulk), undead ? dark : main, new Vector3(0, hipY + 0.20f + chestH * 0.5f, 0), Vector3.Zero, Vector3.One * s));
            // 胸甲
            root.AddChild(P(Box(0.54f * bulk, chestH * 0.72f, 0.38f * bulk), metal, new Vector3(0, hipY + 0.26f + chestH * 0.5f, 0.01f), Vector3.Zero, Vector3.One * s));
            if (undead) for (int i = 0; i < 3; i++)
                root.AddChild(P(Box(0.34f * bulk, 0.035f, 0.06f), skin, new Vector3(0, hipY + 0.30f + i * 0.11f, 0.20f * bulk), Vector3.Zero, Vector3.One * s));
            // 护肩
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -0.30f : 0.30f) * bulk;
                root.AddChild(P(Ball(0.125f * bulk), metal, new Vector3(x, shoulderY - 0.02f, 0), Vector3.Zero, new Vector3(1f, 0.85f, 1f) * s));
                if (d.tier >= 3) root.AddChild(P(Prism(0.13f, 0.20f, 0.13f), accent, new Vector3(x * 1.15f, shoulderY + 0.06f, 0), new Vector3(0, 0, i == 0 ? 20 : -20), Vector3.One * s));
            }
            // ---- 手臂 ----
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -0.32f : 0.32f) * bulk;
                root.AddChild(P(Cyl(0.062f * bulk, 0.055f * bulk, 0.46f), undead ? skin : dark, new Vector3(x, shoulderY - 0.26f, 0.01f), new Vector3(0, 0, i == 0 ? -7 : 7), Vector3.One * s));
                root.AddChild(P(Box(0.11f * bulk, 0.13f, 0.12f), metal, new Vector3(x * 1.03f, shoulderY - 0.48f, 0.02f), Vector3.Zero, Vector3.One * s));
            }
            // ---- 头 ----
            root.AddChild(P(Cyl(0.055f, 0.06f, 0.10f), undead ? skin : dark, new Vector3(0, shoulderY + 0.07f, 0), Vector3.Zero, Vector3.One * s));
            if (undead)
            {
                root.AddChild(P(Ball(0.145f), skin, new Vector3(0, headY, 0.01f), Vector3.Zero, new Vector3(1f, 1.15f, 0.98f) * s));
                root.AddChild(P(Box(0.20f, 0.075f, 0.14f), skin, new Vector3(0, headY - 0.10f, 0.05f), Vector3.Zero, Vector3.One * s));
                root.AddChild(P(Ball(0.032f), glow, new Vector3(-0.058f, headY + 0.02f, 0.13f), Vector3.Zero, Vector3.One));
                root.AddChild(P(Ball(0.032f), glow, new Vector3(0.058f, headY + 0.02f, 0.13f), Vector3.Zero, Vector3.One));
                for (int i = 0; i < 4; i++) root.AddChild(P(Box(0.03f, 0.05f, 0.02f), skin, new Vector3(-0.05f + i * 0.033f, headY - 0.065f, 0.15f), Vector3.Zero, Vector3.One * s));
            }
            else
            {
                root.AddChild(P(Ball(0.135f), skin, new Vector3(0, headY, 0), Vector3.Zero, new Vector3(1f, 1.08f, 0.98f) * s));
                // 头盔
                root.AddChild(P(Ball(0.155f), metal, new Vector3(0, headY + 0.03f, -0.005f), Vector3.Zero, new Vector3(1f, 0.9f, 1.02f) * s));
                root.AddChild(P(Box(0.26f, 0.03f, 0.05f), glow, new Vector3(0, headY + 0.01f, 0.125f), Vector3.Zero, Vector3.One * s));
                if (d.HasTag("hero")) root.AddChild(P(Cyl(0.02f, 0.16f, 0.26f), accent, new Vector3(0, headY + 0.20f, 0), Vector3.Zero, Vector3.One * s));
                if (d.tier >= 4) root.AddChild(P(Cyl(0.0f, 0.15f, 0.18f), accent, new Vector3(0, headY + 0.15f, 0), Vector3.Zero, Vector3.One * s));
            }
            // 披风
            if (d.HasTag("hero") || d.HasTag("assassin") || undead || d.HasTag("shield"))
                root.AddChild(P(Box(0.46f * bulk, 0.78f, 0.04f), cloth, new Vector3(0, shoulderY - 0.42f, -0.20f * bulk), new Vector3(7, 0, 0), Vector3.One * s));

            Weapon(root, d, s, metal, accent, cloth, glow, 0.34f * bulk, shoulderY - 0.42f);
            if (d.HasTag("shield") || d.id == "swordsman" || d.id == "templar") Shield(root, s, metal, accent, -0.36f * bulk, shoulderY - 0.46f);
        }

        // ================================================================ 法师
        static void Robed(Node3D root, UnitDef d, float s, Material main, Material metal, Material accent,
            Material cloth, Material glow, Material skin, Material dark)
        {
            root.AddChild(P(Cyl(0.17f, 0.40f, 1.15f), main, new Vector3(0, 0.58f, 0), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Cyl(0.18f, 0.30f, 0.22f), dark, new Vector3(0, 0.20f, 0), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Box(0.34f, 0.07f, 0.30f), accent, new Vector3(0, 0.86f, 0), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Box(0.44f, 0.44f, 0.30f), main, new Vector3(0, 1.14f, 0), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(TorusMesh0(), metal, new Vector3(0, 1.36f, 0), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Ball(0.135f), skin, new Vector3(0, 1.50f, 0.01f), Vector3.Zero, Vector3.One * s));
            // 尖帽
            root.AddChild(P(Cyl(0.02f, 0.30f, 0.52f), accent, new Vector3(0, 1.86f, 0), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Ring(0.28f, 0.36f), metal, new Vector3(0, 1.62f, 0), new Vector3(90, 0, 0), Vector3.One * s));
            root.AddChild(P(Ball(0.05f), glow, new Vector3(0, 2.14f, 0), Vector3.Zero, Vector3.One));
            for (int i = 0; i < 2; i++)
                root.AddChild(P(Cyl(0.055f, 0.05f, 0.42f), main, new Vector3((i == 0 ? -0.24f : 0.24f), 1.16f, 0.02f), new Vector3(0, 0, i == 0 ? -12 : 12), Vector3.One * s));
            var staff = new Node3D();
            staff.Position = new Vector3(0.34f, 0, 0.04f);
            staff.RotationDegrees = new Vector3(0, 0, -7);
            staff.AddChild(P(Cyl(0.028f, 0.032f, 2.0f), dark, new Vector3(0, 1.0f, 0), Vector3.Zero, Vector3.One * s));
            staff.AddChild(P(Ring(0.10f, 0.16f), metal, new Vector3(0, 2.02f, 0), new Vector3(90, 0, 0), Vector3.One * s));
            staff.AddChild(P(Ball(0.10f), glow, new Vector3(0, 2.02f, 0), Vector3.Zero, Vector3.One * s));
            root.AddChild(staff);
        }

        static TorusMesh TorusMesh0() { var t = new TorusMesh(); t.InnerRadius = 0.12f; t.OuterRadius = 0.19f; return t; }

        // ================================================================ 兽形
        static void Beast(Node3D root, UnitDef d, float s, Material main, Material accent, Material cloth, Material glow, Material dark)
        {
            root.AddChild(P(Cap(0.30f, 0.86f), main, new Vector3(0, 0.86f, 0.02f), new Vector3(90, 0, 0), new Vector3(1f, 1f, 0.82f) * s));
            root.AddChild(P(Box(0.46f, 0.10f, 0.70f), accent, new Vector3(0, 1.02f, -0.02f), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Ball(0.24f), main, new Vector3(0, 0.92f, 0.56f), Vector3.Zero, new Vector3(1f, 0.95f, 1.15f) * s));
            root.AddChild(P(Box(0.24f, 0.18f, 0.34f), dark, new Vector3(0, 0.78f, 0.82f), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Ball(0.045f), glow, new Vector3(-0.09f, 1.00f, 0.78f), Vector3.Zero, Vector3.One));
            root.AddChild(P(Ball(0.045f), glow, new Vector3(0.09f, 1.00f, 0.78f), Vector3.Zero, Vector3.One));
            root.AddChild(P(Prism(0.10f, 0.20f, 0.10f), accent, new Vector3(-0.14f, 1.20f, 0.50f), new Vector3(-14, 0, -16), Vector3.One * s));
            root.AddChild(P(Prism(0.10f, 0.20f, 0.10f), accent, new Vector3(0.14f, 1.20f, 0.50f), new Vector3(-14, 0, 16), Vector3.One * s));
            for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
                root.AddChild(P(Cap(0.075f, 0.62f), dark, new Vector3(i == 0 ? -0.21f : 0.21f, 0.34f, j == 0 ? 0.30f : -0.30f), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Cap(0.05f, 0.5f), cloth, new Vector3(0, 0.98f, -0.62f), new Vector3(-58, 0, 0), Vector3.One * s));
            for (int i = 0; i < 3; i++) root.AddChild(P(Prism(0.07f, 0.16f, 0.05f), accent, new Vector3(-0.10f + i * 0.10f, 1.24f, -0.30f), new Vector3(-20, 0, 0), Vector3.One * s));
        }

        // ================================================================ 机械
        static void Machine(Node3D root, UnitDef d, float s, Material metal, Material accent, Material dark, Material glow)
        {
            root.AddChild(P(Box(0.62f, 0.50f, 0.56f), metal, new Vector3(0, 0.78f, 0), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Box(0.68f, 0.08f, 0.62f), accent, new Vector3(0, 0.55f, 0), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Box(0.40f, 0.30f, 0.36f), dark, new Vector3(0, 1.16f, -0.02f), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Ball(0.07f), glow, new Vector3(0, 1.18f, 0.17f), Vector3.Zero, Vector3.One));
            root.AddChild(P(Box(0.20f, 0.05f, 0.05f), glow, new Vector3(0, 1.18f, 0.19f), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Ring(0.13f, 0.19f), accent, new Vector3(0, 0.80f, 0.30f), new Vector3(90, 0, 0), Vector3.One * s));
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.36f : 0.36f;
                root.AddChild(P(Box(0.22f, 0.26f, 0.68f), dark, new Vector3(x, 0.22f, 0), Vector3.Zero, Vector3.One * s));
                root.AddChild(P(Cyl(0.10f, 0.10f, 0.24f), accent, new Vector3(x, 0.22f, -0.34f), new Vector3(90, 0, 0), Vector3.One * s));
                root.AddChild(P(Cyl(0.10f, 0.10f, 0.24f), accent, new Vector3(x, 0.22f, 0.34f), new Vector3(90, 0, 0), Vector3.One * s));
            }
            float len = d.id == "ballista" ? 1.3f : (d.id == "cannoneer" ? 1.0f : 0.8f);
            float rad = d.id == "cannoneer" ? 0.13f : 0.075f;
            root.AddChild(P(Cyl(rad * 0.85f, rad, len), accent, new Vector3(0.14f, 1.02f, len * 0.32f), new Vector3(90, 0, 0), Vector3.One * s));
            root.AddChild(P(Cyl(rad * 1.1f, rad * 1.1f, 0.10f), dark, new Vector3(0.14f, 1.02f, len * 0.80f), new Vector3(90, 0, 0), Vector3.One * s));
            if (d.id == "ballista")
            {
                root.AddChild(P(Cyl(0.03f, 0.03f, 1.5f), dark, new Vector3(-0.16f, 1.02f, 0.3f), new Vector3(90, 0, 0), Vector3.One * s));
                root.AddChild(P(Prism(0.16f, 0.30f, 0.10f), glow, new Vector3(0.14f, 1.02f, len * 0.85f), new Vector3(90, 0, 0), Vector3.One * s));
            }
        }

        // ================================================================ 飞行
        static void Flyer(Node3D root, UnitDef d, float s, Material metal, Material accent, Material cloth, Material glow, Material dark)
        {
            root.AddChild(P(Cap(0.19f, 0.62f), metal, new Vector3(0, 0.92f, 0), Vector3.Zero, new Vector3(1.05f, 1f, 0.8f) * s));
            root.AddChild(P(Ball(0.13f), metal, new Vector3(0, 1.34f, 0.04f), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Box(0.24f, 0.16f, 0.20f), dark, new Vector3(0, 1.28f, 0.14f), Vector3.Zero, Vector3.One * s));
            root.AddChild(P(Ball(0.042f), glow, new Vector3(-0.06f, 1.36f, 0.20f), Vector3.Zero, Vector3.One));
            root.AddChild(P(Ball(0.042f), glow, new Vector3(0.06f, 1.36f, 0.20f), Vector3.Zero, Vector3.One));
            root.AddChild(P(Prism(0.09f, 0.22f, 0.09f), accent, new Vector3(-0.10f, 1.52f, 0.02f), new Vector3(0, 0, -20), Vector3.One * s));
            root.AddChild(P(Prism(0.09f, 0.22f, 0.09f), accent, new Vector3(0.10f, 1.52f, 0.02f), new Vector3(0, 0, 20), Vector3.One * s));
            for (int i = 0; i < 2; i++)
            {
                float dir = i == 0 ? -1f : 1f;
                var wing = new Node3D();
                wing.Position = new Vector3(dir * 0.20f, 1.10f, -0.02f);
                wing.RotationDegrees = new Vector3(0, dir * -16f, dir * 22f);
                wing.AddChild(P(Box(0.85f, 0.05f, 0.34f), cloth, new Vector3(dir * 0.45f, 0, 0), Vector3.Zero, Vector3.One * s));
                wing.AddChild(P(Box(0.60f, 0.04f, 0.22f), accent, new Vector3(dir * 0.32f, -0.02f, 0.16f), Vector3.Zero, Vector3.One * s));
                wing.AddChild(P(Prism(0.10f, 0.22f, 0.10f), dark, new Vector3(dir * 0.10f, 0.04f, -0.06f), new Vector3(0, 0, dir * -30f), Vector3.One * s));
                root.AddChild(wing);
            }
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.30f : 0.30f;
                root.AddChild(P(Cyl(0.06f, 0.05f, 0.5f), dark, new Vector3(x, 0.72f, 0.04f), new Vector3(0, 0, i == 0 ? 18 : -18), Vector3.One * s));
                root.AddChild(P(Box(0.12f, 0.14f, 0.12f), metal, new Vector3(x * 1.25f, 0.48f, 0.06f), Vector3.Zero, Vector3.One * s));
            }
            root.AddChild(P(Cyl(0.05f, 0.02f, 0.55f), metal, new Vector3(0, 0.66f, -0.28f), new Vector3(-35, 0, 0), Vector3.One * s));
        }

        // ================================================================ 武器
        static void Weapon(Node3D root, UnitDef d, float s, Material metal, Material accent, Material cloth, Material glow, float handX, float handY)
        {
            var w = new Node3D();
            w.Position = new Vector3(handX, handY, 0.05f);
            switch (WeaponKind(d))
            {
                case "sword":
                    w.RotationDegrees = new Vector3(-14, 0, -6);
                    w.AddChild(P(Box(0.075f, 0.80f, 0.022f), metal, new Vector3(0, 0.46f, 0), Vector3.Zero, Vector3.One * s));
                    w.AddChild(P(Prism(0.075f, 0.14f, 0.022f), metal, new Vector3(0, 0.90f, 0), Vector3.Zero, Vector3.One * s));
                    w.AddChild(P(Box(0.24f, 0.05f, 0.055f), accent, new Vector3(0, 0.06f, 0), Vector3.Zero, Vector3.One * s));
                    w.AddChild(P(Cyl(0.026f, 0.026f, 0.16f), cloth, new Vector3(0, -0.03f, 0), Vector3.Zero, Vector3.One * s));
                    break;
                case "spear":
                    w.RotationDegrees = new Vector3(-7, 0, -5);
                    w.AddChild(P(Cyl(0.024f, 0.024f, 1.85f), cloth, new Vector3(0, 0.78f, 0), Vector3.Zero, Vector3.One * s));
                    w.AddChild(P(Cyl(0.0f, 0.058f, 0.30f), metal, new Vector3(0, 1.82f, 0), Vector3.Zero, Vector3.One * s));
                    w.AddChild(P(Box(0.06f, 0.10f, 0.06f), accent, new Vector3(0, 1.68f, 0), Vector3.Zero, Vector3.One * s));
                    break;
                case "glaive":
                    w.RotationDegrees = new Vector3(-9, 0, -8);
                    w.AddChild(P(Cyl(0.028f, 0.028f, 2.0f), cloth, new Vector3(0, 0.86f, 0), Vector3.Zero, Vector3.One * s));
                    w.AddChild(P(Box(0.05f, 0.62f, 0.17f), metal, new Vector3(0.03f, 1.86f, 0.02f), new Vector3(0, 0, 14), Vector3.One * s));
                    w.AddChild(P(Prism(0.06f, 0.16f, 0.16f), accent, new Vector3(0.03f, 2.20f, 0.02f), new Vector3(0, 0, 14), Vector3.One * s));
                    break;
                case "bow":
                    for (int i = 0; i < 5; i++)
                    {
                        float t = (i - 2) / 2f;
                        w.AddChild(P(Box(0.05f, 0.19f, 0.05f), accent, new Vector3(0.04f * (1f - t * t), 0.20f + t * 0.36f, 0.22f), new Vector3(0, 0, t * 26f), Vector3.One * s));
                    }
                    w.AddChild(P(Cyl(0.008f, 0.008f, 0.90f), Mat(new Color(0.88f, 0.88f, 0.84f), 0f, 0.7f), new Vector3(0.02f, 0.20f, 0.28f), Vector3.Zero, Vector3.One * s));
                    break;
                case "staff":
                    w.AddChild(P(Cyl(0.028f, 0.032f, 1.6f), cloth, new Vector3(0, 0.78f, 0), Vector3.Zero, Vector3.One * s));
                    w.AddChild(P(Ball(0.10f), glow, new Vector3(0, 1.62f, 0), Vector3.Zero, Vector3.One * s));
                    break;
                case "club":
                    w.RotationDegrees = new Vector3(-16, 0, -8);
                    w.AddChild(P(Cyl(0.05f, 0.062f, 1.0f), cloth, new Vector3(0, 0.50f, 0), Vector3.Zero, Vector3.One * s));
                    w.AddChild(P(Box(0.26f, 0.36f, 0.26f), metal, new Vector3(0, 1.12f, 0), Vector3.Zero, Vector3.One * s));
                    for (int i = 0; i < 3; i++) w.AddChild(P(Prism(0.09f, 0.12f, 0.09f), accent, new Vector3(0, 1.30f, 0), new Vector3(0, i * 120f, 0), Vector3.One * s));
                    break;
                case "dagger":
                    w.RotationDegrees = new Vector3(-10, 0, -7);
                    w.AddChild(P(Box(0.055f, 0.36f, 0.022f), metal, new Vector3(0, 0.26f, 0), Vector3.Zero, Vector3.One * s));
                    w.AddChild(P(Prism(0.055f, 0.09f, 0.022f), metal, new Vector3(0, 0.46f, 0), Vector3.Zero, Vector3.One * s));
                    w.AddChild(P(Box(0.15f, 0.04f, 0.04f), accent, new Vector3(0, 0.05f, 0), Vector3.Zero, Vector3.One * s));
                    break;
                case "claw":
                    for (int i = 0; i < 3; i++)
                        w.AddChild(P(Cyl(0.0f, 0.03f, 0.26f), metal, new Vector3(-0.06f + i * 0.06f, 0.16f, 0.10f), new Vector3(148, 0, 0), Vector3.One * s));
                    break;
            }
            if (d.HasTag("assassin"))
                w.AddChild(P(Box(0.05f, 0.32f, 0.022f), metal, new Vector3(-0.10f, 0.24f, -0.03f), new Vector3(0, 0, 9), Vector3.One * s));
            if (d.matk > d.atk && WeaponKind(d) != "staff")
                w.AddChild(P(Ball(0.08f), glow, new Vector3(0.12f, 0.34f, 0.10f), Vector3.Zero, Vector3.One));
            root.AddChild(w);
        }

        static string WeaponKind(UnitDef d)
        {
            if (d.HasTag("machine")) return "none";
            if (d.id == "guan_yu") return "glaive";
            if (d.id == "gargoyle") return "claw";
            if (d.HasTag("assassin")) return "dagger";
            if (d.HasTag("magic")) return "staff";
            if (d.HasTag("ranged")) return "bow";
            if (d.id == "titan" || d.id == "chaos_golem") return "club";
            if (d.id == "vampire" || d.HasTag("beast")) return "claw";
            if (d.id == "spearman" || d.id == "pikeman" || d.id == "zhao_yun" || d.id == "knight" || d.id == "dragon_knight" || d.id == "wolf_rider") return "spear";
            return "sword";
        }

        static void Shield(Node3D root, float s, Material metal, Material accent, float x, float y)
        {
            var sh = new Node3D();
            sh.Position = new Vector3(x, y, 0.10f);
            sh.RotationDegrees = new Vector3(0, 26, 0);
            sh.AddChild(P(Box(0.36f, 0.50f, 0.05f), metal, Vector3.Zero, Vector3.Zero, Vector3.One * s));
            sh.AddChild(P(Box(0.26f, 0.36f, 0.06f), accent, new Vector3(0, 0, 0.01f), Vector3.Zero, Vector3.One * s));
            sh.AddChild(P(Ball(0.075f), Mat(new Color(1f, 0.85f, 0.42f), 0.8f, 0.25f), new Vector3(0, 0.02f, 0.05f), Vector3.Zero, Vector3.One * s));
            root.AddChild(sh);
        }
    }
}
