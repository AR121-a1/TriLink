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
    internal sealed class TriLinkSerialWatcher : IDeviceDiscoveryService, IHardwareCommandService, INodeCapabilityService
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
        private readonly Dictionary<string, TriLinkDevice> _alternatives =
            new Dictionary<string, TriLinkDevice>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _lastRecognitionStatus =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            : this(new CompositeSerialIo(), consecutiveFailureLimit)
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
                var optional = _io as IOptionalTransportPolling;
                if (optional != null) optional.ResetOptionalPolling();
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
            TriLinkDevice binding;
            lock (_sync)
            {
                TriLinkDevice known;
                binding = portName != null && _recognized.TryGetValue(portName, out known) ? CloneDevice(known) : null;
            }
            return Task.Run<IReadOnlyList<TriLinkPeer>>(
                () =>
                {
                    if (!_ioOwner.Wait(1500, _lifetime.Token)) { throw new TimeoutException("USB 通道忙，请稍后重试。"); }
                    try
                    {
                    ThrowIfDisposed();
                    lock (_sync)
                    {
                        TriLinkDevice current;
                        if (binding == null || !_recognized.TryGetValue(portName, out current) || !SameDevice(current, binding))
                            throw new InvalidOperationException("搜索目标身份已失效，请重新识别节点。");
                    }
                    using (var port = _io.OpenPort(portName, 900, _lifetime.Token))
                    {
                        var nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                        _lifetime.Token.ThrowIfCancellationRequested();
                        port.WriteLine(UsbControlProtocol.EncodeHello(nonce));
                        var elapsed = Stopwatch.StartNew();
                        bool identified = false;
                        while (elapsed.ElapsedMilliseconds < 1000)
                        {
                            string line; try { line = ReadBoundedLine(port, _lifetime.Token); } catch (TimeoutException) { continue; }
                            UsbDeviceIdentity identity;
                            if (!UsbControlProtocol.TryParseDevice(line, nonce, out identity)) continue;
                            if (identity.NodeId != binding.NodeId || identity.Capabilities != binding.Capabilities)
                            {
                                InvalidateRecognized(binding);
                                throw new InvalidOperationException("搜索前设备身份已改变，请重新扫描。");
                            }
                            identified = true; break;
                        }
                        if (!identified)
                        {
                            InvalidateRecognized(binding);
                            throw new TimeoutException("搜索前身份复核超时；未发送 SEARCH。");
                        }
                        nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                        _lifetime.Token.ThrowIfCancellationRequested();
                        port.WriteLine(UsbControlProtocol.EncodeSearch(nonce));
                        elapsed.Restart();
                        return ReadSearchResponse(
                            () => ReadBoundedLine(port, _lifetime.Token), nonce, () => elapsed.ElapsedMilliseconds < 1200);
                    }
                    }
                    finally { _ioOwner.Release(); }
                });
        }

        public Task<string> ExecuteAsync(string portName, string command, string arguments)
        {
            TriLinkDevice binding = null;
            lock (_sync)
            {
                TriLinkDevice recognized;
                if (portName != null && _recognized.TryGetValue(portName, out recognized))
                    binding = CloneDevice(recognized);
            }
            return ExecuteAuthorizedAsync(portName, command, arguments, binding);
        }

        public async Task<NodeCapabilities> DescribeAsync(string portName)
        {
            TriLinkDevice expected;
            lock (_sync)
            {
                ThrowIfDisposedLocked();
                if (string.IsNullOrWhiteSpace(portName) || !_recognized.TryGetValue(portName, out expected))
                    throw new InvalidOperationException("请先识别原生 USB 节点再读取能力。");
                expected = CloneDevice(expected);
            }
            if ((expected.Capabilities & NodeCapabilities.DiscoveryCapability) == 0)
                return new NodeCapabilities(expected.NodeId, true, 0, null, 0, 0, 0, 0,
                    (expected.Capabilities & 64) != 0 ? NodeCapabilities.RgbModule : 0);
            var line = await ExecuteAuthorizedAsync(portName, "CAPS", "", expected).ConfigureAwait(false);
            var fields = line.Split(' ');
            NodeCapabilities description;
            // ExecuteAuthorizedAsync has already matched the reply nonce to the fresh request.
            if (fields.Length < 3 || !NodeCapabilityProtocol.TryParse(line, fields[2], expected.NodeId, out description))
                throw new FormatException("节点模块描述无效或协议版本不受支持；未采用未经校验的能力数据。");
            return description;
        }

        private Task<string> ExecuteAuthorizedAsync(string portName, string command, string arguments, TriLinkDevice binding)
        {
            // No arbitrary serial scripts/configuration through this application service.
            var allowed = new[] { "ROOM", "ROOMGET", "RGB", "RGBENABLE", "RGBRESULT", "CAPS" };
            if (!allowed.Contains(command) || arguments == null || arguments.Any(c => c < 32 || c > 126)
                || arguments.Length > 300 || (command == "CAPS" && arguments.Length != 0))
                { throw new ArgumentException("非法或超长业务命令。"); }
            uint requiredCapability = command == "CAPS" ? NodeCapabilities.DiscoveryCapability : 64U;
            return Task.Run(() =>
            {
                    if (!_ioOwner.Wait(1500, _lifetime.Token)) { throw new TimeoutException("USB 通道忙，请稍后重试。"); }
                try
                {
                    ThrowIfDisposed();
                    TriLinkDevice expected;
                    lock (_sync)
                    {
                        if (portName == null || !_recognized.TryGetValue(portName, out expected) || (expected.Capabilities & requiredCapability) == 0)
                            throw new InvalidOperationException("请选择已识别且支持所请求功能的 S3。");
                        // Bind before waiting for the shared owner. A queued command must never
                        // silently retarget a replacement device that reuses the same COM name.
                        if (binding == null || expected.NodeId != binding.NodeId
                            || expected.PnpDeviceId != binding.PnpDeviceId || expected.Capabilities != binding.Capabilities
                            || expected.TransportKind != binding.TransportKind)
                            throw new InvalidOperationException("排队期间端口设备身份或能力已变化，请重新选择节点。");
                        expected = binding;
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
                                if (identity.NodeId != expected.NodeId || (identity.Capabilities & requiredCapability) == 0)
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
                            string line; try { line = ReadBoundedLine(port, _lifetime.Token, command == "CAPS"); } catch (TimeoutException) { continue; }
                            if (command == "CAPS" && (line.Length > 160 || line.Any(c => c < 32 || c > 126)))
                                throw new FormatException("节点模块描述含非法字符或超过长度上限。");
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

        private static string ReadBoundedLine(ISerialConnection port, CancellationToken cancellation, bool preserveWhitespace = false)
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
                if (next == '\n')
                {
                    var text = line.ToString();
                    if (!preserveWhitespace) return text.Trim();
                    // CAPS validates the bytes, not a Trim()-repaired representation.
                    return text.EndsWith("\r", StringComparison.Ordinal) ? text.Substring(0, text.Length - 1) : text;
                }
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
                foreach (var portName in _lastRecognitionStatus.Keys.Where(port => !currentPorts.Contains(port)).ToList())
                    _lastRecognitionStatus.Remove(portName);
                foreach (var portName in _alternatives.Keys.Where(port => !currentPorts.Contains(port)).ToList())
                    _alternatives.Remove(portName);
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
                    TriLinkDevice alternative;
                    if (!session.RefreshRecognized && _alternatives.TryGetValue(candidate.PortName, out alternative)
                        && string.Equals(alternative.PnpDeviceId, candidate.PnpDeviceId, StringComparison.OrdinalIgnoreCase)
                        && _recognized.Values.Any(device => device.NodeId == alternative.NodeId
                            && device.TransportKind == NodeTransportKind.UsbSerial)) continue;
                    if (previous != null && !session.RefreshRecognized
                        && string.Equals(previous.PnpDeviceId, candidate.PnpDeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                TriLinkDevice recognized;
                string diagnostic;
                bool identified = TryRecognize(candidate, session.Token, out recognized, out diagnostic);
                lock (_sync)
                {
                    EnsureCurrentScanLocked(session);
                    if (!identified)
                    {
                        _alternatives.Remove(candidate.PortName);
                        if (previous != null)
                        {
                            _recognized.Remove(candidate.PortName);
                            RaiseDeviceRemovedLocked(previous);
                        }
                        EnsureCurrentScanLocked(session);
                        PublishRecognitionStatusLocked(candidate, diagnostic);
                        continue;
                    }
                    if (previous != null && previous.NodeId != recognized.NodeId)
                    {
                        _recognized.Remove(previous.PortName);
                        RaiseDeviceRemovedLocked(previous);
                        EnsureCurrentScanLocked(session);
                    }
                    // CDC wins for one physical node. NCM may be selected after a later
                    // explicit/new discovery, never as a retry of a failed business write.
                    var duplicate = _recognized.Values.FirstOrDefault(device => device.NodeId == recognized.NodeId
                        && device.PortName != recognized.PortName);
                    if (duplicate != null)
                    {
                        if (recognized.TransportKind == NodeTransportKind.UsbNcm
                            || duplicate.TransportKind == NodeTransportKind.UsbSerial)
                        {
                            _alternatives[recognized.PortName] = recognized;
                            PublishRecognitionStatusLocked(candidate, "同一节点已有 CDC 主通道；当前端点仅作备选，不建立重复会话。");
                            continue;
                        }
                        _recognized.Remove(duplicate.PortName);
                        _alternatives[duplicate.PortName] = duplicate;
                        RaiseDeviceRemovedLocked(duplicate);
                        EnsureCurrentScanLocked(session);
                    }
                    _alternatives.Remove(recognized.PortName);
                    _recognized[recognized.PortName] = recognized;
                    if (previous == null || !SameDevice(previous, recognized))
                    {
                        var handler = DeviceArrived;
                        if (handler != null) handler(this, new TriLinkDeviceEventArgs(CloneDevice(recognized)));
                    }
                    EnsureCurrentScanLocked(session);
                    PublishRecognitionStatusLocked(candidate, diagnostic);
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
                : "发现 " + candidates.Count + " 个候选数据口，正在验证 HELLO；候选不代表设备已连接。";
            var optional = _io as IOptionalTransportPolling;
            if (optional != null && !string.IsNullOrEmpty(optional.Diagnostic)) status += " " + optional.Diagnostic;
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
                .Select(group => group.First()).OrderBy(port => port.TransportKind).ToList();
        }

        private bool TryRecognize(
            SerialCandidate candidate,
            CancellationToken cancellation,
            out TriLinkDevice recognized,
            out string diagnostic)
        {
            recognized = null;
            diagnostic = "HELLO 握手超时：未收到本轮身份回应，请检查原生 USB 数据线与固件。";
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
                    var handshakeClock = Stopwatch.StartNew();
                    while (handshakeClock.ElapsedMilliseconds < 850)
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
                                TransportKind = candidate.TransportKind,
                                ManagementUrl = candidate.TransportKind == NodeTransportKind.UsbNcm ? NcmEndpoint.ManagementUrl : null,
                            };
                            diagnostic = "已识别 TriLink " + identity.NodeId
                                + ((identity.Capabilities & 64) != 0
                                    ? "，真实 Room/RGB 能力可用。"
                                    : "；当前固件未声明真实 Room/RGB 能力，请更新兼容固件。");
                            return true;
                        }

                        var fields = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (fields.Length >= 3 && fields[0] == UsbControlProtocol.Prefix && fields[2] == nonce)
                        {
                            if (fields[1] == "DEVICE")
                            {
                                diagnostic = "身份回应无效：MAC、名称或能力字段未通过验证；没有建立设备连接。";
                                return false;
                            }
                            if (fields[1] == "ERROR")
                            {
                                diagnostic = fields.Length == 4 && fields[3] == "NOT_READY"
                                    ? "固件尚未就绪（NOT_READY），请等待启动完成后重新连接。"
                                    : "固件拒绝 HELLO 身份请求，请核对兼容的 TriLink 固件。";
                                return false;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(line))
                            diagnostic = "HELLO 握手未完成：只收到无关或旧回应，没有本轮匹配的设备身份。";
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                cancellation.ThrowIfCancellationRequested();
                diagnostic = "端口忙或访问受限，请关闭串口监视器/烧录工具后重新连接。";
                return false;
            }
            catch (InvalidOperationException)
            {
                cancellation.ThrowIfCancellationRequested();
                diagnostic = "端口当前不可用，请检查驱动和连接后重试。";
                return false;
            }
            catch (System.IO.InvalidDataException)
            {
                cancellation.ThrowIfCancellationRequested();
                diagnostic = "身份回应超过安全长度上限，请核对数据口与兼容固件。";
                return false;
            }
            catch (System.IO.IOException)
            {
                cancellation.ThrowIfCancellationRequested();
                diagnostic = "端口读写失败或设备已断开，请检查数据线/驱动并重新连接。";
                return false;
            }
            catch (TimeoutException)
            {
                cancellation.ThrowIfCancellationRequested();
                diagnostic = "端口读写超时，HELLO 身份尚未确认；请重新连接。";
                return false;
            }

            cancellation.ThrowIfCancellationRequested();
            return false;
        }

        private void PublishRecognitionStatusLocked(SerialCandidate candidate, string diagnostic)
        {
            // Called only after the current scan generation is checked. A reused COM
            // is a new diagnostic subject when its PnP identity changes; never expose
            // raw driver errors or untrusted reply text in the UI status stream.
            var key = (candidate.PnpDeviceId ?? string.Empty) + "\n" + diagnostic;
            string previous;
            if (_lastRecognitionStatus.TryGetValue(candidate.PortName, out previous) && previous == key) return;
            _lastRecognitionStatus[candidate.PortName] = key;
            RaiseStatus(candidate.PortName + " · " + diagnostic);
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
                && left.TransportKind == right.TransportKind
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
                TransportKind = source.TransportKind,
                ManagementUrl = source.ManagementUrl,
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
