using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Robust.Shared.Network;

/// <summary>
///     The side-independent part of the network manager: lifecycle, state queries, message registration,
///     sending and connection events. Use <see cref="IClientNetManager"/> or <see cref="IServerNetManager"/> when
///     side-specific functionality is needed.
/// </summary>
/// <remarks>
///     <para>
///         Content must not implement this interface; the engine's net manager is the only real implementation
///         (plus an in-process fake used by the integration tests).
///     </para>
///     <para>
///         Unless stated otherwise, members are meant to be called from the main (game) thread, that is the thread
///         that called <see cref="Initialize"/>. The interface is not thread-safe in general. Only
///         <see cref="CreateNetMessage{T}"/> carries an explicit thread-safety promise.
///     </para>
///     <para>
///         Every data packet consists of one byte holding the message type id followed by the message payload,
///         optionally encrypted. The ids are assigned by the server at registration time (starting at 1, ascending
///         in registration order; 0 is used by the string table message itself) and transmitted to each client
///         when it connects. Names, not ids, are what identifies a message type between the two ends. Because the
///         id is a single byte, at most 255 message types can be registered per manager, including the engine's
///         own.
///     </para>
/// </remarks>
[NotContentImplementable]
public interface INetManager
{
    /// <summary>
    ///     <see langword="true"/> once <see cref="Initialize"/> has been called with <c>true</c>. It is
    ///     <see langword="false"/> before initialization and on clients.
    /// </summary>
    bool IsServer { get; }

    /// <summary>
    ///     The exact negation of <see cref="IsServer"/>. Note that this means it is <see langword="true"/> before
    ///     <see cref="Initialize"/> has been called.
    /// </summary>
    bool IsClient { get; }

    /// <summary>
    ///     <see langword="true"/> while networking is started, meaning at least one underlying network peer
    ///     exists: after <see cref="StartServer"/>, or after a client has begun connecting. It is
    ///     <see langword="false"/> after <see cref="Shutdown"/> or a reset.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    ///     <see langword="true"/> if at least one network peer currently holds at least one transport
    ///     connection. On a server that means at least one client is connected; on a client it means the
    ///     transport connection to the server exists.
    /// </summary>
    /// <remarks>
    ///     This can become true slightly before the handshake has finished, i.e. before the
    ///     <see cref="Connected"/> event. Use that event, or the handshake state of the channel, to find out
    ///     whether a connection is fully established.
    /// </remarks>
    bool IsConnected { get; }

    /// <summary>
    ///     Traffic totals (bytes and packets, sent and received) summed over every peer of this manager. A new
    ///     snapshot is computed on each access.
    /// </summary>
    NetworkStats Statistics { get; }

    /// <summary>
    ///     The currently open channels, including those whose handshake has not completed yet. A client has zero
    ///     or one channel.
    /// </summary>
    /// <remarks>
    ///     This is a live view of the manager's internal collection. Do not connect, disconnect or otherwise
    ///     change channel membership while enumerating it; copy it first if that is needed.
    /// </remarks>
    IEnumerable<INetChannel> Channels { get; }

    /// <summary>
    ///     The number of entries in <see cref="Channels"/>.
    /// </summary>
    int ChannelCount { get; }

    /// <summary>
    ///     The configured network port. The value is read from the corresponding configuration variable on every
    ///     access, so it is the configured port and not necessarily the port that was actually bound.
    /// </summary>
    int Port { get; }

    /// <summary>
    ///     A live, read-only map from message CLR type to the total number of bytes received for it. The map is
    ///     only filled by debug builds of the engine and stays empty in release builds. It is not thread-safe.
    /// </summary>
    IReadOnlyDictionary<Type, long> MessageBandwidthUsage { get; }

    /// <summary>
    ///     Clears <see cref="MessageBandwidthUsage"/>.
    /// </summary>
    void ResetBandwidthMetrics();

    /// <summary>
    ///     Prepares the manager for use. Call it once per manager lifetime (again only after
    ///     <see cref="Shutdown"/>), before registering any message type or starting the network. The host
    ///     application does this at startup.
    /// </summary>
    /// <remarks>
    ///     This records which side the manager represents and remembers the calling thread as the main thread,
    ///     synchronizes the network library's clock with the game's real-time clock, subscribes to
    ///     network-related configuration variables, sets up the string table (including registering the internal
    ///     string table message) and, on a server, generates the encryption key pair.
    /// </remarks>
    /// <param name="isServer">
    ///     <see langword="true"/> to initialize as a server, <see langword="false"/> for a client.
    /// </param>
    /// <exception cref="InvalidOperationException">The manager has already been initialized.</exception>
    void Initialize(bool isServer);

    /// <summary>
    ///     Opens the listening network peers on the configured bind addresses (a comma separated list) and port.
    ///     Dual-stack and UPnP behaviour is driven by configuration. Only the server host calls this; content
    ///     never does.
    /// </summary>
    /// <remarks>
    ///     This is meant for servers that are not yet running. Violations are caught by debug assertions rather
    ///     than by exceptions. A warning is logged if no peer could be created, or if dual-stack was requested
    ///     without an IPv6 bind address.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     An entry of the configured bind address list is not a valid IP address.
    /// </exception>
    void StartServer();

    /// <summary>
    ///     Stops networking completely. This does everything a reset does: it disconnects all channels using
    ///     <paramref name="reason"/>, shuts down the peers (blocking the calling thread, while polling, until they
    ///     have finished) and clears the cached id-to-message table and the string table.
    /// </summary>
    /// <remarks>
    ///     In addition, shutdown forgets every registered message type, unsubscribes the configuration hooks,
    ///     removes all subscribers from <see cref="Connecting"/>, <see cref="Connected"/> and
    ///     <see cref="Disconnect"/> (and from the client-only connect-failed event), and marks the manager as
    ///     uninitialized so that <see cref="Initialize"/> may be called again. Anything that wants events or
    ///     message handlers after a restart must therefore subscribe and register again. A client-side reset, in
    ///     contrast, keeps registrations and subscribers and only clears the id table.
    /// </remarks>
    /// <param name="reason">The text given to disconnected channels as the reason.</param>
    void Shutdown(string reason);

    /// <summary>
    ///     Drains and handles everything the network peers have received: log output from the network library,
    ///     connection status changes (which lead to channel creation or removal and to the
    ///     <see cref="Connected"/> and <see cref="Disconnect"/> events) and data packets (which are decrypted,
    ///     turned into messages, read with <see cref="NetMessage.ReadFromBuffer"/> and handed to the callback
    ///     registered for them). It also updates the network metrics.
    /// </summary>
    /// <remarks>
    ///     Call this frequently (every frame or tick) from the main thread. All receive callbacks and the
    ///     events, other than <see cref="Connecting"/>, run on that thread inside this call. The method does not
    ///     block waiting for traffic.
    ///     <para>
    ///         Packets are handled as follows. A packet is ignored if the peer is shutting down or not running.
    ///         A zero-length packet, or a packet from a connection that has no channel yet, causes that connection
    ///         to be disconnected, as does a packet that fails decryption. The first byte is the message id; if no
    ///         receive callback is registered for it the channel is disconnected. A message that is not flagged
    ///         <see cref="NetMessageAccept.Handshake"/> and arrives before the channel's handshake is complete
    ///         disconnects the channel. If <see cref="NetMessage.ReadFromBuffer"/> throws anything, the channel is
    ///         disconnected ("Failed to deserialize packet.") and the message is dropped. An exception thrown by
    ///         a receive callback is logged and swallowed; it disconnects nobody and does not stop later packets
    ///         from being processed.
    ///     </para>
    /// </remarks>
    void ProcessPackets();

    /// <summary>
    ///     Sends <paramref name="message"/> to every connected client. Server only.
    /// </summary>
    /// <remarks>
    ///     Returns silently if there are no connections. Channels whose handshake has not completed are skipped.
    ///     Otherwise this behaves like <see cref="ServerSendMessage"/> for each remaining channel, so the message
    ///     is serialized once per channel. See <see cref="ServerSendMessage"/> for the failure behaviour common to
    ///     all send methods. Passing <see langword="null"/> is not supported.
    /// </remarks>
    /// <param name="message">The message that is sent to every eligible channel.</param>
    void ServerSendToAll(NetMessage message);

    /// <summary>
    ///     Sends <paramref name="message"/> to a single client. Server only.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Sending is fire-and-forget. The message is serialized immediately on the caller's thread, then its
    ///         <see cref="NetMessage.DeliveryMethod"/> and <see cref="NetMessage.SequenceChannel"/> are read, then
    ///         the packet is encrypted (when the channel is encrypted) and handed to the transport. Success of the
    ///         delivery is not reported; a refusal from the transport is only logged as a warning. The first
    ///         byte written is the wire id looked up from <see cref="NetMessage.MsgName"/>.
    ///     </para>
    ///     <para>
    ///         Messages with ordered or sequenced delivery are encrypted and sent in order per channel; other
    ///         messages may be encrypted on a worker thread when sent from the main thread.
    ///     </para>
    ///     <para>
    ///         If the manager has not been initialized, nothing happens and no error is raised, which covers
    ///         sends that happen during shutdown ordering. If the target channel is no longer connected, an error
    ///         with a stack trace is logged and the method returns. This method does not check whether the
    ///         channel's handshake has completed, so handshake-flagged messages can be sent during the handshake
    ///         window.
    ///     </para>
    /// </remarks>
    /// <param name="message">The message to send.</param>
    /// <param name="recipient">The channel to send to.</param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="recipient"/> is not an instance of the engine's own channel class (for example a
    ///     mocked or foreign <see cref="INetChannel"/>). The parameter name is <c>recipient</c>.
    /// </exception>
    /// <exception cref="NetManagerException">
    ///     The message's name was never registered with this manager. This is thrown before anything is sent.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <see cref="NetMessage.DeliveryMethod"/> threw, for example because the message never overrode
    ///     <see cref="NetMessage.MsgGroup"/>. This happens after the message was serialized.
    /// </exception>
    void ServerSendMessage(NetMessage message, INetChannel recipient);

    /// <summary>
    ///     Sends <paramref name="message"/> to each channel in <paramref name="recipients"/>, in list order,
    ///     using <see cref="ServerSendMessage"/>. Server only.
    /// </summary>
    /// <remarks>
    ///     Returns silently if the manager has no connections at all. Unlike <see cref="ServerSendToAll"/>, this
    ///     does not skip channels whose handshake is incomplete. If sending to one recipient throws (for example
    ///     the <see cref="ArgumentException"/> described at <see cref="ServerSendMessage"/>), the loop is
    ///     aborted: earlier recipients have already been sent to, later ones will not be. A
    ///     <see langword="null"/> list causes an exception. The parameter is deliberately a
    ///     <see cref="List{T}"/> and not an <see cref="IEnumerable{T}"/>.
    /// </remarks>
    /// <param name="message">The message to send.</param>
    /// <param name="recipients">The channels to send to.</param>
    void ServerSendToMany(NetMessage message, List<INetChannel> recipients);

    /// <summary>
    ///     Sends <paramref name="message"/> to the server over the single client channel. Client only.
    /// </summary>
    /// <remarks>
    ///     This must only be used on a manager that was initialized as a client (<c>Initialize(false)</c>). If
    ///     the client is not connected, an error with a stack trace is logged and the method returns without
    ///     throwing. Calling it in the very early part of connecting, when the transport is connected but the
    ///     channel object has not been created yet, is not supported and may fail unpredictably. The send
    ///     behaviour and failure modes described at <see cref="ServerSendMessage"/> otherwise apply.
    /// </remarks>
    /// <param name="message">The message to send.</param>
    void ClientSendMessage(NetMessage message);

    /// <summary>
    ///     Raised on the server while an incoming connection is being logged in, after the user's identity
    ///     (account, or guest name and id) is known and before the channel is created and the player counts as
    ///     connected. Server only.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Handlers are awaited one after another in subscription order and all receive the same
    ///         <see cref="NetConnectingArgs"/> instance. Every handler runs even if an earlier one denied the
    ///         connection; denial is only recorded on the arguments, and a later denial replaces the stored
    ///         reason. Afterwards, if the arguments are denied, the connection is dropped with a
    ///         "Connect denied" message containing the reason; otherwise login continues and an older connection
    ///         by the same user id is kicked first.
    ///     </para>
    ///     <para>
    ///         Handlers return a <see cref="Task"/> so that they can do asynchronous work such as database
    ///         lookups; the task should complete and not fault, and exceptions are not caught by the code that
    ///         raises the event. Adding and removing handlers is not thread-safe, and subscribing the same handler
    ///         twice runs it twice. <see cref="Shutdown"/> removes all subscribers.
    ///     </para>
    /// </remarks>
    event Func<NetConnectingArgs, Task> Connecting;

    /// <summary>
    ///     Raised once per channel after the entire connection handshake has completed: on a server after the
    ///     serializer and file-transfer handshakes with that client have finished, on a client after both have
    ///     completed with the server. <see cref="NetChannelArgs.Channel"/> is the new channel.
    /// </summary>
    /// <remarks>
    ///     The channel is marked as handshake-complete immediately before the event is raised, so handlers may
    ///     already send messages. It is raised from the main flow of packet processing. Handlers should not
    ///     throw. <see cref="Shutdown"/> removes all subscribers.
    /// </remarks>
    event EventHandler<NetChannelArgs> Connected;

    /// <summary>
    ///     Raised after a channel has been removed from the manager (so it no longer appears in
    ///     <see cref="Channels"/>), for whatever reason: the peer left, was kicked, timed out or the manager shut
    ///     down. On a client it is also raised when the server connection is lost. The arguments carry the
    ///     channel and the decoded reason, consisting of text and a redial flag.
    /// </summary>
    /// <remarks>
    ///     Handlers should not throw. An exception from a handler propagates, unless the engine was built with
    ///     its exception-tolerance symbol, in which case it is caught and logged so the internal state remains
    ///     consistent. <see cref="Shutdown"/> removes all subscribers. A failed attempt to establish an outgoing
    ///     connection is not reported through this event or the other two; see
    ///     <see cref="IClientNetManager"/> for the connect-failed event.
    /// </remarks>
    event EventHandler<NetDisconnectedArgs> Disconnect;

    /// <summary>
    ///     Registers a message type so that it can be sent and, optionally, received. This is called during the
    ///     initialization of managers and systems, before any connection is made (on the server before clients
    ///     join, otherwise they are told about new ids later by a table update; on a client before connecting).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The type is identified by its <see cref="NetMessage.MsgName"/>, which is read from a throw-away
    ///         instance made with the public parameterless constructor (this is why <typeparamref name="T"/>
    ///         needs <c>new()</c>). The name is added to the string table. On a server this immediately assigns
    ///         the next free wire id, in registration order, and broadcasts the new entry to connected clients if
    ///         the server is already running. On a client nothing is assigned, as the table is received from the
    ///         server on connecting.
    ///     </para>
    ///     <para>
    ///         Registering a name that is already registered on the same manager, whether by a different type or
    ///         by the same type again, throws <see cref="ArgumentException"/>. There is no replace semantics and
    ///         no way to add a second handler: one name has one callback, set only here. Client and server are
    ///         separate manager instances, so registering the same type on both from shared code is fine.
    ///     </para>
    ///     <para>
    ///         The callback is attached only if <paramref name="rxCallback"/> is not <see langword="null"/> and
    ///         <paramref name="accept"/> contains the flag for the side this manager runs on
    ///         (<see cref="NetMessageAccept.Server"/> on a server, <see cref="NetMessageAccept.Client"/> on a
    ///         client). A callback that is dropped because of <paramref name="accept"/> is ignored without error.
    ///         The type is registered either way, so it can still be sent and its name is in the string table, but
    ///         receiving a message that has no callback is a protocol violation and disconnects the sender.
    ///         Passing no callback is therefore the right choice for message types that this side only sends.
    ///     </para>
    ///     <para>
    ///         <see cref="NetMessageAccept.Handshake"/> in <paramref name="accept"/> allows the message to be
    ///         received before the channel's initial handshake has completed; unflagged messages arriving earlier
    ///         get the sender disconnected.
    ///     </para>
    ///     <para>
    ///         When a callback is attached, the manager caches the id-to-type mapping: immediately on a server,
    ///         and on a client whenever the id table arrives from the server (again after every reconnect). The
    ///         callback runs once per received message on the main thread inside <see cref="ProcessPackets"/>,
    ///         with the deserialized message whose <see cref="NetMessage.MsgChannel"/> is set. Exceptions it
    ///         throws are logged and swallowed.
    ///     </para>
    ///     <para>
    ///         Wire ids are a single byte, so no more than about 255 message types, counting the engine's own,
    ///         can be registered or received per manager. Ids beyond 255 are never mapped for receiving.
    ///     </para>
    /// </remarks>
    /// <typeparam name="T">The message type. It must have a public parameterless constructor.</typeparam>
    /// <param name="rxCallback">
    ///     Invoked for every received message of this type, or <see langword="null"/> for none.
    /// </param>
    /// <param name="accept">
    ///     Which side(s) may receive the message, plus the optional <see cref="NetMessageAccept.Handshake"/> flag.
    ///     Defaults to <see cref="NetMessageAccept.Both"/>.
    /// </param>
    /// <exception cref="ArgumentException">A message with the same name is already registered.</exception>
    void RegisterNetMessage<T>(ProcessMessage<T>? rxCallback = null,
        NetMessageAccept accept = NetMessageAccept.Both)
        where T : NetMessage, new();

    /// <summary>
    ///     Creates a new, default-constructed message of type <typeparamref name="T"/>. It needs no
    ///     registration and has no channel set. This method may be called from any thread and does not throw.
    /// </summary>
    /// <remarks>
    ///     Kept only for source compatibility. The integration-test implementation additionally verifies that
    ///     the type was registered.
    /// </remarks>
    /// <typeparam name="T">The message type.</typeparam>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    [Obsolete("Construct the message directly with 'new' instead of asking the net manager to create it.")]
    T CreateNetMessage<T>() where T : NetMessage, new();
}
