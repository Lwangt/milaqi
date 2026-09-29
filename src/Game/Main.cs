using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    public enum GameMode { Local, Host, Client }

    public partial class Main : Control
    {
        public GameDatabase db;
        public Match match;
        public AiController ai;
        public AiController ai0;

        void EnsureDemoAi()
        {
            if (demoMode && ai0 == null && match != null) ai0 = new AiController(match, match.players[0]);
        }
        public BattleView view;
        public Hud hud;
        public NetManager net;
        public StartMenu menu;

        public bool demoMode;
        public GameMode mode = GameMode.Local;
        public int localIndex;
        public bool matchStarted;

        public string selectedSkill;
        public bool gemShopOpen;
        float _accum;
        const float FixedStep = 1f / 30f;
        public int seed = 20260101;
        int _lastRound = -1;
        int _aiFlip;

        /// <summary>
        /// 强制启用 1600x900 画布缩放：不同 DPI / 分辨率下窗口可能拿不到 1600x900，
        /// 没有缩放的话界面右侧与底部会被直接裁掉。
        /// </summary>
        void SetupWindow()
        {
            var win = GetWindow();
            win.ContentScaleSize = new Vector2I(1600, 900);
            win.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
            win.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
            // 屏幕装不下 1600x900 时才缩小窗口（画布会自动等比缩放）
            var usable = DisplayServer.ScreenGetUsableRect();
            if (usable.Size.X < 800 || usable.Size.Y < 600) return;
            if (usable.Size.X >= 1600 && usable.Size.Y >= 900) return;
            int w = Math.Max(960, (int)(usable.Size.X * 0.96f));
            int h = Math.Max(600, (int)(usable.Size.Y * 0.96f));
            win.Size = new Vector2I(w, h);
        }

        public override void _Ready()
        {
            SetupWindow();
            db = LoadDatabase();

            bool wantModelSheet = false;
            foreach (var a in OS.GetCmdlineArgs()) if (a == "--modelsheet") wantModelSheet = true;
            foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--modelsheet") wantModelSheet = true;
            if (wantModelSheet) { BuildModelSheet(); InitShotMode(false); return; }
            bool selfTest = false;
            foreach (var a in OS.GetCmdlineArgs()) if (a == "--selftest" || a == "selftest") selfTest = true;
            foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--selftest" || a == "selftest") selfTest = true;
            if (selfTest) { RunSelfTest(); return; }

            // 卡牌与头像直接用程序化生成的 2D 精灵，省掉 3D 离线烘焙（启动更快）
            BuildUiAndStart();
        }

        public BattleWorld3D world;

        void BuildUiAndStart()
        {
            // 2D 战场：程序化生成的精细兵种精灵 + 手绘质感的草地，
            // 比 3D 原语更接近参考画面，同时省掉三维开销。
            view = new BattleView();
            view.main = this;
            view.Use3D = false;
            view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(view);

            hud = new Hud();
            hud.main = this;
            AddChild(hud);

            net = new NetManager();
            net.main = this;
            AddChild(net);

            menu = new StartMenu();
            menu.main = this;
            AddChild(menu);

            StartNewMatch(seed, false);
            bool autoLocal = false;
            foreach (var a in OS.GetCmdlineArgs()) if (a == "--local") autoLocal = true;
            foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--local") autoLocal = true;
            if (autoLocal) StartLocalMatch(); else menu.ShowMenu(true);
            foreach (var a in OS.GetCmdlineArgs()) if (a == "--demo") demoMode = true;
            foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--demo") demoMode = true;
            if (demoMode && ai == null && match != null) ai = new AiController(match, match.players[1]);
            if (demoMode) { ai0 = null; EnsureDemoAi(); }
            // 联机测试用命令行入口
            string hostArg = null, joinArg = null, portArg = null;
            var allArgs = new System.Collections.Generic.List<string>();
            foreach (var a in OS.GetCmdlineArgs()) allArgs.Add(a);
            foreach (var a in OS.GetCmdlineUserArgs()) allArgs.Add(a);
            foreach (var a in allArgs)
            {
                if (a == "--host") hostArg = "host";
                else if (a.StartsWith("--join=")) joinArg = a.Substring(7);
                else if (a.StartsWith("--port=")) portArg = a.Substring(7);
            }
            int netPort = 27015;
            if (portArg != null) int.TryParse(portArg, out netPort);
            if (hostArg != null) { GD.Print("NET: starting host on " + netPort); StartHostGame(netPort); }
            else if (joinArg != null) { GD.Print("NET: joining " + joinArg + ":" + netPort); JoinGame(joinArg, netPort); }
            InitShotMode();
            GetTree().Root.SizeChanged += OnResize;
        }

        public GameDatabase LoadDatabase()
        {
            return GameDatabase.Load(ReadRes);
        }

        /// <summary>调试：把所有兵种 3D 模型排成网格渲染，用于检查建模质量。</summary>
        void BuildModelSheet()
        {
            var env = new WorldEnvironment();
            var e = new Godot.Environment();
            e.BackgroundMode = Godot.Environment.BGMode.Color;
            e.BackgroundColor = new Color(0.07f, 0.08f, 0.12f);
            e.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            e.AmbientLightColor = new Color(0.55f, 0.6f, 0.75f);
            e.AmbientLightEnergy = 0.5f;
            e.TonemapMode = Godot.Environment.ToneMapper.Aces;
            env.Environment = e;
            AddChild(env);

            var key = new DirectionalLight3D();
            key.RotationDegrees = new Vector3(-48, 38, 0);
            key.LightEnergy = 1.5f;
            key.ShadowEnabled = true;
            AddChild(key);
            var fill = new DirectionalLight3D();
            fill.RotationDegrees = new Vector3(-20, -130, 0);
            fill.LightEnergy = 0.55f;
            fill.LightColor = new Color(0.7f, 0.8f, 1f);
            AddChild(fill);

            var grid = new Node3D();
            AddChild(grid);
            int cols = 6, n = 0;
            var units = new List<UnitDef>();
            for (int i = 0; i < db.Units.Length; i++) if (db.Units[i].unlockRound < 900) units.Add(db.Units[i]);
            foreach (var d in units)
            {
                var holder = new Node3D();
                float spacing = 2.6f;
                holder.Position = new Vector3((n % cols - (cols - 1) * 0.5f) * spacing, (n / cols) * 2.6f + 0.6f, 0);
                holder.AddChild(UnitModel.Build(d, null));
                grid.AddChild(holder);
                var lbl = new Label3D();
                lbl.Text = d.name;
                lbl.FontSize = 48;
                lbl.PixelSize = 0.004f;
                lbl.Position = new Vector3(0, -0.15f, 0.6f);
                lbl.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
                holder.AddChild(lbl);
                n++;
            }
            float rows = Mathf.Ceil(units.Count / (float)cols);
            var cam = new Camera3D();
            AddChild(cam);
            float cx = 0, cy = (rows - 1) * 2.6f * 0.5f;
            cam.Position = new Vector3(cx, cy + 0.8f, 9.5f + rows * 0.9f);
            cam.LookAt(new Vector3(cx, cy - 0.2f, 0));
            GD.Print("MODELSHEET: " + units.Count + " models, rows=" + rows);
        }

        /// <summary>无窗口自检：验证数据加载与一局完整模拟（导出后的 exe 也用它验证）。</summary>
        void RunSelfTest()
        {
            GD.Print("=== Milaqi selftest ===");
            GD.Print("units=" + db.Units.Length + " relics=" + db.Relics.Length + " skills=" + db.Skills.Length);
            if (db.Units.Length < 30 || db.Relics.Length < 20 || db.Skills.Length < 6)
            {
                GD.Print("SELFTEST FAIL: 数据未正确打包进可执行文件");
                GetTree().Quit(2);
                return;
            }
            var m = new Match(db, 4242);
            m.Start();
            if (m.players[0].roster.Count < 2)
            {
                GD.Print("SELFTEST FAIL: 初始部队为空");
                GetTree().Quit(2);
                return;
            }
            var a0 = new AiController(m, m.players[0], db.Archetype("undead_swarm"), db.DifficultyOr(2));
            var a1 = new AiController(m, m.players[1], db.Archetype("iron_wall"), db.DifficultyOr(2));
            int guard = 0;
            float t = 0f;
            while (m.phase != MatchPhase.GameOver && guard++ < 200000)
            {
                a0.Update(1f / 30f);
                a1.Update(1f / 30f);
                m.Tick(1f / 30f);
                t += 1f / 30f;
            }
            GD.Print("模拟完成: 回合=" + m.round + " 用时=" + t.ToString("0.0") + "s 胜者=" + m.winnerIndex);
            bool ok = m.phase == MatchPhase.GameOver && m.round >= 5 && db.Balance.baseHp > 0;
            GD.Print(ok ? "SELFTEST OK" : "SELFTEST FAIL");
            GetTree().Quit(ok ? 0 : 1);
        }

        public static string ReadRes(string rel)
        {
            var fa = Godot.FileAccess.Open("res://" + rel, Godot.FileAccess.ModeFlags.Read);
            if (fa == null)
            {
                GD.PushError("无法读取数据文件: " + rel + " (" + Godot.FileAccess.GetOpenError() + ")");
                return "{}";
            }
            string text = fa.GetAsText();
            fa.Close();
            return text;
        }

        // ---------------------------------------------------------------- 开局
        public void StartNewMatch(int newSeed, bool broadcast)
        {
            seed = newSeed;
            match = new Match(db, newSeed);
            match.Start();
            ai = mode == GameMode.Local ? new AiController(match, match.players[1]) : null;
            view.match = match;
            hud.match = match;
            if (world != null) world.match = match;
            selectedSkill = null;
            view.selectedSkill = null;
            gemShopOpen = false;
            _lastRound = -1;
            matchStarted = true;
            LayoutAll();
            ai0 = null;
            EnsureDemoAi();
            if (broadcast && mode == GameMode.Host) net.BroadcastSnapshot(Snapshot.Write(match, db));
        }

        public void StartLocalMatch()
        {
            mode = GameMode.Local;
            localIndex = 0;
            StartNewMatch(seed + 1, false);
            menu.ShowMenu(false);
        }

        public void StartHostGame(int port)
        {
            mode = GameMode.Host;
            localIndex = 0;
            if (!net.StartHost(port)) { menu.ShowMenu(true); return; }
            StartNewMatch(seed + 1, false);
            menu.ShowMenu(false);
        }

        public void JoinGame(string ip, int port)
        {
            if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
            mode = GameMode.Client;
            localIndex = 1;
            match = new Match(db, 1);
            match.Start();
            ai = null;
            view.match = match;
            hud.match = match;
            if (world != null) world.match = match;
            matchStarted = true;
            net.StartClient(ip, port);
            menu.ShowMenu(false);
        }

        public void OnRemoteJoined()
        {
            if (mode != GameMode.Host) return;
            StartNewMatch(seed + 1, true);
        }

        public void OnRemoteLeft()
        {
            if (mode == GameMode.Host) GD.Print("远端玩家已离开");
        }

        public void OnConnectedAsClient()
        {
            mode = GameMode.Client;
            localIndex = 1;
            GD.Print("已作为客户端连接");
        }

        void OnResize() { LayoutAll(); }

        /// <summary>同步 2D 覆盖层与 3D 战场的取景。</summary>
        public void LayoutAll()
        {
            if (view == null) return;
            view.Layout();
            if (world != null) world.Layout(view.FieldRect, new Vector2(1600, 900));
        }

        // ---------------------------------------------------------------- 主循环
        // 调试用：--shots=2,8,45 在指定秒数截屏到 build/shots/ 后退出
        bool shotMode;
        float shotElapsed;
        readonly System.Collections.Generic.List<float> shotTimes = new System.Collections.Generic.List<float>();
        int shotIndex;
        void InitShotMode() { InitShotMode(true); }

        void InitShotMode(bool autoStart)
        {
            string raw = null;
            var all = new System.Collections.Generic.List<string>();
            foreach (var a in OS.GetCmdlineArgs()) all.Add(a);
            foreach (var a in OS.GetCmdlineUserArgs()) all.Add(a);
            foreach (var a in all)
            {
                if (a.StartsWith("--shots=")) raw = a.Substring("--shots=".Length);
                else if (a.StartsWith("shots=")) raw = a.Substring("shots=".Length);
            }
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var part in raw.Split(','))
            {
                float v;
                if (float.TryParse(part, out v)) shotTimes.Add(v);
            }
            shotMode = shotTimes.Count > 0;
            if (shotMode && autoStart && !matchStarted && match != null) StartLocalMatch();
        }

        void UpdateShots(double delta)
        {
            if (!shotMode) return;
            shotElapsed += (float)delta;
            while (shotIndex < shotTimes.Count && shotElapsed >= shotTimes[shotIndex])
            {
                var img = GetViewport().GetTexture().GetImage();
                string dir = ProjectSettings.GlobalizePath("res://build/shots");
                DirAccess.MakeDirRecursiveAbsolute(dir);
                string path = dir + "/shot_" + shotIndex + "_" + shotTimes[shotIndex].ToString("0") + "s.png";
                var err = img.SavePng(path);
                GD.Print("SHOT " + shotIndex + " -> " + path + " (" + err + ")");
                shotIndex++;
            }
            if (shotIndex >= shotTimes.Count) GetTree().Quit(0);
        }

        // 调试用：--clicktest 在战斗阶段注入一次真实鼠标点击，验证输入链路是否通畅
        // （曾经 HUD 的全屏 root 吃掉整屏鼠标事件，导致技能点不出去）
        public bool clickTest;
        bool clickTestChecked;
        float clickTestT;
        int clickTestStage;

        void UpdateClickTest(double delta)
        {
            if (!clickTestChecked)
            {
                clickTestChecked = true;
                foreach (var a in OS.GetCmdlineArgs()) if (a == "--clicktest") clickTest = true;
                foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--clicktest") clickTest = true;
                if (clickTest && !matchStarted && match != null) StartLocalMatch();
            }
            if (!clickTest || match == null || view == null) return;
            // 先把遗物选掉：三选一弹窗是模态的，会盖住战场（否则测不到战场点击）
            var p0 = match.players[localIndex];
            if (!p0.relicPicked && p0.activeRelicOffers.Count > 0) match.ChooseRelic(p0, p0.activeRelicOffers[0]);
            if (match.phase != MatchPhase.Battle) return;
            clickTestT += (float)delta;
            if (clickTestStage == 0 && clickTestT > 1.2f)
            {
                clickTestStage = 1;
                OnSkill(0);
                GD.Print("CLICKTEST skill selected = " + (selectedSkill ?? "null"));
            }
            else if (clickTestStage == 1 && clickTestT > 1.9f)
            {
                clickTestStage = 2;
                var pos = view.ToScreen(match.sim.fieldWidth * 0.5f, match.sim.fieldHeight * 0.5f);
                // 必须先把光标移进窗口，并注入一次移动事件，否则 Godot 的 mouse_over 不会更新
                Input.WarpMouse(pos);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = pos, GlobalPosition = pos });
                var hov = GetViewport().GuiGetHoveredControl();
                GD.Print("CLICKTEST hovered=" + (hov != null ? hov.GetType().Name + "/" + hov.Name : "null"));
                if (hov == null)
                {
                    // 逐层排查：把 Hud 的每个子控件都报一遍，找出谁挡住了战场
                    var hud = GetNodeOrNull<Hud>("Hud");
                    if (hud == null) foreach (var c in GetChildren()) if (c is Hud h2) hud = h2;
                    if (hud != null)
                    {
                        foreach (var ch in hud.GetChildren())
                        {
                            var ctl = ch as Control;
                            if (ctl == null) continue;
                            bool hit = ctl.Visible && ctl.MouseFilter != Control.MouseFilterEnum.Ignore
                                && new Rect2(ctl.GlobalPosition, ctl.Size).HasPoint(pos);
                            if (hit) GD.Print("CLICKTEST BLOCKER = " + ctl.GetType().Name + "/" + ctl.Name
                                + " rect=" + new Rect2(ctl.GlobalPosition, ctl.Size) + " filter=" + ctl.MouseFilter);
                        }
                    }
                }
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = pos, GlobalPosition = pos });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = pos, GlobalPosition = pos });
                GD.Print("CLICKTEST injected click at " + pos);
            }
            else if (clickTestStage == 2 && clickTestT > 3.0f)
            {
                clickTestStage = 3;
                bool cast = false;
                for (int i = 0; i < match.log.Count; i++)
                    if (match.log[i].text != null && match.log[i].text.Contains("释放")) cast = true;
                GD.Print("CLICKTEST RESULT cast=" + cast + " selectedSkill=" + (selectedSkill ?? "null") + " phase=" + match.phase);
                GetTree().Quit(0);
            }
        }

        public override void _Process(double delta)
        {
            UpdateShots(delta);
            UpdateClickTest(delta);
            if (match == null) return;
            if (world != null) world.Sync((float)delta);
            if (match.round != _lastRound) { _lastRound = match.round; match.players[0].skillCharges.Clear(); match.players[1].skillCharges.Clear(); }
            if (mode != GameMode.Client)
            {
                float dt = (float)delta;
                if (dt > 0.1f) dt = 0.1f;
                _accum += dt;
                int guard = 0;
                while (_accum >= FixedStep && guard++ < 8)
                {
                    // 双方 AI 同时存在时随机化顺序，避免固定的行动先后造成隐性不平衡
                    if (demoMode && ai0 != null && ai != null)
                    {
                        if (((_aiFlip++) & 1) == 0) { ai.Update(FixedStep); ai0.Update(FixedStep); }
                        else { ai0.Update(FixedStep); ai.Update(FixedStep); }
                    }
                    else
                    {
                        if (ai != null) ai.Update(FixedStep);
                        if (demoMode && ai0 != null) ai0.Update(FixedStep);
                    }
                    match.Tick(FixedStep);
                    _accum -= FixedStep;
                }
            }
            else
            {
                _accum = 0f;
            }
            view.QueueRedraw();
        }

        int _snapshots;
        public void OnSnapshot(byte[] data)
        {
            Snapshot.Apply(match, data, db);
            matchStarted = true;
            _snapshots++;
            if (_snapshots == 1 || _snapshots % 100 == 0)
                GD.Print("NET: snapshot #" + _snapshots + " size=" + data.Length + " round=" + match.round + " phase=" + match.phase + " units=" + match.sim.Units.Count);
        }

        public void OnRemoteCommand(byte[] data) { ApplyCommand(1, data); }

        // ---------------------------------------------------------------- 指令
        void Send(byte cmd, Action<BinaryWriter> body)
        {
            byte[] data = NetManager.Pack(w => { w.Write(cmd); if (body != null) body(w); });
            if (mode == GameMode.Client) net.SendCommand(data);
            else ApplyCommand(localIndex, data);
        }

        public void ApplyCommand(int pi, byte[] data)
        {
            if (match == null || pi < 0 || pi > 1) return;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                byte id = r.ReadByte();
                var p = match.players[pi];
                switch (id)
                {
                    case NetManager.CmdBuy: match.BuyUnit(p, r.ReadInt32()); break;
                    case NetManager.CmdReroll: match.Reroll(p); break;
                    case NetManager.CmdBuyXp: match.BuyXp(p); break;
                    case NetManager.CmdReady: match.SetReady(p, r.ReadBoolean()); break;
                    case NetManager.CmdDeploy:
                        {
                            string uid = r.ReadString();
                            float x = r.ReadSingle(), y = r.ReadSingle();
                            match.Place(p, uid, x, y);
                            break;
                        }
                    case NetManager.CmdSell:
                        {
                            float x = r.ReadSingle(), y = r.ReadSingle();
                            match.SellAt(p, x, y, 34f);
                            break;
                        }
                    case NetManager.CmdAutoDeploy: match.AutoDeployAll(p); break;
                    case NetManager.CmdUnplace: match.UnplaceAll(p); break;
                    case NetManager.CmdRecallUnit: match.UnplaceUnit(p, r.ReadInt32()); break;
                    case NetManager.CmdRerollGems: match.RerollGemShop(p); break;
                    case NetManager.CmdPickRelic: match.ChooseRelic(p, r.ReadString()); break;
                    case NetManager.CmdBuyGemRelic: match.BuyGemRelic(p, r.ReadString()); break;
                    case NetManager.CmdUpgradeSkill: match.UpgradeSkill(p, r.ReadString()); break;
                    case NetManager.CmdCast:
                        {
                            string sid = r.ReadString();
                            float x = r.ReadSingle(), y = r.ReadSingle();
                            match.CastSkill(p, sid, x, y);
                            break;
                        }
                    case NetManager.CmdNewMatch: StartNewMatch(r.ReadInt32(), true); break;
                }
            }
        }

        // ---------------------------------------------------------------- UI 回调
        /// <summary>当前在「待出战队列」里选中的兵种（点击战场即部署它）</summary>
        public string queueSelected;
        ulong _lastBuyMs;

        public void OnBuyUnit(int slot)
        {
            // 防抖：防止一次点击被重复派发（双击/触控板/低帧率）导致连买
            ulong now = Time.GetTicksMsec();
            if (now - _lastBuyMs < 150) return;
            _lastBuyMs = now;
            Send(NetManager.CmdBuy, w => w.Write(slot));
        }

        public void OnQueueSelect(string unitId)
        {
            if (unitId == null) { queueSelected = null; return; }
            queueSelected = queueSelected == unitId ? null : unitId;
        }

        /// <summary>点击队列里的第 idx 个单位：已上场则撤回，未上场则选中/取消选中。</summary>
        public void OnQueueChip(int idx)
        {
            var p = match.players[localIndex];
            if (idx < 0 || idx >= p.roster.Count) return;
            var o = p.roster[idx];
            if (o.placed)
            {
                queueSelected = null;
                Send(NetManager.CmdRecallUnit, w => w.Write(idx));
                return;
            }
            queueSelected = queueSelected == o.id ? null : o.id;
        }

        public void OnUnplaceAll()
        {
            Send(NetManager.CmdUnplace, null);
            queueSelected = null;
        }

        public void OnBuyXp() { Send(NetManager.CmdBuyXp, null); }

        public void OnAutoDeploy() { Send(NetManager.CmdAutoDeploy, null); }

        public void OnReady()
        {
            var p = match.players[localIndex];
            Send(NetManager.CmdReady, w => w.Write(!p.ready));
        }

        public void OnToggleGemShop() { gemShopOpen = !gemShopOpen; }
        public void OnRerollGemShop() { Send(NetManager.CmdRerollGems, null); }

        public void OnPickRelic(int index)
        {
            var p = match.players[localIndex];
            if (index < 0 || index >= p.activeRelicOffers.Count) return;
            string id = p.activeRelicOffers[index];
            Send(NetManager.CmdPickRelic, w => w.Write(id));
        }

        public void OnBuyGemRelic(int index)
        {
            var p = match.players[localIndex];
            if (index < 0 || index >= p.gemShop.Count) return;
            string id = p.gemShop[index];
            Send(NetManager.CmdBuyGemRelic, w => w.Write(id));
        }

        public void OnUpgradeSkill(int index)
        {
            var p = match.players[localIndex];
            if (index < 0 || index >= p.skills.Count) return;
            string id = p.skills[index];
            Send(NetManager.CmdUpgradeSkill, w => w.Write(id));
        }

        public void OnSkill(int index)
        {
            var p = match.players[localIndex];
            if (index < 0 || index >= p.skills.Count) return;
            string sid = p.skills[index];
            selectedSkill = selectedSkill == sid ? null : sid;
            view.selectedSkill = selectedSkill;
        }

        public void OnFieldClick(Vector2 fieldPos, bool casting)
        {
            var p = match.players[localIndex];
            if (casting && !string.IsNullOrEmpty(selectedSkill))
            {
                string sid = selectedSkill;
                Send(NetManager.CmdCast, w => { w.Write(sid); w.Write(fieldPos.X); w.Write(fieldPos.Y); });
                selectedSkill = null;
                view.selectedSkill = null;
                return;
            }
            if (match.phase != MatchPhase.Prep) return;
            if (p.pendingDeploy.Count == 0) return;
            bool mySide = localIndex == 0
                ? fieldPos.X <= match.sim.fieldWidth * 0.5f
                : fieldPos.X >= match.sim.fieldWidth * 0.5f;
            if (!mySide) return;
            // 优先部署队列里选中的兵种，没选就按队列顺序取第一个
            string uid = null;
            if (!string.IsNullOrEmpty(queueSelected) && p.pendingDeploy.Contains(queueSelected)) uid = queueSelected;
            if (uid == null) uid = p.pendingDeploy[0];
            Send(NetManager.CmdDeploy, w => { w.Write(uid); w.Write(fieldPos.X); w.Write(fieldPos.Y); });
        }

        /// <summary>
        /// 判断指针是否落在任何 UI 控件上（用于把「战场点击」和「界面点击」分开）。
        /// 走 Godot 的 GUI 命中测试；同时把 HUD 的主要面板矩形也纳入判断。
        /// </summary>
        public bool IsPointerOverUi(Vector2 pos)
        {
            var hov = GetViewport().GuiGetHoveredControl();
            if (hov != null && hov != view) return true;
            if (hud != null && hud.BlocksPoint(pos)) return true;
            return false;
        }

        public void OnFieldRightClick(Vector2 fieldPos)
        {
            if (match.phase != MatchPhase.Prep) return;
            Send(NetManager.CmdSell, w => { w.Write(fieldPos.X); w.Write(fieldPos.Y); });
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (clickTest && @event is InputEventMouseButton mb0)
                GD.Print("CLICKTEST Main._UnhandledInput " + mb0.ButtonIndex);
            if (match == null) return;
            if (@event is InputEventKey k && k.Pressed && !k.Echo)
            {
                switch (k.Keycode)
                {
                    case Key.Space: OnReady(); break;
                    case Key.E: OnBuyXp(); break;
                    case Key.D: OnAutoDeploy(); break;
                    case Key.G: OnToggleGemShop(); break;
                    case Key.M: menu.ShowMenu(!menu.Visible); break;
                    case Key.F5: if (mode != GameMode.Client) StartNewMatch(seed + 1, mode == GameMode.Host); break;
                    case Key.Escape:
                        selectedSkill = null; view.selectedSkill = null; gemShopOpen = false;
                        break;
                }
            }
        }
    }
}
