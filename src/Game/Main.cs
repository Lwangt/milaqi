using System;
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

        public override void _Ready()
        {
            db = LoadDatabase();

            bool selfTest = false;
            foreach (var a in OS.GetCmdlineArgs()) if (a == "--selftest" || a == "selftest") selfTest = true;
            foreach (var a in OS.GetCmdlineUserArgs()) if (a == "--selftest" || a == "selftest") selfTest = true;
            if (selfTest) { RunSelfTest(); return; }

            view = new BattleView();
            view.main = this;
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
            var a0 = new AiController(m, m.players[0]);
            var a1 = new AiController(m, m.players[1]);
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
            selectedSkill = null;
            view.selectedSkill = null;
            gemShopOpen = false;
            _lastRound = -1;
            matchStarted = true;
            view.Layout();
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

        void OnResize()
        {
            if (view != null) view.Layout();
        }

        // ---------------------------------------------------------------- 主循环
        // 调试用：--shots=2,8,45 在指定秒数截屏到 build/shots/ 后退出
        bool shotMode;
        float shotElapsed;
        readonly System.Collections.Generic.List<float> shotTimes = new System.Collections.Generic.List<float>();
        int shotIndex;
        void InitShotMode()
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
            if (shotMode && !matchStarted) StartLocalMatch();
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

        public override void _Process(double delta)
        {
            UpdateShots(delta);
            if (match == null) return;
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
                            match.Deploy(p, uid, x, y);
                            break;
                        }
                    case NetManager.CmdAutoDeploy: match.AutoDeployAll(p); break;
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
        public void OnBuyUnit(int slot)
        {
            Send(NetManager.CmdBuy, w => w.Write(slot));
        }

        public void OnReroll() { Send(NetManager.CmdReroll, null); }
        public void OnBuyXp() { Send(NetManager.CmdBuyXp, null); }

        public void OnAutoDeploy() { Send(NetManager.CmdAutoDeploy, null); }

        public void OnReady()
        {
            var p = match.players[localIndex];
            Send(NetManager.CmdReady, w => w.Write(!p.ready));
        }

        public void OnToggleGemShop() { gemShopOpen = !gemShopOpen; }

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
            string uid = p.pendingDeploy[0];
            Send(NetManager.CmdDeploy, w => { w.Write(uid); w.Write(fieldPos.X); w.Write(fieldPos.Y); });
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (match == null) return;
            if (@event is InputEventKey k && k.Pressed && !k.Echo)
            {
                switch (k.Keycode)
                {
                    case Key.Space: OnReady(); break;
                    case Key.R: OnReroll(); break;
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
