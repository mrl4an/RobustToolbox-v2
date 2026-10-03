using System;
using System.Net;
using Lidgren.Network;
using Robust.Shared.ViewVariables;

namespace Robust.Shared.Network
{
    /// <summary>
    ///     A connection from this machine to one remote peer.
    /// </summary>
    [NotContentImplementable]
    public interface INetChannel
    {
        /// <summary>
        ///     The network manager (the owning peer) that this channel belongs to.
        /// </summary>
        INetManager NetPeer { get; }

        /// <summary>
        ///     A 64-bit number that identifies this connection. It is an identifier, not a count, and has no unit.
        /// </summary>
        long ConnectionId { get; }

        /// <summary>
        ///     The IP end point.
        /// </summary>
        IPEndPoint RemoteEndPoint { get; }

        /// <summary>
        ///     The session ID for this channel.
        ///     On the server, this is the session ID for this client.
        ///     On the client, this is the session ID for the client.
        /// </summary>
        [ViewVariables]
        NetUserId UserId { get; }

        [ViewVariables]
        string UserName { get; }

        [ViewVariables]
        LoginType AuthType { get; }

        /// <summary>
        ///     Offset between local RealTime and remote RealTime.
        /// </summary>
        TimeSpan RemoteTimeOffset { get; }

        /// <summary>
        ///     Remote RealTime.
        /// </summary>
        TimeSpan RemoteTime { get; }

        /// <summary>
        ///     The average round-trip time to the remote peer, in milliseconds.
        ///     This is a smoothed value, not a single measurement.
        /// </summary>
        [ViewVariables]
        short Ping { get; }

        /// <summary>
        ///     Whether or not the channel is currently connected to a remote peer.
        /// </summary>
        [ViewVariables]
        bool IsConnected { get; }

        NetUserData UserData { get; }

        /// <summary>
        ///     Has the serializer handshake completed and <see cref="INetManager.Connected"/> been ran?
        /// </summary>
        bool IsHandshakeComplete { get; }

        /// <summary>
        /// Diagnostic indicating the maximum transmission unit being used for this connection.
        /// </summary>
        /// <seealso cref="CVars.NetMtu"/>
        [ViewVariables]
        public int CurrentMtu { get; }

        /// <summary>
        ///     Creates a new, empty instance of the message type <typeparamref name="T"/>, ready to be filled in
        ///     and then passed to <see cref="SendMessage"/>.
        /// </summary>
        /// <typeparam name="T">The message type to create. It needs a public parameterless constructor.</typeparam>
        /// <returns>A new message instance.</returns>
        /// <remarks>
        ///     Deprecated: callers should construct the message type themselves.
        /// </remarks>
        [Obsolete("Just new NetMessage directly")]
        T CreateNetMessage<T>()
            where T : NetMessage, new();

        /// <summary>
        ///     Sends a message to the remote peer over this channel.
        /// </summary>
        /// <param name="message">The message to send. It should already be filled in.</param>
        void SendMessage(NetMessage message);

        /// <summary>
        ///     Closes this channel's connection to the remote peer.
        /// </summary>
        /// <param name="reason">A human-readable explanation of why the connection is being closed.</param>
        void Disconnect(string reason);

        /// <summary>
        ///     Closes this channel's connection to the remote peer, optionally without notifying it.
        /// </summary>
        /// <param name="reason">A human-readable explanation of why the connection is being closed.</param>
        /// <param name="sendBye">
        ///     If true, the remote peer is told that it was disconnected, along with the reason, so it can treat
        ///     the close as orderly. If false, no goodbye is sent and the remote side is silently abandoned; it
        ///     will only find out through its own timeout.
        /// </param>
        void Disconnect(string reason, bool sendBye);

        /// <summary>
        ///     Check whether the networking layer has space to immediately send a message with the given parameters.
        /// </summary>
        /// <remarks>
        ///     If this returns true, messages may still be sent, but they will be queued until there is space available.
        /// </remarks>
        bool CanSendImmediately(NetDeliveryMethod method, int sequenceChannel);
    }
}
