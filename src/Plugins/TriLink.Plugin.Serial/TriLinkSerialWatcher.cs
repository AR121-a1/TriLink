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
    internal sealed class TriLinkSerialWatcher : IDeviceDiscoveryService
    {
        private const int DefaultConsecutiveFailureLimit = 5;
        private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(3);

        private readonly object _sync = new object();
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
                        ScanCore();
                        HandleScanSuccess();
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
                    using (var port = OpenPort(portName, 900))
                    {
                        var nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                        port.WriteLine(UsbControlProtocol.EncodeSearch(nonce));
                        var elapsed = Stopwatch.StartNew();
                        return ReadSearchResponse(
                            () => port.ReadLine().Trim(), nonce, () => elapsed.ElapsedMilliseconds < 1200);
                    }
                });
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
                            line = port.ReadLine().Trim();
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
