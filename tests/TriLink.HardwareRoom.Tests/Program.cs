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
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            var bytes=Snapshot();var packet=new RoomWire(bytes);
            Check(packet.Active && packet.Synchronized && !packet.RgbEnabled,"local status flags do not imply RGB enabled");
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
            using(var view=new HardwareRoomView(service,service)) {
                view.Size=new Size(1000,700);view.CreateControl();view.PerformLayout();
                Check(service.Calls==0,"opening module performs no hardware write or automatic polling");
                Render(view,args[0]+".empty.png");
                Check(((ComboBox)view.Controls.Find("HardwarePorts",true).Single()).Items.Count==1,"only capable devices listed");
                view.RefreshAll().GetAwaiter().GetResult();
                Check(service.Calls==20 && service.Searches==1,"bounded 19 pages plus RGB result and one peer search");
                Check(((ListBox)view.Controls.Find("RoomMembers",true).Single()).Items.Count==2,"view renders hardware membership");
                Check(view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("leader=100000000001"),"hardware leader visible");
                Check(view.Controls.Find("HardwareMonitor",true).Single().Text=="启动状态轮询","polling remains opt-in after refresh");
                var members=(ListBox)view.Controls.Find("RoomMembers",true).Single();members.SelectedIndex=1;
                var rooms=(ListBox)view.Controls.Find("NearbyRooms",true).Single();rooms.SelectedIndex=0;
                view.RefreshAll().GetAwaiter().GetResult();
                Check((string)members.SelectedItem=="200000000002" && rooms.SelectedIndex==0,"periodic refresh preserves stable selections");
                using(var bitmap=new Bitmap(view.Width,view.Height)) {view.DrawToBitmap(bitmap,new Rectangle(Point.Empty,view.Size));bitmap.Save(args[0]);}
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var automatic=typeof(HardwareRoomView).GetField("_automatic",flags);
                var run=typeof(HardwareRoomView).GetMethod("Run",flags);
                service.BadResult=true;
                Func<Task> refreshAction=()=>view.RefreshAll();
                ((Task)run.Invoke(view,new object[]{refreshAction})).GetAwaiter().GetResult();
                Check(members.Items.Count==0 && rooms.Items.Count==0,"malformed response invalidates stale actionable membership");
                Check(view.Controls.Find("HardwareRoomStatus",true).Single().Text.Contains("失效"),"failure explicitly marks state invalid");
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
            service=new Fake {Delay=true};var pendingView=new HardwareRoomView(service,service);
            // No WinForms sync context: continuations execute on the completing test thread.
            var context=System.Threading.SynchronizationContext.Current;
            System.Threading.SynchronizationContext.SetSynchronizationContext(null);
            var refresh=pendingView.RefreshAll();pendingView.Dispose();service.Pending.SetResult("TRILINK/3 EMPTY n");refresh.GetAwaiter().GetResult();
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            Check(service.Calls==1,"closing module stops the remaining page batch");
            service=new Fake {Delay=true};
            using(var pausedView=new HardwareRoomView(service,service)) {
                var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                typeof(HardwareRoomView).GetField("_automatic",flags).SetValue(pausedView,true);
                System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                var pausedRefresh=pausedView.RefreshAll();
                var monitor=(Button)pausedView.Controls.Find("HardwareMonitor",true).Single();
                typeof(Control).GetMethod("OnClick",flags).Invoke(monitor,new object[]{EventArgs.Empty});
                service.Pending.SetResult("TRILINK/3 EMPTY n");pausedRefresh.GetAwaiter().GetResult();
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                Check(service.Calls==1,"manual pause cancels remaining refresh pages without closing view");
            }
            Console.WriteLine("PASS hardware-room checks="+checks+" (fake USB service, no physical hardware)");return 0;
        } catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private sealed class Fake : IHardwareCommandService,IDeviceDiscoveryService
    {
        public int Calls,Searches;public bool Delay,BadResult;public TaskCompletionSource<string> Pending=new TaskCompletionSource<string>();
        public Task<string> ExecuteAsync(string port,string command,string arguments)
        {
            ++Calls;if(Delay)return Pending.Task;
            if(command=="RGBRESULT")return Task.FromResult(BadResult?"INVALID RGBRESULT n 00000000 0":"TRILINK/3 RGBRESULT n 00000000 0");
            if(command!="ROOMGET")throw new Exception("unexpected write "+command);
            return Task.FromResult(arguments=="00" || arguments=="01" ? "TRILINK/3 ROOMSTATE n "+arguments+" "+RoomWire.Hex(Snapshot()) : "TRILINK/3 EMPTY n");
        }
        public IReadOnlyList<TriLinkDevice> Devices {get {return new[]{new TriLinkDevice {PortName="COM-FAKE",NodeId="100000000001",Capabilities=64},new TriLinkDevice {PortName="OLD",Capabilities=1}};}}
        public Task<IReadOnlyList<TriLinkPeer>> SearchNearbyAsync(string port){++Searches;return Task.FromResult<IReadOnlyList<TriLinkPeer>>(new[]{new TriLinkPeer{NodeId="200000000002"}});}
        public bool IsPolling {get{return false;}}public int ConsecutiveFailures {get{return 0;}}public int FailureLimit {get{return 5;}}
        public event EventHandler<TriLinkDeviceEventArgs> DeviceArrived {add{}remove{}}
        public event EventHandler<TriLinkDeviceEventArgs> DeviceRemoved {add{}remove{}}
        public event EventHandler<string> Status {add{}remove{}}
        public event EventHandler PollingStateChanged {add{}remove{}}
        public void Start(){}public void ResumePolling(){}public void PausePolling(){}public void RequestScan(){}public void Dispose(){}
    }
}
