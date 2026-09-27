using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Milaqi.Core;

namespace Milaqi.Game
{
    /// <summary>
    /// 联机层：主机权威模型。
    /// 主机(房主)跑全部模拟，客户端只发送指令、接收状态快照渲染。
    /// 使用 Godot 内置 ENetMultiplayerPeer（UDP），一方创建房间，另一方填 IP 加入。
    /// </summary>
    public partial class NetManager : Node
    {
        public Main main;
        public bool isHost;
        public bool active;
        public int port = 27015;
        public string address = "127.0.0.1";
        public string status = "";
        public int remotePeerId = -1;

        ENetMultiplayerPeer peer;
        float syncTimer;
        public const float SyncInterval = 1f / 20f;

        // 指令 ID
        public const byte CmdBuy = 1;
        public const byte CmdReroll = 2;
        public const byte CmdBuyXp = 3;
        public const byte CmdReady = 4;
        public const byte CmdDeploy = 5;
        public const byte CmdAutoDeploy = 6;
        public const byte CmdPickRelic = 7;
        public const byte CmdBuyGemRelic = 8;
        public const byte CmdUpgradeSkill = 9;
        public const byte CmdCast = 10;
        public const byte CmdNewMatch = 11;
        public const byte CmdHello = 12;
        public const byte CmdChat = 13;
        public const byte CmdSell = 14;
        public const byte CmdUnplace = 15;
        public const byte CmdRecallUnit = 16;

        public override void _Ready()
        {
            Name = "Net";
            Multiplayer.PeerConnected += OnPeerConnected;
            Multiplayer.PeerDisconnected += OnPeerDisconnected;
            Multiplayer.ConnectedToServer += OnConnectedToServer;
            Multiplayer.ConnectionFailed += OnConnectionFailed;
            Multiplayer.ServerDisconnected += OnServerDisconnected;
        }

        public string[] LocalIps()
        {
            var list = new List<string>();
            try
            {
                foreach (var ip in IP.GetLocalAddresses())
                {
                    if (ip.Contains(":")) continue; // 跳过 IPv6
                    list.Add(ip);
                }
            }
            catch { }
            if (list.Count == 0) list.Add("127.0.0.1");
            return list.ToArray();
        }

        public bool StartHost(int p)
        {
            port = p;
            peer = new ENetMultiplayerPeer();
            var err = peer.CreateServer(port, 1, 0, 0, 0);
            if (err != Error.Ok) { status = "创建房间失败：" + err; return false; }
            Multiplayer.MultiplayerPeer = peer;
            isHost = true;
            active = true;
            status = "房间已创建，监听 UDP " + port + "，等待对方填入你的 IP 加入…";
            return true;
        }

        public bool StartClient(string addr, int p)
        {
            address = addr;
            port = p;
            peer = new ENetMultiplayerPeer();
            var err = peer.CreateClient(address, port, 0, 0, 0);
            if (err != Error.Ok) { status = "连接失败：" + err; return false; }
            Multiplayer.MultiplayerPeer = peer;
            isHost = false;
            active = true;
            status = "正在连接 " + address + ":" + port + " …";
            return true;
        }

        public void Disconnect()
        {
            active = false;
            remotePeerId = -1;
            if (peer != null) { peer.Close(); peer = null; }
            Multiplayer.MultiplayerPeer = null;
            status = "已断开";
        }

        void OnPeerConnected(long id)
        {
            if (!isHost) return;
            remotePeerId = (int)id;
            status = "对方已连接（peer " + id + "）";
            if (main != null) main.OnRemoteJoined();
        }

        void OnPeerDisconnected(long id)
        {
            status = "对方已断开连接";
            remotePeerId = -1;
            if (main != null) main.OnRemoteLeft();
        }

        void OnConnectedToServer()
        {
            status = "已连接到主机";
            if (main != null) main.OnConnectedAsClient();
        }

        void OnConnectionFailed()
        {
            status = "连接失败：对方未开房或 IP/端口不正确";
            active = false;
        }

        void OnServerDisconnected()
        {
            status = "与主机断开连接";
            active = false;
        }

        // ---------------------------------------------------------------- 指令
        public void SendCommand(byte[] data)
        {
            if (!active || peer == null) return;
            if (isHost) return;
            RpcId(1, "Cmd", data);
        }

        [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
        public void Cmd(byte[] data)
        {
            if (!isHost || main == null) return;
            main.OnRemoteCommand(data);
        }

        // ---------------------------------------------------------------- 快照
        [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
        public void SnapshotMsg(byte[] data)
        {
            if (isHost || main == null) return;
            main.OnSnapshot(data);
        }

        public void BroadcastSnapshot(byte[] data)
        {
            if (!active || !isHost || peer == null) return;
            if (remotePeerId < 0 && Multiplayer.GetPeers().Length == 0) return;
            Rpc("SnapshotMsg", data);
        }

        public override void _Process(double delta)
        {
            if (!active || !isHost || main == null) return;
            if (main.match == null || main.mode == GameMode.Client) return;
            if (Multiplayer.GetPeers().Length == 0) return;
            syncTimer -= (float)delta;
            if (syncTimer <= 0f)
            {
                syncTimer = SyncInterval;
                BroadcastSnapshot(Snapshot.Write(main.match, main.db));
            }
        }

        // ---------------------------------------------------------------- 指令打包
        public static byte[] Pack(Action<BinaryWriter> body)
        {
            using (var ms = new MemoryStream(32))
            using (var w = new BinaryWriter(ms))
            {
                body(w);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
