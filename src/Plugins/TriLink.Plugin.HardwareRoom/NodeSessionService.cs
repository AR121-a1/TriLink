using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TriLink.Core;

namespace TriLink.Plugins.HardwareRoom
{
    // One application coordinator owns the queue, reads, receipt tracking and timer.
    // It never owns a Control, prompts the user, or opens a COM port.
    // Stage A accepts one physical node binding at a time; all calls are serialized
    // on its creating desktop context. Different simultaneous targets are refused.
    public sealed class NodeSessionService : INodeSessionService, IDisposable
    {
        public const int MaxSubscriptions=16;
        private sealed class UserInputException : Exception { public UserInputException(string m):base(m){} }
        private sealed class RefreshInterruptedException : Exception {}
        private sealed class UnknownOutcomeException : Exception { public UnknownOutcomeException(string m):base(m){} }
        private sealed class QueuedIntent
        {
            public string Action,Description,Key,Name,Target,RequestId;
            public TriLinkDevice Device;
            public int Generation;
            public long ExpiresAt;
            public RoomWire Basis,Record;
            public uint TargetBoot;
            public uint CapturedNodeBoot;
            public bool WriteAttempted,Confirmed;
            public readonly HashSet<Subscription> Owners=new HashSet<Subscription>();
        }
        private sealed class Subscription : INodeSessionSubscription
        {
            internal readonly NodeSessionService Service;
            internal bool Disposed,Visible;
            public Subscription(NodeSessionService service) {Service=service;}
            public event EventHandler Changed;
            public NodeSessionSnapshot Snapshot {get {Service.AssertContext();if(Disposed)throw new ObjectDisposedException(nameof(Subscription));return Service.Snapshot();}}
            public Task RefreshAsync() {Service.AssertContext();if(Disposed)throw new ObjectDisposedException(nameof(Subscription));return Service.RefreshRequestedAsync();}
            public NodeSubmissionResult Submit(NodeOperationIntent intent) {Service.AssertContext();return Disposed?Result(NodeSubmissionStatus.Disposed,"订阅已关闭。"):Service.Submit(this,intent);}
            public void SetVisible(bool visible,bool autoStart) {Service.AssertContext();if(Disposed)return;Visible=visible;Service.SetVisibility(autoStart);}
            public void CancelPending() {Service.AssertContext();if(!Disposed)Service.CancelOwner(this);}
            public void Dispose() {Service.AssertContext();if(Disposed)return;Disposed=true;Service.CancelOwner(this);Service._subscriptions.Remove(this);Changed=null;Service.SetVisibility(false);}
            internal void Notify() {if(!Disposed)Changed?.Invoke(this,EventArgs.Empty);}
        }
        private readonly IHardwareCommandService _commands;
        private readonly IDeviceDiscoveryService _discovery;
        private readonly INodeCapabilityService _capabilityService;
        private readonly SynchronizationContext _context;
        private readonly int _thread=Thread.CurrentThread.ManagedThreadId;
        private readonly System.Windows.Forms.Timer _timer=new System.Windows.Forms.Timer {Interval=5000};
        private readonly List<Subscription> _subscriptions=new List<Subscription>();
        private readonly Queue<QueuedIntent> _pendingCommands=new Queue<QueuedIntent>();
        private readonly HashSet<string> _intentKeys=new HashSet<string>();
        private TaskCompletionSource<bool> _refreshWaiter;
        private readonly Dictionary<string,int> _peerRssi=new Dictionary<string,int>();
        private readonly List<string> _peers=new List<string>(),_members=new List<string>();
        private readonly List<RoomWire> _rooms=new List<RoomWire>(),_requests=new List<RoomWire>(),_invites=new List<RoomWire>();
        private readonly List<string> _logs=new List<string>();
        private QueuedIntent _executingIntent;
        private TriLinkDevice _selectedDevice;
        private RoomWire _state;
        private NodeCapabilities _capabilities;
        private NodePollingState _pollingState=NodePollingState.NotStarted;
        private bool _disposed,_draining,_busy,_automatic,_liveStarted,_livePaused,_backgroundReading;
        private bool _refreshRequested,_refreshFullRequested,_refreshInterrupted,_publicRefreshRunning,_rgbPending,_rgbTracking;
        private int _failures,_selectionGeneration,_refreshGeneration;
        private long _lastPeerRefresh;
        private string _lastRgbResult,_lastLogMessage,_invalidMessage,_submittedRgbId;
        private CancellationTokenSource _rgbReceiptStop;
        private sealed class RgbResult { public string Operation;public int Phase;public string Signature {get{return Operation+":"+Phase;}} }
        private static readonly string[] RgbPhases={"等待应用回执","执行器已受理","驱动执行完成（非光学验证）","驱动失败","目标未启用或忙","超时，结果未知"};
        public NodeSessionService(IHardwareCommandService commands,IDeviceDiscoveryService discovery,INodeCapabilityService capabilities=null)
        {
            _commands=commands??throw new ArgumentNullException(nameof(commands));
            _discovery=discovery??throw new ArgumentNullException(nameof(discovery));
            _capabilityService=capabilities;_context=SynchronizationContext.Current??new WindowsFormsSynchronizationContext();
            _timer.Tick+=(_,__)=> {if(_automatic && _subscriptions.Any(s=>s.Visible))RequestRefresh(false);};
            _discovery.DeviceArrived+=DiscoveryChanged;_discovery.DeviceRemoved+=DiscoveryChanged;
        }
        public event EventHandler DevicesChanged;
        internal Task RunCompatibility(Func<Task> action) {return Run(action);}
        internal Task RetryJoinCompatibility() {return RetryJoin();}
        internal Task SendRoomCompatibility(byte kind,RoomWire state,string target,string name) {return SendRoom(kind,state,target,name);}
        internal void RequestRefreshCompatibility(bool full) {RequestRefresh(full);}
        internal IReadOnlyList<string> DiagnosticLogCompatibility() {return _logs.ToList().AsReadOnly();}
        public INodeSessionSubscription Bind(string nodeId,string portName)
        {
            AssertContext();if(_disposed)throw new ObjectDisposedException(nameof(NodeSessionService));
            if(_subscriptions.Count>=MaxSubscriptions)throw new InvalidOperationException("共享节点订阅已满（16项）；请先关闭不用的页面或释放订阅。");
            var device=CapableDevices().FirstOrDefault(d=>Normalize(d.NodeId)==Normalize(nodeId) && d.PortName==portName);
            if(device==null)throw new InvalidOperationException("没有支持 Room/RGB 的已识别设备；请先扫描原生 USB 数据口。");
            if(!SameDevice(_selectedDevice,device)) {
                if(_subscriptions.Count>0)throw new InvalidOperationException("共享会话已绑定另一块设备；本页不能改变其他页面的串口目标。请先关闭旧设备页面。");
                SwitchDevice(device);
            }
            var lease=new Subscription(this);_subscriptions.Add(lease);return lease;
        }
        private void AssertContext() {if(Thread.CurrentThread.ManagedThreadId!=_thread)throw new InvalidOperationException("节点会话操作须在创建它的桌面同步上下文调用。");}
        private List<TriLinkDevice> CapableDevices()
        {return _discovery.Devices.Where(d=>d!=null && (d.Capabilities&64)!=0 && !string.IsNullOrWhiteSpace(d.PortName) && RoomWire.ValidMac(Normalize(d.NodeId)))
            .GroupBy(d=>d.PortName,StringComparer.OrdinalIgnoreCase).Select(g=>CopyDevice(g.First())).ToList();}
        private static string Normalize(string mac) {return (mac??"").Replace(":","").ToUpperInvariant();}
        private static TriLinkDevice CopyDevice(TriLinkDevice d) {return d==null?null:new TriLinkDevice {PortName=d.PortName,NodeId=d.NodeId,PnpDeviceId=d.PnpDeviceId,Capabilities=d.Capabilities,DisplayName=d.DisplayName,TransportKind=d.TransportKind,ManagementUrl=d.ManagementUrl};}
        private static bool SameDevice(TriLinkDevice a,TriLinkDevice b) {return a==null||b==null?a==b:a.PortName==b.PortName && a.TransportKind==b.TransportKind && Normalize(a.NodeId)==Normalize(b.NodeId) && a.Capabilities==b.Capabilities && string.Equals(a.PnpDeviceId,b.PnpDeviceId,StringComparison.OrdinalIgnoreCase);}
        private string Port {get {if(_selectedDevice==null || !CapableDevices().Any(d=>SameDevice(d,_selectedDevice)))throw new UserInputException("没有支持 Room/RGB 的已识别设备；CH343/CH340 是烧录口，请连接 S3 原生 USB 数据口并在主窗口扫描。");return _selectedDevice.PortName;}}
        private Task<string> Exchange(string command,string arguments) {return _commands.ExecuteAsync(Port,command,arguments);}
        private void SwitchDevice(TriLinkDevice device)
        {
            bool replacement=_selectedDevice!=null;++_selectionGeneration;InterruptRefresh();_refreshRequested=false;_refreshFullRequested=false;
            CompleteRefreshWaiter(true);
            ClearQueued("设备移除或更换");StopRgbReceiptTracking();_timer.Stop();_automatic=false;
            if(replacement){_livePaused=true;_pollingState=NodePollingState.DeviceChanged;}
            _selectedDevice=CopyDevice(device);_capabilities=null;_lastRgbResult=null;_invalidMessage=null;_rgbPending=false;_state=null;ClearLists();_peers.Clear();_peerRssi.Clear();Publish();
        }
        private void DiscoveryChanged(object sender,TriLinkDeviceEventArgs args)
        {
            if(_disposed)return;
            if(Thread.CurrentThread.ManagedThreadId!=_thread) {_context.Post(_=> {if(!_disposed)DiscoveryChanged(sender,args);},null);return;}
            if(_selectedDevice!=null && !CapableDevices().Any(d=>SameDevice(d,_selectedDevice))) {
                ++_selectionGeneration;InterruptRefresh();_refreshRequested=false;_refreshFullRequested=false;ClearQueued("设备移除或更换");StopRgbReceiptTracking();
                CompleteRefreshWaiter(true);
                _automatic=false;_livePaused=true;_pollingState=NodePollingState.DeviceChanged;_timer.Stop();_state=null;_capabilities=null;ClearLists();_peers.Clear();_peerRssi.Clear();_rgbPending=false;
                _invalidMessage="设备已移除或更换，旧状态与意图已失效；已提交动作不能撤销，不自动重放。";Log(_invalidMessage);Publish();
            }
            DevicesChanged?.Invoke(this,EventArgs.Empty);
        }
        private void SetVisibility(bool autoStart)
        {
            if(_disposed)return;
            if(autoStart && !_liveStarted && !_livePaused && _selectedDevice!=null && _subscriptions.Any(s=>s.Visible)) {
                _liveStarted=true;_automatic=true;_failures=0;_pollingState=NodePollingState.Running;
                _context.Post(_=> {if(!_disposed && _automatic && !_livePaused && _subscriptions.Any(s=>s.Visible))RequestRefresh();},null);
            }
            if(_automatic && _subscriptions.Any(s=>s.Visible))_timer.Start();else _timer.Stop();Publish();
        }
        public void ResumePolling()
        {
            AssertContext();if(_disposed)throw new ObjectDisposedException(nameof(NodeSessionService));var port=Port;
            _automatic=true;_livePaused=false;_liveStarted=true;_failures=0;_pollingState=NodePollingState.Running;
            if(_subscriptions.Any(s=>s.Visible))_timer.Start();RequestRefresh();Publish();
        }
        public void PausePolling()
        {
            AssertContext();if(_disposed)return;var executing=_executingIntent;
            _automatic=false;_livePaused=true;_pollingState=NodePollingState.ManuallyPaused;_timer.Stop();
            InterruptRefresh();_refreshRequested=false;_refreshFullRequested=false;ClearQueued("手动暂停");
            if(executing!=null) {
                ++_selectionGeneration;
                _invalidMessage=executing.WriteAttempted?"当前动作已提交给 USB 服务，可能在途；暂停不能撤销，结果可能未知，请刷新，不自动重放。":"当前前台校验已暂停；未提交动作停止，请刷新后明确操作。";
                _state=null;ClearLists();_peers.Clear();_peerRssi.Clear();_rgbPending=false;Log(_invalidMessage);
            }
            StopRgbReceiptTracking();CompleteRefreshWaiter(true);Publish();
        }
        private Task RefreshRequestedAsync()
        {
            if(_disposed)throw new ObjectDisposedException(nameof(NodeSessionService));
            if(_refreshWaiter!=null)return _refreshWaiter.Task;
            var waiter=new TaskCompletionSource<bool>();_refreshWaiter=waiter;RequestRefresh();return waiter.Task;
        }
        private void CompleteRefreshWaiter(bool cancelled=false,Exception error=null)
        {var waiter=_refreshWaiter;_refreshWaiter=null;if(waiter==null)return;if(error!=null)waiter.TrySetException(error);else if(cancelled)waiter.TrySetCanceled();else waiter.TrySetResult(true);}
        private static string ActionName(NodeOperationKind kind)
        {
            switch(kind) {
                case NodeOperationKind.CreateRoom:return "HardwareCreate";case NodeOperationKind.LeaveRoom:return "HardwareLeave";
                case NodeOperationKind.RetryJoin:return "HardwareRetryJoin";case NodeOperationKind.JoinRoom:return "HardwareJoin";
                case NodeOperationKind.AcceptInvitation:return "HardwareAcceptInvite";case NodeOperationKind.ApproveJoin:return "HardwareApprove";
                case NodeOperationKind.RejectJoin:return "HardwareReject";case NodeOperationKind.KickMember:return "HardwareKick";
                case NodeOperationKind.InviteMember:return "HardwareInvite";case NodeOperationKind.EnableRgb:return "HardwareRgbEnable";
                case NodeOperationKind.DisableRgb:return "HardwareRgbDisable";case NodeOperationKind.LocalRgbPulse:return "HardwareRgbLocalPulse";
                case NodeOperationKind.MemberRgbPulse:return "HardwareRgbPulse";default:throw new UserInputException("未知操作未提交。");
            }
        }
        private NodeSubmissionResult Submit(Subscription owner,NodeOperationIntent value)
        {
            if(_disposed)return Result(NodeSubmissionStatus.Disposed,"节点服务已关闭。");
            try {
                if(value==null || !Fresh || value.Generation!=_selectionGeneration || value.Basis==null)throw new UserInputException("尚无有效的当前节点状态，或捕获意图已过期；请刷新。");
                var intent=new QueuedIntent {Action=ActionName(value.Kind),Description=value.Description??value.Kind.ToString(),Name=value.Name,Target=value.Target==null?null:Normalize(value.Target),
                    Device=CopyDevice(_selectedDevice),Generation=value.Generation,Basis=new RoomWire(value.Basis.EncodedRecord),Record=value.Record==null?null:new RoomWire(value.Record.EncodedRecord),
                    Confirmed=value.LocalPermissionConfirmed,RequestId=Guid.NewGuid().ToString("N"),
                    CapturedNodeBoot=_capabilities==null || _capabilities.IsLegacy?0:_capabilities.BootId};
                if(!SameViewContext(intent.Basis,_state))throw new UserInputException("Room 快照已变化，请刷新后重新提交。");
                if(intent.Action=="HardwareRgbLocalPulse")intent.Target=LocalMac;
                if(intent.Target!=null){int index=Array.IndexOf(intent.Basis.Members,intent.Target);if(index>=0)intent.TargetBoot=intent.Basis.Number(46+10*index);}
                if((intent.Action=="HardwareJoin" || intent.Action=="HardwareAcceptInvite" || intent.Action=="HardwareApprove" || intent.Action=="HardwareReject") && intent.Record==null)throw new UserInputException("请先选择对应记录。");
                int ttl=(intent.Action=="HardwareApprove" || intent.Action=="HardwareReject")?6000:15000;
                intent.ExpiresAt=System.Diagnostics.Stopwatch.GetTimestamp()+System.Diagnostics.Stopwatch.Frequency*ttl/1000;
                if(intent.Action=="HardwareRgbPulse" || intent.Action=="HardwareRgbLocalPulse")intent.Key="RGB/"+intent.Generation+"/"+Normalize(intent.Device.NodeId)+"/"+intent.Target;
                else {
                    string canonical=intent.Action=="HardwareJoin" || intent.Action=="HardwareAcceptInvite"?"JOIN":intent.Action;
                    intent.Key=canonical+"/"+intent.Generation+"/"+Normalize(intent.Device.NodeId)+"/"+intent.Basis.Room+"/"+intent.Basis.Incarnation+"/"+intent.Target+"/"+intent.TargetBoot;
                    if(intent.Action=="HardwareCreate")intent.Key+="/"+intent.Name;
                    if(intent.Record!=null)intent.Key+="/"+intent.Record.Room+"/"+intent.Record.Incarnation+"/"+intent.Record.Leader+"/"+MemberBoot(intent.Record,intent.Record.Leader);
                    if(intent.Action=="HardwareApprove" || intent.Action=="HardwareReject")intent.Key+="/"+intent.Record.Source+"/"+intent.Record.Number(10)+"/"+intent.Record.Revision+"/"+intent.Record.Term+"/"+intent.Record.Kind+"/"+RoomWire.Hex(intent.Record.Bytes.Skip(34).Take(6).ToArray());
                }
                intent.Owners.Add(owner);return Enqueue(intent);
            }catch(UserInputException ex){Log(ex.Message);return Result(NodeSubmissionStatus.InvalidState,ex.Message);}
            catch(ArgumentException ex){Log(ex.Message);return Result(NodeSubmissionStatus.InvalidState,ex.Message);}
        }
        private static NodeSubmissionResult Result(NodeSubmissionStatus status,string message,string id=null) {return new NodeSubmissionResult {Status=status,Message=message,RequestId=id};}
        private void CancelOwner(Subscription owner)
        {
            var items=_pendingCommands.ToArray();_pendingCommands.Clear();int cancelled=0;
            foreach(var item in items) {item.Owners.Remove(owner);if(item.Owners.Count==0){_intentKeys.Remove(item.Key);++cancelled;}else _pendingCommands.Enqueue(item);}
            if(_executingIntent!=null)_executingIntent.Owners.Remove(owner);
            if(cancelled>0)Log("页面关闭或清空：已撤销 "+cancelled+" 项该页未提交动作；已提交动作不能撤销，由共享服务继续跟踪。");Publish();
        }
        private NodeSubmissionResult Enqueue(QueuedIntent intent)
        {
            if(_intentKeys.Contains(intent.Key)){
                var existing=_executingIntent!=null && _executingIntent.Key==intent.Key?_executingIntent:_pendingCommands.First(i=>i.Key==intent.Key);
                foreach(var owner in intent.Owners)existing.Owners.Add(owner);Log("相同动作已在执行或排队，未重复提交："+intent.Description);return Result(NodeSubmissionStatus.Duplicate,"相同动作已在执行或排队。");}
            if(_pendingCommands.Count>=8){Log("待执行队列已满（8项）；未提交，请等待或清空待执行。");return Result(NodeSubmissionStatus.QueueFull,"待执行队列已满（8项）。");}
            _pendingCommands.Enqueue(intent);_intentKeys.Add(intent.Key);InterruptRefresh();
            Log("已排队："+intent.Description+(intent.Target==null?"":" → "+intent.Target)+"；执行前重新核验，状态变化或超时可能取消。");
            Publish();StartPump();return Result(NodeSubmissionStatus.Queued,"已排队；执行前重新核验。",intent.RequestId);
        }
        private void InterruptRefresh(){++_refreshGeneration;}
        private void ClearQueued(string reason)
        {
            int count=_pendingCommands.Count;
            while(_pendingCommands.Count>0)_intentKeys.Remove(_pendingCommands.Dequeue().Key);
            if(count>0)Log(reason+"：已撤销 "+count+" 项未提交动作；已提交或在途命令不能撤销。");
        }
        private void RequestRefresh(bool full=true)
        {if(_disposed)return;_refreshRequested=true;_refreshFullRequested|=full;StartPump();Publish();}
        private void StartPump()
        {if(!_draining && !_busy && !_disposed){_draining=true;Drain();}}
        private async void Drain()
        {
            try {
                while(!_disposed) {
                    if(_pendingCommands.Count>0) {
                        var intent=_pendingCommands.Dequeue();_executingIntent=intent;_backgroundReading=false;
                        try {await Run(()=>ExecuteIntent(intent));}finally {_intentKeys.Remove(intent.Key);_executingIntent=null;}
                    } else if(_refreshRequested && _selectedDevice!=null) {
                        bool full=_refreshFullRequested;_refreshRequested=false;_refreshFullRequested=false;_backgroundReading=true;
                        try {await Run(()=>RefreshCore(full));}finally {_backgroundReading=false;CompleteRefreshWaiter(_refreshInterrupted);}
                    } else break;
                }
            } finally {_draining=false;if(!_disposed){Publish();}}
        }
        private async Task Run(Func<Task> action)
        {
            if(_busy || _disposed) return;
            _busy=true;_refreshInterrupted=false;Publish();
            int generation=_selectionGeneration;
            try {await action(); if(!_refreshInterrupted && generation==_selectionGeneration)_failures=0;}
            catch(RefreshInterruptedException) { /* Foreground priority/pause is not a transport failure. */ }
            catch(UserInputException ex) {if(!_disposed){Log(ex.Message);if(_backgroundReading)CompleteRefreshWaiter(false,ex);}}
            catch(UnknownOutcomeException ex) {
                if(!_disposed && generation==_selectionGeneration) {
                    _state=null;ClearLists();_peers.Clear();_peerRssi.Clear();_rgbPending=false;
                    _invalidMessage="应用执行结果未知；关联待执行动作已停止，请刷新后明确确认，不自动重放。";
                    ClearQueued("应用执行结果未知，停止关联后续动作");_refreshRequested=false;_refreshFullRequested=false;Log(ex.ToString());
                    CompleteRefreshWaiter(false,ex);
                }
            }
            catch(Exception ex) {
                if(!_disposed && generation==_selectionGeneration) {
                    _state=null;ClearLists();_peers.Clear();_peerRssi.Clear();_rgbPending=false;
                    _invalidMessage="状态已失效，请成功刷新后再操作 Room；上次命令结果可能未知。";
                    if(_executingIntent!=null){ClearQueued("当前动作结果未知，停止关联后续动作");_refreshRequested=false;_refreshFullRequested=false;}
                    Log(ex.ToString()); ++_failures;
                    CompleteRefreshWaiter(false,ex);
                    if(_automatic && _failures>=5) {
                        // A timer tick can have coalesced another batch while the
                        // failing read was in flight. Fuse it as well as the timer.
                        _refreshRequested=false;_refreshFullRequested=false;
                        _automatic=false;_livePaused=true;_timer.Stop();_pollingState=NodePollingState.FailurePaused;Log("连续五次失败，自动轮询已暂停。");
                    }
                }
            }
            finally {_busy=false;if(!_disposed){Publish();if(!_draining)StartPump();}}
        }
        private void CheckIntent(QueuedIntent intent)
        {
            if(_disposed || (!intent.WriteAttempted && intent.Owners.All(s=>s.Disposed)) || intent.Generation!=_selectionGeneration || _selectedDevice==null || !SameDevice(intent.Device,_selectedDevice)
                || !CapableDevices().Any(d=>SameDevice(d,intent.Device)))throw new UserInputException("排队动作的设备身份或选择已变化；未提交后续动作。");
            if(!intent.WriteAttempted && System.Diagnostics.Stopwatch.GetTimestamp()>=intent.ExpiresAt)
                throw new UserInputException("排队动作已超过有效期限（审批6秒，其他15秒）；未提交，请刷新并重新确认。");
        }
        private async Task<string> IntentRead(QueuedIntent intent,string command,string arguments)
        {CheckIntent(intent);var reply=await _commands.ExecuteAsync(intent.Device.PortName,command,arguments);CheckIntent(intent);return reply;}
        private async Task<string> WriteExchange(string command,string arguments)
        {
            var intent=_executingIntent;if(intent==null)return await Exchange(command,arguments);
            CheckIntent(intent);intent.WriteAttempted=true;Publish();
            // ExecuteAsync may already be waiting on the shared owner: invocation is
            // potentially in flight and cannot be cancelled by the view.
            return await _commands.ExecuteAsync(intent.Device.PortName,command,arguments);
        }
        private static uint MemberBoot(RoomWire state,string mac)
        {int index=Array.IndexOf(state.Members,mac);return index<0?0:state.Number(46+10*index);}
        private static bool SameRoom(RoomWire left,RoomWire right)
        {return left.Room==right.Room && left.Incarnation==right.Incarnation;}
        private static bool SameViewContext(RoomWire left,RoomWire right)
        {return left!=null && right!=null && SameRoom(left,right) && left.Revision==right.Revision && left.Term==right.Term && left.Active==right.Active
            && left.Members.SequenceEqual(right.Members) && left.Members.All(mac=>MemberBoot(left,mac)==MemberBoot(right,mac));}
        private void PublishState(RoomWire state)
        {
            if(state==null || state.Kind!=1 || (state.Active && !state.Members.Contains(LocalMac)))throw new InvalidOperationException("设备活动 Room 状态无效。");
            if(!SameViewContext(_state,state)){_requests.Clear();_invites.Clear();}
            _state=state;_invalidMessage=null;ReplaceItems(_members,state.Active?state.Members:new string[0]);Publish();
        }
        private async Task<RoomWire> ReadCurrentState(QueuedIntent intent)
        {
            string reply=intent==null?await Exchange("ROOMGET","00"):await IntentRead(intent,"ROOMGET","00");
            var state=RoomWire.ParseReply(reply,0);PublishState(state);return state;
        }
        private void ValidateBinding(QueuedIntent intent,RoomWire state)
        {
            if(!SameRoom(intent.Basis,state))throw new UserInputException("排队时的 Room/incarnation 已变化；动作已取消，不移到新房间执行。");
            string local=Normalize(intent.Device.NodeId);uint boot=MemberBoot(intent.Basis,local);
            if(boot!=0 && state.Active && MemberBoot(state,local)!=boot)throw new UserInputException("本机成员启动身份已变化；旧动作已取消。");
            if(intent.TargetBoot!=0 && intent.Action!="HardwareJoin" && intent.Action!="HardwareAcceptInvite" && MemberBoot(state,intent.Target)!=intent.TargetBoot)
                throw new UserInputException("所选成员已退出或启动身份改变；旧目标动作已取消。");
            if(intent.Action=="HardwareLeave" && (state.Leader!=intent.Basis.Leader || state.Term!=intent.Basis.Term
                || (state.Leader==local)!=(intent.Basis.Leader==local)))throw new UserInputException("退出动作的 leader/term/角色已变化；不把普通成员退出改成新 leader 交接。");
        }
        private static bool SameTicket(RoomWire expected,RoomWire actual)
        {return expected!=null && actual!=null && SameRoom(expected,actual) && expected.Source==actual.Source && expected.Number(10)==actual.Number(10)
            && expected.Revision==actual.Revision && expected.Term==actual.Term && expected.Kind==actual.Kind
            && expected.Bytes.Skip(34).Take(6).SequenceEqual(actual.Bytes.Skip(34).Take(6));}
        private async Task<RoomWire> FindRecord(QueuedIntent intent,int first,int last,Func<RoomWire,bool> match)
        {
            for(int page=first;page<=last;++page) {
                var record=RoomWire.ParseReply(await IntentRead(intent,"ROOMGET",page.ToString("X2")),page);
                if(record!=null && match(record))return record;
            }
            throw new UserInputException("排队时所选记录已消失、票据/启动身份改变或已到期；没有提交旧动作，请重新选择。");
        }
        private async Task ExecuteIntent(QueuedIntent intent)
        {
            CheckIntent(intent);
            if(intent.CapturedNodeBoot!=0 && _capabilityService!=null) {
                var capabilities=await _capabilityService.DescribeAsync(intent.Device.PortName);CheckIntent(intent);
                if(capabilities==null || capabilities.IsLegacy || Normalize(capabilities.NodeId)!=Normalize(intent.Device.NodeId)
                    || capabilities.BootId!=intent.CapturedNodeBoot) {
                    InvalidateBoot(capabilities!=null && Normalize(capabilities.NodeId)==Normalize(intent.Device.NodeId)?capabilities:null);
                    throw new UserInputException("排队动作的本机启动身份或能力已变化；未写入设备，请手动刷新并重新确认。");
                }
                _capabilities=capabilities;
            }
            var state=await ReadCurrentState(intent);ValidateBinding(intent,state);RoomWire record=null;
            if(intent.Action=="HardwareApprove" || intent.Action=="HardwareReject") {
                record=await FindRecord(intent,7,12,r=>SameTicket(intent.Record,r));
                ReplaceItems(_requests,new[]{record});
            } else if(intent.Action=="HardwareJoin" || intent.Action=="HardwareAcceptInvite") {
                if(intent.Action=="HardwareAcceptInvite")await FindRecord(intent,13,18,r=>SameRoom(intent.Record,r) && r.Kind==4 && r.Source==intent.Record.Source
                    && r.Number(10)==intent.Record.Number(10) && r.Leader==intent.Target && MemberBoot(r,r.Leader)==MemberBoot(intent.Record,intent.Record.Leader));
                record=await FindRecord(intent,1,6,r=>r.Kind==1 && SameRoom(intent.Record,r) && r.Leader==intent.Target
                    && MemberBoot(r,r.Leader)==MemberBoot(intent.Record,intent.Record.Leader));
            } else if(intent.Action=="HardwareInvite") {
                CheckIntent(intent);var peers=await _discovery.SearchNearbyAsync(intent.Device.PortName);CheckIntent(intent);
                if(!peers.Any(p=>Normalize(p.NodeId)==intent.Target))throw new UserInputException("邀请目标不再被发现；旧邀请未发送。");
                if(!_peers.Contains(intent.Target))_peers.Add(intent.Target);
            }
            if(intent.Action.StartsWith("HardwareRgb",StringComparison.Ordinal)) {
                var result=ParseRgbResult(await IntentRead(intent,"RGBRESULT",""));
                _rgbPending=result.Operation!="00000000" && result.Phase<=1;
            }
            // Final authority/version read is adjacent to the write. Ticket checking
            // remains a PC check, not atomic FW ticket authorization.
            state=await ReadCurrentState(intent);ValidateBinding(intent,state);CheckIntent(intent);
            if(record!=null && (intent.Action=="HardwareApprove" || intent.Action=="HardwareReject"))ReplaceItems(_requests,new[]{record});
            switch(intent.Action) {
                case "HardwareCreate":await SendRoom(16,null,null,intent.Name);break;
                case "HardwareLeave":await SendRoom(5,state);break;
                case "HardwareRetryJoin":await RetryJoin();break;
                case "HardwareJoin":case "HardwareAcceptInvite":await SendRoom(3,record,intent.Target);break;
                case "HardwareApprove":await SendRoom(17,state,intent.Target);break;
                case "HardwareReject":await SendRoom(18,state,intent.Target);break;
                case "HardwareKick":await SendRoom(19,state,intent.Target);break;
                case "HardwareInvite":await SendRoom(4,state,intent.Target);break;
                case "HardwareRgbEnable":await EnableRgb();break;
                case "HardwareRgbDisable":await DisableRgb();break;
                case "HardwareRgbLocalPulse":await LocalPulse();break;
                case "HardwareRgbPulse":if(!CanPulse(intent.Target))throw new UserInputException("原目标不再符合 RGB 执行条件；动作取消。");await SubmitAndAwaitRgb(intent.Target);break;
                default:throw new UserInputException("未知排队动作未执行。");
            }
        }
        private async Task SendRoom(byte kind,RoomWire state,string target=null,string name=null)
        {
            ValidateRoomAction(kind,state,target,name);
            if((kind==17 || kind==18 || kind==19 || kind==4) && target==null) throw new UserInputException("请选择目标。");
            int generation=_selectionGeneration;
            string command;
            try {command=RoomWire.Command(kind,state,target,name);}
            catch(ArgumentException ex) {throw new UserInputException(ex.Message);}
            ConfirmOk(await WriteExchange("ROOM",command),"ROOM");
            if(_disposed || generation!=_selectionGeneration) return;
            Log("命令已由本机 S3 接收；加入/退出及副本同步以刷新状态为准。");
            await ReadCurrentState(_executingIntent);_refreshRequested=true;
        }
        private Task RetryJoin()
        {
            if(_state==null || !_state.Waiting || !_state.JoinUncertain)
                throw new UserInputException("请刷新状态；仅结果待确认且自动重试已暂停时可手动重试。");
            return SendRoom(20,null);
        }
        private async Task EnableRgb()
        {
            if(!Fresh || _state.Waiting || _state.Retiring || _state.RgbEnabled || _state.RgbFaulted || _rgbPending)
                throw new UserInputException("请刷新设备状态；仅在本机 RGB 关闭、无故障且没有待完成操作时可确认启用。");
            int generation=_selectionGeneration;
            if(_executingIntent==null || !_executingIntent.Confirmed) throw new UserInputException("本机 RGB 许可必须由管理端显式确认。");
            if(_disposed || generation!=_selectionGeneration)throw new UserInputException("确认期间所选设备已变化，本次 RGB 启用已取消；请重新核对目标。");
            if(_executingIntent!=null){var current=await ReadCurrentState(_executingIntent);ValidateBinding(_executingIntent,current);if(current.RgbFaulted || current.RgbEnabled || current.Waiting || current.Retiring)throw new UserInputException("确认期间 RGB 状态已变化，许可请求取消。");}
            ConfirmOk(await WriteExchange("RGBENABLE","1"),"RGBENABLE"); if(_disposed || generation!=_selectionGeneration)return;
            await ReadCurrentState(_executingIntent);_refreshRequested=true;if(_disposed || generation!=_selectionGeneration)return;
            Log(_state.RgbEnabled?"已读回本机 RGB 执行许可：允许；启用不点灯，请点击本机双闪测试。远端需在其自己的管理端确认许可。"
                :"启用请求已受理，但未读回允许状态；没有发送双闪。请核对当前状态、故障与接线。");
        }
        private async Task DisableRgb()
        {
            if(!Fresh || !_state.RgbEnabled || _rgbPending)throw new UserInputException("请刷新状态；本机 RGB 已关闭或操作尚未完成。");
            int generation=_selectionGeneration;ConfirmOk(await WriteExchange("RGBENABLE","0"),"RGBENABLE");if(_disposed || generation!=_selectionGeneration)return;
            await ReadCurrentState(_executingIntent);_refreshRequested=true;if(_disposed || generation!=_selectionGeneration)return;
            Log(!_state.RgbEnabled?"已读回本机 RGB 执行许可：关闭；这是禁止后续执行，不是灭灯的光学证明。"
                :"停用请求已受理，但设备仍报告执行许可开启；未宣称停用完成，请重新核对状态。");
        }
        private async Task LocalPulse()
        {
            string capturedName=_executingIntent==null?(_executingIntent==null?"TriLink Room":_executingIntent.Name):_executingIntent.Name;
            if(!CanLocalPulseFor(capturedName))throw new UserInputException("本机双闪需要最新状态、已确认的 RGB 许可，且无等待、退出公告、故障或在途任务。");
            int generation=_selectionGeneration;string local=LocalMac;
            if(!_state.Active) {
                string name=capturedName;
                if(_executingIntent==null || !_executingIntent.Confirmed) throw new UserInputException("创建单机 Room 并双闪必须显式确认。");
                if(_disposed || generation!=_selectionGeneration)throw new UserInputException("确认期间所选设备已变化，本次创建和双闪已取消。");
                if(_executingIntent!=null){var current=await ReadCurrentState(_executingIntent);ValidateBinding(_executingIntent,current);}
                if(!CanLocalPulseFor(name) || _state.Active || !CanStartRoom || !ValidName(name))throw new UserInputException("设备或 Room 状态已变化，请刷新后重新确认。");
                ConfirmOk(await WriteExchange("ROOM",RoomWire.Command(16,null,null,name)),"ROOM");
                if(_disposed || generation!=_selectionGeneration)return;
                await ReadCurrentState(_executingIntent);_refreshRequested=true;if(_disposed || generation!=_selectionGeneration)return;
                if(!CanLocalPulseFor(name) || !_state.Active || !_state.Members.Contains(local) || _state.Name!=name)throw new UserInputException("创建请求已发送，但未确认可执行的本机 Room 状态；没有发送双闪，请检查刷新结果。");
                Log("已读回真实 Room："+_state.Name+" · "+_state.Count+"/6；仅测试本机，测试后保留 Room，不自动退出。");
            }
            await SubmitAndAwaitRgb(local);
        }
        private async Task SubmitAndAwaitRgb(string target)
        {
            int generation=_selectionGeneration;
            string port=Port,local=LocalMac;uint room=_state.Room,incarnation=_state.Incarnation,revision=_state.Revision,term=_state.Term;
            var basis=_state;
            int localIndex=Array.IndexOf(_state.Members,local);uint localBoot=_state.Number(46+10*localIndex);
            var reply=await WriteExchange("RGB",target+" 000808"); if(_disposed || generation!=_selectionGeneration)return;
            var fields=reply.Split(' ');
            if(fields.Length!=4 || fields[0]!="TRILINK/3" || fields[1]!="QUEUED" || fields[3].Length!=8
                || fields[3].Any(c=>!Uri.IsHexDigit(c)) || fields[3]=="00000000")throw new InvalidOperationException("RGB 排队回复格式错误，操作结果未知；请刷新回执。");
            string operation=fields[3].ToUpperInvariant();_rgbPending=true;_rgbTracking=true;_submittedRgbId=operation;
            var stop=new System.Threading.CancellationTokenSource();_rgbReceiptStop=stop;
            Log("RGB "+operation+" → "+target+" 已排队；不是执行完成，开始最多12秒的只读回执跟踪，不会重发双闪。");Publish();
            var elapsed=System.Diagnostics.Stopwatch.StartNew();string last=null;
            try {
                while(!_disposed && generation==_selectionGeneration && !stop.IsCancellationRequested) {
                    var replyResult=await ReadRgbCommandBounded(port,"RGBRESULT","",operation,generation,elapsed,stop);
                    if(replyResult==null)return;
                    var result=ParseRgbResult(replyResult);
                    if(elapsed.ElapsedMilliseconds>=12000)throw new TimeoutException("RGB "+operation+" 回执超过跟踪期限，结果未知；没有重发。");
                    if(result.Operation!=operation)throw new InvalidOperationException("RGB 回执ID与本次 "+operation+" 不一致，结果未知；可能被其他视图更新，不会重发。");
                    if(result.Phase==5)throw new UnknownOutcomeException("RGB "+operation+" → "+target+"：超时，结果未知；已停止关联待执行动作，不会重新发送双闪。");
                    if(result.Phase>=2) {
                        var contextReply=await ReadRgbCommandBounded(port,"ROOMGET","00",operation,generation,elapsed,stop);
                        if(contextReply==null)return;
                        var state=RoomWire.ParseReply(contextReply,0);int currentIndex=state==null?-1:Array.IndexOf(state.Members,local);
                        if(!Fresh || state==null || state.Kind!=1 || !state.Active || LocalMac!=local || state.Room!=room || state.Incarnation!=incarnation
                            || state.Revision!=revision || state.Term!=term || currentIndex<0 || state.Number(46+10*currentIndex)!=localBoot
                            || !state.Members.SequenceEqual(basis.Members) || !state.Members.Contains(target)
                            || Enumerable.Range(0,state.Count).Any(i=>state.Number(46+10*i)!=basis.Number(46+10*i)))
                            throw new InvalidOperationException("RGB 跟踪期间设备或 Room 上下文已变化，执行结果未知；不会重发。");
                        // Only local status for the exact already-refreshed snapshot is replaced.
                        // Never publish a different membership version as a partial full refresh.
                        _state=state;_rgbPending=false;_lastRgbResult=result.Signature;Publish();
                        Log("RGB "+operation+" → "+target+"："+RgbPhases[result.Phase]+"；终态已读回。");return;
                    }
                    _rgbPending=true;
                    if(result.Signature!=last){Log("RGB "+operation+" → "+target+"："+RgbPhases[result.Phase]+"。" );last=result.Signature;}
                    await Task.Delay(Math.Min(350,Math.Max(1,12000-(int)elapsed.ElapsedMilliseconds)),stop.Token);
                }
            } catch(OperationCanceledException) {if(!_disposed && generation==_selectionGeneration)throw;}
            finally {
                if(ReferenceEquals(_rgbReceiptStop,stop))_rgbReceiptStop=null;
                stop.Dispose();_rgbTracking=false;_submittedRgbId=null;
                if(!_disposed){Publish();}
            }
        }
        private async Task<string> ReadRgbCommandBounded(string port,string command,string arguments,string operation,int generation,
            System.Diagnostics.Stopwatch elapsed,System.Threading.CancellationTokenSource stop)
        {
            if(_disposed || generation!=_selectionGeneration || stop.IsCancellationRequested)return null;
            int remaining=12000-(int)elapsed.ElapsedMilliseconds;
            if(remaining<=0)throw new TimeoutException("RGB "+operation+" 回执跟踪超时，结果未知；没有重发，请手动刷新。");
            var read=_commands.ExecuteAsync(port,command,arguments);
            using(var deadline=System.Threading.CancellationTokenSource.CreateLinkedTokenSource(stop.Token)) {
                var limit=Task.Delay(remaining,deadline.Token);var completed=await Task.WhenAny(read,limit);deadline.Cancel();
                if(completed!=read) {
                    ObserveLateRead(read);
                    if(_disposed || generation!=_selectionGeneration || stop.IsCancellationRequested)return null;
                    throw new TimeoutException("RGB "+operation+" 回执跟踪超时，结果未知；已开始的只读事务仍由共享 USB 服务收尾，不会重发。");
                }
            }
            if(_disposed || generation!=_selectionGeneration || stop.IsCancellationRequested){ObserveLateRead(read);return null;}
            string reply=await read;
            if(elapsed.ElapsedMilliseconds>=12000)throw new TimeoutException("RGB "+operation+" 回执超过跟踪期限，结果未知；没有重发。");
            return reply;
        }
        private static void ObserveLateRead(Task<string> read)
        {read.ContinueWith(t=>{var ignored=t.Exception;},System.Threading.CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
        private void StopRgbReceiptTracking() {if(_rgbReceiptStop!=null)_rgbReceiptStop.Cancel();}
        private static RgbResult ParseRgbResult(string reply)
        {
            var fields=reply.Split(' ');
            if(fields.Length!=5 || fields[0]!="TRILINK/3" || fields[1]!="RGBRESULT" || fields[3].Length!=8
                || fields[3].Any(c=>!Uri.IsHexDigit(c)))throw new InvalidOperationException("RGB 回执格式错误，执行结果未知。");
            int phase;if(!int.TryParse(fields[4],out phase) || phase<0 || phase>5)throw new InvalidOperationException("RGB 状态无效，执行结果未知。");
            return new RgbResult {Operation=fields[3].ToUpperInvariant(),Phase=phase};
        }
        private static void ConfirmOk(string reply,string command)
        {var fields=reply.Split(' ');if(fields.Length!=4 || fields[0]!="TRILINK/3" || fields[1]!="OK" || fields[3]!=command)
            throw new InvalidOperationException(command+" 确认回复格式错误，操作结果未知；请刷新状态。");}
        public async Task RefreshAll()
        {
            if(_draining || _publicRefreshRunning)throw new InvalidOperationException("刷新批次已由队列调度或正在执行；请使用本页刷新按钮合并请求，不并行读取。");
            _publicRefreshRunning=true;
            bool own=!_busy && !_draining;
            if(own){_busy=true;_backgroundReading=true;Publish();}
            try {await RefreshCore(true);}
            finally {_publicRefreshRunning=false;if(own){_busy=false;_backgroundReading=false;if(!_disposed){Publish();StartPump();}}}
        }
        private async Task<T> BackgroundRead<T>(Func<Task<T>> read,int deviceGeneration,int refreshGeneration)
        {
            try {var value=await read();if(_disposed || deviceGeneration!=_selectionGeneration || refreshGeneration!=_refreshGeneration)throw new RefreshInterruptedException();return value;}
            catch {if(_disposed || deviceGeneration!=_selectionGeneration || refreshGeneration!=_refreshGeneration)throw new RefreshInterruptedException();throw;}
        }
        private async Task RefreshCore(bool full)
        {
            if(_disposed)return;
            int generation=_selectionGeneration,refreshGeneration=_refreshGeneration;string port=Port;
            try {
                var state=RoomWire.ParseReply(await BackgroundRead(()=>_commands.ExecuteAsync(port,"ROOMGET","00"),generation,refreshGeneration),0);
                if(state==null || state.Kind!=1)throw new InvalidOperationException("本机 Room 状态缺失或类型错误。");
                var requests=new List<RoomWire>();
                bool leader=state.Active && state.Leader==LocalMac;
                if(full || leader) {
                    for(int page=7;page<=12;++page) {
                        int current=page;var item=RoomWire.ParseReply(await BackgroundRead(()=>_commands.ExecuteAsync(port,"ROOMGET",current.ToString("X2")),generation,refreshGeneration),page);
                        if(item!=null && (item.Kind==3 || item.Kind==7) && SameRoom(item,state))requests.Add(item);
                    }
                    var anchor=RoomWire.ParseReply(await BackgroundRead(()=>_commands.ExecuteAsync(port,"ROOMGET","00"),generation,refreshGeneration),0);
                    if(!SameViewContext(state,anchor))requests.Clear();state=anchor;
                    PublishState(state);ReplaceItems(_requests,requests);
                }
                if(full || !state.Active) {
                    var rooms=new List<RoomWire>();var invites=new List<RoomWire>();
                    for(int page=1;page<=6;++page) {
                        int current=page;var item=RoomWire.ParseReply(await BackgroundRead(()=>_commands.ExecuteAsync(port,"ROOMGET",current.ToString("X2")),generation,refreshGeneration),page);
                        if(item!=null && item.Kind==1 && item.Count>0)rooms.Add(item);
                    }
                    for(int page=13;page<=18;++page) {
                        int current=page;var item=RoomWire.ParseReply(await BackgroundRead(()=>_commands.ExecuteAsync(port,"ROOMGET",current.ToString("X2")),generation,refreshGeneration),page);
                        if(item!=null && item.Kind==4)invites.Add(item);
                    }
                    var peers=await BackgroundRead(()=>_discovery.SearchNearbyAsync(port),generation,refreshGeneration);
                    ReplaceItems(_rooms,rooms);ReplaceItems(_invites,invites);PublishPeers(peers);
                }
                else if(System.Diagnostics.Stopwatch.GetTimestamp()-_lastPeerRefresh>=System.Diagnostics.Stopwatch.Frequency*15) {
                    // Active-room control reads stay light; discovery still refreshes
                    // at most once per 15 seconds and yields at this single I/O boundary.
                    var peers=await BackgroundRead(()=>_discovery.SearchNearbyAsync(port),generation,refreshGeneration);PublishPeers(peers);
                }
                var result=ParseRgbResult(await BackgroundRead(()=>_commands.ExecuteAsync(port,"RGBRESULT",""),generation,refreshGeneration));
                var final=RoomWire.ParseReply(await BackgroundRead(()=>_commands.ExecuteAsync(port,"ROOMGET","00"),generation,refreshGeneration),0);
                if(!SameViewContext(state,final))requests.Clear();PublishState(final);
                if(full || leader)ReplaceItems(_requests,requests);
                _rgbPending=result.Operation!="00000000" && result.Phase<=1;Publish();
                if(_capabilityService!=null && (full || _capabilities==null)) {
                    var capabilities=await BackgroundRead(()=>_capabilityService.DescribeAsync(port),generation,refreshGeneration);
                    if(capabilities==null || Normalize(capabilities.NodeId)!=LocalMac)throw new InvalidOperationException("能力描述与当前节点身份不匹配。");
                    // ROOMSTATE source.boot belongs to the original room author,
                    // not necessarily this board. Only the local membership entry
                    // can be compared with CAPS; an idle board has no such entry.
                    if(!capabilities.IsLegacy && final.Active && MemberBoot(final,LocalMac)!=capabilities.BootId)
                        throw new InvalidOperationException("本机成员启动身份与能力描述不匹配；请重新刷新，旧意图不执行。");
                    if(_capabilities!=null && !_capabilities.IsLegacy && !capabilities.IsLegacy && _capabilities.BootId!=capabilities.BootId) {
                        InvalidateBoot(capabilities);throw new InvalidOperationException(_invalidMessage);
                    }
                    _capabilities=capabilities;Publish();
                }
                if(!_rgbTracking && result.Operation!="00000000" && result.Signature!=_lastRgbResult)Log("设备最近 RGB "+result.Operation+"："+RgbPhases[result.Phase]+"（非本次操作的跟踪确认）。");
                _lastRgbResult=result.Signature;
            } catch(RefreshInterruptedException) {_refreshInterrupted=true;}
        }
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
        private bool CanLocalPulse {get {return CanLocalPulseFor((_executingIntent==null?"TriLink Room":_executingIntent.Name));}}
        private void ValidateRoomAction(byte kind,RoomWire state,string target,string name)
        {
            bool allowed=false;
            switch(kind) {
                case 16: allowed=CanStartRoom && ValidName(name);break;
                case 3: allowed=CanJoin(state) && target==state.Leader;break;
                case 4: allowed=Fresh && _state.Active && !_state.Waiting && _state.Count<6 && target!=null
                    && RoomWire.ValidMac(target) && !_state.Members.Contains(target) && _peers.Contains(target);break;
                case 5: allowed=Fresh && _state.Active && !_state.Waiting && (!Leader || AuthorityReady);break;
                case 17: allowed=AuthorityReady && _state.Count<6 && _requests.Cast<RoomWire>().Any(r=>MatchingRequest(r) && r.Kind==3 && r.Source==target);break;
                case 18: allowed=AuthorityReady && _requests.Cast<RoomWire>().Any(r=>MatchingRequest(r) && (r.Kind==3 || r.Kind==7) && r.Source==target);break;
                case 19: allowed=AuthorityReady && target!=null && target!=LocalMac && _state.Members.Contains(target);break;
                case 20: allowed=Fresh && !_state.Active && _state.Waiting && _state.JoinConfirmed && _state.JoinUncertain;break;
            }
            if(!allowed)throw new UserInputException("当前设备或 Room 状态不允许该操作；请成功刷新，并核对角色、所选记录与下方状态说明。");
            if(kind!=16 && kind!=3 && kind!=20 && (state==null || state.Room!=_state.Room || state.Incarnation!=_state.Incarnation
                || state.Revision!=_state.Revision || state.Term!=_state.Term))
                throw new UserInputException("所选 Room 版本已变化，请刷新后重新选择。");
        }

        private void PublishPeers(IReadOnlyList<TriLinkPeer> peers)
        {
            _lastPeerRefresh=System.Diagnostics.Stopwatch.GetTimestamp();_peers.Clear();_peerRssi.Clear();
            foreach(var p in peers.Where(p=>p!=null && RoomWire.ValidMac(Normalize(p.NodeId))).GroupBy(p=>Normalize(p.NodeId)).Select(g=>g.First()))
            {_peerRssi[Normalize(p.NodeId)]=p.Rssi;_peers.Add(Normalize(p.NodeId));}Publish();
        }
        private void ReplaceItems<T>(List<T> list,IEnumerable<T> values) {list.Clear();list.AddRange(values);Publish();}
        private void ClearLists() {_rooms.Clear();_members.Clear();_requests.Clear();_invites.Clear();}
        private void InvalidateBoot(NodeCapabilities capabilities)
        {
            ++_selectionGeneration;InterruptRefresh();ClearQueued("本机节点重新启动或能力改变");_refreshRequested=false;_refreshFullRequested=false;
            _state=null;ClearLists();_peers.Clear();_peerRssi.Clear();_rgbPending=false;
            _automatic=false;_livePaused=true;_timer.Stop();_pollingState=NodePollingState.DeviceChanged;
            _invalidMessage="本机节点启动身份已变化，旧意图已失效；请手动刷新，不自动重放。";_capabilities=capabilities;
            CompleteRefreshWaiter(false,new InvalidOperationException(_invalidMessage));Log(_invalidMessage);Publish();
        }
        private static NodeRoomState Record(RoomWire s) {return s==null?null:new NodeRoomState(s.Bytes,s.Name,s.Leader,s.Room,s.Incarnation,s.Revision,s.Term,s.Active,s.Synchronized,s.Members);}
        private static NodeOperationSnapshot Operation(QueuedIntent i) {return i==null?null:new NodeOperationSnapshot {RequestId=i.RequestId,Description=i.Description,Target=i.Target,WriteAttempted=i.WriteAttempted};}
        private NodeSessionSnapshot Snapshot()
        {
            return new NodeSessionSnapshot {Device=CopyDevice(_selectedDevice),Generation=_selectionGeneration,Room=Record(_state),
                NearbyRooms=_rooms.Select(Record).ToList().AsReadOnly(),Requests=_requests.Select(Record).ToList().AsReadOnly(),Invitations=_invites.Select(Record).ToList().AsReadOnly(),
                PeerRssi=new System.Collections.ObjectModel.ReadOnlyDictionary<string,int>(new Dictionary<string,int>(_peerRssi)),
                QueuedOperations=_pendingCommands.Select(Operation).ToList().AsReadOnly(),ExecutingOperation=Operation(_executingIntent),
                Busy=_busy,BackgroundReading=_backgroundReading,RgbPending=_rgbPending,RgbTracking=_rgbTracking,RgbOperationId=_submittedRgbId,
                ConsecutiveFailures=_failures,InvalidReason=_invalidMessage,PollingState=_automatic?NodePollingState.Running:_pollingState,Log=_logs.ToList().AsReadOnly(),Capabilities=_capabilities};
        }
        private void Publish()
        {
            if(_disposed)return;int cadence=Leader?2000:5000;if(_timer.Interval!=cadence)_timer.Interval=cadence;
            foreach(var lease in _subscriptions.ToArray())lease.Notify();
        }
        private void Log(string message)
        {
            if(message==_lastLogMessage)return;_lastLogMessage=message;
            _logs.Add(DateTime.Now.ToString("HH:mm:ss")+" · "+message);if(_logs.Count>64)_logs.RemoveAt(0);Publish();
        }
        public void Dispose()
        {
            AssertContext();if(_disposed)return;_disposed=true;++_selectionGeneration;InterruptRefresh();_automatic=false;_timer.Stop();_timer.Dispose();
            _refreshRequested=false;_refreshFullRequested=false;ClearQueued("节点服务关闭");StopRgbReceiptTracking();
            _discovery.DeviceArrived-=DiscoveryChanged;_discovery.DeviceRemoved-=DiscoveryChanged;
            foreach(var lease in _subscriptions.ToArray()){lease.Disposed=true;}_subscriptions.Clear();DevicesChanged=null;CompleteRefreshWaiter(true);
        }
    }
}
