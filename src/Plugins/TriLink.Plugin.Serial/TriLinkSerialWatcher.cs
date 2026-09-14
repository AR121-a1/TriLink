using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TriLink.Core;

namespace TriLink.MinClient.Serial
{
    internal sealed class TriLinkSerialWatcher : IDeviceDiscoveryService
    {
        private const int DefaultConsecutiveFailureLimit = 5;
        private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(3);
        private static readonly Regex ComPortRegex =
            new Regex(@"\((COM\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly object _sync = new object();
        private readonly Dictionary<string, TriLinkDevice> _recognized =
            new Dictionary<string, TriLinkDevice>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _manualPorts;
        private readonly ConsecutiveFailureLimiter _failureLimiter;
        private Timer _timer;
        private int _scanActive;
        private bool _pollingEnabled;
        private bool _disposed;

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
                    var peers = new List<TriLinkPeer>();
                    using (var port = OpenPort(portName, 900))
                    {
                        var nonce = Guid.NewGuid().ToString("N").Substring(0, 8);
                        port.WriteLine(UsbControlProtocol.EncodeSearch(nonce));
                        var deadline = DateTime.UtcNow.AddMilliseconds(1200);
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

                            if (UsbControlProtocol.IsSearchEnd(line, nonce))
                            {
                                break;
                            }

                            UsbPeerAdvertisement advertisement;
                            if (UsbControlProtocol.TryParsePeer(line, out advertisement))
                            {
                                peers.Add(
                                    new TriLinkPeer
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
                    }

                    return peers.AsReadOnly();
                });
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
            var candidates = new Dictionary<string, SerialCandidate>(
                StringComparer.OrdinalIgnoreCase);

            using (var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name, PNPDeviceID, Manufacturer FROM Win32_SerialPort"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    var portName = Convert.ToString(item["DeviceID"], CultureInfo.InvariantCulture);
                    var name = Convert.ToString(item["Name"], CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(portName))
                    {
                        var match = ComPortRegex.Match(name ?? string.Empty);
                        portName = match.Success ? match.Groups[1].Value : null;
                    }

                    if (string.IsNullOrWhiteSpace(portName))
                    {
                        continue;
                    }

                    var candidate = new SerialCandidate
                    {
                        PortName = portName,
                        Name = name ?? string.Empty,
                        PnpDeviceId = Convert.ToString(
                            item["PNPDeviceID"],
                            CultureInfo.InvariantCulture) ?? string.Empty,
                        Manufacturer = Convert.ToString(
                            item["Manufacturer"],
                            CultureInfo.InvariantCulture) ?? string.Empty,
                    };
                    if (LooksLikeTriLink(candidate) || _manualPorts.Contains(portName))
                    {
                        candidates[portName] = candidate;
                    }
                }
            }

            foreach (var manualPort in _manualPorts)
            {
                if (!candidates.ContainsKey(manualPort))
                {
                    candidates[manualPort] = new SerialCandidate
                    {
                        PortName = manualPort,
                        Name = "Manually configured TriLink port",
                        PnpDeviceId = string.Empty,
                        Manufacturer = string.Empty,
                    };
                }
            }

            return candidates.Values.ToList();
        }

        private static bool LooksLikeTriLink(SerialCandidate candidate)
        {
            return ContainsIgnoreCase(candidate.Name, "TriLink")
                || ContainsIgnoreCase(candidate.Manufacturer, "TriLink")
                || ContainsIgnoreCase(candidate.PnpDeviceId, "VID_303A");
        }

        private static bool ContainsIgnoreCase(string value, string fragment)
        {
            return value != null
                && value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
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

        private sealed class SerialCandidate
        {
            public string PortName { get; set; }

            public string Name { get; set; }

            public string PnpDeviceId { get; set; }

            public string Manufacturer { get; set; }
        }
    }
}
