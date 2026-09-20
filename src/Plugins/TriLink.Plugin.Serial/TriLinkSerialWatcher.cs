using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TriLink.Core;

namespace TriLink.MinClient.Serial
{
    internal sealed class TriLinkSerialWatcher : IDeviceDiscoveryService, IHardwareCommandService
    {
        private const int DefaultConsecutiveFailureLimit = 5;
        private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(3);

        private readonly object _sync = new object();
        private readonly SemaphoreSlim _ioOwner = new SemaphoreSlim(1, 1);
        private long _nextIoTick;
        private readonly Dictionary<string, TriLinkDevice> _recognized =
            new Dictionary<string, TriLinkDevice>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _manualPorts;
        private readonly ConsecutiveFailureLimiter _failureLimiter;
        private Timer _timer;
        private int _scanActive;
        private bool _pollingEnabled;
        private bool _disposed;
        private string _lastDiscoveryStatus;

        public TriLinkSerialWatcher(int consecutiveFailureLimit = DefaultConsecutiveFailureLimit)
        {
            _failureLimiter = new ConsecutiveFailureLimiter(consecutiveFailureLimit);
            var configured = Environment.GetEnvironmentVariable("TRILINK_PORTS") ?? string.Empty;
            _manualPorts = new HashSet<string>(
                configured.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(port => port.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        public event EventHandler<TriLinkDeviceEventArgs> DeviceArrived;

        public event EventHandler<TriLinkDeviceEventArgs> DeviceRemoved;

        public event EventHandler<string> Status;

        public event EventHandler PollingStateChanged;

        public bool IsPolling
        {
            get
            {
                lock (_sync)
                {
                    return _pollingEnabled && !_disposed;
                }
            }
        }

        public int ConsecutiveFailures
        {
            get
            {
                lock (_sync)
                {
                    return _failureLimiter.ConsecutiveFailures;
                }
            }
        }

        public int FailureLimit
        {
            get { return _failureLimiter.FailureLimit; }
        }

        public IReadOnlyList<TriLinkDevice> Devices
        {
            get
            {
                lock (_sync)
                {
                    return _recognized.Values
                        .Select(CloneDevice)
                        .OrderBy(device => device.PortName, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                        .AsReadOnly();
                }
            }
        }

        public void Start()
        {
            ResumePolling();
        }

        public void ResumePolling()
        {
            lock (_sync)
            {
                ThrowIfDisposedLocked();
                _failureLimiter.Reset();
                _pollingEnabled = true;
                if (_timer == null)
                {
                    _timer = new Timer(
                        _ => RequestScan(),
                        null,
                        Timeout.InfiniteTimeSpan,
                        Timeout.InfiniteTimeSpan);
                }

                _timer.Change(TimeSpan.Zero, PollingInterval);
            }

            RaisePollingStateChanged();
        }

        public void PausePolling()
        {
            var changed = false;
            lock (_sync)
            {
                ThrowIfDisposedLocked();
                if (_pollingEnabled)
                {
                    _pollingEnabled = false;
                    changed = true;
                    if (_timer != null)
                    {
                        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    }
                }
            }

            if (changed)
            {
                RaisePollingStateChanged();
            }
        }

        public void RequestScan()
        {
            if (!IsPolling || Interlocked.Exchange(ref _scanActive, 1) != 0)
            {
                return;
            }

            Task.Run(
                () =>
                {
                    try
                    {
                        if (!_ioOwner.Wait(0)) { return; }
                        try { ScanCore(); HandleScanSuccess(); }
                        finally { _ioOwner.Release(); }
                    }
                    catch (Exception exception)
                    {
                        HandleScanFailure(exception);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _scanActive, 0);
                    }
                });
        }

        public Task<IReadOnlyList<TriLinkPeer>> SearchNearbyAsync(string portName)
        {
            ThrowIfDisposed();
            return Task.Run<IReadOnlyList<TriLinkPeer>>(
                () =>
                {
                    if (!_ioOwner.Wait(1500)) { throw new TimeoutException("USB 通道忙，请稍后重试。"); }
                    try
                    {
                    ThrowIfDisposed();
                    using (var port = OpenPort(portName, 900))
                    {
                        var nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                        port.WriteLine(UsbControlProtocol.EncodeSearch(nonce));
                        var elapsed = Stopwatch.StartNew();
                        return ReadSearchResponse(
                            () => ReadBoundedLine(port), nonce, () => elapsed.ElapsedMilliseconds < 1200);
                    }
                    }
                    finally { _ioOwner.Release(); }
                });
        }

        public Task<string> ExecuteAsync(string portName, string command, string arguments)
        {
            // No arbitrary serial scripts/configuration through this application service.
            var allowed = new[] { "ROOM", "ROOMGET", "RGB", "RGBENABLE", "RGBRESULT" };
            if (!allowed.Contains(command) || arguments == null || arguments.Any(c => c < 32 || c > 126)
                || arguments.Length > 300) { throw new ArgumentException("非法或超长业务命令。"); }
            return Task.Run(() =>
            {
                if (!_ioOwner.Wait(1500)) { throw new TimeoutException("USB 通道忙，请稍后重试。"); }
                try
                {
                    ThrowIfDisposed();
                    TriLinkDevice expected;
                    lock (_sync)
                    {
                        if (!_recognized.TryGetValue(portName, out expected) || (expected.Capabilities & 64) == 0)
                            throw new InvalidOperationException("请选择已识别且支持真实 Room/RGB 的新版 S3。");
                        expected = CloneDevice(expected);
                    }
                    // Includes fresh HELLO plus one command: at most ~13 lines/s across all ports.
                    long delay = _nextIoTick - Stopwatch.GetTimestamp();
                    if (delay > 0) { Thread.Sleep((int)Math.Min(150, delay * 1000 / Stopwatch.Frequency + 1)); }
                    _nextIoTick = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 150 / 1000;
                    using (var port = OpenPort(portName, 150))
                    {
                        var nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                        port.WriteLine(UsbControlProtocol.EncodeHello(nonce));
                        var elapsed = Stopwatch.StartNew();
                        bool identified = false;
                        while (elapsed.ElapsedMilliseconds < 1000)
                        {
                            string line; try { line = ReadBoundedLine(port); } catch (TimeoutException) { continue; }
                            UsbDeviceIdentity identity;
                            if (UsbControlProtocol.TryParseDevice(line, nonce, out identity))
                            {
                                if (identity.NodeId != expected.NodeId || (identity.Capabilities & 64) == 0)
                                    throw new InvalidOperationException("端口设备身份或能力已改变，请重新扫描。");
                                identified = true; break;
                            }
                        }
                        if (!identified) { throw new TimeoutException("发送业务前的身份复核超时。"); }
                        nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                        port.WriteLine("TRILINK/3 " + command + " " + nonce
                            + (arguments.Length == 0 ? "" : " " + arguments));
                        elapsed.Restart();
                        while (elapsed.ElapsedMilliseconds < 1800)
                        {
                            string line; try { line = ReadBoundedLine(port); } catch (TimeoutException) { continue; }
                            var fields = line.Split(' ');
                            if (fields.Length >= 3 && fields[0] == "TRILINK/3" && fields[2] == nonce)
                            {
                                if (fields[1] == "ERROR") throw new InvalidOperationException(
                                    "设备拒绝操作：请检查角色、成员同步状态、容量和是否有在途任务。");
                                return line;
                            }
                        }
                        throw new TimeoutException("业务回复超时，结果未知；先刷新状态，不自动重放操作。");
                    }
                }
                finally { _ioOwner.Release(); }
            });
        }

        private static string ReadBoundedLine(SerialPort port)
        {
            var line = new StringBuilder(128);
            var elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < 1000)
            {
                int next;
                try { next = port.ReadChar(); }
                catch (TimeoutException) { continue; } // Preserve partial lines within this bounded read.
                if (next == '\n') return line.ToString().Trim();
                if (line.Length >= 511) throw new System.IO.InvalidDataException("USB 回复超过 511 字符上限。");
                line.Append((char)next);
            }
            throw new TimeoutException("USB 半行回复超时。");
        }

        internal static IReadOnlyList<TriLinkPeer> ReadSearchResponse(
            Func<string> readLine, string nonce, Func<bool> beforeDeadline)
        {
            var peers = new List<TriLinkPeer>();
            while (beforeDeadline())
            {
                string line;
                try { line = readLine(); }
                catch (TimeoutException) { continue; }
                if (UsbControlProtocol.IsSearchEnd(line, nonce)) { return peers.AsReadOnly(); }
                UsbPeerAdvertisement advertisement;
                if (UsbControlProtocol.TryParsePeer(line, out advertisement))
                {
                    if (peers.Count >= 64) { throw new System.IO.InvalidDataException("邻居响应超过客户端安全上限。"); }
                    peers.Add(new TriLinkPeer
                    {
                        NodeId = advertisement.NodeId,
                        DisplayName = advertisement.DisplayName,
                        Rssi = advertisement.Rssi,
                        RoomId = advertisement.RoomId,
                        RoomName = advertisement.RoomName,
                        LeaderNodeId = advertisement.LeaderNodeId,
                    });
                }
            }
            throw new TimeoutException("S3 未返回匹配的 END，不能把超时当作发现 0 台；请检查原生 USB 数据口。");
        }

        public void Dispose()
        {
            Timer timer;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _pollingEnabled = false;
                timer = _timer;
                _timer = null;
            }

            if (timer != null)
            {
                timer.Dispose();
            }
        }

        private void HandleScanSuccess()
        {
            var recovered = false;
            lock (_sync)
            {
                if (_disposed || !_pollingEnabled)
                {
                    return;
                }

                recovered = _failureLimiter.RecordSuccess();
            }

            if (recovered)
            {
                RaiseStatus("串口轮询恢复正常，连续失败计数已清零。");
            }
        }

        private void HandleScanFailure(Exception exception)
        {
            int failureCount;
            int failureLimit;
            bool tripped;
            lock (_sync)
            {
                if (_disposed || !_pollingEnabled)
                {
                    return;
                }

                tripped = _failureLimiter.RecordFailure();
                failureCount = _failureLimiter.ConsecutiveFailures;
                failureLimit = _failureLimiter.FailureLimit;
                if (tripped)
                {
                    _pollingEnabled = false;
                    if (_timer != null)
                    {
                        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    }
                }
            }

            if (tripped)
            {
                RaiseStatus(
                    string.Format(
                        "串口轮询已自动暂停：连续 {0} 次失败达到上限。最后错误：{1}。请点击“启动 USB 轮询”重试。",
                        failureCount,
                        exception.Message));
                RaisePollingStateChanged();
                return;
            }

            RaiseStatus(
                string.Format(
                    "串口扫描失败（{0}/{1}）：{2}",
                    failureCount,
                    failureLimit,
                    exception.Message));
        }

        private void ScanCore()
        {
            var candidates = EnumerateCandidates();
            var currentPorts = new HashSet<string>(
                candidates.Select(candidate => candidate.PortName),
                StringComparer.OrdinalIgnoreCase);
            List<TriLinkDevice> removed;

            lock (_sync)
            {
                removed = _recognized.Values
                    .Where(device => !currentPorts.Contains(device.PortName))
                    .Select(CloneDevice)
                    .ToList();
                foreach (var device in removed)
                {
                    _recognized.Remove(device.PortName);
                }
            }

            foreach (var device in removed)
            {
                var handler = DeviceRemoved;
                if (handler != null)
                {
                    handler(this, new TriLinkDeviceEventArgs(device));
                }
            }

            foreach (var candidate in candidates)
            {
                lock (_sync)
                {
                    if (_recognized.ContainsKey(candidate.PortName))
                    {
                        continue;
                    }
                }

                TriLinkDevice recognized;
                if (!TryRecognize(candidate, out recognized))
                {
                    continue;
                }

                lock (_sync)
                {
                    _recognized[recognized.PortName] = recognized;
                }

                var handler = DeviceArrived;
                if (handler != null)
                {
                    handler(this, new TriLinkDeviceEventArgs(CloneDevice(recognized)));
                }
            }
        }

        private List<SerialCandidate> EnumerateCandidates()
        {
            var present = WindowsSerialPortCatalog.Enumerate();
            var candidates = SelectCandidates(present, _manualPorts);
            var bridges = string.Join(", ", present.Where(port => port.IsProgrammingBridge)
                .Select(port => port.PortName));
            var status = candidates.Count == 0
                ? (bridges.Length == 0 ? "未检测到 S3 原生 USB 数据口。"
                    : "检测到烧录口 " + bridges + "，不是 TriLink 数据口。")
                    + "请连接 S3 原生 USB（VID_303A）；未执行模拟搜索。"
                : "串口枚举正常：发现 " + candidates.Count + " 个候选数据口，身份以 HELLO 握手为准。";
            if (!string.Equals(status, _lastDiscoveryStatus, StringComparison.Ordinal))
            {
                _lastDiscoveryStatus = status;
                RaiseStatus(status);
            }
            return candidates;
        }

        internal static List<SerialCandidate> SelectCandidates(
            IEnumerable<SerialCandidate> present, IEnumerable<string> manualPorts)
        {
            var manual = new HashSet<string>(manualPorts, StringComparer.OrdinalIgnoreCase);
            return present.Where(port => port.IsValidPort
                    && (port.IsProtocolCandidate || manual.Contains(port.PortName)))
                .GroupBy(port => port.PortName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToList();
        }

        private static bool TryRecognize(
            SerialCandidate candidate,
            out TriLinkDevice recognized)
        {
            recognized = null;
            try
            {
                using (var port = OpenPort(candidate.PortName, 180))
                {
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();
                    var nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                    port.WriteLine(UsbControlProtocol.EncodeHello(nonce));
                    var deadline = DateTime.UtcNow.AddMilliseconds(850);
                    while (DateTime.UtcNow < deadline)
                    {
                        string line;
                        try
                        {
                            line = ReadBoundedLine(port);
                        }
                        catch (TimeoutException)
                        {
                            continue;
                        }

                        UsbDeviceIdentity identity;
                        if (UsbControlProtocol.TryParseDevice(line, nonce, out identity))
                        {
                            recognized = new TriLinkDevice
                            {
                                PortName = candidate.PortName,
                                NodeId = identity.NodeId,
                                DisplayName = identity.DisplayName,
                                Capabilities = identity.Capabilities,
                                PnpDeviceId = candidate.PnpDeviceId,
                            };
                            return true;
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (System.IO.IOException)
            {
                return false;
            }

            return false;
        }

        private static SerialPort OpenPort(string portName, int readTimeoutMs)
        {
            var port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
            {
                DtrEnable = false,
                RtsEnable = false,
                Handshake = Handshake.None,
                NewLine = "\n",
                Encoding = new UTF8Encoding(false),
                ReadTimeout = readTimeoutMs,
                WriteTimeout = 400,
            };
            port.Open();
            return port;
        }

        private static TriLinkDevice CloneDevice(TriLinkDevice source)
        {
            return new TriLinkDevice
            {
                PortName = source.PortName,
                NodeId = source.NodeId,
                DisplayName = source.DisplayName,
                Capabilities = source.Capabilities,
                PnpDeviceId = source.PnpDeviceId,
            };
        }

        private void RaiseStatus(string message)
        {
            var handler = Status;
            if (handler != null)
            {
                handler(this, message);
            }
        }

        private void RaisePollingStateChanged()
        {
            var handler = PollingStateChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void ThrowIfDisposed()
        {
            lock (_sync)
            {
                ThrowIfDisposedLocked();
            }
        }

        private void ThrowIfDisposedLocked()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TriLinkSerialWatcher));
            }
        }

    }
}
