using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>兵种 3D 立绘贴图缓存（启动时离线烘焙一次）。</summary>
    public static class UnitPortrait
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        public static bool Ready;
        public static int Count { get { return Cache.Count; } }

        public static void Set(string id, Texture2D tex) { if (id != null) Cache[id] = tex; }

        /// <summary>优先用 3D 烘焙贴图，没有则退回矢量图。</summary>
        public static Texture2D Get(string id)
        {
            Texture2D t;
            if (id != null && Cache.TryGetValue(id, out t) && t != null) return t;
            return UnitArt.Get(id);
        }
    }

    /// <summary>
    /// 启动时把每个兵种的 3D 模型离屏渲染成 256x256 贴图。
    /// 每个模型占用 2 帧，33 个兵种约 1~2 秒，期间显示加载页。
    /// </summary>
    public partial class PortraitBaker : Node
    {
        public const int Size = 256;

        public async Task Bake(GameDatabase db)
        {
            var vp = new SubViewport();
            vp.Size = new Vector2I(Size, Size);
            vp.TransparentBg = true;
            vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            vp.Msaa3D = Viewport.Msaa.Msaa4X;
            vp.OwnWorld3D = true;
            AddChild(vp);

            var world = vp.FindWorld3D();
            world.Environment = new Godot.Environment();
            world.Environment.BackgroundMode = Godot.Environment.BGMode.ClearColor;
            world.Environment.BackgroundColor = new Color(0, 0, 0, 0);
            world.Environment.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            world.Environment.AmbientLightColor = new Color(0.62f, 0.68f, 0.82f);
            world.Environment.AmbientLightEnergy = 0.85f;
            world.Environment.TonemapMode = Godot.Environment.ToneMapper.Aces;
            world.Environment.TonemapWhite = 1.2f;

            var key = new DirectionalLight3D();
            key.RotationDegrees = new Vector3(-42, 34, 0);
            key.LightEnergy = 1.9f;
            vp.AddChild(key);
            var rim = new DirectionalLight3D();
            rim.RotationDegrees = new Vector3(-14, -128, 0);
            rim.LightEnergy = 0.9f;
            rim.LightColor = new Color(0.62f, 0.78f, 1f);
            vp.AddChild(rim);
            var rim2 = new DirectionalLight3D();
            rim2.RotationDegrees = new Vector3(-70, 10, 0);
            rim2.LightEnergy = 0.45f;
            vp.AddChild(rim2);

            var cam = new Camera3D();
            cam.Fov = 32f;
            vp.AddChild(cam);

            var units = new List<UnitDef>();
            for (int i = 0; i < db.Units.Length; i++)
            {
                var u = db.Units[i];
                if (u != null && u.unlockRound < 900) units.Add(u);
            }

            for (int i = 0; i < units.Count; i++)
            {
                var d = units[i];
                var slot = new Node3D();
                vp.AddChild(slot);
                var model = UnitModel.Build(d, null);
                model.RotateY(Mathf.DegToRad(24f));
                slot.AddChild(model);

                float h = UnitModel.ApproxHeight(d);
                float dist = h * 1.75f + 0.55f;
                cam.Position = new Vector3(0, h * 0.52f, dist);
                cam.LookAt(new Vector3(0, h * 0.48f, 0));

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var img = vp.GetTexture().GetImage();
                if (img != null)
                {
                    img.Convert(Image.Format.Rgba8);
                    UnitPortrait.Set(d.id, ImageTexture.CreateFromImage(img));
                }
                slot.QueueFree();
            }
            UnitPortrait.Ready = true;
            GD.Print("PORTRAIT: baked " + UnitPortrait.Count + " unit portraits");
        }
    }
}
