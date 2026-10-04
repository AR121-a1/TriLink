using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using TriLink.Plugin;

namespace TriLink.Plugins.Thunder
{
    public sealed class ThunderView : UserControl
    {
        private readonly GameSession _session;
        private readonly Timer _timer = new Timer { Interval = 33 };
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly HashSet<Keys> _keys = new HashSet<Keys>();
        private readonly Bitmap _frame = new Bitmap(320, 400, PixelFormat.Format32bppPArgb);
        private readonly Graphics _frameGraphics;
        private readonly PixelSprites _sprites = new PixelSprites();
        private readonly Font _hudFont = new Font("Consolas", 10F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font _titleFont = new Font("Consolas", 22F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font _infoFont = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font _uiFont = new Font("Microsoft YaHei UI", 9F);
        private readonly StringFormat _center = new StringFormat { Alignment = StringAlignment.Center };
        private readonly NumericUpDown _localPort = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = 47830, Width = 72, Name = "LocalPort" };
        private readonly TextBox _hostAddress = new TextBox { Text = "127.0.0.1", Width = 150, Name = "HostAddress" };
        private readonly NumericUpDown _hostPort = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = 47830, Width = 72, Name = "HostPort" };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Name = "GameStatus" };
        private readonly Button _solo = new Button { Text = "单人练习", Name = "StartSolo" };
        private readonly Button _host = new Button { Text = "创建双人房", Name = "StartHost" };
        private readonly Button _join = new Button { Text = "加入双人房", Name = "StartJoin" };
        private readonly Button _stop = new Button { Text = "停止", Name = "StopGame" };
        private readonly Button _restart = new Button { Text = "重开", Name = "RestartGame" };
        private readonly ToolTip _tips = new ToolTip();
        private readonly GameCanvas _canvas;
        private GameSessionMode _lastMode = GameSessionMode.Solo;
        private Form _owner;
        private uint _presentationTick;
        private string _operationError;
        private bool _disposing;

        public ThunderView(IGameLinkFactory links)
        {
            if (links == null) { throw new ArgumentNullException(nameof(links)); }
            _session = new GameSession(links);
            _frameGraphics = Graphics.FromImage(_frame);
            _frameGraphics.SmoothingMode = SmoothingMode.None;
            _frameGraphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            _canvas = new GameCanvas(_frame) { Dock = DockStyle.Fill, Name = "GameCanvas", TabStop = true };
            Dock = DockStyle.Fill;
            Font = _uiFont;
            BackColor = Color.FromArgb(13, 23, 40);
            ForeColor = Color.FromArgb(221, 234, 248);
            BuildLayout();
            _solo.Click += (_, __) => Start(GameSessionMode.Solo);
            _host.Click += (_, __) => Start(GameSessionMode.Host);
            _join.Click += (_, __) => Start(GameSessionMode.Join);
            _stop.Click += (_, __) => Stop();
            _restart.Click += (_, __) => Start(_lastMode);
            _canvas.PreviewKeyDown += (_, args) => { if (IsGameKey(args.KeyCode)) { args.IsInputKey = true; } };
            _canvas.KeyDown += KeyPressed;
            _canvas.KeyUp += KeyReleased;
            _canvas.LostFocus += (_, __) => ClearInput();
            _canvas.MouseDown += (_, __) => _canvas.Focus();
            _timer.Tick += AdvanceFrame;
            _tips.SetToolTip(_localPort, "同一台电脑的两个游戏窗口应选择不同的本地端口。");
            _tips.SetToolTip(_hostAddress, "加入房间时填创建者的电脑 IP；同机练习可使用 127.0.0.1。");
            _tips.SetToolTip(_hostPort, "填写创建房间者选择的本地端口。");
            RenderFrame();
            UpdateStatus();
        }

        private void BuildLayout()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            foreach (var button in new[] { _solo, _host, _join, _stop, _restart })
            {
                button.AutoSize = true;
                button.Height = 26;
                button.Margin = new Padding(0, 0, 6, 0);
                button.BackColor = Color.FromArgb(29, 52, 78);
                button.ForeColor = ForeColor;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(63, 103, 143);
                actions.Controls.Add(button);
            }
            layout.Controls.Add(actions, 0, 0);
            var ports = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            AddPortLabel(ports, "本地端口");
            ports.Controls.Add(_localPort);
            AddPortLabel(ports, "对端地址");
            ports.Controls.Add(_hostAddress);
            AddPortLabel(ports, "对端端口");
            ports.Controls.Add(_hostPort);
            foreach (Control control in ports.Controls) { control.Margin = new Padding(0, 1, 6, 0); }
            layout.Controls.Add(ports, 0, 1);
            _status.Margin = Padding.Empty;
            layout.Controls.Add(_status, 0, 2);
            _canvas.Margin = Padding.Empty;
            layout.Controls.Add(_canvas, 0, 3);
            layout.Controls.Add(new Label
            {
                Text = "移动 WASD / 方向键  ·  自动开火  ·  空格加强射击  ·  双人共享得分",
                AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Margin = Padding.Empty, ForeColor = Color.FromArgb(159, 185, 216)
            }, 0, 4);
            Controls.Add(layout);
        }

        private static void AddPortLabel(FlowLayoutPanel row, string text)
        {
            row.Controls.Add(new Label { Text = text, AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
        }

        protected override void OnParentChanged(EventArgs args)
        {
            base.OnParentChanged(args);
            BindOwner();
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            BindOwner();
            SynchronizeTimer();
        }

        private void BindOwner()
        {
            var form = FindForm();
            if (_owner == form) { return; }
            if (_owner != null)
            {
                _owner.Deactivate -= OwnerDeactivated;
                _owner.VisibleChanged -= OwnerVisibilityChanged;
            }
            _owner = form;
            if (_owner == null) { return; }
            _owner.MinimumSize = new Size(Math.Max(640, _owner.MinimumSize.Width), Math.Max(560, _owner.MinimumSize.Height));
            if (!_owner.Visible)
            {
                var workingArea = Screen.FromControl(_owner).WorkingArea;
                var borderWidth = Math.Max(0, _owner.Width - _owner.ClientSize.Width);
                var borderHeight = Math.Max(0, _owner.Height - _owner.ClientSize.Height);
                var minimumClientWidth = Math.Max(1, _owner.MinimumSize.Width - borderWidth);
                var minimumClientHeight = Math.Max(1, _owner.MinimumSize.Height - borderHeight);
                _owner.ClientSize = new Size(
                    Math.Max(minimumClientWidth, Math.Min(680, workingArea.Width - borderWidth)),
                    Math.Max(minimumClientHeight, Math.Min(920, workingArea.Height - borderHeight)));
            }
            _owner.Deactivate += OwnerDeactivated;
            _owner.VisibleChanged += OwnerVisibilityChanged;
        }

        private void OwnerDeactivated(object sender, EventArgs args) { ClearInput(); }
        private void OwnerVisibilityChanged(object sender, EventArgs args) { ApplyVisibility(); }

        protected override void OnVisibleChanged(EventArgs args)
        {
            base.OnVisibleChanged(args);
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            if (_session == null || _disposing) { return; }
            if (!Visible)
            {
                ClearInput();
                if (_session.Mode == GameSessionMode.Host || _session.Mode == GameSessionMode.Join)
                {
                    _session.Stop();
                    UpdateStatus();
                }
            }
            SynchronizeTimer();
        }

        private void Start(GameSessionMode mode)
        {
            ClearInput();
            _operationError = null;
            try
            {
                if (mode == GameSessionMode.Host) { _session.Host((int)_localPort.Value, NewSeed()); }
                else if (mode == GameSessionMode.Join) { _session.Join(_hostAddress.Text.Trim(), (int)_hostPort.Value, (int)_localPort.Value); }
                else { _session.StartSolo(NewSeed()); }
                _lastMode = mode;
                _presentationTick = 0;
                _canvas.Focus();
            }
            catch (Exception exception)
            {
                _session.Stop();
                _operationError = "无法开始：" + exception.Message;
            }
            RenderFrame();
            UpdateStatus();
            SynchronizeTimer();
        }

        private static uint NewSeed() { return unchecked((uint)Environment.TickCount ^ (uint)DateTime.UtcNow.Ticks); }

        private void Stop()
        {
            ClearInput();
            _session.Stop();
            _operationError = null;
            SynchronizeTimer();
            UpdateStatus();
            RenderFrame();
        }

        private void SynchronizeTimer()
        {
            if (_disposing) { return; }
            if (Visible && IsHandleCreated && NeedsUpdates())
            {
                if (!_timer.Enabled) { _clock.Restart(); _timer.Start(); }
            }
            else { _timer.Stop(); _clock.Reset(); }
        }

        private void AdvanceFrame(object sender, EventArgs args)
        {
            if (!Visible || !NeedsUpdates()) { SynchronizeTimer(); return; }
            var elapsed = (int)Math.Max(1, Math.Min(int.MaxValue, _clock.ElapsedMilliseconds));
            _clock.Restart();
            try
            {
                _session.Update(Input(), elapsed);
                _presentationTick++;
            }
            catch (Exception exception)
            {
                _session.Stop();
                _operationError = "游戏已停止：" + exception.Message;
                ClearInput();
            }
            RenderFrame();
            UpdateStatus();
            SynchronizeTimer();
        }

        private byte Input()
        {
            byte input = 0;
            if (_keys.Contains(Keys.A) || _keys.Contains(Keys.Left)) { input |= GameEngine.InputLeft; }
            if (_keys.Contains(Keys.D) || _keys.Contains(Keys.Right)) { input |= GameEngine.InputRight; }
            if (_keys.Contains(Keys.W) || _keys.Contains(Keys.Up)) { input |= GameEngine.InputUp; }
            if (_keys.Contains(Keys.S) || _keys.Contains(Keys.Down)) { input |= GameEngine.InputDown; }
            if (_keys.Contains(Keys.Space)) { input |= GameEngine.InputFire; }
            return input;
        }

        private bool NeedsUpdates()
        {
            return _session.Mode != GameSessionMode.Idle
                && (_session.Mode != GameSessionMode.Solo || _session.Running);
        }

        private static bool IsGameKey(Keys key)
        {
            return key == Keys.A || key == Keys.D || key == Keys.W || key == Keys.S
                || key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Space;
        }

        private void KeyPressed(object sender, KeyEventArgs args)
        {
            if (!IsGameKey(args.KeyCode)) { return; }
            _keys.Add(args.KeyCode);
            args.Handled = true;
            args.SuppressKeyPress = true;
        }

        private void KeyReleased(object sender, KeyEventArgs args)
        {
            if (!IsGameKey(args.KeyCode)) { return; }
            _keys.Remove(args.KeyCode);
            args.Handled = true;
        }

        private void ClearInput() { _keys.Clear(); }

        private void UpdateStatus()
        {
            _status.Text = _operationError ?? _session.Status;
            _status.ForeColor = _operationError == null ? Color.FromArgb(161, 217, 240) : Color.FromArgb(255, 150, 148);
            _stop.Enabled = _session.Mode != GameSessionMode.Idle;
            _restart.Enabled = true;
        }

        private void RenderFrame()
        {
            var engine = _session.Engine;
            var tick = engine == null ? _presentationTick : engine.Tick;
            _sprites.Background(_frameGraphics, tick);
            if (engine == null)
            {
                DrawAttractScene(tick);
            }
            else
            {
                foreach (var entity in engine.Enemies)
                { if (entity.Active) { _sprites.Enemy(_frameGraphics, entity.X, entity.Y, entity.Kind); } }
                foreach (var entity in engine.Bullets)
                { if (entity.Active) { _sprites.Bullet(_frameGraphics, entity.X, entity.Y, entity.Kind); } }
                foreach (var entity in engine.EnemyBullets)
                { if (entity.Active) { _sprites.EnemyBullet(_frameGraphics, entity.X, entity.Y); } }
                for (var index = 0; index < engine.Players.Length; index++)
                {
                    var player = engine.Players[index];
                    if (player.Active) { _sprites.Player(_frameGraphics, player.X, player.Y, index, tick, player.InvulnerableTicks); }
                }
                foreach (var entity in engine.Explosions)
                { if (entity.Active) { _sprites.Explosion(_frameGraphics, entity.X, entity.Y, entity.Age); } }
                DrawHud(engine);
                if (engine.GameOver) { DrawOverlay("MISSION END", "点击重开，继续合作迎战", _sprites.Gold); }
                else if (_session.Mode == GameSessionMode.Idle) { DrawOverlay("PAUSED", "点击重开开始新一局", _sprites.White); }
            }
            _canvas.Invalidate();
        }

        private void DrawAttractScene(uint tick)
        {
            _sprites.Enemy(_frameGraphics, 66, 114, 0);
            _sprites.Enemy(_frameGraphics, 249, 128, 1);
            _sprites.Enemy(_frameGraphics, 160, 90, 2);
            _sprites.EnemyBullet(_frameGraphics, 68, 177);
            _sprites.EnemyBullet(_frameGraphics, 246, 192);
            _sprites.Bullet(_frameGraphics, 108, 256, 0);
            _sprites.Bullet(_frameGraphics, 108, 219, 0);
            _sprites.Bullet(_frameGraphics, 211, 239, 1);
            _sprites.Bullet(_frameGraphics, 211, 205, 1);
            _sprites.Explosion(_frameGraphics, 265, 238, 5);
            _sprites.Player(_frameGraphics, 108, 331, 0, tick, 0);
            _sprites.Player(_frameGraphics, 211, 331, 1, tick, 0);
            _frameGraphics.FillRectangle(_sprites.Panel, new Rectangle(0, 0, 320, 29));
            _frameGraphics.DrawString("THUNDER WING  /  CO-OP", _hudFont, _sprites.White, new Point(8, 9));
            _frameGraphics.DrawString("雷霆战机", _titleFont, _sprites.White, new RectangleF(0, 43, 320, 34), _center);
            var waiting = _session.Mode == GameSessionMode.Host || _session.Mode == GameSessionMode.Join;
            DrawOverlay(waiting ? "READY TO CONNECT" : "FLY TOGETHER",
                waiting ? "等待另一位飞行员加入" : "单人练习 / 双人合作", _sprites.Cyan);
            _frameGraphics.DrawString("P1", _hudFont, _sprites.Cyan, new Point(101, 358));
            _frameGraphics.DrawString("P2", _hudFont, _sprites.Gold, new Point(204, 358));
        }

        private void DrawHud(GameEngine engine)
        {
            _frameGraphics.FillRectangle(_sprites.Panel, new Rectangle(0, 0, 320, 29));
            _frameGraphics.DrawString("SCORE " + engine.Score.ToString("D6") + "   WAVE " + engine.Wave.ToString("D2"),
                _hudFont, _sprites.White, new Point(8, 2));
            for (var index = 0; index < engine.Players.Length; index++)
            {
                if (index == 1 && !engine.Cooperative) { continue; }
                var player = engine.Players[index];
                var label = "P" + (index + 1) + " " + (player.Active ? "LIFE " + player.Lives : player.Lives > 0 ? "READY" : "OUT");
                _frameGraphics.DrawString(label, _hudFont, index == 0 ? _sprites.Cyan : _sprites.Gold,
                    new Point(index == 0 ? 8 : 168, 15));
            }
        }

        private void DrawOverlay(string title, string subtitle, Brush accent)
        {
            _frameGraphics.FillRectangle(_sprites.Panel, new Rectangle(16, 148, 288, 68));
            _frameGraphics.FillRectangle(accent, new Rectangle(16, 148, 288, 2));
            _frameGraphics.FillRectangle(accent, new Rectangle(16, 214, 288, 2));
            _frameGraphics.DrawString(title, _titleFont, accent, new RectangleF(16, 159, 288, 27), _center);
            _frameGraphics.DrawString(subtitle, _infoFont, _sprites.White, new RectangleF(16, 193, 288, 18), _center);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposing)
            {
                _disposing = true;
                _timer.Stop();
                _timer.Tick -= AdvanceFrame;
                _timer.Dispose();
                if (_owner != null)
                {
                    _owner.Deactivate -= OwnerDeactivated;
                    _owner.VisibleChanged -= OwnerVisibilityChanged;
                }
                _keys.Clear();
                _session.Dispose();
                _tips.Dispose();
                _frameGraphics.Dispose();
                _frame.Dispose();
                _sprites.Dispose();
                _hudFont.Dispose(); _titleFont.Dispose(); _infoFont.Dispose(); _center.Dispose();
            }
            base.Dispose(disposing);
            if (disposing) { _uiFont.Dispose(); }
        }

        private sealed class GameCanvas : Control
        {
            private readonly Bitmap _frame;
            public GameCanvas(Bitmap frame)
            {
                _frame = frame;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
                BackColor = Color.FromArgb(3, 7, 16);
            }

            protected override bool IsInputKey(Keys keyData)
            {
                return IsGameKey(keyData & Keys.KeyCode) || base.IsInputKey(keyData);
            }

            protected override void OnPaintBackground(PaintEventArgs args) { }

            protected override void OnPaint(PaintEventArgs args)
            {
                args.Graphics.Clear(BackColor);
                var scale = Math.Max(1, Math.Min(ClientSize.Width / 320, ClientSize.Height / 400));
                var width = 320 * scale;
                var height = 400 * scale;
                args.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                args.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                args.Graphics.DrawImage(_frame, new Rectangle((ClientSize.Width - width) / 2,
                    (ClientSize.Height - height) / 2, width, height), new Rectangle(0, 0, 320, 400), GraphicsUnit.Pixel);
                base.OnPaint(args);
            }
        }
    }
}
