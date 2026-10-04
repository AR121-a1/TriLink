using System;

namespace TriLink.Plugin
{
    /// <summary>Creates transport sessions owned and disposed by the game caller.</summary>
    [PluginService("trilink.game-link")]
    public interface IGameLinkFactory
    {
        IGameLink CreateLink();
    }

    /// <summary>
    /// A bounded datagram transport. Creating a link does not open a connection or start work.
    /// The caller drives receives and owns peer validation and game protocol state.
    /// </summary>
    public interface IGameLink : IDisposable
    {
        /// <summary>
        /// Validates and normalizes an adapter-defined target without opening a connection.
        /// The returned representation must match GameDatagram.PeerAddress for that peer.
        /// </summary>
        string NormalizePeerAddress(string peerAddress);

        /// <summary>
        /// Opens a local endpoint or channel. The adapter defines the identifier;
        /// the UDP adapter uses a port and treats zero as automatic selection.
        /// </summary>
        void Open(int localPort);

        /// <summary>Receives without waiting; a closed link returns false.</summary>
        bool TryReceive(out GameDatagram datagram);

        /// <summary>
        /// Sends at most 128 bytes to an adapter-defined peer address and endpoint or channel.
        /// The UDP adapter accepts only IPv4 address literals and ports 1 through 65535.
        /// </summary>
        void Send(string peerAddress, int peerPort, byte[] payload);

        bool IsOpen { get; }

        /// <summary>The actual local endpoint or channel identifier, or zero when closed.</summary>
        int LocalPort { get; }
    }

    public sealed class GameDatagram
    {
        public const int MaxPayloadLength = 128;

        public string PeerAddress { get; set; }

        public int PeerPort { get; set; }

        public byte[] Payload { get; set; }
    }
}
