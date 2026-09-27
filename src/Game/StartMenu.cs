using System;
using Godot;

namespace Milaqi.Game
{
    /// <summary>开始菜单：单机 / 创建房间 / 填 IP 加入房间。</summary>
    public partial class StartMenu : Control
    {
        public Main main;
        Control root;
        Label status;
        Label ipHint;
        LineEdit ipField;
        LineEdit portField;
        Button localBtn, hostBtn, joinBtn, resumeBtn, reconnectBtn;

        public override void _Ready()
        {
            SetAnchorsPreset(Control.LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;

            root = new Control();
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(root);

            var bg = new ColorRect();
            bg.Color = new Color(0.04f, 0.05f, 0.08f, 0.96f);
            bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(bg);

            var panel = Panel(460, 90, 680, 720);
            Label(panel, 30, 20, 620, 56, "米拉奇战纪", 40, new Color(1f, 0.85f, 0.4f), HorizontalAlignment.Center);
            Label(panel, 30, 78, 620, 30, "双人实时对抗 · 回合经济 + 自动战斗 + 肉鸽遗物", 17, new Color(0.7f, 0.76f, 0.85f), HorizontalAlignment.Center);

            localBtn = Btn(panel, 60, 130, 560, 62, "单机测试：对战 AI", 22, () => main.StartLocalMatch());

            Label(panel, 60, 214, 560, 30, "—— 联机（一方开房，另一方填 IP 加入）——", 16, new Color(0.6f, 0.68f, 0.8f), HorizontalAlignment.Center);

            Label(panel, 60, 252, 120, 30, "端口", 17, Colors.White, HorizontalAlignment.Left);
            portField = new LineEdit();
            portField.Text = "27015";
            portField.Position = new Vector2(130, 248);
            portField.Size = new Vector2(150, 36);
            panel.AddChild(portField);

            hostBtn = Btn(panel, 300, 246, 320, 40, "创建房间（我当主机）", 18, () => main.StartHostGame(int.Parse(portField.Text)));

            ipHint = Label(panel, 60, 292, 560, 56, "", 15, new Color(0.55f, 0.85f, 1f), HorizontalAlignment.Left);
            ipHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            Label(panel, 60, 356, 120, 30, "主机 IP", 17, Colors.White, HorizontalAlignment.Left);
            ipField = new LineEdit();
            ipField.Text = "127.0.0.1";
            ipField.PlaceholderText = "对方（主机）的 IP，例如 192.168.1.20";
            ipField.Position = new Vector2(160, 352);
            ipField.Size = new Vector2(460, 36);
            panel.AddChild(ipField);

            joinBtn = Btn(panel, 60, 398, 560, 44, "加入房间", 19, () => main.JoinGame(ipField.Text.Trim(), int.Parse(portField.Text)));

            Label(panel, 60, 456, 560, 90,
                "说明：\n· 局域网内直接可用（Windows 防火墙需放行 UDP 端口）\n· 公网联机需要主机方有公网 IP 并做端口映射\n· 双方都在家庭宽带的 NAT 后面时无法直连，需要中继服务",
                14, new Color(0.65f, 0.7f, 0.78f), HorizontalAlignment.Left).AutowrapMode = TextServer.AutowrapMode.WordSmart;

            status = Label(panel, 60, 556, 560, 80, "", 16, new Color(1f, 0.9f, 0.6f), HorizontalAlignment.Left);
            status.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            resumeBtn = Btn(panel, 60, 644, 270, 50, "返回游戏", 18, () => Visible = false);
            resumeBtn.Visible = false;
            reconnectBtn = Btn(panel, 350, 644, 270, 50, "断开联机", 18, () => { if (main.net != null) main.net.Disconnect(); });
            reconnectBtn.Visible = false;
        }

        Panel Panel(float x, float y, float w, float h)
        {
            var p = new Panel();
            var sb = new StyleBoxFlat();
            sb.BgColor = new Color(0.09f, 0.10f, 0.15f, 0.98f);
            sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 10;
            sb.BorderWidthTop = sb.BorderWidthBottom = sb.BorderWidthLeft = sb.BorderWidthRight = 1;
            sb.BorderColor = new Color(0.25f, 0.3f, 0.42f);
            p.AddThemeStyleboxOverride("panel", sb);
            p.Position = new Vector2(x, y);
            p.Size = new Vector2(w, h);
            root.AddChild(p);
            return p;
        }

        Label Label(Control parent, float x, float y, float w, float h, string text, int size, Color color, HorizontalAlignment align)
        {
            var l = new Label();
            l.Text = text;
            l.Position = new Vector2(x, y);
            l.Size = new Vector2(w, h);
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            l.HorizontalAlignment = align;
            l.MouseFilter = MouseFilterEnum.Ignore;
            parent.AddChild(l);
            return l;
        }

        Button Btn(Control parent, float x, float y, float w, float h, string text, int size, Action onPress)
        {
            var b = new Button();
            b.Text = text;
            b.Position = new Vector2(x, y);
            b.Size = new Vector2(w, h);
            b.AddThemeFontSizeOverride("font_size", size);
            b.Pressed += onPress;
            parent.AddChild(b);
            return b;
        }

        public override void _Process(double delta)
        {
            if (!Visible) return;
            string s = "";
            if (main != null && main.net != null)
            {
                s = main.net.status;
                if (main.net.isHost && main.net.active)
                {
                    var ips = main.net.LocalIps();
                    s += "\n本机可用的 IP：";
                    for (int i = 0; i < ips.Length; i++) s += (i > 0 ? " / " : "") + ips[i];
                    s += "\n让对方在「主机 IP」里填写以上地址（同一局域网选 192.168.x.x）";
                }
            }
            status.Text = s;
            if (ipHint != null)
                ipHint.Text = main != null && main.net != null && main.net.isHost
                    ? "已开房：等待对方连接…"
                    : "";
        }

        public void ShowMenu(bool show)
        {
            Visible = show;
            if (resumeBtn != null) resumeBtn.Visible = main != null && main.matchStarted;
            if (reconnectBtn != null) reconnectBtn.Visible = main != null && main.net != null && main.net.active;
        }
    }
}
