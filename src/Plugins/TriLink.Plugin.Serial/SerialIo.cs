using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace TriLink.MinClient.Serial
{
    // Internal adapter seam: deterministic tests never enumerate or open real COM ports.
    internal interface ISerialIo
    {
        List<SerialCandidate> Enumerate();
        ISerialConnection OpenPort(string portName, int readTimeoutMs, CancellationToken cancellation);
    }

    internal interface ISerialConnection : IDisposable
    {
        void DiscardInBuffer();
        void DiscardOutBuffer();
        void WriteLine(string line);
        int ReadChar();
    }

    internal sealed class WindowsSerialIo : ISerialIo
    {
        public List<SerialCandidate> Enumerate() { return WindowsSerialPortCatalog.Enumerate(); }

        public ISerialConnection OpenPort(string portName, int readTimeoutMs, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
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
            try
            {
                cancellation.ThrowIfCancellationRequested();
                port.Open();
                cancellation.ThrowIfCancellationRequested();
                return new Connection(port);
            }
            catch
            {
                port.Dispose();
                throw;
            }
        }

        private sealed class Connection : ISerialConnection
        {
            private readonly SerialPort _port;
            public Connection(SerialPort port) { _port = port; }
            public void DiscardInBuffer() { _port.DiscardInBuffer(); }
            public void DiscardOutBuffer() { _port.DiscardOutBuffer(); }
            public void WriteLine(string line) { _port.WriteLine(line); }
            public int ReadChar() { return _port.ReadChar(); }
            public void Dispose() { _port.Dispose(); }
        }
    }
}
