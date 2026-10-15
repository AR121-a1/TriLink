using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TriLink.Core;

namespace TriLink.Plugins.HardwareRoom
{
    public sealed class HardwareRoomView : UserControl
    {
        private sealed class UserInputException : Exception {public UserInputException(string m):base(m){}}
        private enum ViewState {NoDevice,UnsupportedDevice,NeedsRefresh,Ready,Busy,Invalidated}
        private readonly IDeviceDiscoveryService _discovery;
        private readonly INodeSessionService _sessionService;
        private readonly NodeSessionService _ownedService;
        private INodeSessionSubscription _subscription;
        private NodeSessionSnapshot _snapshot;
        private readonly Func<string,string,bool> _confirm;
        private readonly bool _autoLive;
        private readonly Dictionary<string,int> _peerRssi=new Dictionary<string,int>();
        private readonly Dictionary<string,Button> _buttons=new Dictionary<string,Button>();
        private readonly ToolTip _tooltips=new ToolTip {ShowAlways=true};
        private readonly ComboBox _ports=new ComboBox {Width=280,DropDownWidth=600,FormattingEnabled=true,DropDownStyle=ComboBoxStyle.DropDownList,Name="HardwarePorts"};
        private readonly TextBox _name=new TextBox {Width=125,Text="TriLink Room",MaxLength=23,Name="HardwareRoomName"};
        private readonly ListBox _rooms=new ListBox {Dock=DockStyle.Fill,Name="NearbyRooms"},_members=new ListBox {Dock=DockStyle.Fill,Name="RoomMembers"},
            _requests=new ListBox {Dock=DockStyle.Fill,Name="RoomRequests"},_invites=new ListBox {Dock=DockStyle.Fill,Name="RoomInvites"};
        private readonly ComboBox _peers=new ComboBox {Width=240,DropDownWidth=480,FormattingEnabled=true,DropDownStyle=ComboBoxStyle.DropDownList,Name="HardwareInvitePeers"};
        private readonly Label _status=new Label {Dock=DockStyle.Fill,AutoEllipsis=true,Name="HardwareRoomStatus"};
        private readonly TextBox _log=new TextBox {Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Name="HardwareRoomLog"};
        private readonly Button _monitor=new Button {AutoSize=true,Text="启动状态轮询",Name="HardwareMonitor"};
        private readonly Label _queueStatus=new Label {Dock=DockStyle.Bottom,Height=22,AutoEllipsis=true,Name="HardwareCommandQueue"};
        private RoomWire _state;
        private TriLinkDevice _selectedDevice;
        private NodeOperationSnapshot _executingIntent;
        private IReadOnlyList<NodeOperationSnapshot> _queuedOperations=new NodeOperationSnapshot[0];
        private bool _busy,_automatic,_rgbPending,_rgbTracking,_backgroundReading,_reloadPorts;
        private int _failures;
        private string _invalidMessage,_submittedRgbId;
        public HardwareRoomView(IHardwareCommandService commands,IDeviceDiscoveryService discovery):this(commands,discovery,null,true){}
        internal HardwareRoomView(IHardwareCommandService commands,IDeviceDiscoveryService discovery,Func<string,string,bool> confirm):this(commands,discovery,confirm,false){}
        internal HardwareRoomView(IHardwareCommandService commands,IDeviceDiscoveryService discovery,Func<string,string,bool> confirm,bool autoLive)
            :this(new NodeSessionService(commands,discovery),discovery,confirm,autoLive,true){}
        public HardwareRoomView(INodeSessionService sessions,IDeviceDiscoveryService discovery):this(sessions,discovery,null,true,false){}
        internal HardwareRoomView(INodeSessionService sessions,IDeviceDiscoveryService discovery,Func<string,string,bool> confirm,bool autoLive,bool ownsService=false)
        {
            _sessionService=sessions??throw new ArgumentNullException(nameof(sessions));_discovery=discovery??throw new ArgumentNullException(nameof(discovery));
            _ownedService=ownsService?(NodeSessionService)sessions:null;_autoLive=autoLive;Dock=DockStyle.Fill;
            _confirm=confirm??((m,c)=>MessageBox.Show(this,m,c,MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(31,90,150),
                Text = "真实 Room / RGB · 状态保存在 S3，各电脑只显示自己的设备\n需配套新版固件；本页不使用模拟数据。RGB 默认关闭，未确认引脚不要启用。" },0,0);
            var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
            top.Controls.Add(_ports); Add(top,"更新连接","HardwareReloadPorts");
            Add(top,"刷新设备与 Room","HardwareRefresh"); top.Controls.Add(_monitor); top.Controls.Add(_name);
            Add(top,"创建","HardwareCreate"); Add(top,"退出","HardwareLeave");
            Add(top,"重试入群确认","HardwareRetryJoin");
            Add(top,"清空待执行","HardwareClearQueue");
            var statusPanel=new Panel {Dock=DockStyle.Fill};statusPanel.Controls.Add(_status);statusPanel.Controls.Add(_queueStatus);
            layout.Controls.Add(top,0,1); layout.Controls.Add(statusPanel,0,2);
            var grids = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            grids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); grids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            grids.RowStyles.Add(new RowStyle(SizeType.Percent,50)); grids.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            grids.Controls.Add(Group("附近 Room（选中后申请）",_rooms),0,0);
            grids.Controls.Add(Group("当前成员（选中后踢出 / RGB）",_members),1,0);
            grids.Controls.Add(Group("待审批（仅 leader 操作）",_requests),0,1);
            grids.Controls.Add(Group("收到邀请（接受后仍需审批）",_invites),1,1);
            layout.Controls.Add(grids,0,3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
            Add(actions,"申请加入","HardwareJoin");Add(actions,"同意","HardwareApprove");Add(actions,"拒绝","HardwareReject");
            Add(actions,"接受邀请","HardwareAcceptInvite");Add(actions,"踢出","HardwareKick");
            actions.Controls.Add(_peers); Add(actions,"邀请设备","HardwareInvite");
            Add(actions,"允许本机 GPIO48 RGB","HardwareRgbEnable"); Add(actions,"撤销本机 RGB 许可","HardwareRgbDisable");
            Add(actions,"本机青色双闪","HardwareRgbLocalPulse");Add(actions,"目标青色双闪","HardwareRgbPulse");
            layout.Controls.Add(actions,0,4); layout.Controls.Add(_log,0,5); Controls.Add(layout);

            _monitor.Click+=(_,__)=> {try {if(_automatic || _executingIntent!=null || _rgbTracking)_sessionService.PausePolling();else _sessionService.ResumePolling();}catch(Exception ex){Log(ex.ToString());}};
            _ports.SelectedIndexChanged+=(_,__)=> {if(!_reloadPorts)SelectedDeviceChanged();};
            // Formatting never changes the endpoint string used by the session.
            _ports.Format+=(_,args)=> {var endpoint=args.ListItem as string;var d=CapableDevices().FirstOrDefault(x=>x.PortName==endpoint);if(d!=null)args.Value=d.TransportKind==NodeTransportKind.UsbNcm?"USB 网卡 NCM · "+Normalize(d.NodeId):"USB 串口 CDC · "+endpoint;};
            foreach(var list in new[]{_rooms,_members,_requests,_invites})list.SelectedIndexChanged+=(_,__)=>UpdateActions();
            _peers.SelectedIndexChanged+=(_,__)=>{UpdateActions();UpdateStatus();};
            _peers.Format+=(_,args)=>{var mac=args.ListItem as string;int rssi;if(mac!=null && _peerRssi.TryGetValue(mac,out rssi))args.Value=mac+" · RSSI "+rssi+" dBm · 仅发现，能力未验证";};
            _name.TextChanged+=(_,__)=>{UpdateActions();UpdateStatus();};
            _sessionService.DevicesChanged+=SessionDevicesChanged;
            HandleCreated+=(_,__)=>TryStartLiveRefresh();VisibleChanged+=(_,__)=>TryStartLiveRefresh();
            Resize+=(_,__)=>{bool narrow=ClientSize.Width<740;layout.RowStyles[0].Height=narrow?72:60;layout.RowStyles[1].Height=narrow?112:72;
                layout.RowStyles[2].Height=narrow?166:126;layout.RowStyles[4].Height=narrow?192:112;};
            ReloadPorts();UpdateStatus();UpdateActions();
        }
        private static GroupBox Group(string title,Control child) {var group=new GroupBox {Text=title,Dock=DockStyle.Fill};group.Controls.Add(child);return group;}
        private void Add(Control parent,string title,string name)
        {
            var button=new Button {Text=title,Name=name,AutoSize=true};_buttons.Add(name,button);
            button.Click+=(_,__)=> {
                if(!button.Enabled)return;
                if(name=="HardwareReloadPorts"){ReloadPorts();return;}
                if(name=="HardwareClearQueue"){_subscription?.CancelPending();return;}
                if(name=="HardwareRefresh"){RequestRefresh();return;}
                try {var intent=CaptureIntent(name);if(intent!=null){var result=_subscription.Submit(intent);if(result.Status!=NodeSubmissionStatus.Queued)Log(result.Message);}}
                catch(UserInputException ex){Log(ex.Message);}
            };parent.Controls.Add(button);
        }
        private void SessionDevicesChanged(object sender,EventArgs e) {if(!IsDisposed && !Disposing)ReloadPorts();}
        private static string Normalize(string mac) {return (mac??"").Replace(":","").ToUpperInvariant();}
        private static bool SameDevice(TriLinkDevice a,TriLinkDevice b) {return a==null||b==null?a==b:a.PortName==b.PortName && a.TransportKind==b.TransportKind && Normalize(a.NodeId)==Normalize(b.NodeId) && a.Capabilities==b.Capabilities && string.Equals(a.PnpDeviceId,b.PnpDeviceId,StringComparison.OrdinalIgnoreCase);}
        private List<TriLinkDevice> CapableDevices() {return _discovery.Devices.Where(d=>d!=null && (d.Capabilities&64)!=0 && !string.IsNullOrWhiteSpace(d.PortName) && RoomWire.ValidMac(Normalize(d.NodeId))).GroupBy(d=>d.PortName).Select(g=>g.First()).ToList();}
        private void ReloadPorts()
        {
            var devices=CapableDevices();var old=_ports.SelectedItem as string;_reloadPorts=true;
            try {_ports.Items.Clear();foreach(var d in devices)_ports.Items.Add(d.PortName);if(old!=null && _ports.Items.Contains(old))_ports.SelectedItem=old;else if(_ports.Items.Count>0)_ports.SelectedIndex=0;}
            finally {_reloadPorts=false;}
            var selected=devices.FirstOrDefault(d=>d.PortName==(_ports.SelectedItem as string));if(!SameDevice(_selectedDevice,selected))SelectedDeviceChanged();else{UpdateStatus();UpdateActions();}
        }
        private void SelectedDeviceChanged()
        {
            if(_subscription!=null){_subscription.Changed-=SessionChanged;_subscription.Dispose();_subscription=null;}
            _selectedDevice=null;_snapshot=null;_state=null;_invalidMessage=null;_rgbPending=false;_rgbTracking=false;_executingIntent=null;_busy=false;_automatic=false;
            _queuedOperations=new NodeOperationSnapshot[0];ClearLists();_peers.Items.Clear();_peerRssi.Clear();
            var selected=CapableDevices().FirstOrDefault(d=>d.PortName==(_ports.SelectedItem as string));
            if(selected!=null) {
                try {_subscription=_sessionService.Bind(selected.NodeId,selected.PortName);_subscription.Changed+=SessionChanged;ApplySnapshot();TryStartLiveRefresh();}
                catch(InvalidOperationException ex){_invalidMessage=ex.Message;Log(ex.ToString());}
            }
            UpdateStatus();UpdateActions();
        }
        private void SessionChanged(object sender,EventArgs e) {if(!IsDisposed && !Disposing)ApplySnapshot();}
        private void ApplySnapshot()
        {
            if(_subscription==null)return;_snapshot=_subscription.Snapshot;_selectedDevice=_snapshot.Device;_state=Wire(_snapshot.Room);
            _busy=_snapshot.Busy;_backgroundReading=_snapshot.BackgroundReading;_automatic=_snapshot.PollingState==NodePollingState.Running;
            _failures=_snapshot.ConsecutiveFailures;_invalidMessage=_snapshot.InvalidReason;_rgbPending=_snapshot.RgbPending;_rgbTracking=_snapshot.RgbTracking;
            _submittedRgbId=_snapshot.RgbOperationId;_executingIntent=_snapshot.ExecutingOperation;_queuedOperations=_snapshot.QueuedOperations;
            ReplaceItems(_members,_state!=null && _state.Active?_state.Members:new string[0]);ReplaceItems(_rooms,_snapshot.NearbyRooms.Select(Wire));
            ReplaceItems(_requests,_snapshot.Requests.Select(Wire));ReplaceItems(_invites,_snapshot.Invitations.Select(Wire));
            var selected=_peers.SelectedItem;_peers.Items.Clear();_peerRssi.Clear();foreach(var p in _snapshot.PeerRssi){_peerRssi[p.Key]=p.Value;_peers.Items.Add(p.Key);}
            if(selected!=null && _peers.Items.Contains(selected))_peers.SelectedItem=selected;else if(_peers.Items.Count>0)_peers.SelectedIndex=0;
            string text=string.Join(Environment.NewLine,_snapshot.Log);if(text.Length>8000)text=text.Substring(text.Length-8000);
            if(_log.Text!=text)_log.Text=text;
            _monitor.Text=_automatic?"暂停状态轮询":_snapshot.PollingState==NodePollingState.FailurePaused || _snapshot.PollingState==NodePollingState.DeviceChanged?"手动重启状态轮询":"启动状态轮询";
            UpdateStatus();UpdateActions();
            if(_selectedDevice!=null) {_status.Text+="\n连接："+(_selectedDevice.TransportKind==NodeTransportKind.UsbNcm?"USB NCM 网卡":"USB CDC 串口（兼容 Windows 10）");_tooltips.SetToolTip(_ports,_selectedDevice.PortName);}
            if(_snapshot.Capabilities!=null) {
                var c=_snapshot.Capabilities;_status.Text+="\n能力："+(c.IsLegacy?"旧固件，版本 / 预算未声明":"固件 "+c.FirmwareVersion+" / 节点契约 "+c.ContractVersion+" / 模块 0x"+c.ModuleMask.ToString("X"));
            }
        }
        private static RoomWire Wire(NodeRoomState record) {return record==null?null:new RoomWire(record.EncodedRecord);}
        private static NodeRoomState Record(RoomWire r) {return r==null?null:new NodeRoomState(r.Bytes,r.Name,r.Leader,r.Room,r.Incarnation,r.Revision,r.Term,r.Active,r.Synchronized,r.Members);}
        private static RoomWire Selected(ListBox list) {return list.SelectedItem as RoomWire??throw new UserInputException("请先选择对应记录。");}
        private NodeOperationIntent CaptureIntent(string action)
        {
            if(!Fresh || _snapshot==null)throw new UserInputException("尚无可操作设备状态，请先刷新。");
            var intent=new NodeOperationIntent {Description=_buttons[action].Text,Name=_name.Text,Generation=_snapshot.Generation,Basis=_snapshot.Room};
            RoomWire record=null;
            switch(action) {
                case "HardwareCreate":intent.Kind=NodeOperationKind.CreateRoom;break;
                case "HardwareLeave":intent.Kind=NodeOperationKind.LeaveRoom;break;
                case "HardwareRetryJoin":intent.Kind=NodeOperationKind.RetryJoin;break;
                case "HardwareJoin":intent.Kind=NodeOperationKind.JoinRoom;record=Selected(_rooms);intent.Target=record.Leader;break;
                case "HardwareAcceptInvite":intent.Kind=NodeOperationKind.AcceptInvitation;record=Selected(_invites);intent.Target=record.Leader;break;
                case "HardwareApprove":intent.Kind=NodeOperationKind.ApproveJoin;record=Selected(_requests);intent.Target=record.Source;break;
                case "HardwareReject":intent.Kind=NodeOperationKind.RejectJoin;record=Selected(_requests);intent.Target=record.Source;break;
                case "HardwareKick":intent.Kind=NodeOperationKind.KickMember;intent.Target=_members.SelectedItem as string;break;
                case "HardwareInvite":intent.Kind=NodeOperationKind.InviteMember;intent.Target=_peers.SelectedItem as string;break;
                case "HardwareRgbDisable":intent.Kind=NodeOperationKind.DisableRgb;break;
                case "HardwareRgbPulse":intent.Kind=NodeOperationKind.MemberRgbPulse;intent.Target=_members.SelectedItem as string;break;
                case "HardwareRgbEnable":
                    intent.Kind=NodeOperationKind.EnableRgb;
                    intent.LocalPermissionConfirmed=_confirm("所选设备："+_selectedDevice.PortName+" · "+LocalMac+"\n仅当已核对所选板为 GPIO48 / WS2812 GRB 单灯时启用。\n本操作仅授予执行许可，不点灯；随后可显式点击本机双闪测试。重启后许可关闭。是否确认？","确认本机 RGB 接线与许可");
                    if(!intent.LocalPermissionConfirmed)return null;break;
                case "HardwareRgbLocalPulse":
                    intent.Kind=NodeOperationKind.LocalRgbPulse;intent.Target=LocalMac;
                    if(!_state.Active) {
                        intent.LocalPermissionConfirmed=_confirm("所选设备："+_selectedDevice.PortName+" · "+LocalMac+"\nRoom 名："+intent.Name+"\n当前固件要求 RGB 在活动 Room 内执行。将创建真实、可被附近设备发现的一人 Room，再向本机发送一次低亮青色双闪。\n测试后保留 Room，不自动退出。是否确认创建并测试？","创建单机 Room 并双闪");
                        if(!intent.LocalPermissionConfirmed)return null;
                    }break;
                default:throw new UserInputException("未知操作未提交。");
            }
            intent.Record=Record(record);
            if(IsDisposed || _snapshot==null || intent.Generation!=_snapshot.Generation)throw new UserInputException("确认期间所选设备已变化，本次操作已取消；请重新核对目标。");
            return intent;
        }
        private void TryStartLiveRefresh() {if(_subscription!=null && !IsDisposed && !Disposing)_subscription.SetVisible(Visible && IsHandleCreated,_autoLive);}
        private void RequestRefresh(bool full=true)
        {if(_subscription==null || IsDisposed)return;if(_ownedService!=null)_ownedService.RequestRefreshCompatibility(full);else {_subscription.RefreshAsync().ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously);}}
        public Task RefreshAll() {if(_ownedService!=null)return _ownedService.RefreshAll();return _subscription==null?Task.FromException(new InvalidOperationException("没有已绑定设备。")):_subscription.RefreshAsync();}
        // Source-compatible test seams route through the coordinator, never own I/O.
        private async Task Run(Func<Task> action)
        {await _ownedService.RunCompatibility(action);if(_subscription==null && !IsDisposed)_log.Text=string.Join(Environment.NewLine,_ownedService.DiagnosticLogCompatibility());}
        private Task RetryJoin() {return _ownedService.RetryJoinCompatibility();}
        private Task SendRoom(byte kind,RoomWire state,string target=null,string name=null) {return _ownedService.SendRoomCompatibility(kind,state,target,name);}
        private static void ReplaceItems(ListBox list,IEnumerable<object> values)
        {
            var selected=list.SelectedItem;list.BeginUpdate();
            try {
                list.Items.Clear();
                foreach(var value in values) {
                    int index=list.Items.Add(value);
                    var old=selected as RoomWire;var next=value as RoomWire;
                    if(old!=null && next!=null ? old.Room==next.Room && old.Incarnation==next.Incarnation
                        && old.Kind==next.Kind && old.Source==next.Source : Equals(selected,value)) list.SelectedIndex=index;
                }
            } finally {list.EndUpdate();}
        }
        private void ClearLists() {_rooms.Items.Clear();_members.Items.Clear();_requests.Items.Clear();_invites.Items.Clear();}
        private bool Fresh {get{return _selectedDevice!=null && _state!=null && _invalidMessage==null;}}
        private string LocalMac {get{return _selectedDevice==null?"":Normalize(_selectedDevice.NodeId);}}
        private bool Leader {get{return Fresh && _state.Active && _state.Leader==LocalMac;}}
        private bool AuthorityReady {get{return Leader && _state.Synchronized && !_state.Waiting && _state.Revision<uint.MaxValue && _state.Term<uint.MaxValue;}}
        private bool CanStartRoom {get{return Fresh && !_state.Active && !_state.Waiting && !_state.JoinConfirmed && !_state.JoinUncertain && !_state.Retiring;}}
        private static bool ValidName(string name)
        {try {RoomWire.Command(16,null,null,name);return true;}catch(ArgumentException){return false;}}
        private bool CanJoin(RoomWire room)
        {return CanStartRoom && room!=null && room.Count>0 && room.Count<6 && RoomWire.ValidMac(room.Leader) && !room.Members.Contains(LocalMac);}
        private bool MatchingRequest(RoomWire request)
        {return Fresh && request!=null && request.Room==_state.Room && request.Incarnation==_state.Incarnation && RoomWire.ValidMac(request.Source);}
        private bool CanPulse(string target)
        {return Fresh && _state.Active && !_state.Waiting && !_rgbPending && target!=null && _state.Members.Contains(target)
            && (target!=LocalMac || (_state.RgbEnabled && !_state.RgbFaulted));}
        private bool CanLocalPulseFor(string name) {return Fresh && !_state.Waiting && !_state.Retiring && !_state.RgbFaulted && _state.RgbEnabled && !_rgbPending
            && (_state.Active?_state.Members.Contains(LocalMac):CanStartRoom && ValidName(name));}
        private bool CanLocalPulse {get {return CanLocalPulseFor(_name.Text);}}
        private ViewState CurrentViewState()
        {
            if(_busy && _executingIntent!=null)return ViewState.Busy;
            if(_selectedDevice==null)return _discovery.Devices.Count>0?ViewState.UnsupportedDevice:ViewState.NoDevice;
            if(_invalidMessage!=null)return ViewState.Invalidated;
            return _state==null?ViewState.NeedsRefresh:ViewState.Ready;
        }
        private void SetAction(string name,bool enabled,string reason)
        {var button=_buttons[name];button.Enabled=enabled;_tooltips.SetToolTip(button,reason);}
        private void UpdateActions()
        {
            if(IsDisposed)return;
            // A good last-known snapshot may submit an intention while background
            // reads or another foreground item are in progress. Execution revalidates it.
            bool idle=true,ready=Fresh;
            _ports.Enabled=idle;_peers.Enabled=ready && _peers.Items.Count>0;
            _name.Enabled=idle && CanStartRoom;
            SetAction("HardwareReloadPorts",idle,"从共享识别结果更新列表，不会另开串口或执行设备写操作。");
            SetAction("HardwareRefresh",_selectedDevice!=null,"只读刷新合并为一项；合法前台动作可在每笔I/O边界优先排队。");
            SetAction("HardwareClearQueue",_queuedOperations.Count>0,"只清空未提交待执行项；不能撤销已经提交给USB服务的命令。");
            _monitor.Enabled=_automatic || _rgbTracking || (idle && _selectedDevice!=null);
            _monitor.Text=_executingIntent!=null && !_automatic?(_rgbTracking?"暂停回执跟踪":"暂停后续操作"):_automatic?"暂停状态轮询"
                :(_failures>=5 || _monitor.Text=="手动重启状态轮询"?"手动重启状态轮询":"启动状态轮询");
            _tooltips.SetToolTip(_monitor,_rgbTracking?"暂停只停止本次后续回执读取，不撤销已发送双闪，也不会重放；已开始的USB事务由共享服务收尾。"
                :"leader每2秒、其他每5秒合并只读刷新；活动Room邻居SEARCH每15秒，连续五次通信失败自动暂停。");
            SetAction("HardwareCreate",idle && CanStartRoom && ValidName(_name.Text),"须成功刷新为未入房、无请求、无退出重播；名称为 1–23 个 UTF-8 字节。");
            SetAction("HardwareJoin",idle && CanJoin(_rooms.SelectedItem as RoomWire),"未入房时选中人数少于 6 的附近 Room；入群待确认或退出重播时不可另发申请。");
            SetAction("HardwareAcceptInvite",idle && CanJoin(_invites.SelectedItem as RoomWire),"选中邀请后向对应 leader 申请，仍需审批；已入房或结果未知时不可申请。");
            SetAction("HardwareLeave",ready && _state.Active && !_state.Waiting && (!Leader || AuthorityReady),"成员可退出；leader 须等成员同步确认完成，避免丢失接任公告。");
            SetAction("HardwareRetryJoin",ready && !_state.Active && _state.Waiting && _state.JoinConfirmed && _state.JoinUncertain,"只重试已经同意且结果待确认的入群票据，不重新创建或申请 Room。");
            var request=_requests.SelectedItem as RoomWire;
            SetAction("HardwareApprove",idle && AuthorityReady && _state.Count<6 && MatchingRequest(request) && request.Kind==3,"仅已同步 leader 可同意选中的新申请；已同意待确认的票据不能再次同意。");
            SetAction("HardwareReject",idle && AuthorityReady && MatchingRequest(request) && (request.Kind==3 || request.Kind==7),"仅已同步 leader 可拒绝选中的申请或已同意待确认票据。");
            var member=_members.SelectedItem as string;var peer=_peers.SelectedItem as string;
            SetAction("HardwareKick",idle && AuthorityReady && member!=null && member!=LocalMac && _state.Members.Contains(member),"仅已同步 leader 可踢出其他成员；不能踢自己，自己请退出。");
            SetAction("HardwareInvite",ready && _state.Active && !_state.Waiting && _state.Count<6 && peer!=null && RoomWire.ValidMac(peer) && !_state.Members.Contains(peer),"每位成员都可邀请附近的非成员；目标接受后仍由 leader 审批，最多 6 人。");
            bool ourPending=_rgbPending && _rgbTracking && _executingIntent!=null;
            SetAction("HardwareRgbEnable",ready && !_state.Waiting && !_state.Retiring && !_state.RgbEnabled && !_state.RgbFaulted && (!_rgbPending || ourPending),"仅在本机确认 GPIO48 / WS2812 GRB 单灯接线后显式启用，不会自动启用远端。");
            SetAction("HardwareRgbDisable",ready && _state.RgbEnabled && (!_rgbPending || ourPending),"撤销许可可排在本视图的在途任务后；出队时再次读取真实许可和pending状态。");
            _buttons["HardwareRgbLocalPulse"].Text=Fresh && !_state.Active?"创建单机 Room 并双闪":"本机青色双闪";
            bool localOffer=CanLocalPulse || (ourPending && ready && _state.Active && !_state.Waiting && !_state.Retiring && _state.RgbEnabled && !_state.RgbFaulted && _state.Members.Contains(LocalMac));
            bool memberOffer=CanPulse(member) || (ourPending && ready && _state.Active && !_state.Waiting && member!=null && _state.Members.Contains(member)
                && (member!=LocalMac || (_state.RgbEnabled && !_state.RgbFaulted)));
            SetAction("HardwareRgbLocalPulse",localOffer,"无需成员选择；同一在途/排队动作去重，无Room时须显式确认创建真实一人Room；不自动重发。");
            SetAction("HardwareRgbPulse",memberOffer,"选中成员后捕获MAC/boot；可排在当前动作后，出队再次核验，不自动重放未知动作。");
            _queueStatus.Text="当前："+(_executingIntent!=null?_executingIntent.Description+(_executingIntent.WriteAttempted?"（已提交/可能在途）":"（核验中）"):_backgroundReading?"后台只读，可让位":"空闲")
                +" · 待执行 "+_queuedOperations.Count+"/8 · "+string.Join(" → ",_queuedOperations.Select(i=>i.Description+(i.Target==null?"":" "+i.Target)));
        }
        private void UpdateStatus()
        {
            switch(CurrentViewState()) {
                case ViewState.NoDevice: _status.Text="未连接已识别的 S3 原生 USB 数据口。CH343 / CH340 是烧录口，不能操作 Room。\n请连接核心板原生 USB，在主窗口扫描识别后，本页会自动更新端口；无设备时业务按钮不发送命令。";return;
                case ViewState.UnsupportedDevice: _status.Text="已有识别记录，但没有支持真实 Room / RGB 的设备（需要设备声明该能力和有效 MAC）。\n请核对配套固件、原生 USB 数据口并重新扫描；本页不会退回模拟 Room。";return;
                case ViewState.NeedsRefresh: _status.Text="已识别 "+_selectedDevice.PortName+" · 本机="+LocalMac+"；请点击“刷新设备与 Room”。\n刷新前不执行 Room / RGB 写操作；默认真实界面可见后自动只读刷新，手动暂停后不自动恢复。";return;
                case ViewState.Invalidated: _status.Text=_invalidMessage+"\n仍可手动刷新恢复；不会重复写入、自动重建房间或自动启用 RGB。";return;
                case ViewState.Busy: _status.Text=_rgbTracking?"正在读取 RGB "+_submittedRgbId+" 的回执（最多12秒）；合法动作可排队，同一动作去重，不重放未知结果。\n可暂停/清空未提交项；已提交动作可能在途，不能撤销。"
                    :"正在核验/执行前台动作；其他合法意图可进入有界FIFO队列，不读取后来变动的UI选择。\n执行前重新核验设备、Room、票据、权限；状态变化或过期会取消，不保证每项最终执行。";return;
            }
            _status.Text=(_state.Active? _state.Name+" · "+_state.Count+"/6 · leader="+_state.Leader+" · revision="+_state.Revision
                    +" · 本机="+LocalMac+" / "+(Leader?"leader":"成员") :"本机未加入 Room")
                +(_state.Retiring?" · 正在重播退出公告；退出后10秒内暂不能创建或加入，请稍后刷新":"")
                +(_state.JoinUncertain?" · 入群结果待确认：自动重试已暂停，请手动重试；勿重启设备或另建房间"
                    :_state.JoinConfirmed?" · 已确认入群，等待最终名单":_state.Waiting?" · 请求等待处理":"")
                +(_state.RgbFaulted?" · 本机 RGB 驱动故障，已停用；请检查接线并协调退出 Room 后重启设备"
                    :_state.RgbEnabled?" · 本机 RGB 许可已开启（启用不点灯）":" · 本机 RGB 执行许可关闭")
                +(_rgbPending?" · RGB 等待执行回执，请勿重复发送":"")
                +"\n"+(_state.Active?(Leader?(_state.Synchronized?"leader 已收到成员确认：选中申请可审批，选中其他成员可踢出。":"leader 正在等待成员确认：审批、踢出与退出暂不可用，请稍后刷新。")
                    :"普通成员可邀请和退出；同意、拒绝与踢出由 leader 操作。")
                    :CanStartRoom?(ValidName(_name.Text)?"可创建 Room，或选中附近 Room / 邀请后申请加入；选择记录后对应按钮启用。":"Room 名须为 1–23 个 UTF-8 字节且不含控制字符，修改名称后才能创建。")
                    :"现有请求、结果待确认或退出公告未结束；请按状态等待或使用明确的重试入口。")
                +(CanLocalPulse?(_state.Active?"\n可直接点击“本机青色双闪”，无需选中成员。":"\n只有一块板也可确认“创建单机 Room 并双闪”；新Room可被发现并保留，不会自动退出。")
                    :(!_state.RgbEnabled && !_state.RgbFaulted?"\n启用仅授予RGB执行许可，不代表灯亮；确认接线并允许后，再显式选择本机双闪测试。":""))
                +"\n"+NearbySummary;
        }
        private string NearbySummary {get {
            string text="附近已发现 "+_peerRssi.Count+" 个节点；仅无线发现，不代表已加入 Room 或验证业务能力。";
            var mac=_peers.SelectedItem as string;int rssi;
            return mac!=null && _peerRssi.TryGetValue(mac,out rssi)?text+" 选中 "+mac+" / RSSI "+rssi+" dBm / 能力未验证。":text;
        }}

        private void Log(string message) {if(IsDisposed)return;if(_log.TextLength>8000)_log.Clear();_log.AppendText(DateTime.Now.ToString("HH:mm:ss")+" · "+message+Environment.NewLine);}
        protected override void Dispose(bool disposing)
        {
            if(disposing) {_sessionService.DevicesChanged-=SessionDevicesChanged;if(_subscription!=null){_subscription.Changed-=SessionChanged;_subscription.Dispose();_subscription=null;}
                _ownedService?.Dispose();_tooltips.Dispose();}
            base.Dispose(disposing);
        }
    }
}
