using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly ISerialIo _io;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private long _nextIoTick;
        private readonly Dictionary<string, TriLinkDevice> _recognized =
            new Dictionary<string, TriLinkDevice>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _manualPorts;
        private readonly ConsecutiveFailureLimiter _failureLimiter;
        private Timer _timer;
        private ScanSession _scanSession;
        private ScanSession _activeScanSession;
        private bool _scanRequested;
        private bool _pollingEnabled;
        private bool _disposed;
        private string _lastDiscoveryStatus;

        public TriLinkSerialWatcher(int consecutiveFailureLimit = DefaultConsecutiveFailureLimit)
            : this(new WindowsSerialIo(), consecutiveFailureLimit)
        {
        }

        internal TriLinkSerialWatcher(ISerialIo io, int consecutiveFailureLimit = DefaultConsecutiveFailureLimit)
        {
            _io = io ?? throw new ArgumentNullException(nameof(io));
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
                RetireScanSessionLocked();
                _scanSession = new ScanSession();
                _scanRequested = true;
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

                _timer.Change(PollingInterval, PollingInterval);
            }

            RaisePollingStateChanged();
            RequestScan();
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
                    _scanRequested = false;
                    RetireScanSessionLocked();
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
            ScanSession session;
            lock (_sync)
            {
                if (!_pollingEnabled || _disposed) { return; }
                if (_activeScanSession != null)
                {
                    _scanRequested = true;
                    return;
                }
                session = _scanSession;
                _activeScanSession = session;
                _scanRequested = false;
            }

            Task.Run(
                async () =>
                {
                    try
                    {
                        // A manual resume waits asynchronously behind current I/O. Cancellation
                        // removes an old queued scan without reserving a thread or COM handle.
                        if (session.RefreshRecognized)
                            await _ioOwner.WaitAsync(session.Token).ConfigureAwait(false);
                        else if (!_ioOwner.Wait(0)) { return; }
                        try { ScanCore(session); HandleScanSuccess(session); }
                        finally { _ioOwner.Release(); }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception exception)
                    {
                        HandleScanFailure(session, exception);
                    }
                    finally
                    {
                        bool retry;
                        lock (_sync)
                        {
                            _activeScanSession = null;
                            if (!ReferenceEquals(session, _scanSession)) session.Cancellation.Dispose();
                            retry = _scanRequested && _pollingEnabled && !_disposed;
                        }
                        if (retry) RequestScan();
                    }
                });
        }

        public Task<IReadOnlyList<TriLinkPeer>> SearchNearbyAsync(string portName)
        {
            ThrowIfDisposed();
            return Task.Run<IReadOnlyList<TriLinkPeer>>(
                () =>
                {
                    if (!_ioOwner.Wait(1500, _lifetime.Token)) { throw new TimeoutException("USB 通道忙，请稍后重试。"); }
                    try
                    {
                    ThrowIfDisposed();
                    using (var port = _io.OpenPort(portName, 900, _lifetime.Token))
                    {
                        var nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                        _lifetime.Token.ThrowIfCancellationRequested();
                        port.WriteLine(UsbControlProtocol.EncodeSearch(nonce));
                        var elapsed = Stopwatch.StartNew();
                        return ReadSearchResponse(
                            () => ReadBoundedLine(port, _lifetime.Token), nonce, () => elapsed.ElapsedMilliseconds < 1200);
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
                    if (!_ioOwner.Wait(1500, _lifetime.Token)) { throw new TimeoutException("USB 通道忙，请稍后重试。"); }
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
                    bool identified = false;
                    try
                    {
                    using (var port = _io.OpenPort(portName, 150, _lifetime.Token))
                    {
                        var nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                        _lifetime.Token.ThrowIfCancellationRequested();
                        port.WriteLine(UsbControlProtocol.EncodeHello(nonce));
                        var elapsed = Stopwatch.StartNew();
                        while (elapsed.ElapsedMilliseconds < 1000)
                        {
                            string line; try { line = ReadBoundedLine(port, _lifetime.Token); } catch (TimeoutException) { continue; }
                            UsbDeviceIdentity identity;
                            if (UsbControlProtocol.TryParseDevice(line, nonce, out identity))
                            {
                                if (identity.NodeId != expected.NodeId || (identity.Capabilities & 64) == 0)
                                {
                                    InvalidateRecognized(expected);
                                    throw new InvalidOperationException("端口设备身份或能力已改变，请重新扫描。");
                                }
                                identified = true; break;
                            }
                        }
                        if (!identified)
                        {
                            InvalidateRecognized(expected);
                            throw new TimeoutException("发送业务前的身份复核超时。");
                        }
                        nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                        _lifetime.Token.ThrowIfCancellationRequested();
                        port.WriteLine("TRILINK/3 " + command + " " + nonce
                            + (arguments.Length == 0 ? "" : " " + arguments));
                        elapsed.Restart();
                        while (elapsed.ElapsedMilliseconds < 1800)
                        {
                            string line; try { line = ReadBoundedLine(port, _lifetime.Token); } catch (TimeoutException) { continue; }
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
                    catch
                    {
                        if (!identified) InvalidateRecognized(expected);
                        throw;
                    }
                }
                finally { _ioOwner.Release(); }
            });
        }

        private static string ReadBoundedLine(ISerialConnection port, CancellationToken cancellation)
        {
            var line = new StringBuilder(128);
            var elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < 1000)
            {
                cancellation.ThrowIfCancellationRequested();
                int next;
                try { next = port.ReadChar(); }
                catch (TimeoutException) { continue; } // Preserve partial lines within this bounded read.
                cancellation.ThrowIfCancellationRequested();
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
                _scanRequested = false;
                RetireScanSessionLocked();
                _lifetime.Cancel();
                timer = _timer;
                _timer = null;
            }

            if (timer != null)
            {
                timer.Dispose();
            }
        }

        private void HandleScanSuccess(ScanSession session)
        {
            var recovered = false;
            lock (_sync)
            {
                if (!IsCurrentScanLocked(session))
                {
                    return;
                }

                recovered = _failureLimiter.RecordSuccess();
                session.RefreshRecognized = false;
                if (recovered) RaiseStatus("串口轮询恢复正常，连续失败计数已清零。");
            }
        }

        private void HandleScanFailure(ScanSession session, Exception exception)
        {
            int failureCount;
            int failureLimit;
            bool tripped;
            lock (_sync)
            {
                if (!IsCurrentScanLocked(session))
                {
                    return;
                }

                tripped = _failureLimiter.RecordFailure();
                failureCount = _failureLimiter.ConsecutiveFailures;
                failureLimit = _failureLimiter.FailureLimit;
                if (tripped)
                {
                    _pollingEnabled = false;
                    _scanRequested = false;
                    RetireScanSessionLocked();
                    if (_timer != null)
                    {
                        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    }
                }
                if (tripped)
                {
                    RaiseStatus(string.Format(
                        "串口轮询已自动暂停：连续 {0} 次失败达到上限。最后错误：{1}。请点击“启动 USB 轮询”重试。",
                        failureCount, exception.Message));
                    RaisePollingStateChanged();
                }
                else RaiseStatus(string.Format("串口扫描失败（{0}/{1}）：{2}",
                    failureCount, failureLimit, exception.Message));
            }
        }

        private void ScanCore(ScanSession session)
        {
            session.Token.ThrowIfCancellationRequested();
            var candidates = EnumerateCandidates(session);
            var currentPorts = new HashSet<string>(
                candidates.Select(candidate => candidate.PortName),
                StringComparer.OrdinalIgnoreCase);
            List<TriLinkDevice> removed;

            lock (_sync)
            {
                EnsureCurrentScanLocked(session);
                removed = _recognized.Values
                    .Where(device => !currentPorts.Contains(device.PortName))
                    .Select(CloneDevice)
                    .ToList();
                foreach (var device in removed)
                {
                    EnsureCurrentScanLocked(session);
                    _recognized.Remove(device.PortName);
                    RaiseDeviceRemovedLocked(device);
                }
            }

            foreach (var candidate in candidates)
            {
                TriLinkDevice previous;
                lock (_sync)
                {
                    EnsureCurrentScanLocked(session);
                    _recognized.TryGetValue(candidate.PortName, out previous);
                    if (previous != null && !session.RefreshRecognized
                        && string.Equals(previous.PnpDeviceId, candidate.PnpDeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                TriLinkDevice recognized;
                bool identified = TryRecognize(candidate, session.Token, out recognized);
                lock (_sync)
                {
                    EnsureCurrentScanLocked(session);
                    if (!identified)
                    {
                        if (previous != null)
                        {
                            _recognized.Remove(candidate.PortName);
                            RaiseDeviceRemovedLocked(previous);
                        }
                        continue;
                    }
                    if (previous != null && previous.NodeId != recognized.NodeId)
                    {
                        _recognized.Remove(previous.PortName);
                        RaiseDeviceRemovedLocked(previous);
                        EnsureCurrentScanLocked(session);
                    }
                    _recognized[recognized.PortName] = recognized;
                    if (previous == null || !SameDevice(previous, recognized))
                    {
                        var handler = DeviceArrived;
                        if (handler != null) handler(this, new TriLinkDeviceEventArgs(CloneDevice(recognized)));
                    }
                }
            }
        }

        private List<SerialCandidate> EnumerateCandidates(ScanSession session)
        {
            var present = _io.Enumerate();
            session.Token.ThrowIfCancellationRequested();
            var candidates = SelectCandidates(present, _manualPorts);
            var bridges = string.Join(", ", present.Where(port => port.IsProgrammingBridge)
                .Select(port => port.PortName));
            var status = candidates.Count == 0
                ? (bridges.Length == 0 ? "未检测到 S3 原生 USB 数据口。"
                    : "检测到烧录口 " + bridges + "，不是 TriLink 数据口。")
                    + "请连接 S3 原生 USB（VID_303A）；未执行模拟搜索。"
                : "串口枚举正常：发现 " + candidates.Count + " 个候选数据口，身份以 HELLO 握手为准。";
            lock (_sync)
            {
                EnsureCurrentScanLocked(session);
                if (!string.Equals(status, _lastDiscoveryStatus, StringComparison.Ordinal))
                {
                    _lastDiscoveryStatus = status;
                    RaiseStatus(status);
                }
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

        private bool TryRecognize(
            SerialCandidate candidate,
            CancellationToken cancellation,
            out TriLinkDevice recognized)
        {
            recognized = null;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                using (var port = _io.OpenPort(candidate.PortName, 180, cancellation))
                {
                    cancellation.ThrowIfCancellationRequested();
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();
                    var nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                    cancellation.ThrowIfCancellationRequested();
                    port.WriteLine(UsbControlProtocol.EncodeHello(nonce));
                    var deadline = DateTime.UtcNow.AddMilliseconds(850);
                    while (DateTime.UtcNow < deadline)
                    {
                        string line;
                        try
                        {
                            line = ReadBoundedLine(port, cancellation);
                        }
                        catch (TimeoutException)
                        {
                            continue;
                        }

                        UsbDeviceIdentity identity;
                        if (UsbControlProtocol.TryParseDevice(line, nonce, out identity))
                        {
                            cancellation.ThrowIfCancellationRequested();
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
                cancellation.ThrowIfCancellationRequested();
                return false;
            }
            catch (InvalidOperationException)
            {
                cancellation.ThrowIfCancellationRequested();
                return false;
            }
            catch (System.IO.IOException)
            {
                cancellation.ThrowIfCancellationRequested();
                return false;
            }

            cancellation.ThrowIfCancellationRequested();
            return false;
        }

        private bool IsCurrentScanLocked(ScanSession session)
        {
            return !_disposed && _pollingEnabled && ReferenceEquals(session, _scanSession)
                && !session.Token.IsCancellationRequested;
        }

        private void EnsureCurrentScanLocked(ScanSession session)
        {
            if (!IsCurrentScanLocked(session)) throw new OperationCanceledException(session.Token);
        }

        private void RetireScanSessionLocked()
        {
            var session = _scanSession;
            _scanSession = null;
            if (session == null) return;
            session.Cancellation.Cancel();
            if (!ReferenceEquals(session, _activeScanSession)) session.Cancellation.Dispose();
        }

        private void InvalidateRecognized(TriLinkDevice expected)
        {
            lock (_sync)
            {
                if (_disposed) return;
                TriLinkDevice current;
                if (_recognized.TryGetValue(expected.PortName, out current) && SameDevice(current, expected))
                {
                    _recognized.Remove(expected.PortName);
                    RaiseDeviceRemovedLocked(current);
                }
            }
        }

        private void RaiseDeviceRemovedLocked(TriLinkDevice device)
        {
            var handler = DeviceRemoved;
            if (handler != null) handler(this, new TriLinkDeviceEventArgs(CloneDevice(device)));
        }

        private static bool SameDevice(TriLinkDevice left, TriLinkDevice right)
        {
            return left.NodeId == right.NodeId && left.Capabilities == right.Capabilities
                && left.DisplayName == right.DisplayName
                && string.Equals(left.PnpDeviceId, right.PnpDeviceId, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class ScanSession
        {
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public readonly CancellationToken Token;
            public bool RefreshRecognized = true;
            public ScanSession() { Token = Cancellation.Token; }
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
