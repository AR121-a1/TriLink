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
                var countSelector = (NumericUpDown)view.Controls.Find("PlayerCount", true).Single();
                check(countSelector.Minimum == 2 && countSelector.Maximum == GameEngine.MaxPlayers,
                    "room participant selector covers two through six players");
                check(EndpointControls(view).All(control => control.Enabled), "idle game permits endpoint and participant editing");
                Fits(view, check, "normal game window");
                Shot(form, Path.Combine(uiDirectory, "ui-thunder-ready.png"));
                Click(view, "StartSolo");
                var session = Field<GameSession>(view, "_session");
                check(session.Mode == GameSessionMode.Solo && timer.Enabled && links.Created == 0,
                    "practice advances only its visible timer and does not create a game link");
                check(EndpointControls(view).All(control => !control.Enabled), "a running practice locks endpoint and participant edits");
                for (var index = 0; index < 190; index++) { session.Update(GameEngine.InputFire, 33); }
                Render(view);
                check(ReferenceEquals(frame, Field<Bitmap>(view, "_frame")), "successive renders reuse the one pixel bitmap");
                Shot(form, Path.Combine(uiDirectory, "ui-thunder-solo.png"));
                Click(view, "RestartGame");
                Invoke(view, "KeyPressed", view, new KeyEventArgs(Keys.Right));
                check(((byte)Invoke(view, "Input") & GameEngine.InputRight) != 0, "game canvas records movement input");
                view.Controls.Find("StartHost", true).Single().Focus();
                Application.DoEvents();
                check((byte)Invoke(view, "Input") == 0, "moving focus outside the canvas clears held keys");
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
                check(EndpointControls(view).All(control => !control.Enabled), "waiting online rooms lock their session configuration");
                using (var waitingFrame = (Bitmap)frame.Clone())
                {
                    form.Hide();
                    check(FramesDiffer(waitingFrame, frame), "hiding a waiting room immediately redraws its stopped preview");
                }
                Application.DoEvents();
                check(session.Mode == GameSessionMode.Idle && !timer.Enabled && links.Disposed == 1,
                    "hiding an online game closes its link and stops its timer");
                check(EndpointControls(view).All(control => control.Enabled), "stopping restores endpoint and participant editing");
            }
            check(view.IsDisposed && !timer.Enabled, "closing the game disposes the view and stops its timer");
            var disposed = false;
            try { frame.GetPixel(0, 0); } catch (ArgumentException) { disposed = true; }
            check(disposed, "closing the game releases its cached bitmap");

            foreach (var participants in new[] { 2, 3, GameEngine.MaxPlayers })
            { VerifyCooperativeScene(check, uiDirectory, participants); }
        }

        private static void VerifyCooperativeScene(Action<bool, string> check, string uiDirectory, int participants)
        {
            var links = new MemoryLinks();
            var views = Enumerable.Range(0, participants).Select(_ => new ThunderView(links)).ToArray();
            var forms = views.Select(Window).ToArray();
            var sessions = views.Select(view => Field<GameSession>(view, "_session")).ToArray();
            var host = views[0];
            var hostForm = forms[0];
            var hostSession = sessions[0];
            try
            {
                foreach (var form in forms) { form.Show(); }
                Application.DoEvents();
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
                ((NumericUpDown)host.Controls.Find("PlayerCount", true).Single()).Value = participants;
                for (var index = 1; index < participants; ++index)
                { ((NumericUpDown)views[index].Controls.Find("LocalPort", true).Single()).Value = 47830 + index; }
                Click(host, "StartHost");
                for (var index = 1; index < participants - 1; ++index) { Click(views[index], "StartJoin"); }
                if (participants > 2)
                {
                    for (var step = 0; step < 20; ++step)
                    { for (var index = 0; index < participants - 1; ++index) { sessions[index].Update(0, 33); } }
                    check(!hostSession.Running && hostSession.ReadyPlayerCount == participants - 1,
                        participants + " player host waits until its selected participant count is ready");
                    check(sessions.Skip(1).Take(participants - 2).All(session => session.Connected && !session.Running && session.Engine.Tick == 0),
                        participants + " player early joins retain a waiting world without simulation ticks");
                    Render(host); Render(views[1]);
                    check(views[0].Controls.Find("GameStatus", true).Single().Text.Contains("到齐 " + (participants - 1) + "/" + participants),
                        participants + " player waiting host shows its actual ready progress");
                    check(views[1].Controls.Find("GameStatus", true).Single().Text.Contains("等待队伍到齐"),
                        participants + " player waiting join does not invent other players' readiness");
                    check(((NumericUpDown)views[1].Controls.Find("PlayerCount", true).Single()).Value == participants
                        && sessions[1].Connected && !sessions[1].Running && sessions[1].Engine.Tick == 0,
                        participants + " player Start synchronizes the participant control without restarting the waiting session");
                    if (participants == 3)
                    {
                        Shot(hostForm, Path.Combine(uiDirectory, "ui-thunder-waiting-host.png"));
                        Shot(forms[1], Path.Combine(uiDirectory, "ui-thunder-waiting-join.png"));
                    }
                }
                Click(views[participants - 1], "StartJoin");
                for (var step = 0; step < 150; step++)
                {
                    foreach (var session in sessions) { session.Update(GameEngine.InputFire, 33); }
                }
                check(sessions.All(session => session.Connected && session.Running && session.Engine.PlayerCount == participants),
                    "the view controls start all " + participants + " cooperative ships only after everyone joins");
                check(sessions.Select(session => session.LocalPlayerIndex).Distinct().Count() == participants,
                    participants + " player views receive distinct player seats automatically");
                foreach (var view in views) { Render(view); }
                check(views.Select((view, index) => view.Controls.Find("GameStatus", true).Single().Text.Contains("本机 P" + (index + 1))).All(value => value),
                    participants + " player views display their local seat identity");
                check(views.All(view => ((NumericUpDown)view.Controls.Find("PlayerCount", true).Single()).Value == participants),
                    participants + " player running views display the assigned participant count");
                // Freeze visible test timers before inspecting exact fixture pixels. No wall-clock
                // event may advance a bullet between Render and the color assertions/screenshots.
                foreach (var view in views) { Field<Timer>(view, "_timer").Stop(); }
                // A deterministic render fixture covers every sprite layer without waiting for random collisions.
                var engine = hostSession.Engine;
                Array.Clear(engine.Bullets, 0, engine.Bullets.Length);
                engine.Enemies[0] = new GameEntity { Active = true, X = 75, Y = 95, Kind = 0 };
                engine.Enemies[1] = new GameEntity { Active = true, X = 239, Y = 123, Kind = 1 };
                engine.Enemies[2] = new GameEntity { Active = true, X = 160, Y = 72, Kind = 2 };
                engine.EnemyBullets[0] = new GameEntity { Active = true, X = 80, Y = 175 };
                engine.Explosions[0] = new GameEntity { Active = true, X = 244, Y = 210, Age = 3 };
                for (var index = 0; index < participants; ++index)
                {
                    engine.Players[index].X = 24 + (index + 1) * 272 / (participants + 1);
                    engine.Players[index].Y = 331;
                    engine.Players[index].InvulnerableTicks = 0;
                    engine.Players[index].Active = true;
                    engine.Bullets[index] = new GameEntity { Active = true, X = engine.Players[index].X, Y = 270, Kind = index };
                }
                Render(host);
                var frame = Field<Bitmap>(host, "_frame");
                var shipColors = engine.Players.Take(participants).Select(player => frame.GetPixel(player.X - 3, player.Y - 4).ToArgb()).ToArray();
                var bulletColors = engine.Players.Take(participants).Select(player => frame.GetPixel(player.X - 2, 266).ToArgb()).ToArray();
                check(shipColors.Distinct().Count() == participants && shipColors.SequenceEqual(bulletColors),
                    participants + " player ships and owned bullets use distinct matching colors");
                Shot(hostForm, Path.Combine(uiDirectory, participants == 2 ? "ui-thunder-coop.png" : "ui-thunder-coop-" + participants + ".png"));
                if (participants == GameEngine.MaxPlayers)
                {
                    hostForm.Size = new Size(640, 560); Application.DoEvents();
                    Fits(host, check, "six player minimum game window");
                    Shot(hostForm, Path.Combine(uiDirectory, "ui-thunder-six-minimum.png"));
                }
                using (var onlineFrame = (Bitmap)frame.Clone())
                {
                    hostForm.Hide();
                    check(FramesDiffer(onlineFrame, frame), participants + " player hidden online view immediately redraws PAUSED instead of a stale play frame");
                }
                for (var index = 1; index < participants; ++index)
                {
                    sessions[index].Update(0, 33);
                    Invoke(views[index], "AdvanceFrame", views[index], EventArgs.Empty);
                }
                check(sessions.All(session => session.Mode == GameSessionMode.Idle) && links.Disposed == participants
                    && views.All(view => !Field<Timer>(view, "_timer").Enabled),
                    "hiding a " + participants + " player cooperative view releases every peer and timer");
                hostForm.Show(); Application.DoEvents();
                check(!Field<Timer>(host, "_timer").Enabled && hostSession.Mode == GameSessionMode.Idle,
                    "restoring the stopped " + participants + " player view does not silently reconnect");
                if (participants == 3) { Shot(hostForm, Path.Combine(uiDirectory, "ui-thunder-stopped.png")); }
            }
            finally { foreach (var form in forms) { form.Dispose(); } }
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
            foreach (var controlName in new[] { "StartSolo", "StartHost", "StartJoin", "StopGame", "RestartGame", "PlayerCount", "LocalPort", "HostAddress", "HostPort" })
            {
                var control = view.Controls.Find(controlName, true).Single();
                check(control.Right <= control.Parent.ClientSize.Width && control.Bottom <= control.Parent.ClientSize.Height,
                    name + " keeps " + controlName + " inside its toolbar");
            }
        }

        private static IEnumerable<Control> EndpointControls(Control view)
        { return new[] { "PlayerCount", "LocalPort", "HostAddress", "HostPort" }.Select(name => view.Controls.Find(name, true).Single()); }

        private static bool FramesDiffer(Bitmap first, Bitmap second)
        {
            for (var y = 0; y < first.Height; ++y)
            { for (var x = 0; x < first.Width; ++x) { if (first.GetPixel(x, y) != second.GetPixel(x, y)) { return true; } } }
            return false;
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
