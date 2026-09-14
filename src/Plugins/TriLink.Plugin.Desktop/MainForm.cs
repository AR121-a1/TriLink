using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TriLink.Core;
using TriLink.Plugin;

namespace TriLink.MinClient
{
    internal sealed class MainForm : Form
    {
        private const int WmDeviceChange = 0x0219;

        private readonly IRoomNetwork _network;
        private readonly IDeviceDiscoveryService _serialWatcher;
        private readonly ISimulationControl _simulation;
        private readonly IPluginCatalog _pluginCatalog;
        private readonly string _profileName;
        private readonly NotifyIcon _trayIcon;
        private readonly ComboBox _currentNodeCombo = new ComboBox();
        private readonly Button _simulateButton = new Button();
        private readonly Button _pollingButton = new Button();
        private readonly Button _searchButton = new Button();
        private readonly Button _createRoomButton = new Button();
        private readonly Button _leaveRoomButton = new Button();
        private readonly Button _backgroundButton = new Button();
        private readonly Label _modeLabel = new Label();
        private readonly Label _roomHeader = new Label();
        private readonly Label _replicaLabel = new Label();
        private readonly Label _statusLabel = new Label();
        private readonly Label _pluginHealthLabel = new Label();
        private readonly DataGridView _nearbyGrid;
        private readonly DataGridView _membersGrid;
        private readonly DataGridView _requestsGrid;
        private readonly DataGridView _invitationsGrid;
        private readonly DataGridView _pluginsGrid;
        private readonly TextBox _eventLog = new TextBox();
        private SplitContainer _mainSplit;
        private TabControl _roomTabs;
        private TabPage _pluginsTab;

        private bool _refreshing;
        private bool _exitRequested;
        private readonly bool _showPluginsOnStart;

        public MainForm(
            bool demoMode,
            bool showPluginsOnStart,
            IRoomNetwork network,
            IDeviceDiscoveryService serialWatcher,
            ISimulationControl simulation,
            IPluginCatalog pluginCatalog,
            string profileName)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _serialWatcher = serialWatcher
                ?? throw new ArgumentNullException(nameof(serialWatcher));
            _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            _pluginCatalog = pluginCatalog
                ?? throw new ArgumentNullException(nameof(pluginCatalog));
            _profileName = string.IsNullOrWhiteSpace(profileName) ? "desktop" : profileName;
            _showPluginsOnStart = showPluginsOnStart;

            TraceLifecycle("construct");
            Text = "TriLink 最小客户端";
            MinimumSize = new Size(980, 640);
            Size = new Size(1180, 760);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(245, 247, 250);

            _nearbyGrid = CreateGrid();
            _membersGrid = CreateGrid();
            _requestsGrid = CreateGrid();
            _invitationsGrid = CreateGrid();
            _pluginsGrid = CreateGrid();

            _network.Changed += NetworkChanged;
            _network.Notice += NetworkNotice;

            _serialWatcher.DeviceArrived += SerialDeviceArrived;
            _serialWatcher.DeviceRemoved += SerialDeviceRemoved;
            _serialWatcher.Status += SerialStatus;
            _serialWatcher.PollingStateChanged += SerialPollingStateChanged;

            _trayIcon = CreateTrayIcon();
            BuildLayout();
            BindHandlers();
            foreach (var device in _serialWatcher.Devices)
            {
                AddOrUpdateSerialDevice(device, false);
            }

            ReloadNodeChoices("10:00:00:00:00:01");
            RefreshUi();

            Shown += (_, __) =>
            {
                BalanceMainPanels();
                Log("客户端已启动：宿主已加载首方插件，USB CDC 识别服务就绪。", false);
                if (demoMode)
                {
                    Log("三节点模拟已上线。可从电脑 A 创建房间并邀请 B/C。", false);
                }

                Log("按 X 隐藏到托盘；双击托盘图标恢复，右键选择“退出”关闭客户端。", false);
            };
            Resize += (_, __) => BalanceMainPanels();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == HostWindowMessages.ActivateMessage)
            {
                RestoreMainWindow();
                message.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref message);
            if (message.Msg == WmDeviceChange)
            {
                _serialWatcher.RequestScan();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            TraceLifecycle(
                "form-closing reason=" + e.CloseReason
                + " exitRequested=" + _exitRequested
                + " visible=" + Visible);
            base.OnFormClosing(e);
            // Explicit exit and Windows shutdown must not be converted to tray mode.
            if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                EnterBackgroundMode();
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TraceLifecycle(
                "shown visible=" + Visible
                + " state=" + WindowState
                + " handle=" + Handle);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            TraceLifecycle("visible-changed visible=" + Visible);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _network.Changed -= NetworkChanged;
                _network.Notice -= NetworkNotice;
                _serialWatcher.DeviceArrived -= SerialDeviceArrived;
                _serialWatcher.DeviceRemoved -= SerialDeviceRemoved;
                _serialWatcher.Status -= SerialStatus;
                _serialWatcher.PollingStateChanged -= SerialPollingStateChanged;
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            base.Dispose(disposing);
        }

        internal void SaveScreenshot(string path)
        {
            using (var bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        internal void ExitApplication()
        {
            _exitRequested = true;
            Close();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(14),
                BackColor = BackColor,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 145F));
            Controls.Add(root);

            root.Controls.Add(BuildToolbar(), 0, 0);

            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 8,
                BackColor = BackColor,
            };
            _mainSplit.Panel1.Padding = new Padding(0, 0, 4, 0);
            _mainSplit.Panel2.Padding = new Padding(4, 0, 0, 0);
            _mainSplit.Panel1.Controls.Add(BuildNearbyPanel());
            _mainSplit.Panel2.Controls.Add(BuildRoomPanel());
            root.Controls.Add(_mainSplit, 0, 1);

            var logGroup = new GroupBox
            {
                Text = "事件",
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
            };
            _eventLog.Dock = DockStyle.Fill;
            _eventLog.Multiline = true;
            _eventLog.ReadOnly = true;
            _eventLog.ScrollBars = ScrollBars.Vertical;
            _eventLog.BackColor = Color.White;
            _eventLog.BorderStyle = BorderStyle.FixedSingle;
            logGroup.Controls.Add(_eventLog);
            root.Controls.Add(logGroup, 0, 2);
        }

        private Control BuildToolbar()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.White,
                Padding = new Padding(12, 9, 12, 9),
                Margin = new Padding(0, 0, 0, 10),
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
            };
            actions.Controls.Add(new Label
            {
                Text = "当前电脑",
                AutoSize = true,
                Margin = new Padding(0, 9, 8, 0),
            });

            _currentNodeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _currentNodeCombo.DisplayMember = "Label";
            _currentNodeCombo.Width = 170;
            _currentNodeCombo.Margin = new Padding(0, 4, 12, 0);
            actions.Controls.Add(_currentNodeCombo);

            ConfigureButton(_simulateButton, "模拟上线", Color.FromArgb(235, 239, 245));
            ConfigureButton(_pollingButton, "启动轮询", Color.FromArgb(235, 239, 245));
            ConfigureButton(_searchButton, "搜索设备", Color.FromArgb(31, 111, 235), Color.White);
            ConfigureButton(_createRoomButton, "创建 Room", Color.FromArgb(30, 142, 90), Color.White);
            ConfigureButton(_leaveRoomButton, "退出 Room", Color.FromArgb(235, 239, 245));
            ConfigureButton(_backgroundButton, "驻留后台", Color.FromArgb(235, 239, 245));
            actions.Controls.Add(_simulateButton);
            actions.Controls.Add(_pollingButton);
            actions.Controls.Add(_searchButton);
            actions.Controls.Add(_createRoomButton);
            actions.Controls.Add(_leaveRoomButton);
            actions.Controls.Add(_backgroundButton);
            panel.Controls.Add(actions, 0, 0);

            var status = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
            };
            _modeLabel.AutoSize = true;
            _modeLabel.Font = new Font(Font, FontStyle.Bold);
            _modeLabel.ForeColor = Color.FromArgb(31, 111, 235);
            _statusLabel.AutoSize = true;
            _statusLabel.ForeColor = Color.DimGray;
            _pluginHealthLabel.AutoSize = true;
            _pluginHealthLabel.ForeColor = Color.FromArgb(30, 142, 90);
            status.Controls.Add(_modeLabel);
            status.Controls.Add(_statusLabel);
            status.Controls.Add(_pluginHealthLabel);
            panel.Controls.Add(status, 1, 0);
            return panel;
        }

        private Control BuildNearbyPanel()
        {
            var group = new GroupBox
            {
                Text = "附近设备",
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
            };
            _nearbyGrid.Columns.Add(TextColumn("device", "设备", 145));
            _nearbyGrid.Columns.Add(TextColumn("link", "链路", 72));
            _nearbyGrid.Columns.Add(TextColumn("rssi", "RSSI", 52));
            _nearbyGrid.Columns.Add(TextColumn("room", "公开 Room", 135));
            _nearbyGrid.Columns.Add(TextColumn("leader", "Leader", 90));
            _nearbyGrid.Columns.Add(ButtonColumn("join", "加入", "加入", 55));
            _nearbyGrid.Columns.Add(ButtonColumn("invite", "邀请", "邀请", 55));
            _nearbyGrid.CellContentClick += NearbyGridCellContentClick;
            group.Controls.Add(_nearbyGrid);
            return group;
        }

        private Control BuildRoomPanel()
        {
            var group = new GroupBox
            {
                Text = "Room：对等副本 + Leader 准入控制",
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var header = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
            };
            _roomHeader.AutoSize = true;
            _roomHeader.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            _replicaLabel.AutoSize = true;
            _replicaLabel.ForeColor = Color.DimGray;
            header.Controls.Add(_roomHeader);
            header.Controls.Add(_replicaLabel);
            layout.Controls.Add(header, 0, 0);

            _roomTabs = new TabControl { Dock = DockStyle.Fill };
            _roomTabs.TabPages.Add(BuildMembersTab());
            _roomTabs.TabPages.Add(BuildRequestsTab());
            _roomTabs.TabPages.Add(BuildInvitationsTab());
            _pluginsTab = BuildPluginsTab();
            _roomTabs.TabPages.Add(_pluginsTab);
            if (_showPluginsOnStart)
            {
                _roomTabs.SelectedTab = _pluginsTab;
            }

            layout.Controls.Add(_roomTabs, 0, 1);
            group.Controls.Add(layout);
            return group;
        }

        private TabPage BuildMembersTab()
        {
            var page = new TabPage("成员");
            _membersGrid.Columns.Add(TextColumn("order", "顺序", 52));
            _membersGrid.Columns.Add(TextColumn("member", "成员", 145));
            _membersGrid.Columns.Add(TextColumn("id", "节点 ID", 145));
            _membersGrid.Columns.Add(TextColumn("role", "角色", 74));
            _membersGrid.Columns.Add(ButtonColumn("kick", "管理", "踢出", 58));
            _membersGrid.CellContentClick += MembersGridCellContentClick;
            page.Controls.Add(_membersGrid);
            return page;
        }

        private TabPage BuildRequestsTab()
        {
            var page = new TabPage("入房申请");
            _requestsGrid.Columns.Add(TextColumn("candidate", "申请设备", 145));
            _requestsGrid.Columns.Add(TextColumn("sponsor", "来源", 110));
            _requestsGrid.Columns.Add(TextColumn("time", "时间", 70));
            _requestsGrid.Columns.Add(ButtonColumn("approve", "准入", "同意", 58));
            _requestsGrid.Columns.Add(ButtonColumn("reject", "拒绝", "拒绝", 58));
            _requestsGrid.CellContentClick += RequestsGridCellContentClick;
            page.Controls.Add(_requestsGrid);
            return page;
        }

        private TabPage BuildInvitationsTab()
        {
            var page = new TabPage("收到邀请");
            _invitationsGrid.Columns.Add(TextColumn("room", "Room", 145));
            _invitationsGrid.Columns.Add(TextColumn("from", "邀请者", 130));
            _invitationsGrid.Columns.Add(ButtonColumn("accept", "接受", "接受", 58));
            _invitationsGrid.Columns.Add(ButtonColumn("decline", "拒绝", "拒绝", 58));
            _invitationsGrid.CellContentClick += InvitationsGridCellContentClick;
            page.Controls.Add(_invitationsGrid);
            return page;
        }

        private TabPage BuildPluginsTab()
        {
            var page = new TabPage("插件");
            _pluginsGrid.Columns.Add(TextColumn("plugin", "插件", 135));
            _pluginsGrid.Columns.Add(TextColumn("id", "ID", 125));
            _pluginsGrid.Columns.Add(TextColumn("version", "版本", 58));
            _pluginsGrid.Columns.Add(TextColumn("state", "状态", 68));
            var capabilities = TextColumn("capabilities", "能力", 180);
            capabilities.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _pluginsGrid.Columns.Add(capabilities);
            page.Controls.Add(_pluginsGrid);
            return page;
        }

        private void BindHandlers()
        {
            _currentNodeCombo.SelectedIndexChanged += (_, __) =>
            {
                if (!_refreshing)
                {
                    RefreshUi();
                }
            };
            _simulateButton.Click += (_, __) =>
            {
                _simulation.SetAllOnline(true);
                Log("模拟链路：S3-A、S3-B、S3-C 均已上线。", true);
                ShowNotice("TriLink 模拟设备已连接", "发现 S3-B 与 S3-C，可搜索、加入或邀请。", false);
            };
            _pollingButton.Click += (_, __) =>
            {
                if (_serialWatcher.IsPolling)
                {
                    _serialWatcher.PausePolling();
                    Log("USB 串口轮询已手动暂停。", false);
                }
                else
                {
                    _serialWatcher.ResumePolling();
                    Log("USB 串口轮询已手动启动；失败计数清零并立即扫描。", true);
                }
            };
            _searchButton.Click += async (_, __) => await SearchNearbyAsync();
            _createRoomButton.Click += (_, __) => RunOperation(
                () => _network.CreateRoom(
                    CurrentNodeId,
                    CurrentNode.DisplayName.Replace(" / ", " ") + " 的 Room"));
            _leaveRoomButton.Click += (_, __) => RunOperation(
                () => _network.LeaveRoom(CurrentNodeId));
            _backgroundButton.Click += (_, __) => EnterBackgroundMode();
        }

        private void RefreshUi()
        {
            if (_refreshing || CurrentNode == null)
            {
                return;
            }

            _refreshing = true;
            try
            {
                var current = CurrentNode;
                var room = _network.GetRoomForNode(current.NodeId);
                _modeLabel.Text = current.IsSimulated ? "SIMULATION" : "USB CDC";
                _statusLabel.Text = current.IsOnline
                    ? current.DisplayName + " · 在线"
                    : current.DisplayName + " · 离线";
                _statusLabel.ForeColor = current.IsOnline
                    ? Color.FromArgb(30, 142, 90)
                    : Color.Firebrick;

                _createRoomButton.Enabled = room == null && current.IsOnline;
                _leaveRoomButton.Enabled = room != null;
                PopulateNearby(current.NodeId);
                PopulateRoom(room, current.NodeId);
                PopulateInvitations(current.NodeId);
                PopulatePlugins();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void PopulateNearby(string currentNodeId)
        {
            _nearbyGrid.Rows.Clear();
            foreach (var item in _network.SearchNearby(currentNodeId))
            {
                var roomText = string.IsNullOrWhiteSpace(item.RoomName)
                    ? "—"
                    : item.RoomName;
                var leader = string.IsNullOrWhiteSpace(item.LeaderNodeId)
                    ? "—"
                    : ShortId(item.LeaderNodeId);
                var index = _nearbyGrid.Rows.Add(
                    item.Node.DisplayName,
                    item.Node.Transport,
                    item.Node.Rssi == 0 ? "—" : item.Node.Rssi.ToString(),
                    roomText,
                    leader,
                    item.CanRequestJoin ? "加入" : "—",
                    item.CanInvite ? "邀请" : "—");
                var row = _nearbyGrid.Rows[index];
                row.Tag = item;
                if (!item.CanRequestJoin)
                {
                    row.Cells["join"].Style.ForeColor = Color.Gray;
                }

                if (!item.CanInvite)
                {
                    row.Cells["invite"].Style.ForeColor = Color.Gray;
                }
            }
        }

        private void PopulateRoom(IRoomSession room, string currentNodeId)
        {
            _membersGrid.Rows.Clear();
            _requestsGrid.Rows.Clear();

            if (room == null)
            {
                _roomHeader.Text = "未加入 Room";
                _replicaLabel.Text = "创建 Room 后成为初始 leader；其他节点可申请加入。";
                return;
            }

            var snapshot = room.Snapshot;
            var lifecycle = snapshot.Lifecycle == RoomLifecycle.WaitingForFirstPeer
                ? "等待首位成员"
                : "正式房间";
            _roomHeader.Text = snapshot.RoomName + " · " + lifecycle;
            _replicaLabel.Text = string.Format(
                "ID {0} · term {1} · rev {2} · 副本 {3}/{4} {5}",
                snapshot.RoomId,
                snapshot.Term,
                snapshot.Revision,
                room.Replicas.Count,
                snapshot.Members.Count,
                room.ReplicasAgree() ? "一致" : "不一致");

            foreach (var member in snapshot.Members.OrderBy(item => item.JoinOrder))
            {
                var isLeader = string.Equals(
                    member.NodeId,
                    snapshot.LeaderNodeId,
                    StringComparison.Ordinal);
                var canKick = room.IsLeader(currentNodeId)
                    && !string.Equals(member.NodeId, currentNodeId, StringComparison.Ordinal);
                var index = _membersGrid.Rows.Add(
                    member.JoinOrder,
                    member.DisplayName,
                    member.NodeId,
                    isLeader ? "Leader" : "成员",
                    canKick ? "踢出" : "—");
                _membersGrid.Rows[index].Tag = member;
            }

            foreach (var request in snapshot.PendingJoinRequests)
            {
                var sponsor = string.Equals(
                    request.RequestedByNodeId,
                    request.CandidateNodeId,
                    StringComparison.Ordinal)
                    ? "主动加入"
                    : "由 " + ShortId(request.RequestedByNodeId) + " 邀请";
                var leaderCanAct = room.IsLeader(currentNodeId);
                var index = _requestsGrid.Rows.Add(
                    request.CandidateName,
                    sponsor,
                    request.CreatedUtc.ToLocalTime().ToString("HH:mm:ss"),
                    leaderCanAct ? "同意" : "—",
                    leaderCanAct ? "拒绝" : "—");
                _requestsGrid.Rows[index].Tag = request;
            }
        }

        private void PopulateInvitations(string currentNodeId)
        {
            _invitationsGrid.Rows.Clear();
            foreach (var invitation in _network.GetPendingInvitations(currentNodeId))
            {
                var index = _invitationsGrid.Rows.Add(
                    invitation.RoomName,
                    invitation.FromDisplayName,
                    "接受",
                    "拒绝");
                _invitationsGrid.Rows[index].Tag = invitation;
            }
        }

        private void PopulatePlugins()
        {
            var plugins = _pluginCatalog.Plugins;
            _pluginsGrid.Rows.Clear();
            foreach (var plugin in plugins)
            {
                var capabilities = plugin.Capabilities == null
                    ? string.Empty
                    : string.Join(", ", plugin.Capabilities);
                var stateText = PluginStateText(plugin.State);
                var index = _pluginsGrid.Rows.Add(
                    plugin.DisplayName,
                    plugin.Id,
                    plugin.Version,
                    stateText,
                    capabilities);
                var row = _pluginsGrid.Rows[index];
                row.Tag = plugin;
                if (plugin.State == PluginState.Failed)
                {
                    row.DefaultCellStyle.ForeColor = Color.Firebrick;
                    row.Cells["capabilities"].Value = "错误：" + plugin.Error;
                }
            }

            var active = plugins.Count(item => item.State == PluginState.Active);
            var healthy = plugins.All(item => item.State != PluginState.Failed);
            _pluginHealthLabel.Text = string.Format(
                "PROFILE {0} · PLUGINS {1}/{2} · API {3}",
                _profileName,
                active,
                plugins.Count,
                PluginApi.Version);
            _pluginHealthLabel.ForeColor = healthy
                ? Color.FromArgb(30, 142, 90)
                : Color.Firebrick;
            _pluginsTab.Text = "插件 (" + plugins.Count + ")";
        }

        private async Task SearchNearbyAsync()
        {
            var current = CurrentNode;
            if (current == null)
            {
                return;
            }

            _searchButton.Enabled = false;
            try
            {
                if (!current.IsSimulated && !string.IsNullOrWhiteSpace(current.PortName))
                {
                    Log("正在通过 " + current.PortName + " 请求 S3 搜索结果……", false);
                    var peers = await _serialWatcher.SearchNearbyAsync(current.PortName);
                    foreach (var peer in peers)
                    {
                        _network.AddOrUpdateNode(
                            new NodeInfo(peer.NodeId, peer.DisplayName, false)
                            {
                                IsOnline = true,
                                Transport = "ESP-NOW",
                                Rssi = peer.Rssi,
                            });
                        if (!string.IsNullOrWhiteSpace(peer.RoomId))
                        {
                            Log(
                                string.Format(
                                    "发现 {0}，广播 Room={1}，leader={2}",
                                    peer.DisplayName,
                                    peer.RoomName,
                                    ShortId(peer.LeaderNodeId)),
                                false);
                        }
                    }

                    Log("真实搜索完成：" + peers.Count + " 个 peer。", true);
                }
                else
                {
                    var count = _network.SearchNearby(current.NodeId).Count;
                    Log("模拟搜索完成：发现 " + count + " 个附近设备。", true);
                }

                RefreshUi();
            }
            catch (Exception exception)
            {
                Log("搜索失败：" + exception.Message, false);
            }
            finally
            {
                _searchButton.Enabled = true;
            }
        }

        private void NearbyGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            var item = _nearbyGrid.Rows[e.RowIndex].Tag as NearbyNodeView;
            if (item == null)
            {
                return;
            }

            var columnName = _nearbyGrid.Columns[e.ColumnIndex].Name;
            if (columnName == "join")
            {
                if (!item.CanRequestJoin)
                {
                    Log("该设备当前没有可加入的 Room，或本机已在其他 Room 中。", false);
                    return;
                }

                RunOperation(() => _network.RequestJoin(CurrentNodeId, item.Node.NodeId));
            }
            else if (columnName == "invite")
            {
                if (!item.CanInvite)
                {
                    Log("邀请不可用：请先加入 Room，且目标必须尚未入房。", false);
                    return;
                }

                RunOperation(() => _network.Invite(CurrentNodeId, item.Node.NodeId));
            }
        }

        private void MembersGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || _membersGrid.Columns[e.ColumnIndex].Name != "kick")
            {
                return;
            }

            var member = _membersGrid.Rows[e.RowIndex].Tag as RoomMember;
            var room = _network.GetRoomForNode(CurrentNodeId);
            if (member == null || room == null || !room.IsLeader(CurrentNodeId))
            {
                Log("只有当前 leader 可以踢出其他成员。", false);
                return;
            }

            RunOperation(() => _network.Kick(CurrentNodeId, member.NodeId));
        }

        private void RequestsGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            var request = _requestsGrid.Rows[e.RowIndex].Tag as JoinRequest;
            if (request == null)
            {
                return;
            }

            var columnName = _requestsGrid.Columns[e.ColumnIndex].Name;
            if (columnName == "approve")
            {
                RunOperation(() => _network.ApproveJoin(CurrentNodeId, request.RequestId));
            }
            else if (columnName == "reject")
            {
                RunOperation(() => _network.RejectJoin(CurrentNodeId, request.RequestId));
            }
        }

        private void InvitationsGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            var invitation = _invitationsGrid.Rows[e.RowIndex].Tag as RoomInvitation;
            if (invitation == null)
            {
                return;
            }

            var columnName = _invitationsGrid.Columns[e.ColumnIndex].Name;
            if (columnName == "accept")
            {
                RunOperation(
                    () => _network.AcceptInvitation(CurrentNodeId, invitation.InvitationId));
            }
            else if (columnName == "decline")
            {
                RunOperation(
                    () => _network.DeclineInvitation(CurrentNodeId, invitation.InvitationId));
            }
        }

        private void RunOperation(Func<OperationResult> operation)
        {
            try
            {
                var result = operation();
                Log(result.Message, result.Success);
                RefreshUi();
            }
            catch (Exception exception)
            {
                Log("操作失败：" + exception.Message, false);
            }
        }

        private void NetworkChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshUi));
            }
            else
            {
                RefreshUi();
            }
        }

        private void NetworkNotice(object sender, NetworkNoticeEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => NetworkNotice(sender, e)));
                return;
            }

            Log("[通知→" + ShortId(e.TargetNodeId) + "] " + e.Message, false);
            if (string.Equals(e.TargetNodeId, CurrentNodeId, StringComparison.Ordinal))
            {
                ShowNotice(e.Title, e.Message, false);
            }
        }

        private void SerialDeviceArrived(object sender, TriLinkDeviceEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SerialDeviceArrived(sender, e)));
                return;
            }

            AddOrUpdateSerialDevice(e.Device, true);
        }

        private void AddOrUpdateSerialDevice(TriLinkDevice device, bool notify)
        {
            _network.AddOrUpdateNode(
                new NodeInfo(device.NodeId, device.DisplayName, false)
                {
                    IsOnline = true,
                    Transport = "USB CDC",
                    PortName = device.PortName,
                });
            ReloadNodeChoices(device.NodeId);
            if (!notify)
            {
                return;
            }

            Log(
                string.Format(
                    "识别到 TriLink：{0}，端口 {1}，caps=0x{2:X8}",
                    device.DisplayName,
                    device.PortName,
                    device.Capabilities),
                true);
            ShowNotice(
                "TriLink 设备已连接",
                device.DisplayName + " · " + device.PortName,
                true);
        }

        private void SerialDeviceRemoved(object sender, TriLinkDeviceEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SerialDeviceRemoved(sender, e)));
                return;
            }

            try
            {
                _network.SetNodeOnline(e.Device.NodeId, false);
            }
            catch (ArgumentException)
            {
                // A device can disappear while its arrival callback is still queued.
            }

            Log(e.Device.DisplayName + " 已从 " + e.Device.PortName + " 断开。", false);
        }

        private void SerialStatus(object sender, string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SerialStatus(sender, message)));
                return;
            }

            Log(message, false);
        }

        private void SerialPollingStateChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SerialPollingStateChanged(sender, e)));
                return;
            }

            var running = _serialWatcher.IsPolling;
            _pollingButton.Text = running ? "暂停轮询" : "启动轮询";
            _pollingButton.BackColor = running
                ? Color.FromArgb(235, 239, 245)
                : Color.FromArgb(196, 78, 48);
            _pollingButton.ForeColor = running
                ? Color.FromArgb(35, 43, 54)
                : Color.White;
        }

        private void ShowNotice(string title, string message, bool bringToFront)
        {
            _trayIcon.ShowBalloonTip(4500, title, message, ToolTipIcon.Info);
            if (!bringToFront)
            {
                return;
            }

            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ReloadNodeChoices(string preferredNodeId)
        {
            _refreshing = true;
            try
            {
                var selectedId = preferredNodeId;
                if (string.IsNullOrWhiteSpace(selectedId) && CurrentNode != null)
                {
                    selectedId = CurrentNode.NodeId;
                }

                _currentNodeCombo.Items.Clear();
                foreach (var node in _network.Nodes)
                {
                    _currentNodeCombo.Items.Add(new NodeChoice(node));
                }

                var selected = _currentNodeCombo.Items
                    .Cast<NodeChoice>()
                    .FirstOrDefault(item => string.Equals(
                        item.Node.NodeId,
                        selectedId,
                        StringComparison.Ordinal));
                _currentNodeCombo.SelectedItem = selected
                    ?? _currentNodeCombo.Items.Cast<NodeChoice>().FirstOrDefault();
            }
            finally
            {
                _refreshing = false;
            }

            RefreshUi();
        }

        private void BalanceMainPanels()
        {
            if (_mainSplit == null || _mainSplit.Width < 920)
            {
                return;
            }

            var desired = (int)Math.Round(_mainSplit.Width * 0.54);
            var maximum = _mainSplit.Width
                - _mainSplit.SplitterWidth
                - 420;
            _mainSplit.SplitterDistance = Math.Max(
                480,
                Math.Min(desired, maximum));
        }

        private NotifyIcon CreateTrayIcon()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开 TriLink", null, (_, __) =>
            {
                RestoreMainWindow();
            });
            menu.Items.Add("隐藏到托盘", null, (_, __) =>
            {
                EnterBackgroundMode();
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (_, __) =>
            {
                ExitApplication();
            });

            var icon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "TriLink 最小客户端",
                Visible = true,
                ContextMenuStrip = menu,
            };
            icon.DoubleClick += (_, __) =>
            {
                RestoreMainWindow();
            };
            return icon;
        }

        private void EnterBackgroundMode()
        {
            Log("已按用户操作驻留后台；再次启动客户端或双击托盘图标可恢复。", false);
            Hide();
            _trayIcon.ShowBalloonTip(
                2500,
                "TriLink 正在后台运行",
                "双击托盘图标或再次启动 TriLink 可恢复主窗口。",
                ToolTipIcon.Info);
        }

        private void RestoreMainWindow()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RestoreMainWindow));
                return;
            }

            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            BringToFront();
            Activate();
        }

        private void Log(string message, bool success)
        {
            var marker = success ? "✓" : "·";
            _eventLog.AppendText(
                DateTime.Now.ToString("HH:mm:ss")
                + " "
                + marker
                + " "
                + message
                + Environment.NewLine);
        }

        private NodeInfo CurrentNode
        {
            get
            {
                var choice = _currentNodeCombo.SelectedItem as NodeChoice;
                return choice == null ? null : choice.Node;
            }
        }

        private string CurrentNodeId
        {
            get
            {
                var node = CurrentNode;
                return node == null ? null : node.NodeId;
            }
        }

        private static DataGridView CreateGrid()
        {
            return new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                GridColor = Color.FromArgb(226, 231, 238),
            };
        }

        private static DataGridViewTextBoxColumn TextColumn(
            string name,
            string header,
            int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
        }

        private static DataGridViewButtonColumn ButtonColumn(
            string name,
            string header,
            string text,
            int width)
        {
            return new DataGridViewButtonColumn
            {
                Name = name,
                HeaderText = header,
                Text = text,
                Width = width,
                UseColumnTextForButtonValue = false,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
        }

        private static void ConfigureButton(
            Button button,
            string text,
            Color backColor,
            Color? foreColor = null)
        {
            button.Text = text;
            button.AutoSize = true;
            button.Height = 30;
            button.Margin = new Padding(0, 2, 8, 0);
            button.Padding = new Padding(8, 0, 8, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = backColor;
            button.ForeColor = foreColor ?? Color.FromArgb(35, 43, 54);
            button.UseVisualStyleBackColor = false;
        }

        private static string ShortId(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return "—";
            }

            return nodeId.Length <= 8 ? nodeId : nodeId.Substring(nodeId.Length - 8);
        }

        private static string PluginStateText(PluginState state)
        {
            switch (state)
            {
                case PluginState.Active:
                    return "运行中";
                case PluginState.Configured:
                    return "已配置";
                case PluginState.Stopped:
                    return "已停止";
                case PluginState.Disabled:
                    return "已禁用";
                case PluginState.Failed:
                    return "失败";
                default:
                    return "已发现";
            }
        }

        private static void TraceLifecycle(string message)
        {
            var path = Environment.GetEnvironmentVariable("TRILINK_LIFECYCLE_LOG");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                File.AppendAllText(
                    Path.GetFullPath(path),
                    DateTime.Now.ToString("O")
                    + " pid=" + System.Diagnostics.Process.GetCurrentProcess().Id
                    + " " + message
                    + Environment.NewLine);
            }
            catch (IOException)
            {
                // Diagnostics must never block the client UI.
            }
            catch (UnauthorizedAccessException)
            {
                // Diagnostics must never block the client UI.
            }
        }

        private sealed class NodeChoice
        {
            public NodeChoice(NodeInfo node)
            {
                Node = node;
            }

            public NodeInfo Node { get; private set; }

            public string Label
            {
                get
                {
                    return Node.DisplayName + (Node.IsOnline ? string.Empty : "（离线）");
                }
            }

            public override string ToString()
            {
                return Label;
            }
        }
    }
}
