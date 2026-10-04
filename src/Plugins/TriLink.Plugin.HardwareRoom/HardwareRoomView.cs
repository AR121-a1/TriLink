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
        private sealed class UserInputException : Exception
        { public UserInputException(string message):base(message) {} }
        private readonly IHardwareCommandService _commands;
        private readonly IDeviceDiscoveryService _discovery;
        private readonly ComboBox _ports = new ComboBox { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList, Name = "HardwarePorts" };
        private readonly TextBox _name = new TextBox { Width = 125, Text = "TriLink Room", MaxLength = 23 };
        private readonly ListBox _rooms = new ListBox { Dock = DockStyle.Fill, Name = "NearbyRooms" };
        private readonly ListBox _members = new ListBox { Dock = DockStyle.Fill, Name = "RoomMembers" };
        private readonly ListBox _requests = new ListBox { Dock = DockStyle.Fill, Name = "RoomRequests" };
        private readonly ListBox _invites = new ListBox { Dock = DockStyle.Fill, Name = "RoomInvites" };
        private readonly ComboBox _peers = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Name = "HardwareRoomStatus" };
        private readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
        private readonly Button _monitor = new Button { AutoSize = true, Text = "启动状态轮询", Name = "HardwareMonitor" };
        private readonly Timer _timer = new Timer { Interval = 5000 };
        private RoomWire _state;
        private bool _busy, _automatic;
        private int _failures;
        private int _selectionGeneration;
        private string _lastRgbResult;
        public HardwareRoomView(IHardwareCommandService commands, IDeviceDiscoveryService discovery)
        {
            _commands = commands; _discovery = discovery; Dock = DockStyle.Fill;
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
            top.Controls.Add(_ports); Add(top,"更新端口",()=> { ReloadPorts(); return Task.CompletedTask; });
            Add(top,"刷新设备与 Room",RefreshAll); top.Controls.Add(_monitor); top.Controls.Add(_name);
            Add(top,"创建",()=>SendRoom(16,null,null,_name.Text)); Add(top,"退出",()=>SendRoom(5,RequireState()));
            Add(top,"重试入群确认",RetryJoin);
            layout.Controls.Add(top,0,1); layout.Controls.Add(_status,0,2);
            var grids = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            grids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); grids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            grids.RowStyles.Add(new RowStyle(SizeType.Percent,50)); grids.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            grids.Controls.Add(Group("附近 Room（选中后申请）",_rooms),0,0);
            grids.Controls.Add(Group("当前成员（选中后踢出 / RGB）",_members),1,0);
            grids.Controls.Add(Group("待审批（仅 leader 操作）",_requests),0,1);
            grids.Controls.Add(Group("收到邀请（接受后仍需审批）",_invites),1,1);
            layout.Controls.Add(grids,0,3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
            Add(actions,"申请加入",()=> {var selected=Selected(_rooms); return SendRoom(3,selected,selected.Leader);});
            Add(actions,"同意",()=>SendRoom(17,RequireState(),Selected(_requests).Source));
            Add(actions,"拒绝",()=>SendRoom(18,RequireState(),Selected(_requests).Source));
            Add(actions,"接受邀请",()=> {var selected=Selected(_invites);return SendRoom(3,selected,selected.Leader);});
            Add(actions,"踢出",()=>SendRoom(19,RequireState(),_members.SelectedItem as string));
            actions.Controls.Add(_peers); Add(actions,"邀请设备",()=>SendRoom(4,RequireState(),_peers.SelectedItem as string));
            Add(actions,"启用本机 GPIO48 RGB",EnableRgb); Add(actions,"禁用本机 RGB",()=>Exchange("RGBENABLE","0"));
            Add(actions,"目标青色双闪",()=>Pulse());
            layout.Controls.Add(actions,0,4); layout.Controls.Add(_log,0,5); Controls.Add(layout);
            _monitor.Click += async (_,__) => {
                _automatic=!_automatic; _failures=0;
                _monitor.Text=_automatic?"暂停状态轮询":"启动状态轮询";
                if(_automatic) {_timer.Start(); await Run(RefreshAll);} else {_timer.Stop();++_selectionGeneration;}
            };
            _timer.Tick += async (_,__) => { if(_automatic && Visible) await Run(RefreshAll); };
            _ports.SelectedIndexChanged += (_,__) => { ++_selectionGeneration; _lastRgbResult=null; _state=null; ClearLists(); _peers.Items.Clear(); UpdateStatus(); };
            ReloadPorts(); UpdateStatus();
        }
        private static GroupBox Group(string title, Control child)
        { var group=new GroupBox {Text=title,Dock=DockStyle.Fill};group.Controls.Add(child);return group; }
        private void Add(Control parent,string title,Func<Task> action)
        { var button=new Button {Text=title,AutoSize=true};button.Click+=async (_,__)=>await Run(action);parent.Controls.Add(button); }
        private async Task Run(Func<Task> action)
        {
            if(_busy || IsDisposed) return;
            _busy=true;
            int generation=_selectionGeneration;
            try {await action(); if(generation==_selectionGeneration)_failures=0;}
            catch(UserInputException ex) {if(!IsDisposed)Log(ex.Message);}
            catch(Exception ex) {
                if(!IsDisposed && generation==_selectionGeneration) {
                    _state=null;ClearLists();_peers.Items.Clear();
                    _status.Text="状态已失效，请成功刷新后再操作 Room；上次命令结果可能未知。";
                    Log(ex.Message); ++_failures;
                    if(_automatic && _failures>=5) {_automatic=false;_timer.Stop();_monitor.Text="手动重启状态轮询";Log("连续五次失败，自动轮询已暂停。");}
                }
            }
            finally {_busy=false;}
        }
        private void ReloadPorts()
        {
            var old=_ports.SelectedItem as string; _ports.Items.Clear();
            foreach(var device in _discovery.Devices.Where(d=>(d.Capabilities&64)!=0)) _ports.Items.Add(device.PortName);
            if(old!=null && _ports.Items.Contains(old)) _ports.SelectedItem=old;
            else if(_ports.Items.Count>0) _ports.SelectedIndex=0;
        }
        private string Port {get {return _ports.SelectedItem as string ?? throw new InvalidOperationException("没有支持 Room/RGB 的已识别设备；请先连接原生 USB 数据口并完成扫描。");}}
        private async Task<string> Exchange(string command,string arguments)
        { return await _commands.ExecuteAsync(Port,command,arguments); }
        private RoomWire RequireState()
        { if(_state==null || !_state.Active) throw new UserInputException("本机尚未入 Room，请先刷新。");return _state; }
        private static RoomWire Selected(ListBox list)
        { return list.SelectedItem as RoomWire ?? throw new UserInputException("请先选择对应记录。"); }
        private async Task SendRoom(byte kind,RoomWire state,string target=null,string name=null)
        {
            if((kind==17 || kind==18 || kind==19 || kind==4) && target==null) throw new UserInputException("请选择目标。");
            int generation=_selectionGeneration;
            string command;
            try {command=RoomWire.Command(kind,state,target,name);}
            catch(ArgumentException ex) {throw new UserInputException(ex.Message);}
            await Exchange("ROOM",command);
            if(IsDisposed || generation!=_selectionGeneration) return;
            Log("命令已由本机 S3 接收；加入/退出及副本同步以刷新状态为准。");
            await RefreshAll();
        }
        private Task RetryJoin()
        {
            if(_state==null || !_state.Waiting || !_state.JoinUncertain)
                throw new UserInputException("请刷新状态；仅结果待确认且自动重试已暂停时可手动重试。");
            return SendRoom(20,null);
        }
        private async Task EnableRgb()
        {
            if(MessageBox.Show(this,"仅当已核对所选板为 GPIO48 / WS2812 GRB 单灯时启用。\n本操作不验证接线，重启后自动关闭。是否确认？",
                "确认本机 RGB 接线",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes) return;
            await Exchange("RGBENABLE","1"); if(!IsDisposed) Log("本机 RGB 已允许执行；请在每个目标设备自己的电脑上分别确认启用。");
        }
        private async Task Pulse()
        {
            RequireState(); var target=_members.SelectedItem as string;
            if(target==null) throw new UserInputException("请选择目标成员。");
            var reply=await Exchange("RGB",target+" 000808"); if(!IsDisposed) Log(reply+"；这是排队结果，不是执行完成。请刷新读取回执。");
        }
        public async Task RefreshAll()
        {
            if(IsDisposed) return;
            string port=Port;
            int generation=_selectionGeneration;
            var pages=new RoomWire[18];
            for(int page=1;page<=18;++page) {
                pages[page-1]=RoomWire.ParseReply(await _commands.ExecuteAsync(port,"ROOMGET",page.ToString("X2")),page);
                if(IsDisposed || generation!=_selectionGeneration) return;
            }
            var peers=await _discovery.SearchNearbyAsync(port);
            if(IsDisposed || generation!=_selectionGeneration) return;
            var rgb=await _commands.ExecuteAsync(port,"RGBRESULT","");
            if(IsDisposed || generation!=_selectionGeneration) return;
            var state=RoomWire.ParseReply(await _commands.ExecuteAsync(port,"ROOMGET","00"),0);
            if(IsDisposed || generation!=_selectionGeneration) return;
            var result=rgb.Split(' ');
            if(state==null || state.Kind!=1) throw new InvalidOperationException("本机 Room 状态缺失或类型错误。");
            if(result.Length!=5 || result[0]!="TRILINK/3" || result[1]!="RGBRESULT"
                || result[3].Length!=8 || result[3].Any(c=>!Uri.IsHexDigit(c))) throw new InvalidOperationException("RGB 回执格式错误。");
            int phase; if(!int.TryParse(result[4],out phase) || phase<0 || phase>5) throw new InvalidOperationException("RGB 状态无效。");
            // Validate the entire refresh before publishing any part of it.
            _state=state;
            ReplaceItems(_rooms,pages.Take(6).Where(p=>p!=null && p.Count>0));
            ReplaceItems(_members,_state.Active?_state.Members:new string[0]);
            ReplaceItems(_requests,pages.Skip(6).Take(6).Where(p=>p!=null && (p.Kind==3 || p.Kind==7) && p.Room==state.Room && p.Incarnation==state.Incarnation));
            ReplaceItems(_invites,pages.Skip(12).Where(p=>p!=null && p.Kind==4));
            var peerSelection=_peers.SelectedItem;_peers.Items.Clear();
            foreach(var peer in peers) _peers.Items.Add(peer.NodeId.Replace(":",""));
            if(peerSelection!=null && _peers.Items.Contains(peerSelection))_peers.SelectedItem=peerSelection;
            UpdateStatus();
            string signature=result[3]+":"+phase;
            if(result[3]!="00000000" && signature!=_lastRgbResult) Log("RGB "+result[3]+"："+new[]{"等待应用回执","执行器已受理","驱动执行完成（非光学验证）","驱动失败","目标未启用或忙","超时，结果未知"}[phase]);
            _lastRgbResult=signature;
        }
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
        private void UpdateStatus()
        {
            _status.Text=_state==null?"请选择设备并刷新；本页轮询独立受控，默认不启动。":
                (_state.Active? _state.Name+" · "+_state.Count+"/6 · leader="+_state.Leader+" · revision="+_state.Revision
                    +(_state.Synchronized?" · 成员已确认":" · 等待同步 / 本机为成员") :"本机未加入 Room")
                +(_state.Retiring?" · 正在重播退出公告；退出后10秒内暂不能创建或加入，请稍后刷新":"")
                +(_state.JoinUncertain?" · 入群结果待确认：自动重试已暂停，请手动重试；勿重启设备或另建房间"
                    :_state.JoinConfirmed?" · 已确认入群，等待最终名单":_state.Waiting?" · 请求等待处理":"")
                +(_state.RgbFaulted?" · 本机 RGB 驱动故障，已停用；请检查接线并协调退出 Room 后重启设备"
                    :_state.RgbEnabled?" · 本机 RGB 已启用":" · 本机 RGB 关闭");
        }
        private void Log(string message)
        {if(_log.TextLength>8000) _log.Clear(); _log.AppendText(DateTime.Now.ToString("HH:mm:ss")+" · "+message+Environment.NewLine);}
        protected override void Dispose(bool disposing)
        {if(disposing){_automatic=false;_timer.Stop();_timer.Dispose();}base.Dispose(disposing);}
    }
}
