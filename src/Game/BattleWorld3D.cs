using System;
using System.Collections.Generic;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>
    /// 战场的 3D 世界：草地地形 + 真实光照阴影 + 单位与特效。
    /// 逻辑仍由 BattleSim 驱动，这里只负责表现；坐标映射 1 世界单位 = 10 战场单位。
    /// </summary>
    public partial class BattleWorld3D : Node3D
    {
        public Match match;
        public Main main;

        public const float W = 0.1f;             // 战场单位 → 世界单位的缩放
        Camera3D _cam;
        DirectionalLight3D _sun;
        Node3D _unitsRoot, _fxRoot;
        float _fieldW, _fieldD;
        Rect2 _rect;
        Vector2 _screen;
        float _unitScaleFactor = 1.0f;

        readonly Dictionary<int, UnitActor3D> _actors = new Dictionary<int, UnitActor3D>();
        readonly Dictionary<int, float> _deadAt = new Dictionary<int, float>();
        readonly List<Effect> _fx = new List<Effect>();
        float _t;

        sealed class Effect
        {
            public Node3D node;
            public MeshInstance3D mesh;
            public StandardMaterial3D mat;
            public float t, dur;
            public int kind;      // 0 挥砍 1 弹丸 2 命中 3 死亡 4 冲击环
            public Vector3 from, to;
            public float size;
        }

        public override void _Ready()
        {
            BuildEnvironment();
            BuildTerrain();
            BuildDecor();
            _unitsRoot = new Node3D(); AddChild(_unitsRoot);
            _fxRoot = new Node3D(); AddChild(_fxRoot);
            _cam = new Camera3D();
            _cam.Projection = Camera3D.ProjectionType.Orthogonal;
            _cam.Fov = 45f;
            AddChild(_cam);
        }

        // ------------------------------------------------------------------ 环境
        void BuildEnvironment()
        {
            var we = new WorldEnvironment();
            var env = new Godot.Environment();
            env.BackgroundMode = Godot.Environment.BGMode.Sky;
            var sky = new Sky();
            var skyMat = new ProceduralSkyMaterial();
            skyMat.SkyTopColor = new Color(0.32f, 0.55f, 0.92f);
            skyMat.SkyHorizonColor = new Color(0.76f, 0.86f, 0.95f);
            skyMat.SkyCurve = 0.12f;
            skyMat.GroundBottomColor = new Color(0.30f, 0.36f, 0.24f);
            skyMat.GroundHorizonColor = new Color(0.62f, 0.70f, 0.55f);
            skyMat.SunAngleMax = 30f;
            skyMat.SunCurve = 0.08f;
            sky.SkyMaterial = skyMat;
            env.Sky = sky;
            env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
            env.AmbientLightSkyContribution = 1.0f;
            env.AmbientLightEnergy = 0.62f;
            env.SsaoEnabled = true;
            env.SsaoIntensity = 1.6f;
            env.SsaoRadius = 1.4f;
            env.GlowEnabled = true;
            env.GlowIntensity = 0.55f;
            env.GlowBloom = 0.18f;
            env.GlowHdrThreshold = 1.0f;
            env.TonemapMode = Godot.Environment.ToneMapper.Aces;
            env.TonemapExposure = 1.05f;
            env.TonemapWhite = 6f;
            env.AdjustmentEnabled = true;
            env.AdjustmentSaturation = 1.16f;
            env.AdjustmentContrast = 1.06f;
            env.AdjustmentBrightness = 1.02f;
            env.FogEnabled = true;
            env.FogLightColor = new Color(0.74f, 0.84f, 0.90f);
            env.FogLightEnergy = 1f;
            env.FogDensity = 0.0026f;
            env.FogSkyAffect = 0.4f;
            we.Environment = env;
            AddChild(we);

            _sun = new DirectionalLight3D();
            _sun.RotationDegrees = new Vector3(-38f, 28f, 0f);
            _sun.LightEnergy = 1.85f;
            _sun.LightColor = new Color(1f, 0.96f, 0.85f);
            _sun.ShadowEnabled = true;
            _sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal;
            _sun.DirectionalShadowMaxDistance = 260f;
            _sun.ShadowOpacity = 0.85f;
            _sun.ShadowBias = 0.03f;
            _sun.ShadowNormalBias = 0.6f;
            _sun.LightAngularDistance = 1.2f;
            AddChild(_sun);
        }

        // ------------------------------------------------------------------ 地形
        static Texture2D MakeGrassTexture(int size = 512)
        {
            var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
            var n1 = new FastNoiseLite(); n1.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth; n1.Frequency = 0.012f; n1.Seed = 1337;
            var n2 = new FastNoiseLite(); n2.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex; n2.Frequency = 0.09f; n2.Seed = 77;
            var n3 = new FastNoiseLite(); n3.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex; n3.Frequency = 0.35f; n3.Seed = 951;
            var dark = new Color(0.17f, 0.30f, 0.10f);
            var mid = new Color(0.30f, 0.50f, 0.14f);
            var light = new Color(0.52f, 0.72f, 0.24f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = n1.GetNoise2D(x, y) * 0.5f + 0.5f;
                    float b = n2.GetNoise2D(x, y) * 0.5f + 0.5f;
                    float c = n3.GetNoise2D(x, y) * 0.5f + 0.5f;
                    var col = dark.Lerp(mid, Mathf.Clamp(a * 1.3f, 0f, 1f));
                    col = col.Lerp(light, Mathf.Clamp((b - 0.45f) * 1.6f, 0f, 0.65f));
                    // 细碎草叶纹理
                    float blade = (c - 0.5f) * 0.16f;
                    col = new Color(Mathf.Clamp(col.R + blade, 0f, 1f), Mathf.Clamp(col.G + blade * 0.9f, 0f, 1f), Mathf.Clamp(col.B + blade * 0.6f, 0f, 1f));
                    img.SetPixel(x, y, col);
                }
            return ImageTexture.CreateFromImage(img);
        }

        void BuildTerrain()
        {
            _fieldW = match != null ? match.sim.fieldWidth * W : 120f;
            _fieldD = match != null ? match.sim.fieldHeight * W : 56f;
            float tw = _fieldW * 3.4f, td = _fieldD * 4.2f;
            int nx = 96, nz = 72;
            var noise = new FastNoiseLite();
            noise.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
            noise.Frequency = 0.022f; noise.Seed = 4242;
            float H(float x, float z)
            {
                float d = noise.GetNoise2D(x, z);
                float amp = 0.55f;
                // 战场中心保持平坦，边缘起伏
                float edge = Mathf.Clamp((Mathf.Abs(x) / (_fieldW * 0.62f) + Mathf.Abs(z) / (_fieldD * 0.9f)) - 0.85f, 0f, 1f);
                return d * amp * edge;
            }
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float x0 = -tw / 2 + tw * i / nx, x1 = -tw / 2 + tw * (i + 1) / nx;
                    float z0 = -td / 2 + td * j / nz, z1 = -td / 2 + td * (j + 1) / nz;
                    float y00 = H(x0, z0), y10 = H(x1, z0), y01 = H(x0, z1), y11 = H(x1, z1);
                    float uvS = 22f;
                    void V(float x, float y, float z, float u, float v) { st.SetUV(new Vector2(u, v)); st.AddVertex(new Vector3(x, y, z)); }
                    V(x0, y00, z0, x0 / tw * uvS, z0 / td * uvS); V(x1, y10, z0, x1 / tw * uvS, z0 / td * uvS); V(x1, y11, z1, x1 / tw * uvS, z1 / td * uvS);
                    V(x0, y00, z0, x0 / tw * uvS, z0 / td * uvS); V(x1, y11, z1, x1 / tw * uvS, z1 / td * uvS); V(x0, y01, z1, x0 / tw * uvS, z1 / td * uvS);
                }
            st.GenerateNormals();
            var mesh = st.Commit();
            var mat = new StandardMaterial3D();
            mat.AlbedoTexture = MakeGrassTexture();
            mat.Roughness = 0.95f;
            mat.Metallic = 0f;
            mat.Uv1Scale = Vector3.One * 12f;
            mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
            var mi = new MeshInstance3D();
            mi.Mesh = mesh;
            mi.MaterialOverride = mat;
            AddChild(mi);
        }

        /// <summary>草丛：用 MultiMesh 铺几千个交叉草片，做出茂密草原的质感。</summary>
        void BuildDecor()
        {
            var rnd = new RandomNumberGenerator(); rnd.Seed = 20240917;
            // 草片网格（两片交叉的四边形）
            // 草簇：4 片交叉的梯形叶片，根深尖浅的渐变让它看起来立体
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            float w = 0.26f, h = 1.15f;   // 够宽够高才看得见
            var rootCol = new Color(0.34f, 0.44f, 0.20f);
            var tipCol = new Color(0.86f, 1.05f, 0.52f);
            void Blade(float ang, float lean)
            {
                float dx = Mathf.Cos(ang), dz = Mathf.Sin(ang);
                var bl = new Vector3(-dx * w, 0, -dz * w);
                var br = new Vector3(dx * w, 0, dz * w);
                // 叶尖收窄并向一侧倾斜
                var tl = new Vector3(-dx * w * 0.25f + dz * lean, h, -dz * w * 0.25f - dx * lean);
                var tr = new Vector3(dx * w * 0.25f + dz * lean, h, dz * w * 0.25f - dx * lean);
                void V(Vector3 p, Color c) { st.SetColor(c); st.AddVertex(p); }
                V(bl, rootCol); V(br, rootCol); V(tr, tipCol);
                V(bl, rootCol); V(tr, tipCol); V(tl, tipCol);
            }
            Blade(0f, 0.05f);
            Blade(Mathf.Pi * 0.33f, -0.05f);
            Blade(Mathf.Pi * 0.66f, 0.04f);
            Blade(Mathf.Pi * 0.99f, -0.04f);
            st.GenerateNormals();
            var blade = st.Commit();

            var gmat = new StandardMaterial3D();
            gmat.AlbedoColor = Colors.White;
            gmat.Roughness = 0.95f;
            gmat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            gmat.VertexColorUseAsAlbedo = true;
            gmat.BacklightEnabled = true;
            gmat.Backlight = new Color(0.55f, 0.72f, 0.30f);

            var mm = new MultiMesh();
            mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
            mm.UseColors = true;
            mm.Mesh = blade;
            int count = 46000;
            mm.InstanceCount = count;
            float areaW = _fieldW * 1.35f, areaD = _fieldD * 1.8f;
            for (int i = 0; i < count; i++)
            {
                float x = (rnd.Randf() - 0.5f) * areaW;
                float z = (rnd.Randf() - 0.5f) * areaD;
                // 战场中心略微稀疏，避免遮住单位
                float center = Mathf.Clamp(1f - (Mathf.Abs(x) / (_fieldW * 0.55f) + Mathf.Abs(z) / (_fieldD * 0.85f)) * 0.30f, 0.62f, 1f);
                float sc = (0.55f + rnd.Randf() * 0.8f) * center;
                var xf = new Transform3D();
                xf = xf.Rotated(Vector3.Up, rnd.Randf() * Mathf.Tau);
                xf = xf.Scaled(new Vector3(sc * 1.5f, sc * (1.0f + rnd.Randf() * 0.9f), sc * 1.5f));
                xf.Origin = new Vector3(x, 0f, z);
                mm.SetInstanceTransform(i, xf);
                float v = 0.78f + rnd.Randf() * 0.42f;
                mm.SetInstanceColor(i, new Color(v, v * (1.0f + rnd.Randf() * 0.16f), v * 0.86f));
            }
            var mmi = new MultiMeshInstance3D();
            mmi.Multimesh = mm;
            mmi.MaterialOverride = gmat;
            AddChild(mmi);

            // 远处树木：低模圆柱 + 球形树冠
            for (int i = 0; i < 40; i++)
            {
                float side = rnd.Randf() < 0.5f ? -1f : 1f;
                float x = side * (_fieldW * (0.52f + rnd.Randf() * 1.0f));
                float z = (rnd.Randf() - 0.5f) * _fieldD * 2.6f;
                var tree = new Node3D();
                tree.Position = new Vector3(x, 0f, z);
                float s = 1.4f + rnd.Randf() * 1.6f;
                tree.Scale = Vector3.One * s;
                var trunkMat = new StandardMaterial3D(); trunkMat.AlbedoColor = new Color(0.26f, 0.19f, 0.12f); trunkMat.Roughness = 0.95f;
                var tm = new MeshInstance3D(); var cy = new CylinderMesh(); cy.TopRadius = 0.16f; cy.BottomRadius = 0.24f; cy.Height = 1.6f;
                tm.Mesh = cy; tm.MaterialOverride = trunkMat; tm.Position = new Vector3(0, 0.8f, 0); tree.AddChild(tm);
                var leafMat = new StandardMaterial3D();
                leafMat.AlbedoColor = new Color(0.20f + rnd.Randf() * 0.12f, 0.42f + rnd.Randf() * 0.18f, 0.16f); leafMat.Roughness = 1f;
                for (int k = 0; k < 3; k++)
                {
                    var lm = new MeshInstance3D(); var sp = new SphereMesh(); sp.Radius = 0.85f - k * 0.14f; sp.Height = sp.Radius * 2f;
                    lm.Mesh = sp; lm.MaterialOverride = leafMat;
                    lm.Position = new Vector3((rnd.Randf() - 0.5f) * 0.5f, 1.75f + k * 0.42f, (rnd.Randf() - 0.5f) * 0.5f);
                    tree.AddChild(lm);
                }
                AddChild(tree);
            }
            // 战场内近景植被：小灌木与花丛（不挡路，只增加层次）
            var bushMat = new StandardMaterial3D();
            bushMat.AlbedoColor = new Color(0.22f, 0.40f, 0.16f);
            bushMat.Roughness = 1f;
            for (int i = 0; i < 90; i++)
            {
                float x = (rnd.Randf() - 0.5f) * _fieldW * 1.9f;
                float z = (rnd.Randf() - 0.5f) * _fieldD * 2.3f;
                var bush = new Node3D();
                bush.Position = new Vector3(x, 0f, z);
                float sc = 0.5f + rnd.Randf() * 0.9f;
                bush.Scale = new Vector3(sc, sc * (0.7f + rnd.Randf() * 0.6f), sc);
                for (int k = 0; k < 3; k++)
                {
                    var bm = new MeshInstance3D();
                    var sp = new SphereMesh(); sp.Radius = 0.22f + rnd.Randf() * 0.16f; sp.Height = sp.Radius * 2f;
                    bm.Mesh = sp; bm.MaterialOverride = bushMat;
                    bm.Position = new Vector3((rnd.Randf() - 0.5f) * 0.3f, 0.16f + k * 0.12f, (rnd.Randf() - 0.5f) * 0.3f);
                    bush.AddChild(bm);
                }
                AddChild(bush);
            }
            // 花：彩色小球
            var flowerCols = new Color[] { new Color(0.95f, 0.85f, 0.35f), new Color(0.92f, 0.55f, 0.72f), new Color(0.95f, 0.95f, 0.98f), new Color(0.70f, 0.62f, 0.95f) };
            for (int i = 0; i < 260; i++)
            {
                float x = (rnd.Randf() - 0.5f) * _fieldW * 2.1f;
                float z = (rnd.Randf() - 0.5f) * _fieldD * 2.5f;
                var fm = new MeshInstance3D();
                var sp = new SphereMesh(); sp.Radius = 0.07f; sp.Height = 0.14f;
                fm.Mesh = sp;
                var fmat = new StandardMaterial3D();
                fmat.AlbedoColor = flowerCols[(int)(rnd.Randf() * flowerCols.Length) % flowerCols.Length];
                fm.MaterialOverride = fmat;
                fm.Position = new Vector3(x, 0.35f + rnd.Randf() * 0.35f, z);
                AddChild(fm);
            }

            // 散落石头
            for (int i = 0; i < 40; i++)
            {
                float x = (rnd.Randf() - 0.5f) * _fieldW * 2.6f;
                float z = (rnd.Randf() - 0.5f) * _fieldD * 3.0f;
                if (Mathf.Abs(x) < _fieldW * 0.55f && Mathf.Abs(z) < _fieldD * 0.75f) continue;
                var rockMat = new StandardMaterial3D(); rockMat.AlbedoColor = new Color(0.44f, 0.44f, 0.42f); rockMat.Roughness = 0.9f;
                var rm = new MeshInstance3D(); var sp = new SphereMesh(); sp.Radius = 0.3f + rnd.Randf() * 0.5f; sp.Height = sp.Radius * 2f;
                rm.Mesh = sp; rm.MaterialOverride = rockMat;
                rm.Position = new Vector3(x, 0.05f, z);
                rm.Scale = new Vector3(1f, 0.6f + rnd.Randf() * 0.4f, 1f);
                rm.RotationDegrees = new Vector3(0, rnd.Randf() * 360f, 0);
                AddChild(rm);
            }
        }

        // ------------------------------------------------------------------ 相机取景
        public void Layout(Rect2 fieldRect, Vector2 screen, float unitScale = 1f)
        {
            _rect = fieldRect; _screen = screen; _unitScaleFactor = unitScale;
            if (_cam == null) return;
            float tiltDeg = 58f;
            float tilt = Mathf.DegToRad(tiltDeg);
            float projD = _fieldD * Mathf.Sin(tilt);
            float pxPerUnit = Mathf.Min(fieldRect.Size.X / _fieldW, fieldRect.Size.Y / Mathf.Max(0.001f, projD));
            // 正交尺寸 = 可见高度（世界单位）
            _cam.Size = screen.Y / Mathf.Max(0.0001f, pxPerUnit);
            float dist = 140f;
            var center = new Vector3(0, 0, 0);
            _cam.Position = center + new Vector3(0, Mathf.Sin(tilt) * dist, -Mathf.Cos(tilt) * dist);
            _cam.LookAt(center, Vector3.Up);
            // 把战场中心对齐到 fieldRect 的中心
            var right = _cam.GlobalTransform.Basis.X;
            var up = _cam.GlobalTransform.Basis.Y;
            float dx = (fieldRect.Position.X + fieldRect.Size.X * 0.5f - screen.X * 0.5f) / pxPerUnit;
            float dy = -(fieldRect.Position.Y + fieldRect.Size.Y * 0.5f - screen.Y * 0.5f) / pxPerUnit;
            _cam.Position += right * dx + up * dy;
            _unitScaleFactor = pxPerUnit;
        }

        public Vector3 FieldToWorld(float fx, float fz)
        {
            return new Vector3((fx - _fieldW * 0.5f / W * W) * W, 0f, (fz - _fieldD * 0.5f / W * W) * W);
        }

        public Vector2 WorldToScreen(Vector3 wp)
        {
            if (_cam == null) return Vector2.Zero;
            return _cam.UnprojectPosition(wp);
        }

        public float PxPerUnit { get { return _unitScaleFactor; } }

        // ------------------------------------------------------------------ 单位同步
        public void Sync(float dt)
        {
            if (match == null) return;
            _t += dt;
            var sim = match.sim;

            // 收集本帧存活的单位
            var seen = new HashSet<int>();
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var u = sim.Units[i];
                seen.Add(u.id);
                UnitActor3D act;
                if (!_actors.TryGetValue(u.id, out act))
                {
                    act = new UnitActor3D();
                    _unitsRoot.AddChild(act);
                    act.Setup(u, u.team == Team.Left);
                    act.Position = new Vector3((u.x - sim.fieldWidth * 0.5f) * W, 0f, (u.y - sim.fieldHeight * 0.5f) * W);
                    _actors[u.id] = act;
                }
                float rad = u.radius * W;
                float s = 3.4f + rad * 4.6f;   // 单位放大，才有"一支军队"的观感
                act.Root.Scale = Vector3.One * s;
                act.Ring.Scale = Vector3.One * Mathf.Max(0.6f, rad * 1.5f);
                act.Position = new Vector3((u.x - sim.fieldWidth * 0.5f) * W, 0f, (u.y - sim.fieldHeight * 0.5f) * W);
                act.Visible = true;
                // 朝向最近的敌人
                var foe = NearestFoe(sim, u);
                if (foe != null) act.FaceTo(new Vector3((foe.x - sim.fieldWidth * 0.5f) * W, 0f, (foe.y - sim.fieldHeight * 0.5f) * W));
                act.Tick(dt);
            }

            // 处理战斗事件 → 动作 + 特效
            var events = sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                switch (e.kind)
                {
                    case SimEventKind.Hit:
                    case SimEventKind.Shot:
                        {
                            UnitActor3D atk;
                            if (_actors.TryGetValue(e.src, out atk) && atk.unit != null && atk.unit.alive) atk.TriggerAttack();
                            UnitActor3D tgt;
                            if (_actors.TryGetValue(e.dst, out tgt) && tgt.unit != null && tgt.unit.alive) tgt.TriggerHit();
                            SpawnCombatFx(e);
                        }
                        break;
                    case SimEventKind.Death:
                        {
                            UnitActor3D d;
                            if (_actors.TryGetValue(e.src, out d)) { }
                            var p = new Vector3((e.x - sim.fieldWidth * 0.5f) * W, 0.2f, (e.y - sim.fieldHeight * 0.5f) * W);
                            SpawnPuff(p, e.team == Team.Left ? new Color(0.55f, 0.75f, 1f) : new Color(1f, 0.6f, 0.6f), 1.6f);
                        }
                        break;
                }
            }

            // 清理消失的单位（延迟一会儿让倒地动画播完）
            var gone = new List<int>();
            foreach (var kv in _actors)
                if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            for (int i = 0; i < gone.Count; i++)
            {
                var a = _actors[gone[i]];
                if (a.unit != null && !a.unit.alive) { a.Tick(dt); if (a.unit.hp <= -999f) { } }
                if (a.unit == null || !a.unit.alive)
                {
                    float t0; if (!_deadAt.TryGetValue(gone[i], out t0)) { _deadAt[gone[i]] = _t; continue; }
                    if (_t - t0 < 1.2f) continue;
                }
                a.QueueFree();
                _actors.Remove(gone[i]);
                _deadAt.Remove(gone[i]);
            }

            UpdateFx(dt);
        }

        static SimUnit NearestFoe(BattleSim sim, SimUnit u)
        {
            SimUnit best = null; float bd = float.MaxValue;
            for (int i = 0; i < sim.Units.Count; i++)
            {
                var o = sim.Units[i];
                if (!o.alive || o.team == u.team) continue;
                float d = u.Dist2(o);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        // ------------------------------------------------------------------ 特效
        void SpawnCombatFx(SimEvent e)
        {
            var sim = match.sim;
            var from = new Vector3((e.x - sim.fieldWidth * 0.5f) * W, 0.9f, (e.y - sim.fieldHeight * 0.5f) * W);
            var to = new Vector3((e.x2 - sim.fieldWidth * 0.5f) * W, 0.9f, (e.y2 - sim.fieldHeight * 0.5f) * W);
            var def = match.db.Unit(e.label);
            int kind = VfxKind(def);
            var col = VfxColor(kind, e.team);
            float size = 0.35f + (def != null ? def.pop * 0.06f + (def.tier - 1) * 0.06f : 0f);

            if (e.kind == SimEventKind.Shot)
                AddFx(1, from, to, col, size * 0.7f, 0.16f + from.DistanceTo(to) / 90f);
            else
                AddFx(0, from, to, col, size, 0.20f);
            AddFx(2, to, to, col, size * 0.9f, 0.26f);
            if (kind == 3 || kind == 6) AddFx(4, to, to, col, size * 3.2f, 0.4f);
        }

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

        static readonly Color[] FxCore = {
            new Color(1.00f, 0.98f, 0.92f), new Color(0.90f, 0.95f, 1.00f),
            new Color(0.76f, 0.58f, 1.00f), new Color(1.00f, 0.55f, 0.22f),
            new Color(0.95f, 0.52f, 0.24f), new Color(0.62f, 0.36f, 0.92f),
            new Color(1.00f, 0.88f, 0.46f), new Color(0.55f, 1.00f, 0.60f),
            new Color(0.50f, 0.92f, 0.40f),
        };

        static Color VfxColor(int kind, Team team)
        {
            var c = FxCore[Mathf.Clamp(kind, 0, FxCore.Length - 1)];
            var t = team == Team.Left ? new Color(0.42f, 0.72f, 1f) : new Color(1f, 0.48f, 0.46f);
            return c.Lerp(t, 0.18f);
        }

        void AddFx(int kind, Vector3 from, Vector3 to, Color col, float size, float dur)
        {
            var node = new Node3D();
            var mat = new StandardMaterial3D();
            mat.AlbedoColor = col;
            mat.EmissionEnabled = true;
            mat.Emission = col;
            mat.EmissionEnergyMultiplier = 2.2f;
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            Mesh mesh;
            switch (kind)
            {
                case 0: { var b = new BoxMesh(); b.Size = new Vector3(size * 0.16f, size * 1.5f, size * 0.16f); mesh = b; break; }
                case 1: { var s = new SphereMesh(); s.Radius = size * 0.30f; s.Height = s.Radius * 2f; mesh = s; break; }
                case 2: { var s = new SphereMesh(); s.Radius = size * 0.55f; s.Height = s.Radius * 2f; mesh = s; break; }
                case 4: { var t = new TorusMesh(); t.InnerRadius = size * 2.6f; t.OuterRadius = size * 3.0f; mesh = t; break; }
                default: { var s = new SphereMesh(); s.Radius = size * 0.4f; s.Height = s.Radius * 2f; mesh = s; break; }
            }
            var mi = new MeshInstance3D();
            mi.Mesh = mesh; mi.MaterialOverride = mat;
            mi.Position = kind == 4 ? from + new Vector3(0, -0.75f, 0) : from;
            if (kind == 4) mi.RotationDegrees = new Vector3(90, 0, 0);
            if (kind == 0)
            {
                var dir = (to - from);
                if (dir.LengthSquared() > 0.0001f)
                {
                    mi.LookAtFromPosition(from, to, Vector3.Up);
                    mi.RotateObjectLocal(Vector3.Right, 90f);
                }
            }
            node.AddChild(mi);
            _fxRoot.AddChild(node);
            _fx.Add(new Effect { node = node, mesh = mi, mat = mat, t = 0f, dur = dur, kind = kind, from = from, to = to, size = size });
        }

        void SpawnPuff(Vector3 p, Color col, float size)
        {
            var rnd = new RandomNumberGenerator(); rnd.Randomize();
            for (int i = 0; i < 8; i++)
            {
                var dir = new Vector3(rnd.Randf() - 0.5f, rnd.Randf() * 0.7f, rnd.Randf() - 0.5f).Normalized();
                AddFx(3, p + dir * 0.2f, p + dir * (1.5f + rnd.Randf()), col, size * (0.5f + rnd.Randf() * 0.5f), 0.5f + rnd.Randf() * 0.3f);
            }
        }

        void UpdateFx(float dt)
        {
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                f.t += dt;
                float k = Mathf.Clamp(f.t / f.dur, 0f, 1f);
                if (f.kind == 1) f.mesh.GlobalPosition = f.from.Lerp(f.to, Mathf.Min(1f, k * 1.35f));
                if (f.kind == 3) f.mesh.GlobalPosition = f.from.Lerp(f.to, k) + new Vector3(0, k * 0.8f, 0);
                if (f.kind == 0) { f.mesh.Scale = new Vector3(1f, 1f + k * 0.8f, 1f); }
                if (f.kind == 2) { f.mesh.Scale = Vector3.One * (0.5f + k * 2.2f); }
                if (f.kind == 4) { f.mesh.Scale = Vector3.One * (0.6f + k * 1.8f); }
                var c = f.mat.AlbedoColor;
                f.mat.AlbedoColor = new Color(c.R, c.G, c.B, 1f - k);
                f.mat.EmissionEnergyMultiplier = 2.2f * (1f - k);
                if (k >= 1f) { f.node.QueueFree(); _fx.RemoveAt(i); }
            }
        }
    }
}
