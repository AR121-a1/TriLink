using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using TriLink.Plugin;
using TriLink.Plugins.Thunder;

namespace TriLink.Thunder.Tests
{
    public static class ViewChecks
    {
        public static void Run(Action<bool, string> check, string uiDirectory)
        {
            Directory.CreateDirectory(uiDirectory);
            var links = new MemoryLinks();
            var view = new ThunderView(links);
            var frame = Field<Bitmap>(view, "_frame");
            var timer = Field<Timer>(view, "_timer");
            using (var form = Window(view))
            {
                form.Size = new Size(780, 650);
                form.Show(); Application.DoEvents();
                check(links.Created == 0 && !timer.Enabled, "game view opens without a socket or idle timer");
                check(frame.Width == 320 && frame.Height == 400, "pixel frame has a fixed 320 by 400 resolution");
                Fits(view, check, "normal game window");
                Shot(form, Path.Combine(uiDirectory, "ui-thunder-ready.png"));
                Click(view, "StartSolo");
                var session = Field<GameSession>(view, "_session");
                check(session.Mode == GameSessionMode.Solo && timer.Enabled && links.Created == 0,
                    "practice advances only its visible timer and does not create a game link");
                for (var index = 0; index < 190; index++) { session.Update(GameEngine.InputFire, 33); }
                Render(view);
                check(ReferenceEquals(frame, Field<Bitmap>(view, "_frame")), "successive renders reuse the one pixel bitmap");
                Shot(form, Path.Combine(uiDirectory, "ui-thunder-solo.png"));
                Click(view, "RestartGame");
                Invoke(view, "KeyPressed", view, new KeyEventArgs(Keys.Right));
                check(((byte)Invoke(view, "Input") & GameEngine.InputRight) != 0, "game canvas records movement input");
                view.Controls.Find("HostAddress", true).Single().Focus();
                Application.DoEvents();
                check((byte)Invoke(view, "Input") == 0, "moving focus to an address field clears held keys");
                view.Controls.Find("GameCanvas", true).Single().Focus();
                var engine = session.Engine;
                form.Hide(); Application.DoEvents();
                check(!timer.Enabled && session.Mode == GameSessionMode.Solo && ReferenceEquals(session.Engine, engine),
                    "hidden practice pauses and keeps its world");
                form.Show(); Application.DoEvents();
                check(timer.Enabled && ReferenceEquals(session.Engine, engine), "showing practice resumes the preserved world");
                form.Size = new Size(640, 560); Application.DoEvents();
                Fits(view, check, "minimum game window");
                session.Engine.Players[0].InvulnerableTicks = 0;
                Render(view);
                Shot(form, Path.Combine(uiDirectory, "ui-thunder-minimum.png"));
                Click(view, "StartHost");
                check(links.Created == 1 && session.Mode == GameSessionMode.Host && timer.Enabled,
                    "a visible waiting host has one link and continues handshaking");
                form.Hide(); Application.DoEvents();
                check(session.Mode == GameSessionMode.Idle && !timer.Enabled && links.Disposed == 1,
                    "hiding an online game closes its link and stops its timer");
            }
            check(view.IsDisposed && !timer.Enabled, "closing the game disposes the view and stops its timer");
            var disposed = false;
            try { frame.GetPixel(0, 0); } catch (ArgumentException) { disposed = true; }
            check(disposed, "closing the game releases its cached bitmap");

            VerifyCooperativeScene(check, uiDirectory);
        }

        private static void VerifyCooperativeScene(Action<bool, string> check, string uiDirectory)
        {
            var links = new MemoryLinks();
            var host = new ThunderView(links);
            var join = new ThunderView(links);
            using (var hostForm = Window(host))
            using (var joinForm = Window(join))
            {
                hostForm.Show(); joinForm.Show(); Application.DoEvents();
                var workingArea = Screen.FromControl(hostForm).WorkingArea;
                if (workingArea.Width >= hostForm.MinimumSize.Width && workingArea.Height >= hostForm.MinimumSize.Height)
                {
                    check(hostForm.Width <= workingArea.Width && hostForm.Height <= workingArea.Height,
                        "the recommended game window fits the screen's working area");
                }
                var borderWidth = hostForm.Width - hostForm.ClientSize.Width;
                var borderHeight = hostForm.Height - hostForm.ClientSize.Height;
                if (workingArea.Width >= 648 + borderWidth && workingArea.Height >= 906 + borderHeight)
                {
                    var canvas = host.Controls.Find("GameCanvas", true).Single();
                    check(canvas.ClientSize.Width >= 640 && canvas.ClientSize.Height >= 800,
                        "the recommended window supports double-size pixels when the screen allows it");
                }
                ((NumericUpDown)join.Controls.Find("LocalPort", true).Single()).Value = 47831;
                Click(host, "StartHost"); Click(join, "StartJoin");
                var hostSession = Field<GameSession>(host, "_session");
                var joinSession = Field<GameSession>(join, "_session");
                for (var step = 0; step < 150; step++)
                {
                    hostSession.Update(GameEngine.InputFire, 33);
                    joinSession.Update(GameEngine.InputFire, 33);
                }
                check(hostSession.Connected && joinSession.Connected
                    && hostSession.Engine.Players[0].Active && hostSession.Engine.Players[1].Active,
                    "the view's create and join controls start two cooperative ships");
                // A deterministic render fixture covers every sprite layer without waiting for random collisions.
                var engine = hostSession.Engine;
                engine.Enemies[0] = new GameEntity { Active = true, X = 75, Y = 95, Kind = 0 };
                engine.Enemies[1] = new GameEntity { Active = true, X = 239, Y = 123, Kind = 1 };
                engine.Enemies[2] = new GameEntity { Active = true, X = 160, Y = 72, Kind = 2 };
                engine.EnemyBullets[0] = new GameEntity { Active = true, X = 80, Y = 175 };
                engine.Explosions[0] = new GameEntity { Active = true, X = 244, Y = 210, Age = 3 };
                Render(host);
                Shot(hostForm, Path.Combine(uiDirectory, "ui-thunder-coop.png"));
                hostForm.Hide();
                joinSession.Update(0, 33);
                Invoke(join, "AdvanceFrame", join, EventArgs.Empty);
                check(hostSession.Mode == GameSessionMode.Idle && joinSession.Mode == GameSessionMode.Idle
                    && links.Disposed == 2 && !Field<Timer>(host, "_timer").Enabled && !Field<Timer>(join, "_timer").Enabled,
                    "hiding a cooperative view sends departure and releases both peers");
            }
        }

        private static Form Window(Control view)
        {
            var form = new Form { ShowInTaskbar = false, Text = "TriLink 雷霆战机测试", StartPosition = FormStartPosition.Manual };
            form.Controls.Add(view);
            return form;
        }

        private static void Fits(Control view, Action<bool, string> check, string name)
        {
            var canvas = view.Controls.Find("GameCanvas", true).Single();
            check(canvas.ClientSize.Width >= 320 && canvas.ClientSize.Height >= 400,
                name + " shows the full integer-scaled game canvas");
            foreach (var controlName in new[] { "StartSolo", "StartHost", "StartJoin", "StopGame", "RestartGame", "LocalPort", "HostAddress", "HostPort" })
            {
                var control = view.Controls.Find(controlName, true).Single();
                check(control.Right <= control.Parent.ClientSize.Width && control.Bottom <= control.Parent.ClientSize.Height,
                    name + " keeps " + controlName + " inside its toolbar");
            }
        }

        private static void Click(Control view, string name)
        { ((Button)view.Controls.Find(name, true).Single()).PerformClick(); Application.DoEvents(); }
        private static T Field<T>(object value, string name)
        { return (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value); }
        private static object Invoke(object value, string name, params object[] arguments)
        { return value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, arguments); }
        private static void Render(ThunderView view)
        { Invoke(view, "UpdateStatus"); Invoke(view, "RenderFrame"); Application.DoEvents(); }
        private static void Shot(Form form, string path)
        {
            using (var image = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(path); }
        }

        private sealed class MemoryLinks : IGameLinkFactory
        {
            private readonly Dictionary<int, MemoryLink> _open = new Dictionary<int, MemoryLink>();
            public int Created;
            public int Disposed;
            public IGameLink CreateLink() { Created++; return new MemoryLink(this); }

            private sealed class MemoryLink : IGameLink
            {
                private readonly MemoryLinks _factory;
                private readonly Queue<GameDatagram> _incoming = new Queue<GameDatagram>();
                private bool _disposed;
                public MemoryLink(MemoryLinks factory) { _factory = factory; }
                public bool IsOpen { get; private set; }
                public int LocalPort { get; private set; }
                public string NormalizePeerAddress(string peerAddress) { return peerAddress.Trim(); }
                public void Open(int localPort)
                {
                    if (_factory._open.ContainsKey(localPort)) { throw new InvalidOperationException("测试端口正在使用。"); }
                    IsOpen = true; LocalPort = localPort; _factory._open.Add(localPort, this);
                }
                public bool TryReceive(out GameDatagram datagram)
                {
                    datagram = IsOpen && _incoming.Count > 0 ? _incoming.Dequeue() : null;
                    return datagram != null;
                }
                public void Send(string peerAddress, int peerPort, byte[] payload)
                {
                    MemoryLink peer;
                    if (_factory._open.TryGetValue(peerPort, out peer))
                    {
                        peer._incoming.Enqueue(new GameDatagram
                        { PeerAddress = "127.0.0.1", PeerPort = LocalPort, Payload = (byte[])payload.Clone() });
                    }
                }
                public void Dispose()
                {
                    if (_disposed) { return; }
                    _disposed = true; _factory.Disposed++;
                    _factory._open.Remove(LocalPort); IsOpen = false; LocalPort = 0; _incoming.Clear();
                }
            }
        }
    }
}
