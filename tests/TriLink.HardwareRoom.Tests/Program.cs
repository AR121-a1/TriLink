using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TriLink.Core;
using TriLink.Plugins.HardwareRoom;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string name) {++checks;if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    private static void Reject(Action action,string name) {bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(InvalidDataException){rejected=true;}Check(rejected,name);}
    private static void Render(Control view,string path)
    {using(var bitmap=new Bitmap(view.Width,view.Height)){view.DrawToBitmap(bitmap,new Rectangle(Point.Empty,view.Size));bitmap.Save(path);}}
    private static Button Button(Control view,string name) {return (Button)view.Controls.Find(name,true).Single();}
    private static void Click(Control view,string name,bool wait=true)
    {
        bool enabled=Button(view,name).Enabled;
        typeof(Control).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(Button(view,name),new object[]{EventArgs.Empty});
        if(enabled && wait)WaitIdle(view);
    }
    private static void WaitIdle(Control view)
    {
        var busy=StateField("_busy");
        var elapsed=System.Diagnostics.Stopwatch.StartNew();
        while(!view.IsDisposed && (bool)busy.GetValue(view)) {if(elapsed.ElapsedMilliseconds>15000)throw new TimeoutException("fixture action never completed");Application.DoEvents();System.Threading.Thread.Sleep(5);}
        Application.DoEvents();
    }
    private static HardwareRoomView CreateView(Fake service,Func<string,string,bool> confirm)
    {return (HardwareRoomView)Activator.CreateInstance(typeof(HardwareRoomView),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,
        null,new object[]{service,service,confirm},System.Globalization.CultureInfo.InvariantCulture);}
    private static HardwareRoomView CreateView(Fake service) {return CreateView(service,(_,__)=>false);}
    private static HardwareRoomView CreateLiveView(Fake service)
    {return (HardwareRoomView)Activator.CreateInstance(typeof(HardwareRoomView),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,
        null,new object[]{service,service,(Func<string,string,bool>)((_,__)=>false),true},System.Globalization.CultureInfo.InvariantCulture);}
    private static string LogText(Control view) {return view.Controls.Find("HardwareRoomLog",true).Single().Text;}
    // Assertions keep their original intent; coordinator state moved out of the page.
    private static NodeSessionService Coordinator(object view)
    {return (NodeSessionService)typeof(HardwareRoomView).GetField("_sessionService",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(view);}
    private static FieldAccessor StateField(string name) {return new FieldAccessor(name);}
    private sealed class FieldAccessor
    {
        private readonly string _name;
        public FieldAccessor(string name) {_name=name;}
        public object GetValue(object view) {var target=_name=="_autoLive"?view:Coordinator(view);return Field(target).GetValue(target);}
        public void SetValue(object view,object value) {var target=_name=="_autoLive"?view:Coordinator(view);Field(target).SetValue(target,value);}
        private System.Reflection.FieldInfo Field(object target)
        {return target.GetType().GetField(_name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)??throw new InvalidOperationException("Missing test seam: "+_name);}
    }
    private static int QueueDepth(Control view)
    {var queue=StateField("_pendingCommands").GetValue(view);return (int)queue.GetType().GetProperty("Count").GetValue(queue,null);}
    private static void ReleaseRead(Fake service) {service.ReadGate.SetResult(service.CapturedRead.GetAwaiter().GetResult());Application.DoEvents();}
    private static void ReleaseWrite(Fake service) {service.WriteGate.SetResult(service.CapturedWrite.GetAwaiter().GetResult());Application.DoEvents();}
    private static bool AllBusinessDisabled(Control view)
    {return new[]{"HardwareCreate","HardwareJoin","HardwareLeave","HardwareApprove","HardwareReject","HardwareAcceptInvite","HardwareKick",
        "HardwareInvite","HardwareRetryJoin","HardwareRgbEnable","HardwareRgbDisable","HardwareRgbLocalPulse","HardwareRgbPulse"}.All(name=>!Button(view,name).Enabled);}
    private static byte[] Snapshot()
    {
        var bytes=new byte[132];bytes[0]=1;bytes[1]=5;bytes[2]=2;bytes[3]=1;
        bytes[4]=0x10;bytes[9]=1;bytes[10]=101;bytes[14]=7;bytes[18]=1;bytes[22]=101;bytes[26]=2;bytes[30]=1;
        bytes[34]=0x10;bytes[39]=1;bytes[40]=0x10;bytes[45]=1;bytes[46]=101;
        bytes[50]=0x20;bytes[55]=2;bytes[56]=202;bytes[100]=76;bytes[101]=97;bytes[102]=98;bytes[130]=3;
        return bytes;
    }
    [STAThread] private static int Main(string[] args)
    {
        try {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            AppDomain.CurrentDomain.UnhandledException+=(_,e)=> {Console.Error.WriteLine("FAIL unhandled async fixture: "+e.ExceptionObject);Environment.Exit(1);};
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>1 && args[1]=="--pause-race-only") {PauseRaceChecks();Console.WriteLine("PASS pause-race checks="+checks);return 0;}
            if(args.Length>1 && args[1]=="--patch-regressions-only") {FuseOverlapChecks();PulseDedupeChecks();Console.WriteLine("PASS patch-regression checks="+checks);return 0;}
            if(args.Length>1 && args[1]=="--shared-session-only") {SharedSessionChecks(args[0]);Console.WriteLine("PASS shared-session checks="+checks);return 0;}
            var bytes=Snapshot();var packet=new RoomWire(bytes);
            Check(packet.Active && packet.Synchronized && !packet.RgbEnabled,"local status flags do not imply RGB enabled");
            var rgbFault=Snapshot();rgbFault[130]=1|64;
            Check(new RoomWire(rgbFault).RgbFaulted && !new RoomWire(rgbFault).RgbEnabled,"local RGB fault is distinct from enabled or unknown application result");
            var retiring=Snapshot();retiring[130]=128;
            Check(new RoomWire(retiring).Retiring && !new RoomWire(retiring).Active,"retirement announcement does not imply active membership");
            var uncertain=Snapshot();uncertain[130]=4|16|32;
            Check(new RoomWire(uncertain).Waiting && new RoomWire(uncertain).JoinConfirmed && new RoomWire(uncertain).JoinUncertain,"uncertain admission is distinct from active membership");
            Check(new RoomWire(RoomWire.Unhex(RoomWire.Command(20,null))).Kind==20,"manual retry uses dedicated command without new admission ticket");
            Check(packet.Name=="Lab" && packet.Room==1 && packet.Incarnation==101 && packet.Revision==2 && packet.Term==1,"little-endian Room fields");
            Check(packet.Leader=="100000000001" && packet.Members[1]=="200000000002","membership join order retained");
            Check(RoomWire.ParseReply("TRILINK/3 ROOMSTATE n 00 "+RoomWire.Hex(bytes),0).Count==2,"USB Room page decodes");
            Check(RoomWire.ParseReply("TRILINK/3 EMPTY n",1)==null,"empty page supported");
            Reject(()=>RoomWire.ParseReply("TRILINK/3 ROOMSTATE n 01 "+RoomWire.Hex(bytes),0),"wrong page rejected");
            foreach(int length in new[]{0,1,131,133})Reject(()=>new RoomWire(new byte[length]),"length "+length+" rejected");
            foreach(int index in new[]{60,123,131}) {var bad=(byte[])bytes.Clone();bad[index]=1;Reject(()=>new RoomWire(bad),"reserved bytes "+index+" rejected");}
            var duplicate=(byte[])bytes.Clone();Array.Copy(duplicate,40,duplicate,50,10);Reject(()=>new RoomWire(duplicate),"duplicate member rejected");
            var boot=(byte[])bytes.Clone();Array.Clear(boot,46,4);Reject(()=>new RoomWire(boot),"zero boot rejected");
            var utf8=(byte[])bytes.Clone();utf8[100]=255;Reject(()=>new RoomWire(utf8),"invalid UTF-8 rejected");
            var created=new RoomWire(RoomWire.Unhex(RoomWire.Command(16,null,null,"中文 Room")));
            Check(created.Name=="中文 Room" && created.Kind==16,"UTF-8 create command preserves text");
            Reject(()=>RoomWire.Command(16,null,null,new string('中',8)),"24 UTF-8 byte name rejected");
            Reject(()=>RoomWire.Command(16,null,null,"line\n"),"control characters rejected");
            Reject(()=>RoomWire.Command(4,packet,"FFFFFFFFFFFF"),"broadcast invitation target rejected");
            Check(new RoomWire(RoomWire.Unhex(RoomWire.Command(17,packet,"200000000002"))).Bytes[130]==0,"local status not leaked into commands");
            var service=new Fake();
            using(var view=CreateView(service)) {
                view.Size=new Size(1000,700);view.CreateControl();view.PerformLayout();
                Check(service.Calls==0,"opening module performs no hardware write or automatic polling");
                Check(AllBusinessDisabled(view) && Button(view,"HardwareRefresh").Enabled,"identified device needs successful refresh before any business action");
                Check(view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("请点击“刷新设备与 Room”"),"refresh prerequisite is visible rather than silently disabling buttons");
                Render(view,args[0]+".empty.png");
                Check(((ComboBox)view.Controls.Find("HardwarePorts",true).Single()).Items.Count==1,"only capable devices listed");
                view.RefreshAll().GetAwaiter().GetResult();
                Check(service.Calls==22 && service.Searches==1,"bounded full refresh with early authority and request publication plus final context read");
                Check(((ListBox)view.Controls.Find("RoomMembers",true).Single()).Items.Count==2,"view renders hardware membership");
                Check(view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("leader=100000000001"),"hardware leader visible");
                Check(view.Controls.Find("HardwareMonitor",true).Single().Text=="启动状态轮询","polling remains opt-in after refresh");
                Check(!Button(view,"HardwareCreate").Enabled && Button(view,"HardwareLeave").Enabled,"active synchronized leader can leave but cannot create another room");
                Check(!Button(view,"HardwareApprove").Enabled && !Button(view,"HardwareKick").Enabled,"approval and kick require a selection");
                var localMember=(ListBox)view.Controls.Find("RoomMembers",true).Single();localMember.SelectedIndex=0;
                Check(!Button(view,"HardwareKick").Enabled && !Button(view,"HardwareRgbPulse").Enabled,"leader cannot kick self or pulse an unenabled local RGB");
                localMember.SelectedIndex=1;
                Check(Button(view,"HardwareKick").Enabled && Button(view,"HardwareRgbPulse").Enabled,"synchronized leader can kick or queue RGB to a selected remote member");
                service.Uncertain=true;view.RefreshAll().GetAwaiter().GetResult();
                Check(view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("入群结果待确认"),"unknown commit is visible and does not claim membership");
                Check(((ListBox)view.Controls.Find("RoomMembers",true).Single()).Items.Count==0,"unresolved admission has no active member actions");
                Check(Button(view,"HardwareRetryJoin").Enabled && !Button(view,"HardwareCreate").Enabled && !Button(view,"HardwareJoin").Enabled,"uncertain admission exposes only explicit admission retry, never a new room or new join");
                var retryFlags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                ((Task)typeof(HardwareRoomView).GetMethod("RetryJoin",retryFlags).Invoke(view,null)).GetAwaiter().GetResult();
                Check(service.Writes==1 && service.LastKind==20,"manual retry sends one explicit retry, never CREATE or JOIN");
                Render(view,args[0]+".uncertain.png");
                service.Uncertain=false;view.RefreshAll().GetAwaiter().GetResult();
                int beforeFault=service.Writes;service.RgbFault=true;view.RefreshAll().GetAwaiter().GetResult();
                Check(view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("RGB 驱动故障，已停用"),"latched local RGB fault has an actionable recovery message");
                Check(!Button(view,"HardwareRgbEnable").Enabled && !Button(view,"HardwareRgbDisable").Enabled,"latched driver fault cannot be blindly re-enabled or described as still enabled");
                Check(service.Writes==beforeFault,"displaying a driver fault never automatically retries a hardware write");
                Render(view,args[0]+".rgb-fault.png");
                service.RgbFault=false;view.RefreshAll().GetAwaiter().GetResult();
                service.Retiring=true;view.RefreshAll().GetAwaiter().GetResult();
                Check(view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("正在重播退出公告"),"retired leader can see why new membership commands are temporarily blocked");
                Check(!Button(view,"HardwareCreate").Enabled && !Button(view,"HardwareJoin").Enabled && !Button(view,"HardwareRetryJoin").Enabled,"retirement announcement cannot be overwritten by new membership commands");
                Render(view,args[0]+".retiring.png");
                service.Retiring=false;view.RefreshAll().GetAwaiter().GetResult();
                var members=(ListBox)view.Controls.Find("RoomMembers",true).Single();members.SelectedIndex=1;
                var rooms=(ListBox)view.Controls.Find("NearbyRooms",true).Single();rooms.SelectedIndex=0;
                view.RefreshAll().GetAwaiter().GetResult();
                Check((string)members.SelectedItem=="200000000002" && rooms.SelectedIndex==0,"periodic refresh preserves stable selections");
                using(var bitmap=new Bitmap(view.Width,view.Height)) {view.DrawToBitmap(bitmap,new Rectangle(Point.Empty,view.Size));bitmap.Save(args[0]);}
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var automatic=StateField("_automatic");
                var run=typeof(HardwareRoomView).GetMethod("Run",flags);
                service.BadResult=true;
                Func<Task> refreshAction=()=>view.RefreshAll();
                ((Task)run.Invoke(view,new object[]{refreshAction})).GetAwaiter().GetResult();
                Check(members.Items.Count==0 && rooms.Items.Count==0,"malformed response invalidates stale actionable membership");
                Check(view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("失效"),"failure explicitly marks state invalid");
                Check(AllBusinessDisabled(view) && Button(view,"HardwareRefresh").Enabled,"invalid response disables stale business actions but retains manual recovery");
                service.BadResult=false;((Task)run.Invoke(view,new object[]{refreshAction})).GetAwaiter().GetResult();
                Check(members.Items.Count==2,"successful refresh recovers hardware state");
                int beforeInvalidInput=service.Calls;
                Func<Task> invalidName=()=>(Task)typeof(HardwareRoomView).GetMethod("SendRoom",flags).Invoke(view,new object[]{(byte)16,null,null,new string('中',8)});
                ((Task)run.Invoke(view,new object[]{invalidName})).GetAwaiter().GetResult();
                Check(service.Calls==beforeInvalidInput && members.Items.Count==2,"local input errors neither write hardware nor invalidate good state");
                automatic.SetValue(view,true);
                Func<Task> failure=()=>Task.FromException(new IOException("fixture disconnected"));
                for(int i=0;i<4;++i)((Task)run.Invoke(view,new object[]{failure})).GetAwaiter().GetResult();
                Check((bool)automatic.GetValue(view),"four consecutive failures do not prematurely pause polling");
                ((Task)run.Invoke(view,new object[]{failure})).GetAwaiter().GetResult();
                Check(!(bool)automatic.GetValue(view),"five failures pause automatic polling");
                Check(view.Controls.Find("HardwareMonitor",true).Single().Text=="手动重启状态轮询","paused polling exposes manual restart");
                Render(view,args[0]+".paused.png");
            }
            service=new Fake {Requests=true,Invitations=true};
            using(var view=CreateView(service)) {
                view.Size=new Size(600,760);view.CreateControl();view.PerformLayout();view.RefreshAll().GetAwaiter().GetResult();
                var requests=(ListBox)view.Controls.Find("RoomRequests",true).Single();requests.SelectedIndex=0;
                Check(Button(view,"HardwareApprove").Enabled && Button(view,"HardwareReject").Enabled,"synchronized leader may approve or reject a selected fresh join ticket");
                Click(view,"HardwareApprove");Check(service.Writes==1 && service.LastKind==17 && service.LastTarget=="500000000001","approve button sends selected applicant to the existing real Room API");
                requests.SelectedIndex=1;
                Check(!Button(view,"HardwareApprove").Enabled && Button(view,"HardwareReject").Enabled,"already offered ticket may be rejected but cannot be approved twice");
                Click(view,"HardwareReject");Check(service.Writes==2 && service.LastKind==18 && service.LastTarget=="600000000001","reject button preserves applicant identity and Room state");
                var members=(ListBox)view.Controls.Find("RoomMembers",true).Single();members.SelectedIndex=1;
                Click(view,"HardwareKick");Check(service.Writes==3 && service.LastKind==19 && service.LastTarget=="200000000002","kick button targets selected non-leader only");
                service.Unsynchronized=true;view.RefreshAll().GetAwaiter().GetResult();requests.SelectedIndex=0;members.SelectedIndex=1;
                Check(!Button(view,"HardwareApprove").Enabled && !Button(view,"HardwareReject").Enabled && !Button(view,"HardwareKick").Enabled && !Button(view,"HardwareLeave").Enabled,
                    "leader awaiting member acknowledgements cannot mutate membership or exit prematurely");
                Check(view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("leader 正在等待成员确认"),"leader synchronization prerequisite is explained in the status area");
                service.Unsynchronized=false;service.Full=true;view.RefreshAll().GetAwaiter().GetResult();requests.SelectedIndex=0;
                var peers=(ComboBox)view.Controls.Find("HardwareInvitePeers",true).Single();peers.SelectedIndex=0;
                Check(!Button(view,"HardwareApprove").Enabled && !Button(view,"HardwareInvite").Enabled,"six-member Room rejects admission and invitations without issuing commands");
                int beforeReload=service.Calls;Click(view,"HardwareReloadPorts");
                Check(service.Calls==beforeReload && ((ListBox)view.Controls.Find("RoomMembers",true).Single()).Items.Count==6,"read-only port refresh preserves the unchanged device snapshot");
                service.Full=false;service.LocalNodeId="200000000002";service.RaiseArrived();
                Check(AllBusinessDisabled(view) && service.Calls==beforeReload,"same-port identity replacement invalidates stale state without automatic hardware I/O");
                view.RefreshAll().GetAwaiter().GetResult();requests.SelectedIndex=0;members.SelectedIndex=0;peers.SelectedIndex=0;
                Check(!Button(view,"HardwareApprove").Enabled && !Button(view,"HardwareReject").Enabled && !Button(view,"HardwareKick").Enabled && Button(view,"HardwareLeave").Enabled,
                    "ordinary member has leave access but no leader-only approval rejection or kick rights");
                Check(Button(view,"HardwareInvite").Enabled && view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("普通成员可邀请和退出"),"non-leader invitation is available and its approval boundary is clear");
                Click(view,"HardwareInvite");Check(service.LastKind==4 && service.LastTarget=="300000000003","ordinary member invitation follows the real Room API and selected nearby peer");
                Render(view,args[0]+".member-narrow.png");
                service.NoDevices=true;int beforeRemove=service.Calls;service.RaiseRemoved();
                Check(AllBusinessDisabled(view) && !Button(view,"HardwareRefresh").Enabled && !Button(view,"HardwareMonitor").Enabled,"removed device disables every hardware action immediately");
                Check(service.Calls==beforeRemove && view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("CH343 / CH340 是烧录口"),"device removal performs no I/O and points to the native USB data port");
                Click(view,"HardwareCreate");Click(view,"HardwareRefresh");Check(service.Calls==beforeRemove,"disabled buttons cannot submit commands even if click events are raised");
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var run=typeof(HardwareRoomView).GetMethod("Run",flags);
                var log=(TextBox)view.Controls.Find("HardwareRoomLog",true).Single();int previousLines=log.Lines.Count(s=>s.Length>0);
                for(int i=0;i<3;++i)((Task)run.Invoke(view,new object[]{(Func<Task>)(()=>view.RefreshAll())})).GetAwaiter().GetResult();
                Check(log.Lines.Count(s=>s.Length>0)==previousLines+1,"repeated no-device attempts do not flood the event log");
                Check((int)StateField("_failures").GetValue(view)==0,"missing-device prerequisite does not consume the transport failure budget");
                Render(view,args[0]+".no-device.png");
                service.NoDevices=false;service.UnsupportedOnly=true;service.RaiseArrived();
                Check(AllBusinessDisabled(view) && view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("没有支持真实 Room / RGB"),"recognized incompatible firmware is distinct from no device");
                service.UnsupportedOnly=false;service.LocalNodeId="100000000001";service.Inactive=true;service.RaiseArrived();
                Check(service.Calls==beforeRemove && Button(view,"HardwareRefresh").Enabled,"arrival updates the device list without hidden polling or write operations");
                view.RefreshAll().GetAwaiter().GetResult();
                var rooms=(ListBox)view.Controls.Find("NearbyRooms",true).Single();rooms.SelectedIndex=0;
                var invites=(ListBox)view.Controls.Find("RoomInvites",true).Single();invites.SelectedIndex=0;
                Check(Button(view,"HardwareCreate").Enabled && Button(view,"HardwareJoin").Enabled && Button(view,"HardwareAcceptInvite").Enabled,"fresh idle node can create or apply to the selected nearby room or invitation");
                var roomName=(TextBox)view.Controls.Find("HardwareRoomName",true).Single();roomName.Text=new string('中',8);
                Check(!Button(view,"HardwareCreate").Enabled && view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("1–23 个 UTF-8 字节"),"UTF-8 name limit is reflected in the creation button and status before any write");
                roomName.Text="中文 Room";Click(view,"HardwareCreate");Check(service.LastKind==16,"create button uses the real Room command with a valid UTF-8 name");
                Click(view,"HardwareJoin");Check(service.LastKind==3 && service.LastTarget=="300000000003","join button targets the advertised room leader");
                Click(view,"HardwareAcceptInvite");Check(service.LastKind==3 && service.LastTarget=="300000000003","accepting an invitation sends JOIN rather than bypassing leader approval");
                service.Inactive=false;service.RgbEnabled=true;view.RefreshAll().GetAwaiter().GetResult();members.SelectedIndex=0;
                Check(!Button(view,"HardwareRgbEnable").Enabled && Button(view,"HardwareRgbDisable").Enabled && Button(view,"HardwareRgbPulse").Enabled,"confirmed local RGB permit enables local pulse and explicit disable only");
                Click(view,"HardwareRgbPulse");Check(service.LastCommand=="RGB" && Button(view,"HardwareRgbPulse").Enabled && Button(view,"HardwareRgbDisable").Enabled,"queued RGB waits for a same-ID terminal receipt before re-enabling explicit actions");
                Check(LogText(view).Contains("驱动执行完成（非光学验证）") && !LogText(view).Contains("亮灯成功"),"terminal RGB driver receipt does not claim optical illumination");
                service.PendingRgb=false;view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbDisable");
                Check(service.LastCommand=="RGBENABLE" && !service.RgbEnabled && Button(view,"HardwareRgbEnable").Enabled,"disable operation refreshes the board permission instead of retaining stale RGB buttons");
                service.BadWriteReply=true;members.SelectedIndex=1;Click(view,"HardwareKick");
                Check(AllBusinessDisabled(view) && view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("结果可能未知"),"malformed mutation confirmation is not reported as successful or replayed automatically");
                service.BadWriteReply=false;view.RefreshAll().GetAwaiter().GetResult();members.SelectedIndex=1;service.BadWriteReply=true;Click(view,"HardwareRgbPulse");
                Check(AllBusinessDisabled(view) && Button(view,"HardwareRefresh").Enabled,"malformed RGB queue receipt invalidates state but preserves explicit read-only recovery");
            }
            Check(service.Subscriptions==0,"disposing hardware module detaches shared discovery handlers");
            service=new Fake {Delay=true};var pendingView=CreateView(service);
            // No WinForms sync context: continuations execute on the completing test thread.
            var context=System.Threading.SynchronizationContext.Current;
            System.Threading.SynchronizationContext.SetSynchronizationContext(null);
            var refresh=pendingView.RefreshAll();pendingView.Dispose();service.Pending.SetResult("TRILINK/3 EMPTY n");refresh.GetAwaiter().GetResult();
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            Check(service.Calls==1,"closing module stops the remaining page batch");
            service=new Fake {Delay=true};
            using(var pausedView=CreateView(service)) {
                var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                StateField("_automatic").SetValue(pausedView,true);
                System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                var pausedRefresh=pausedView.RefreshAll();
                var monitor=(Button)pausedView.Controls.Find("HardwareMonitor",true).Single();
                typeof(Control).GetMethod("OnClick",flags).Invoke(monitor,new object[]{EventArgs.Empty});
                service.Pending.SetResult("TRILINK/3 EMPTY n");pausedRefresh.GetAwaiter().GetResult();
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                Check(service.Calls==1,"manual pause cancels remaining refresh pages without closing view");
            }
            service=new Fake();
            using(var busyView=CreateView(service)) {
                busyView.Size=new Size(600,760);busyView.CreateControl();busyView.RefreshAll().GetAwaiter().GetResult();
                var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                StateField("_automatic").SetValue(busyView,true);service.Delay=true;int beforeBusy=service.Calls;
                System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                var busyRefresh=(Task)typeof(HardwareRoomView).GetMethod("Run",flags).Invoke(busyView,new object[]{(Func<Task>)(()=>busyView.RefreshAll())});
                Check(Button(busyView,"HardwareLeave").Enabled && Button(busyView,"HardwareRefresh").Enabled && Button(busyView,"HardwareMonitor").Enabled,"background read preserves valid foreground submission and independent pause controls");
                Click(busyView,"HardwareKick");Check(service.Calls==beforeBusy+1,"disabled busy business action cannot queue a second device operation");
                Click(busyView,"HardwareMonitor",false);service.Pending.SetResult("TRILINK/3 EMPTY n");busyRefresh.GetAwaiter().GetResult();
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                Check(service.Calls==beforeBusy+1 && ((ListBox)busyView.Controls.Find("RoomMembers",true).Single()).Items.Count==2 && Button(busyView,"HardwareLeave").Enabled,"manual background pause stops remaining reads without invalidating a good actionable snapshot");
                Check((int)StateField("_failures").GetValue(busyView)==0,"background pause is not a communication failure and does not consume the failure fuse");
            }
            service=new Fake();
            using(var eventView=CreateView(service)) {
                eventView.CreateControl();var handle=eventView.Handle;eventView.RefreshAll().GetAwaiter().GetResult();service.NoDevices=true;int beforeEvent=service.Calls;
                Task.Run(()=>service.RaiseRemoved()).GetAwaiter().GetResult();Application.DoEvents();
                Check(AllBusinessDisabled(eventView) && !Button(eventView,"HardwareRefresh").Enabled && service.Calls==beforeEvent,"background discovery removal is marshalled to UI without an extra serial owner or I/O");
            }
            SingleNodeRgbChecks(args[0]);
            LiveDiscoveryChecks(args[0]);
            PauseRaceChecks();
            QueueChecks(args[0]);
            FuseOverlapChecks();
            PulseDedupeChecks();
            SharedSessionChecks(args[0]);
            Console.WriteLine("PASS hardware-room checks="+checks+" (fake USB service, no physical hardware)");return 0;
        } catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static void SingleNodeRgbChecks(string path)
    {
        var service=new Fake {Inactive=true,NoNeighbors=true,ApplyRoomWrites=true};var confirmations=new List<string>();
        using(var view=CreateView(service,(message,caption)=> {confirmations.Add(caption+"\n"+message);return true;})) {
            view.Size=new Size(1000,860);view.CreateControl();view.RefreshAll().GetAwaiter().GetResult();
            Check(!Button(view,"HardwareRgbLocalPulse").Enabled && Button(view,"HardwareRgbEnable").Enabled,"idle single board must explicitly permit RGB before any local execution");
            Click(view,"HardwareRgbEnable");
            Check(service.Writes==1 && service.RgbWrites==0 && service.Inactive,"RGB enable changes only permission, never emits RGB or creates a room");
            Check(LogText(view).Contains("已读回本机 RGB 执行许可：允许") && LogText(view).Contains("启用不点灯"),"permission success is logged only after true status readback and gives a distinct next step");
            Check(Button(view,"HardwareRgbLocalPulse").Enabled && Button(view,"HardwareRgbLocalPulse").Text=="创建单机 Room 并双闪","one identified idle board has a clearly labelled explicit local test action");
            ((TextBox)view.Controls.Find("HardwareRoomName",true).Single()).Text="UTSolo-RGB";
            Click(view,"HardwareRgbLocalPulse");
            Check(service.RgbWrites==1 && service.Writes==3 && !service.Inactive && service.Single,"confirmed standalone flow creates one real room and emits exactly one RGB action, with no hidden leave");
            Check(service.LastTarget==service.LocalNodeId && ((ListBox)view.Controls.Find("RoomMembers",true).Single()).Items.Count==1,"standalone action targets only the identified local MAC and shows one real member");
            Check(((ListBox)view.Controls.Find("RoomMembers",true).Single()).SelectedItem==null,"local test does not require or fabricate a selected member");
            Check(((ComboBox)view.Controls.Find("HardwareInvitePeers",true).Single()).Items.Count==0 && ((ListBox)view.Controls.Find("NearbyRooms",true).Single()).Items.Count==0,"zero-neighbor single-board test adds no fake peer or remote room");
            Check(confirmations.Count==2 && confirmations[1].Contains("COM-FAKE") && confirmations[1].Contains(service.LocalNodeId)
                && confirmations[1].Contains("UTSolo-RGB") && confirmations[1].Contains("保留 Room，不自动退出"),"standalone confirmation names the COM MAC real discoverable room and retained-room consequence");
            Check(LogText(view).Contains("终态已读回") && LogText(view).Contains("非光学验证"),"standalone action completes same-ID driver receipt tracking without claiming optical feedback");
            Check(!(bool)StateField("_automatic").GetValue(view),"finite RGB tracking does not start continuous polling in an explicitly manual page");
            Render(view,path+".single-done.png");
        }
        service=new Fake {Inactive=true,NoNeighbors=true,RgbEnabled=true,ApplyRoomWrites=true};
        using(var view=CreateView(service,(_,__)=>false)) {
            view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbLocalPulse");
            Check(service.Writes==0 && service.RgbWrites==0 && service.Inactive,"rejecting standalone confirmation performs zero room or GPIO writes");
        }
        service=new Fake {Inactive=true,NoNeighbors=true,RgbEnabled=true};
        using(var view=CreateView(service,(_,__)=> {service.LocalNodeId="200000000002";service.RaiseArrived();return true;})) {
            view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbLocalPulse");
            Check(service.Writes==0 && AllBusinessDisabled(view),"identity change during confirmation cancels standalone creation and RGB without writes");
        }
        service=new Fake {Inactive=true,IgnorePermissionWrite=true};
        using(var view=CreateView(service,(_,__)=>true)) {
            view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbEnable");
            Check(service.Writes==1 && service.RgbWrites==0 && !Button(view,"HardwareRgbLocalPulse").Enabled,"OK permission reply without actual permission cannot unlock local execution");
            Check(LogText(view).Contains("未读回允许状态") && !LogText(view).Contains("已读回本机 RGB 执行许可：允许"),"enable does not report success before permission status matches");
        }
        service=new Fake {Single=true,RgbEnabled=true,IgnorePermissionWrite=true};
        using(var view=CreateView(service,(_,__)=>true)) {
            view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbDisable");
            Check(service.RgbEnabled && LogText(view).Contains("仍报告执行许可开启") && !LogText(view).Contains("已读回本机 RGB 执行许可：关闭"),"disable acknowledgement is not confused with actual permission removal");
        }
        service=new Fake {Single=true,NoNeighbors=true,RgbEnabled=true};
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();int beforeRoom=service.RoomReads;
            Check(Button(view,"HardwareRgbLocalPulse").Enabled && !Button(view,"HardwareRgbPulse").Enabled,"active one-member room supports local action without member selection while selected-target action stays guarded");
            Click(view,"HardwareRgbLocalPulse");
            Check(service.Writes==1 && service.RgbWrites==1 && service.RoomReads==beforeRoom+3,"existing local test adds no CREATE and only minimal preflight plus bounded same-context ROOMGET00 reads");
            Check(Button(view,"HardwareRgbLocalPulse").Enabled && Button(view,"HardwareRgbDisable").Enabled,"terminal local driver result releases pending gate for further explicit user actions");
            service.Waiting=true;view.RefreshAll().GetAwaiter().GetResult();Check(!Button(view,"HardwareRgbLocalPulse").Enabled,"local operation cannot bypass waiting membership state even with permission");
            service.Waiting=false;service.Retiring=true;view.RefreshAll().GetAwaiter().GetResult();Check(!Button(view,"HardwareRgbLocalPulse").Enabled,"local operation cannot bypass retirement announcement");
            service.Retiring=false;service.RgbFault=true;view.RefreshAll().GetAwaiter().GetResult();Check(!Button(view,"HardwareRgbLocalPulse").Enabled,"latched fault blocks local execution");
        }
        service=new Fake {Single=true,RgbEnabled=true};
        foreach(int phase in new[]{0,1,2})service.ReceiptResponses.Enqueue("TRILINK/3 RGBRESULT n 00000001 "+phase);
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();int before=service.RgbReads;Click(view,"HardwareRgbLocalPulse");
            Check(service.RgbWrites==1 && service.RgbReads==before+4,"one pending preflight and progress 0/1/2 use same-ID tracking with no duplicate RGB write");
            Check(LogText(view).Contains("等待应用回执") && LogText(view).Contains("执行器已受理") && LogText(view).Contains("驱动执行完成（非光学验证）"),"all RGB progress stages retain distinct truthful meanings");
        }
        foreach(int phase in new[]{3,4,5}) {
            service=new Fake {Single=true,RgbEnabled=true};service.ReceiptResponses.Enqueue("TRILINK/3 RGBRESULT n 00000001 "+phase);
            using(var view=CreateView(service)) {
                view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbLocalPulse");
                var expected=new[]{"驱动失败","目标未启用或忙","超时，结果未知"}[phase-3];
                Check(service.RgbWrites==1 && LogText(view).Contains(expected) && !LogText(view).Contains("驱动执行完成"),"terminal phase "+phase+" is not successful illumination and is never retried automatically");
            }
        }
        foreach(var reply in new[]{"TRILINK/3 RGBRESULT n 00000002 2","TRILINK/3 RGBRESULT n WRONG 2","TRILINK/3 RGBRESULT n 00000001 6"}) {
            service=new Fake {Single=true,RgbEnabled=true};service.ReceiptResponses.Enqueue(reply);
            using(var view=CreateView(service)) {
                view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbLocalPulse");
                Check(service.RgbWrites==1 && AllBusinessDisabled(view) && LogText(view).Contains("未知"),"mismatched or invalid result is unknown with no replay: "+reply);
            }
        }
        service=new Fake {Single=true,RgbEnabled=true,ChangedRoomAfterReceipt=true};
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbLocalPulse");
            Check(service.RgbWrites==1 && AllBusinessDisabled(view) && !LogText(view).Contains("终态已读回"),"room incarnation change before terminal context check cannot publish a stale success");
        }
        foreach(bool contextHang in new[]{false,true}) {
            service=new Fake {Single=true,RgbEnabled=true,BlockReceipt=!contextHang,BlockContext=contextHang};
            using(var view=CreateView(service)) {
                view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbLocalPulse",false);
                Check(Button(view,"HardwareRgbLocalPulse").Enabled && Button(view,"HardwareRgbDisable").Enabled && Button(view,"HardwareMonitor").Enabled,"in-flight RGB allows deduplicated future intents and remains explicitly pausable");
                Click(view,"HardwareMonitor",false);WaitIdle(view);int calls=service.Calls;
                if(contextHang)service.ContextPending.SetResult("TRILINK/3 ROOMSTATE n 00 "+RoomWire.Hex(Snapshot()));else service.ReceiptPending.SetResult("TRILINK/3 RGBRESULT n 00000001 2");
                Application.DoEvents();Check(service.Calls==calls && service.RgbWrites==1 && AllBusinessDisabled(view) && !LogText(view).Contains("终态已读回"),"pause stops later reads and ignores late receipt/context result without replay");
            }
        }
        service=new Fake {Single=true,RgbEnabled=true,BlockContext=true};var disposed=CreateView(service);
        disposed.RefreshAll().GetAwaiter().GetResult();Click(disposed,"HardwareRgbLocalPulse",false);int beforeDispose=service.Calls;
        disposed.Dispose();Application.DoEvents();service.ContextPending.SetException(new IOException("late fixture context failure"));Application.DoEvents();
        Check(service.Calls==beforeDispose && service.RgbWrites==1 && service.Subscriptions==0,"closing during terminal context check stops later queries and observes late read fault without UI resurrection");
        foreach(bool contextHang in new[]{false,true}) {
            service=new Fake {Single=true,RgbEnabled=true,BlockReceipt=!contextHang,BlockContext=contextHang};
            using(var view=CreateView(service)) {
                view.RefreshAll().GetAwaiter().GetResult();var elapsed=System.Diagnostics.Stopwatch.StartNew();Click(view,"HardwareRgbLocalPulse");
                Check(elapsed.ElapsedMilliseconds>=11500 && elapsed.ElapsedMilliseconds<13500 && AllBusinessDisabled(view) && service.RgbWrites==1,"12-second deadline bounds stalled receipt or terminal-context read without a second RGB write");
                int calls=service.Calls;if(contextHang)service.ContextPending.SetResult("TRILINK/3 ROOMSTATE n 00 "+RoomWire.Hex(Snapshot()));else service.ReceiptPending.SetResult("TRILINK/3 RGBRESULT n 00000001 2");
                Application.DoEvents();Check(service.Calls==calls && AllBusinessDisabled(view) && !LogText(view).Contains("终态已读回"),"timed-out owner read can finish late but cannot publish stale terminal UI state");
            }
        }
    }
    private static void LiveDiscoveryChecks(string path)
    {
        var service=new Fake {Inactive=true};
        using(var publicView=new HardwareRoomView(service,service)) {
            Check(service.Calls==0,"public live constructor performs zero immediate device I/O");
            Check((bool)StateField("_autoLive").GetValue(publicView),"public hardware page opts into visible recognized-device read-only live refresh");
        }
        service=new Fake {Inactive=true,NoDevices=true};
        using(var view=CreateLiveView(service)) {
            Check(service.Calls==0,"configured live page still performs no constructor I/O");
            view.Size=new Size(1000,860);view.CreateControl();var handle=view.Handle;Application.DoEvents();
            Check(service.Calls==0,"visible live page with no recognized native device never opens a serial owner");
            service.NoDevices=false;service.RaiseArrived();Application.DoEvents();WaitIdle(view);
            Check(service.Calls==22 && service.Searches==1 && service.Writes==0,"first identified device arrival starts one read-only full refresh without Room or RGB writes");
            var peers=(ComboBox)view.Controls.Find("HardwareInvitePeers",true).Single();
            Check(peers.Enabled && peers.Items.Count==1 && !Button(view,"HardwareInvite").Enabled,"idle node may inspect discovered peers without pretending it joined a Room");
            Check(view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("RSSI -51 dBm") && view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("能力未验证"),"discovery status exposes MAC RSSI and unverified capability without claiming a logical connection");
            Render(view,path+".live-neighbor.png");
            Click(view,"HardwareMonitor");int calls=service.Calls;
            service.RaiseArrived();view.Visible=false;view.Visible=true;Application.DoEvents();
            Check(service.Calls==calls,"arrival and visibility changes cannot resume an explicitly paused live page");
            Click(view,"HardwareMonitor");Check(service.Calls==calls+22 && service.Writes==0,"manual resume explicitly starts another read-only refresh");
            service.Inactive=false;service.LocalNodeId="200000000002";service.RaiseArrived();Application.DoEvents();
            Check(service.Calls==calls+22 && !Button(view,"HardwareCreate").Enabled,"same-port identity replacement pauses live reads until user explicitly resumes");
            Click(view,"HardwareMonitor");
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var run=typeof(HardwareRoomView).GetMethod("Run",flags);int beforeFault=service.Calls;
            Func<Task> failure=()=>Task.FromException(new IOException("fixture live disconnected"));
            for(int i=0;i<5;++i)((Task)run.Invoke(view,new object[]{failure})).GetAwaiter().GetResult();
            service.RaiseArrived();view.Visible=false;view.Visible=true;Application.DoEvents();
            Check(service.Calls==beforeFault && Button(view,"HardwareMonitor").Text=="手动重启状态轮询","five-failure live fuse cannot be bypassed by discovery or visibility events");
            Render(view,path+".live-paused.png");
        }
    }
    private static void PauseRaceChecks()
    {
        foreach(bool automatic in new[]{false,true}) {
            var service=new Fake {Single=true,RgbEnabled=true,BlockReceipt=true};
            using(var view=CreateView(service)) {
                view.RefreshAll().GetAwaiter().GetResult();var context=System.Threading.SynchronizationContext.Current;
                System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                try {
                    StateField("_automatic").SetValue(view,automatic);Click(view,"HardwareRgbLocalPulse",false);
                    Check(StateField("_executingIntent").GetValue(view)!=null,"pause-race starts with an active submitted action");
                    Click(view,"HardwareMonitor",false);WaitIdle(view);
                    Check(AllBusinessDisabled(view) && LogText(view).Contains("不能撤销"),"inline cancellation clears pump action without dereferencing a null field");
                    Check((int)StateField("_failures").GetValue(view)==0,"inline cancellation does not masquerade as transport failure");
                    int calls=service.Calls;service.ReceiptPending.SetException(new IOException("fixture late paused read"));Application.DoEvents();
                    Check(service.Calls==calls && service.RgbWrites==1,"late read fault after inline cancellation is observed with no replay or extra I/O");
                } finally {System.Threading.SynchronizationContext.SetSynchronizationContext(context);}
            }
        }
    }
    private static void QueueChecks(string path)
    {
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var service=new Fake {Requests=true,RgbEnabled=true};
        using(var view=CreateView(service)) {
            view.Size=new Size(1000,860);view.CreateControl();view.RefreshAll().GetAwaiter().GetResult();
            service.GateReadAt=service.Calls+1;service.GateWriteAt=1;int begin=service.CommandLog.Count;
            StateField("_failures").SetValue(view,3);Click(view,"HardwareRefresh",false);
            var requests=(ListBox)view.Controls.Find("RoomRequests",true).Single();requests.SelectedIndex=0;
            Check(Button(view,"HardwareApprove").Enabled,"background refresh does not disable a valid approval intent");
            Click(view,"HardwareApprove",false);Click(view,"HardwareApprove",false);
            var members=(ListBox)view.Controls.Find("RoomMembers",true).Single();members.SelectedIndex=1;Click(view,"HardwareKick",false);
            var peers=(ComboBox)view.Controls.Find("HardwareInvitePeers",true).Single();peers.SelectedIndex=0;Click(view,"HardwareInvite",false);
            requests.SelectedIndex=1;members.SelectedIndex=0;
            Check(QueueDepth(view)==3 && LogText(view).Contains("未重复提交"),"foreground FIFO captures distinct intents and deduplicates repeated approval");
            Check(view.Controls.Find("HardwareCommandQueue",true).Single().Text.Contains("待执行 3/8"),"queue depth and current work are visible to the user");
            service.RoomRevision=3;ReleaseRead(service);
            Check(service.Writes==1 && (int)StateField("_failures").GetValue(view)==3,"aborted background read yields to first write without changing the failure budget");
            Check(service.CommandLog.Skip(begin).TakeWhile(s=>!s.StartsWith("ROOM ",StringComparison.Ordinal)).Count()==4,"foreground preflight begins immediately after the current read boundary instead of the old 20-page batch");
            Check(new RoomWire(service.RoomWrites[0]).Revision==3 && service.WriteLog[0]=="ROOM 17 500000000001","same-incarnation revision advance rebases the command but preserves captured applicant");
            Render(view,path+".queue-waiting.png");ReleaseWrite(service);WaitIdle(view);
            Check(service.WriteLog.SequenceEqual(new[]{"ROOM 17 500000000001","ROOM 19 200000000002","ROOM 4 300000000003"}),"captured targets execute in FIFO order despite later UI selection changes");
            Check(view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("revision=3"),"late old background snapshot cannot overwrite foreground authoritative state");
        }
        service=new Fake {Inactive=true};
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();service.GateReadAt=service.Calls+1;Click(view,"HardwareRefresh",false);
            var name=(TextBox)view.Controls.Find("HardwareRoomName",true).Single();name.Text="Captured";Click(view,"HardwareCreate",false);name.Text="Changed";
            ReleaseRead(service);WaitIdle(view);
            Check(service.RoomWrites.Count==1 && new RoomWire(service.RoomWrites[0]).Name=="Captured","queued room creation uses enqueue-time text, never a later edited name");
        }
        service=new Fake {Inactive=true};
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();service.GateReadAt=service.Calls+1;Click(view,"HardwareRefresh",false);
            var name=(TextBox)view.Controls.Find("HardwareRoomName",true).Single();
            for(int i=0;i<9;++i){name.Text="Q"+i;Click(view,"HardwareCreate",false);}
            Check(QueueDepth(view)==8 && LogText(view).Contains("队列已满"),"bounded queue rejects the ninth waiting intent explicitly without dropping earlier entries");
            Click(view,"HardwareClearQueue",false);Check(QueueDepth(view)==0 && service.Writes==0,"clear button cancels all not-yet-submitted intents without any device write");
            ReleaseRead(service);WaitIdle(view);Check(service.Writes==0,"cleared queued writes are not revived by late background completion");
        }
        foreach(int change in new[]{0,1,2,3}) {
            service=new Fake {Requests=true};
            using(var view=CreateView(service)) {
                view.RefreshAll().GetAwaiter().GetResult();service.GateReadAt=service.Calls+1;Click(view,"HardwareRefresh",false);
                ((ListBox)view.Controls.Find("RoomRequests",true).Single()).SelectedIndex=0;Click(view,"HardwareApprove",false);
                if(change==0)service.RequestTicket=9;else if(change==1)service.ApplicantBoot=999;else if(change==2)service.RoomIncarnation=102;else service.LocalNodeId="200000000002";
                if(change==3)service.RaiseArrived();ReleaseRead(service);WaitIdle(view);
                Check(service.Writes==0 && (int)StateField("_failures").GetValue(view)==0,"changed ticket boot incarnation or device cancels captured approval without transport-fuse penalty: "+change);
            }
        }
        service=new Fake();
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();service.GateReadAt=service.Calls+1;Click(view,"HardwareRefresh",false);
            ((ListBox)view.Controls.Find("RoomMembers",true).Single()).SelectedIndex=1;Click(view,"HardwareKick",false);service.RemoteMemberBoot=303;
            ReleaseRead(service);WaitIdle(view);Check(service.Writes==0 && LogText(view).Contains("启动身份"),"same-MAC rebooted target is not kicked under an old captured intent");
        }
        service=new Fake {LocalNodeId="200000000002"};
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();service.GateReadAt=service.Calls+1;Click(view,"HardwareRefresh",false);Click(view,"HardwareLeave",false);service.RoomTerm=2;
            ReleaseRead(service);WaitIdle(view);Check(service.Writes==0 && LogText(view).Contains("leader/term/角色"),"queued member leave is cancelled when handover semantics change");
        }
        foreach(bool pause in new[]{false,true}) {
            service=new Fake {Requests=true};
            using(var view=CreateView(service)) {
                view.RefreshAll().GetAwaiter().GetResult();service.GateWriteAt=1;
                ((ListBox)view.Controls.Find("RoomRequests",true).Single()).SelectedIndex=0;Click(view,"HardwareApprove",false);
                ((ListBox)view.Controls.Find("RoomMembers",true).Single()).SelectedIndex=1;Click(view,"HardwareKick",false);
                Check(service.Writes==1 && QueueDepth(view)==1,"invoked write remains foreground single-flight while later valid intent waits");
                Click(view,pause?"HardwareMonitor":"HardwareClearQueue",false);
                Check(service.Writes==1 && QueueDepth(view)==0 && view.Controls.Find("HardwareCommandQueue",true).Single().Text.Contains("已提交/可能在途"),"pause or clear cannot pretend to revoke a write already invoked at the shared owner");
                ReleaseWrite(service);WaitIdle(view);Check(service.Writes==1 && LogText(view).Contains("不能撤销"),"submitted write is awaited to completion, with no revoked queued write or replay dispatched");
            }
        }
        service=new Fake {Requests=true};
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();service.GateWriteAt=1;
            ((ListBox)view.Controls.Find("RoomRequests",true).Single()).SelectedIndex=0;Click(view,"HardwareApprove",false);
            ((ListBox)view.Controls.Find("RoomMembers",true).Single()).SelectedIndex=1;Click(view,"HardwareKick",false);
            service.WriteGate.SetException(new IOException("fixture unknown submitted write"));Application.DoEvents();WaitIdle(view);
            Check(service.Writes==1 && QueueDepth(view)==0 && AllBusinessDisabled(view) && LogText(view).Contains("结果未知"),"unknown write result clears linked later intents and is never replayed automatically");
        }
        service=new Fake {Inactive=true};
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();service.GateReadAt=service.Calls+1;Click(view,"HardwareRefresh",false);Click(view,"HardwareCreate",false);
            var queue=StateField("_pendingCommands").GetValue(view);var item=queue.GetType().GetMethod("Peek").Invoke(queue,null);
            item.GetType().GetField("ExpiresAt").SetValue(item,0L);ReleaseRead(service);WaitIdle(view);
            Check(service.Writes==0 && LogText(view).Contains("有效期限"),"expired waiting intent is cancelled rather than silently executing long after user consent");
        }
        service=new Fake {Requests=true};var closed=CreateView(service);closed.RefreshAll().GetAwaiter().GetResult();service.GateReadAt=service.Calls+1;Click(closed,"HardwareRefresh",false);
        ((ListBox)closed.Controls.Find("RoomRequests",true).Single()).SelectedIndex=0;Click(closed,"HardwareApprove",false);closed.Dispose();ReleaseRead(service);
        Check(service.Writes==0 && service.Subscriptions==0,"closing queued view clears unsubmitted actions and late background reads do not write or resubscribe");
        foreach(bool createRoom in new[]{false,true}) {
            service=new Fake {Single=true,Inactive=createRoom,RgbEnabled=true,ApplyRoomWrites=true,NoNeighbors=true,BlockReceipt=true};
            using(var view=CreateView(service,(_,__)=>true)) {
                view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbLocalPulse",false);
                if(!createRoom)Click(view,"HardwareRefresh",false);
                int calls=service.Calls;Click(view,"HardwareMonitor",false);WaitIdle(view);
                Check(!(bool)StateField("_refreshRequested").GetValue(view)
                    && !(bool)StateField("_refreshFullRequested").GetValue(view),"manual pause clears both coalesced refresh flags before synchronous cancellation finishes pump");
                service.ReceiptPending.SetResult("TRILINK/3 RGBRESULT n 00000001 2");Application.DoEvents();
                Check(service.Calls==calls && AllBusinessDisabled(view) && service.RgbWrites==1,"paused standalone create or pending manual refresh cannot revive UI or issue background queries after cancellation");
            }
        }
        service=new Fake();
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();int beforeSearch=service.Searches;
            Check(((Timer)StateField("_timer").GetValue(view)).Interval==2000,"active leader reads pending requests on a two-second merged cadence");
            StateField("_lastPeerRefresh").SetValue(view,0L);service.PeerRssi=-72;service.GateSearch=true;
            var request=typeof(HardwareRoomView).GetMethod("RequestRefresh",flags);request.Invoke(view,new object[]{false});
            Check(Button(view,"HardwareInvite").Enabled,"slow active-room discovery does not block a valid foreground intention");
            Click(view,"HardwareInvite",false);Check(QueueDepth(view)==1 && service.Writes==0,"foreground submits immediately while the current slow search remains safely owned");
            service.SearchGate.SetResult(service.CapturedPeers);Application.DoEvents();WaitIdle(view);
            Check(service.Writes==1 && service.Searches==beforeSearch+3 && view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("RSSI -72 dBm"),"foreground gets the next boundary and merged low-frequency search eventually updates active-room RSSI");
            int searches=service.Searches;request.Invoke(view,new object[]{false});WaitIdle(view);
            Check(service.Searches==searches,"active-room light refresh does not repeat SEARCH within its 15-second cadence");
            service.LocalNodeId="200000000002";service.RaiseArrived();view.RefreshAll().GetAwaiter().GetResult();
            Check(((Timer)StateField("_timer").GetValue(view)).Interval==5000,"ordinary-member page retains five-second cadence instead of restarting the leader timer on every render");
        }
        service=new Fake {RgbEnabled=true,BlockReceipt=true};
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbLocalPulse",false);
            ((ListBox)view.Controls.Find("RoomMembers",true).Single()).SelectedIndex=1;Click(view,"HardwareRgbPulse",false);Click(view,"HardwareRgbDisable",false);Click(view,"HardwareLeave",false);
            Check(QueueDepth(view)==3,"different target RGB permission and room intentions may wait behind one explicit RGB action");
            service.ReceiptPending.SetResult("TRILINK/3 RGBRESULT n 00000001 5");Application.DoEvents();WaitIdle(view);
            Check(service.Writes==1 && service.RgbWrites==1 && QueueDepth(view)==0 && AllBusinessDisabled(view),"valid phase-five unknown receipt cancels all related queued writes instead of continuing FIFO mutation");
            Check((int)StateField("_failures").GetValue(view)==0 && LogText(view).Contains("应用执行结果未知"),"application unknown is visible but does not masquerade as a USB transport failure");
        }
        service=new Fake();
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();service.GateReadAt=service.Calls+1;Click(view,"HardwareRefresh",false);int calls=service.Calls;bool blocked=false;
            try {view.RefreshAll().GetAwaiter().GetResult();}catch(InvalidOperationException){blocked=true;}
            Check(blocked && service.Calls==calls,"public direct refresh cannot bypass a running scheduler and create parallel batches");
            ReleaseRead(service);WaitIdle(view);
        }
    }
    private static void PulseDedupeChecks()
    {
        var service=new Fake {Single=true,Inactive=true,RgbEnabled=true,ApplyRoomWrites=true,NoNeighbors=true,BlockReceipt=true};
        using(var view=CreateView(service,(_,__)=>true)) {
            view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbLocalPulse",false);
            Check(service.RoomWrites.Count==1 && service.RgbWrites==1 && Button(view,"HardwareRgbLocalPulse").Text=="本机青色双闪",
                "standalone explicit create transitions to one active-room local RGB action");
            Click(view,"HardwareRgbLocalPulse",false);
            ((ListBox)view.Controls.Find("RoomMembers",true).Single()).SelectedIndex=0;Click(view,"HardwareRgbPulse",false);
            Check(QueueDepth(view)==0 && service.RgbWrites==1 && LogText(view).Contains("未重复提交"),
                "same physical local target stays deduplicated across CREATE and both RGB button paths");
            service.BlockReceipt=false;service.ReceiptPending.SetResult("TRILINK/3 RGBRESULT n 00000001 2");Application.DoEvents();WaitIdle(view);
            Check(service.RoomWrites.Count==1 && service.RgbWrites==1 && QueueDepth(view)==0,
                "CREATE-to-active repeated clicks finish with exactly one RGB write, not a second queued pulse");
            Click(view,"HardwareRgbLocalPulse");
            Check(service.RoomWrites.Count==1 && service.RgbWrites==2,
                "completed physical-target dedupe retires so a later explicit local pulse remains available");
        }
        service=new Fake {RgbEnabled=true,BlockReceipt=true};
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();Click(view,"HardwareRgbLocalPulse",false);
            ((ListBox)view.Controls.Find("RoomMembers",true).Single()).SelectedIndex=1;Click(view,"HardwareRgbPulse",false);Click(view,"HardwareRgbPulse",false);Click(view,"HardwareRgbDisable",false);
            Check(QueueDepth(view)==2,"physical-target dedupe permits one different target and separate permission revocation while rejecting its repeated pulse");
            service.BlockReceipt=false;service.ReceiptPending.SetResult("TRILINK/3 RGBRESULT n 00000001 2");Application.DoEvents();WaitIdle(view);
            Check(service.WriteLog.SequenceEqual(new[]{"RGB 100000000001","RGB 200000000002","RGBENABLE 0"}),
                "different-target RGB and permission actions preserve FIFO after target-based deduplication");
        }
        service=new Fake {RgbEnabled=true};
        using(var view=CreateView(service)) {
            view.RefreshAll().GetAwaiter().GetResult();service.GateReadAt=service.Calls+1;Click(view,"HardwareRefresh",false);
            ((ListBox)view.Controls.Find("RoomMembers",true).Single()).SelectedIndex=1;Click(view,"HardwareRgbPulse",false);service.RemoteMemberBoot=303;
            ReleaseRead(service);WaitIdle(view);
            Check(service.RgbWrites==0 && LogText(view).Contains("启动身份"),
                "physical-target dedupe never relaxes captured member-boot validation before RGB dispatch");
        }
    }
    private static void FuseOverlapChecks()
    {
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        foreach(bool fullPending in new[]{false,true}) {
            var service=new Fake();
            using(var view=CreateView(service)) {
                view.CreateControl();var handle=view.Handle;Click(view,"HardwareMonitor");
                var timer=(Timer)StateField("_timer").GetValue(view);
                var tick=typeof(Timer).GetMethod("OnTick",flags);
                service.Delay=true;service.Pending=new TaskCompletionSource<string>();
                int before=service.Calls;tick.Invoke(timer,new object[]{EventArgs.Empty});
                for(int failure=1;failure<=5;++failure) {
                    var pending=service.Pending;
                    tick.Invoke(timer,new object[]{EventArgs.Empty});
                    if(fullPending && failure==5)Click(view,"HardwareRefresh",false);
                    Check(service.Calls==before+failure,"timer overlap coalesces without concurrent I/O before failure "+failure+" / full="+fullPending);
                    service.Pending=new TaskCompletionSource<string>();
                    if(failure==5)service.Delay=false;
                    pending.SetException(new IOException("fixture delayed background failure "+failure));Application.DoEvents();
                }
                WaitIdle(view);
                Check(service.Calls==before+5,"fifth-failure fuse discards pending timer/full refresh instead of starting a healthy sixth batch / full="+fullPending);
                Check((int)StateField("_failures").GetValue(view)==5
                    && !(bool)StateField("_automatic").GetValue(view) && !timer.Enabled,
                    "fifth delayed failure retains fuse count and stops timer / full="+fullPending);
                Check(!(bool)StateField("_refreshRequested").GetValue(view)
                    && !(bool)StateField("_refreshFullRequested").GetValue(view),
                    "fuse clears both coalesced refresh flags / full="+fullPending);
                Check(AllBusinessDisabled(view) && StateField("_state").GetValue(view)==null,
                    "healthy late availability cannot revive invalid state without explicit recovery / full="+fullPending);
                int stopped=service.Calls;tick.Invoke(timer,new object[]{EventArgs.Empty});service.RaiseArrived();view.Visible=false;view.Visible=true;Application.DoEvents();
                Check(service.Calls==stopped && Button(view,"HardwareMonitor").Text=="手动重启状态轮询",
                    "post-fuse ticks arrival and visibility cannot resume polling / full="+fullPending);
                Click(view,"HardwareMonitor");
                Check(service.Calls==stopped+22 && (int)StateField("_failures").GetValue(view)==0
                    && (bool)StateField("_automatic").GetValue(view) && Button(view,"HardwareLeave").Enabled,
                    "explicit manual restart resets fuse and performs exactly one healthy full refresh / full="+fullPending);
                Check(service.Writes==0,"fuse and explicit read-only restart never mutate Room or RGB / full="+fullPending);
            }
        }
    }
    private static HardwareRoomView SharedView(NodeSessionService coordinator,Fake io,bool autoLive=false)
    {return (HardwareRoomView)Activator.CreateInstance(typeof(HardwareRoomView),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,
        null,new object[]{coordinator,io,(Func<string,string,bool>)((_,__)=>true),autoLive,false},System.Globalization.CultureInfo.InvariantCulture);}
    private static void WaitTask(Task task)
    {var elapsed=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted){if(elapsed.ElapsedMilliseconds>2000)throw new TimeoutException("shared fixture never settled");Application.DoEvents();System.Threading.Thread.Sleep(2);}Application.DoEvents();}
    private static void SharedSessionChecks(string path)
    {
        var transportIo=new Fake {Endpoint="NCM:00000000-0000-0000-0000-000000000001",TransportKind=NodeTransportKind.UsbNcm};
        using(var coordinator=new NodeSessionService(transportIo,transportIo))
        using(var page=SharedView(coordinator,transportIo))
        using(var inspect=coordinator.Bind(transportIo.LocalNodeId,transportIo.Endpoint)) {
            page.Size=new Size(1000,860);page.CreateControl();page.RefreshAll().GetAwaiter().GetResult();
            var descriptor=inspect.Snapshot.Device;
            Check(descriptor.TransportKind==NodeTransportKind.UsbNcm && descriptor.ManagementUrl=="http://192.168.240.1/",
                "shared snapshot retains validated transport metadata without turning the endpoint into a serial port or URL");
            Check(((ComboBox)page.Controls.Find("HardwarePorts",true).Single()).SelectedItem.Equals(transportIo.Endpoint)
                && page.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("NCM"),
                "NCM display label leaves the raw machine endpoint intact");
            descriptor.TransportKind=NodeTransportKind.UsbSerial;descriptor.ManagementUrl="http://invalid/";
            Check(inspect.Snapshot.Device.TransportKind==NodeTransportKind.UsbNcm && inspect.Snapshot.Device.ManagementUrl=="http://192.168.240.1/",
                "editing copied transport metadata cannot redirect shared authority");
            Render(page,path+".ncm.png");
            transportIo.GateReadAt=transportIo.Calls+1;var waiter=inspect.RefreshAsync();
            transportIo.TransportKind=NodeTransportKind.UsbSerial;transportIo.RaiseArrived();
            Check(waiter.IsCanceled && inspect.Snapshot.Room==null && inspect.Snapshot.PollingState==NodePollingState.DeviceChanged,
                "transport-kind replacement invalidates an in-flight session even with the same MAC and endpoint text");
            ReleaseRead(transportIo);Check(transportIo.Writes==0,"late old-transport read never authorizes a replacement-transport write");
        }
        var io=new Fake {Requests=true};
        using(var coordinator=new NodeSessionService(io,io))
        using(var a=SharedView(coordinator,io))
        using(var b=SharedView(coordinator,io))
        using(var inspect=coordinator.Bind(io.LocalNodeId,"COM-FAKE")) {
            a.Size=b.Size=new Size(1000,860);a.CreateControl();b.CreateControl();
            Check(io.Calls==0 && io.Subscriptions==2,"two pages share one coordinator and one pair of discovery subscriptions without constructor I/O");
            a.RefreshAll().GetAwaiter().GetResult();
            Check(((ListBox)b.Controls.Find("RoomMembers",true).Single()).Items.Count==2 && Button(b,"HardwareLeave").Enabled,
                "a single page refresh publishes the same typed state and permissions to its other page");
            var copy=inspect.Snapshot;copy.Device.PortName="MUTATED";copy.Room.EncodedRecord[18]=99;copy.Room=null;
            Check(inspect.Snapshot.Device.PortName=="COM-FAKE" && inspect.Snapshot.Room.RoomId==1,"consumer snapshot and wire copy mutation cannot change service authority");
            bool wrongThread=false;try{Task.Run(()=>inspect.Snapshot).GetAwaiter().GetResult();}catch(InvalidOperationException){wrongThread=true;}
            Check(wrongThread,"snapshot reads enforce the same serialized creator-context contract as operations");
            var extra=new List<INodeSessionSubscription>();for(int i=3;i<NodeSessionService.MaxSubscriptions;i++)extra.Add(coordinator.Bind(io.LocalNodeId,"COM-FAKE"));
            bool full=false;try{coordinator.Bind(io.LocalNodeId,"COM-FAKE");}catch(InvalidOperationException){full=true;}
            foreach(var lease in extra)lease.Dispose();
            Check(full && io.Calls==22,"shared subscriptions have a hard bound of sixteen and rejected subscriptions start no work");
            io.SecondDevice=true;bool refused=false;try{coordinator.Bind("300000000003","COM-OTHER");}catch(InvalidOperationException){refused=true;}
            Check(refused && inspect.Snapshot.Device.PortName=="COM-FAKE","a second page cannot silently redirect an existing node binding to another COM target");
            io.SecondDevice=false;io.GateReadAt=io.Calls+1;int start=io.Calls;
            var refresh=inspect.RefreshAsync();bool same=true;
            for(int i=0;i<1000;i++)same&=ReferenceEquals(refresh,inspect.RefreshAsync());
            Check(same && io.Calls==start+1,"one thousand refresh callers share one bounded pending task and one in-flight read");
            ReleaseRead(io);WaitTask(refresh);refresh.GetAwaiter().GetResult();
            Check(io.Calls==start+22,"merged cross-page refresh performs one full batch rather than duplicating every request");
            coordinator.ResumePolling();coordinator.PausePolling();int paused=io.Calls;
            using(var reopened=SharedView(coordinator,io,true)) {
                reopened.CreateControl();var handle=reopened.Handle;io.RaiseArrived();a.Visible=false;a.Visible=true;Application.DoEvents();
                Check(io.Calls==paused && inspect.Snapshot.PollingState==NodePollingState.ManuallyPaused,
                    "manual global pause stays latched through another page opening discovery and visibility changes");
            }
            Check(Button(a,"HardwareMonitor").Text==Button(b,"HardwareMonitor").Text,"both page controls render the same global polling state");
            Click(b,"HardwareMonitor");Check(io.Calls==paused+22 && inspect.Snapshot.PollingState==NodePollingState.Running,
                "explicit recovery from either page starts exactly one shared refresh");
            Render(b,path+".shared-session.png");
            a.Dispose();Check(io.Subscriptions==2 && Button(b,"HardwareLeave").Enabled,"disposing one page only releases its lease not shared discovery or another page state");
        }
        Check(io.Subscriptions==0,"only final coordinator disposal releases its discovery subscriptions");

        io=new Fake {Requests=true};
        using(var coordinator=new NodeSessionService(io,io))
        using(var a=SharedView(coordinator,io))
        using(var b=SharedView(coordinator,io)) {
            a.RefreshAll().GetAwaiter().GetResult();io.GateReadAt=io.Calls+1;Click(a,"HardwareRefresh",false);
            ((ListBox)a.Controls.Find("RoomRequests",true).Single()).SelectedIndex=0;Click(a,"HardwareApprove",false);
            ((ListBox)b.Controls.Find("RoomRequests",true).Single()).SelectedIndex=0;Click(b,"HardwareApprove",false);
            Check(QueueDepth(a)==1 && QueueDepth(b)==1,"duplicate approval from two pages has one global FIFO item");
            a.Dispose();Check(QueueDepth(b)==1,"closing the original page preserves an unsubmitted intent still owned by the second page");
            ReleaseRead(io);WaitIdle(b);Check(io.Writes==1 && io.LastKind==17,"shared deduplicated approval executes once after its remaining owner survives");
        }
        io=new Fake {Requests=true};
        using(var coordinator=new NodeSessionService(io,io))
        using(var a=SharedView(coordinator,io))
        using(var b=SharedView(coordinator,io)) {
            a.RefreshAll().GetAwaiter().GetResult();io.GateReadAt=io.Calls+1;Click(a,"HardwareRefresh",false);
            ((ListBox)a.Controls.Find("RoomRequests",true).Single()).SelectedIndex=0;Click(a,"HardwareApprove",false);
            ((ListBox)b.Controls.Find("RoomMembers",true).Single()).SelectedIndex=1;Click(b,"HardwareKick",false);
            Check(QueueDepth(b)==2,"distinct page intents use the same bounded global FIFO");
            a.Dispose();Check(QueueDepth(b)==1,"page disposal removes only that page's unsubmitted intent");
            ReleaseRead(io);WaitIdle(b);Check(io.Writes==1 && io.LastKind==19,"another page's captured target survives page disposal without redirection");
        }
        io=new Fake {Single=true,RgbEnabled=true,BlockReceipt=true};
        using(var coordinator=new NodeSessionService(io,io))
        using(var a=SharedView(coordinator,io))
        using(var b=SharedView(coordinator,io)) {
            a.RefreshAll().GetAwaiter().GetResult();Click(a,"HardwareRgbLocalPulse",false);Click(b,"HardwareRgbLocalPulse",false);
            Check(io.RgbWrites==1 && QueueDepth(b)==0,"same physical RGB target is deduplicated across independent pages");
            int calls=io.Calls;a.Dispose();Application.DoEvents();
            Check(io.Calls==calls && io.Subscriptions==2 && StateField("_rgbTracking").GetValue(b).Equals(true),
                "closing the sending page leaves its submitted receipt task owned by the shared service");
            io.ReceiptPending.SetResult("TRILINK/3 RGBRESULT n 00000001 2");WaitIdle(b);
            Check(io.RgbWrites==1 && LogText(b).Contains("终态已读回") && Button(b,"HardwareRgbLocalPulse").Enabled,
                "remaining page receives the same operation completion with no replay or UI resurrection");
        }
        io=new Fake {Requests=true};
        using(var coordinator=new NodeSessionService(io,io))
        using(var a=SharedView(coordinator,io))
        using(var b=SharedView(coordinator,io))
        using(var inspect=coordinator.Bind(io.LocalNodeId,"COM-FAKE")) {
            a.RefreshAll().GetAwaiter().GetResult();io.GateWriteAt=1;((ListBox)a.Controls.Find("RoomRequests",true).Single()).SelectedIndex=0;Click(a,"HardwareApprove",false);
            var waiter=inspect.RefreshAsync();Check(!waiter.IsCompleted,"read request queued behind an invoked write remains pending without parallel I/O");
            io.WriteGate.SetException(new IOException("shared write outcome unknown"));WaitTask(waiter);WaitIdle(b);var observed=waiter.Exception;
            Check(waiter.IsFaulted && io.Writes==1 && AllBusinessDisabled(a) && AllBusinessDisabled(b),
                "unknown write clears linked refresh and settles its waiter as failed rather than leaving it pending forever");
        }
        io=new Fake();
        using(var coordinator=new NodeSessionService(io,io))
        using(var a=SharedView(coordinator,io))
        using(var inspect=coordinator.Bind(io.LocalNodeId,"COM-FAKE")) {
            a.RefreshAll().GetAwaiter().GetResult();io.GateReadAt=io.Calls+1;var waiter=inspect.RefreshAsync();int calls=io.Calls;
            coordinator.PausePolling();Check(waiter.IsCanceled,"global pause explicitly cancels the one pending shared refresh task");
            ReleaseRead(io);Check(io.Calls==calls && inspect.Snapshot.PollingState==NodePollingState.ManuallyPaused,"late paused read cannot restart batches or clear the global pause latch");
        }
        io=new Fake();
        using(var coordinator=new NodeSessionService(io,io))
        using(var a=SharedView(coordinator,io))
        using(var inspect=coordinator.Bind(io.LocalNodeId,"COM-FAKE")) {
            a.RefreshAll().GetAwaiter().GetResult();io.GateReadAt=io.Calls+1;var waiter=inspect.RefreshAsync();
            io.LocalNodeId="200000000002";io.RaiseArrived();Check(waiter.IsCanceled && inspect.Snapshot.Room==null,
                "same-port identity replacement cancels shared refresh and invalidates old node state");
            ReleaseRead(io);Check(io.Writes==0,"old node's delayed read cannot cause a write to the replacement device");
        }
        io=new Fake();var caps=new CapabilityFake(io);
        using(var coordinator=new NodeSessionService(io,io,caps))
        using(var inspect=coordinator.Bind(io.LocalNodeId,"COM-FAKE")) {
            inspect.RefreshAsync().GetAwaiter().GetResult();
            Check(caps.Calls==1 && inspect.Snapshot.Capabilities.FirmwareVersion=="0.15.0" && inspect.Snapshot.Capabilities.SupportsRgb,
                "node snapshot includes a real descriptor supplied by the capability adapter");
            inspect.RefreshAsync().GetAwaiter().GetResult();Check(caps.Calls==2,"every full or manual refresh re-reads CAPS rather than authorizing from a cached boot");
            typeof(NodeSessionService).GetMethod("RequestRefresh",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(coordinator,new object[]{false});
            Check(caps.Calls==2,"light periodic reads can reuse descriptive capability metadata");
            caps.Boot=202;var changed=inspect.RefreshAsync();WaitTask(changed);var observed=changed.Exception;
            Check(changed.IsFaulted && inspect.Snapshot.Room==null && io.Writes==0,"fresh CAPS boot mismatch invalidates the Room snapshot without executing an old intent");
        }
        io=new Fake {Inactive=true,IdleSourceZero=true};caps=new CapabilityFake(io);
        using(var coordinator=new NodeSessionService(io,io,caps))
        using(var inspect=coordinator.Bind(io.LocalNodeId,"COM-FAKE")) {
            inspect.RefreshAsync().GetAwaiter().GetResult();
            Check(!inspect.Snapshot.Room.Active && new RoomWire(inspect.Snapshot.Room.EncodedRecord).Number(10)==0 && inspect.Snapshot.Capabilities.BootId==101,
                "real empty Room source boot zero is not confused with the local CAPS boot");
            int generation=inspect.Snapshot.Generation;caps.Boot=202;var restarted=inspect.RefreshAsync();WaitTask(restarted);var observed=restarted.Exception;
            Check(restarted.IsFaulted && inspect.Snapshot.Room==null && inspect.Snapshot.Generation>generation && inspect.Snapshot.PollingState==NodePollingState.DeviceChanged,
                "same-MAC idle reboot detected by fresh CAPS invalidates the binding generation and pauses old intents");
        }
        io=new Fake {LocalNodeId="200000000002"};caps=new CapabilityFake(io) {Boot=202};
        using(var coordinator=new NodeSessionService(io,io,caps))
        using(var inspect=coordinator.Bind(io.LocalNodeId,"COM-FAKE")) {
            inspect.RefreshAsync().GetAwaiter().GetResult();var room=new RoomWire(inspect.Snapshot.Room.EncodedRecord);
            Check(room.Number(10)==101 && room.Number(56)==202 && inspect.Snapshot.Capabilities.BootId==202 && inspect.Snapshot.InvalidReason==null,
                "ordinary-member CAPS matches its own membership boot not the leader/source boot");
        }
        foreach(bool enable in new[]{false,true}) {
            io=new Fake {Inactive=true,IdleSourceZero=true};caps=new CapabilityFake(io);
            using(var coordinator=new NodeSessionService(io,io,caps))
            using(var page=SharedView(coordinator,io))
            using(var inspect=coordinator.Bind(io.LocalNodeId,"COM-FAKE")) {
                page.RefreshAll().GetAwaiter().GetResult();io.GateReadAt=io.Calls+1;Click(page,"HardwareRefresh",false);
                Click(page,enable?"HardwareRgbEnable":"HardwareCreate",false);
                Check(QueueDepth(page)==1,"known-boot idle intent captures its original node context before soft reboot / RGBENABLE="+enable);
                caps.Boot=202;ReleaseRead(io);WaitIdle(page);
                Check(io.Writes==0 && inspect.Snapshot.Room==null && inspect.Snapshot.PollingState==NodePollingState.DeviceChanged
                    && inspect.Snapshot.ConsecutiveFailures==0 && caps.Calls==2,
                    "queued idle action re-reads CAPS and never writes to same-MAC restarted board / RGBENABLE="+enable);
            }
        }
    }
    private sealed class CapabilityFake : INodeCapabilityService
    {
        private readonly Fake _io;public int Calls;public uint Boot=101;
        public CapabilityFake(Fake io){_io=io;}
        public Task<NodeCapabilities> DescribeAsync(string port)
        {++Calls;return Task.FromResult(new NodeCapabilities(_io.LocalNodeId,false,1,"0.15.0",Boot,132,6,6,NodeCapabilities.RgbModule));}
    }
    private sealed class Fake : IHardwareCommandService,IDeviceDiscoveryService
    {
        public string Endpoint="COM-FAKE";public NodeTransportKind TransportKind;
        public int Calls,Searches,Writes,Subscriptions,RgbReads,RoomReads,RgbWrites;public byte LastKind;public string LastTarget,LastCommand,LocalNodeId="100000000001",LastRgbId="00000000",RoomName="Lab";
        public bool Delay,BadResult,BadWriteReply,Uncertain,RgbFault,Retiring,Requests,Invitations,Unsynchronized,Full,Inactive,NoDevices,UnsupportedOnly,RgbEnabled,PendingRgb,
            Single,NoNeighbors,ApplyRoomWrites,IgnorePermissionWrite,BlockReceipt,BlockContext,ChangedRoomAfterReceipt,Waiting,SecondDevice,IdleSourceZero;
        public int CurrentRgbPhase;public readonly Queue<string> ReceiptResponses=new Queue<string>();
        public TaskCompletionSource<string> Pending=new TaskCompletionSource<string>();
        public TaskCompletionSource<string> ReceiptPending=new TaskCompletionSource<string>();
        public TaskCompletionSource<string> ContextPending=new TaskCompletionSource<string>();
        public int GateReadAt=-1,GateWriteAt=-1,RoomRevision=2,RoomIncarnation=101,RoomTerm=1,RequestTicket=2,ApplicantBoot=101,LocalMemberBoot=101,RemoteMemberBoot=202;
        public readonly List<string> CommandLog=new List<string>(),WriteLog=new List<string>();
        public readonly List<byte[]> RoomWrites=new List<byte[]>();
        public TaskCompletionSource<string> ReadGate=new TaskCompletionSource<string>(),WriteGate=new TaskCompletionSource<string>();
        public Task<string> CapturedRead,CapturedWrite;
        public bool GateSearch;public int PeerRssi=-51;public IReadOnlyList<TriLinkPeer> CapturedPeers;
        public TaskCompletionSource<IReadOnlyList<TriLinkPeer>> SearchGate=new TaskCompletionSource<IReadOnlyList<TriLinkPeer>>();
        public Task<string> ExecuteAsync(string port,string command,string arguments)
        {
            ++Calls;CommandLog.Add(command+" "+arguments);if(Delay)return Pending.Task;
            var result=ExecuteCore(command,arguments);
            bool write=command=="ROOM" || command=="RGB" || command=="RGBENABLE";
            if(!write && Calls==GateReadAt){CapturedRead=result;return ReadGate.Task;}
            if(write && Writes==GateWriteAt){CapturedWrite=result;return WriteGate.Task;}
            return result;
        }
        private static void Put(byte[] bytes,int offset,int value)
        {for(int i=0;i<4;++i)bytes[offset+i]=(byte)((uint)value>>(8*i));}
        private Task<string> ExecuteCore(string command,string arguments)
        {
            if(command=="RGBRESULT") {
                ++RgbReads;if(BadResult)return Task.FromResult("INVALID RGBRESULT n 00000000 0");
                if(LastRgbId!="00000000" && BlockReceipt)return ReceiptPending.Task;
                if(LastRgbId!="00000000" && ReceiptResponses.Count>0) {
                    string result=ReceiptResponses.Dequeue();var fields=result.Split(' ');int phase;
                    if(fields.Length==5 && fields[3]==LastRgbId && int.TryParse(fields[4],out phase)){CurrentRgbPhase=phase;PendingRgb=phase<=1;}
                    return Task.FromResult(result);
                }
                return Task.FromResult("TRILINK/3 RGBRESULT n "+LastRgbId+" "+(PendingRgb?1:CurrentRgbPhase));
            }
            if(command=="ROOM") {
                ++Writes;LastCommand=command;var packet=RoomWire.Unhex(arguments);LastKind=packet[0];LastTarget=RoomWire.Hex(packet.Skip(124).Take(6).ToArray());RoomWrites.Add(packet);WriteLog.Add("ROOM "+LastKind+" "+LastTarget);
                if(ApplyRoomWrites && LastKind==16){Inactive=false;Single=true;RoomName=new RoomWire(packet).Name;}
                if(ApplyRoomWrites && LastKind==5)Inactive=true;
                return Task.FromResult(BadWriteReply?"TRILINK/3 WRONG n ROOM":"TRILINK/3 OK n ROOM");
            }
            if(command=="RGB") {++Writes;++RgbWrites;LastCommand=command;LastTarget=arguments.Split(' ')[0];LastRgbId=RgbWrites.ToString("X8");CurrentRgbPhase=2;PendingRgb=false;WriteLog.Add("RGB "+LastTarget);
                return Task.FromResult(BadWriteReply?"TRILINK/3 OK n RGB":"TRILINK/3 QUEUED n "+LastRgbId);}
            if(command=="RGBENABLE") {++Writes;LastCommand=command;if(!IgnorePermissionWrite)RgbEnabled=arguments=="1";WriteLog.Add("RGBENABLE "+arguments);return Task.FromResult(BadWriteReply?"TRILINK/3 WRONG n RGBENABLE":"TRILINK/3 OK n RGBENABLE");}
            if(command!="ROOMGET")throw new Exception("unexpected write "+command);
            ++RoomReads;
            if(arguments=="00" && LastRgbId!="00000000" && BlockContext)return ContextPending.Task;
            var snapshot=Snapshot();
            if(arguments=="01" || arguments=="0D") {
                if(NoNeighbors)return Task.FromResult("TRILINK/3 EMPTY n");
                snapshot[0]=(byte)(arguments=="0D"?4:1);snapshot[4]=0x30;snapshot[9]=3;snapshot[34]=0x30;snapshot[39]=3;
                snapshot[40]=0x30;snapshot[45]=3;snapshot[50]=0x40;snapshot[55]=4;
                if(arguments=="0D" && !Invitations)return Task.FromResult("TRILINK/3 EMPTY n");
            } else if(arguments=="07" || arguments=="08") {
                if(!Requests)return Task.FromResult("TRILINK/3 EMPTY n");
                snapshot[0]=(byte)(arguments=="07"?3:7);snapshot[4]=(byte)(arguments=="07"?0x50:0x60);
                Put(snapshot,10,ApplicantBoot);Put(snapshot,26,RequestTicket);
            } else if(arguments!="00")return Task.FromResult("TRILINK/3 EMPTY n");
            if(arguments=="00") {
                Put(snapshot,22,RoomIncarnation);Put(snapshot,26,RoomRevision);Put(snapshot,30,RoomTerm);Put(snapshot,46,LocalMemberBoot);Put(snapshot,56,RemoteMemberBoot);
                Array.Clear(snapshot,100,24);var name=System.Text.Encoding.UTF8.GetBytes(RoomName);Array.Copy(name,0,snapshot,100,name.Length);
                if(Unsynchronized || LocalNodeId=="200000000002")snapshot[130]=1;
                if(Single){snapshot[2]=1;Array.Clear(snapshot,50,50);}
                if(Inactive){snapshot[2]=0;Array.Clear(snapshot,40,60);snapshot[130]=0;}
                if(Inactive && IdleSourceZero){Array.Clear(snapshot,4,96);Array.Clear(snapshot,100,24);}
                if(Full) {snapshot[2]=6;for(int i=2;i<6;++i){snapshot[40+i*10]=(byte)(0x40+i*2);snapshot[45+i*10]=(byte)(i+1);snapshot[46+i*10]=(byte)(i+10);}}
                if(RgbEnabled)snapshot[130]|=8;
                if(Waiting)snapshot[130]|=4;
                if(ChangedRoomAfterReceipt && LastRgbId!="00000000")snapshot[22]=102;
            }
            if(Uncertain && arguments=="00")snapshot[130]=4|16|32;
            if(RgbFault && arguments=="00")snapshot[130]=1|64;
            if(Retiring && arguments=="00")snapshot[130]=128;
            return Task.FromResult("TRILINK/3 ROOMSTATE n "+arguments+" "+RoomWire.Hex(snapshot));
        }
        public IReadOnlyList<TriLinkDevice> Devices {get {return NoDevices?new TriLinkDevice[0]:UnsupportedOnly?new[]{new TriLinkDevice{PortName="OLD",Capabilities=1}}:
            new[]{new TriLinkDevice {PortName=Endpoint,NodeId=LocalNodeId,Capabilities=64,TransportKind=TransportKind,ManagementUrl=TransportKind==NodeTransportKind.UsbNcm?"http://192.168.240.1/":null},new TriLinkDevice {PortName="OLD",Capabilities=1}}
                .Concat(SecondDevice?new[]{new TriLinkDevice{PortName="COM-OTHER",NodeId="300000000003",Capabilities=64}}:new TriLinkDevice[0]).ToArray();}}
        public Task<IReadOnlyList<TriLinkPeer>> SearchNearbyAsync(string port){++Searches;CapturedPeers=NoNeighbors?new TriLinkPeer[0]:new[]{new TriLinkPeer{NodeId="300000000003",Rssi=PeerRssi}};
            if(GateSearch){GateSearch=false;return SearchGate.Task;}return Task.FromResult(CapturedPeers);}
        public bool IsPolling {get{return false;}}public int ConsecutiveFailures {get{return 0;}}public int FailureLimit {get{return 5;}}
        private EventHandler<TriLinkDeviceEventArgs> _arrived,_removed;
        public event EventHandler<TriLinkDeviceEventArgs> DeviceArrived {add{_arrived+=value;++Subscriptions;}remove{_arrived-=value;--Subscriptions;}}
        public event EventHandler<TriLinkDeviceEventArgs> DeviceRemoved {add{_removed+=value;++Subscriptions;}remove{_removed-=value;--Subscriptions;}}
        public void RaiseArrived(){if(_arrived!=null)_arrived(this,new TriLinkDeviceEventArgs(Devices.FirstOrDefault()));}
        public void RaiseRemoved(){if(_removed!=null)_removed(this,new TriLinkDeviceEventArgs(null));}
        public event EventHandler<string> Status {add{}remove{}}
        public event EventHandler PollingStateChanged {add{}remove{}}
        public void Start(){}public void ResumePolling(){}public void PausePolling(){}public void RequestScan(){}public void Dispose(){}
    }
}
