using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Lidgren.Network;
using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Player;
using Robust.Shared.Profiling;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;
using SpaceWizards.Sodium;

namespace Robust.Shared.Network
{
    /// <summary>
    ///     Untyped receive callback for a registered <see cref="NetMessage"/>. This is the form that is stored
    ///     per message type inside the net manager; typed callbacks are wrapped into it at registration time.
    /// </summary>
    /// <param name="message">The freshly deserialized incoming message.</param>
    public delegate void ProcessMessage(NetMessage message);

    /// <summary>
    ///     Typed receive callback handed to <see cref="INetManager.RegisterNetMessage{T}"/>.
    /// </summary>
    /// <typeparam name="T">The message type the callback handles.</typeparam>
    /// <param name="message">The freshly deserialized incoming message.</param>
    public delegate void ProcessMessage<in T>(T message) where T : NetMessage;

    /// <summary>
    ///     Owns the network connections and handles sending and receiving of packets.
    /// </summary>
    public sealed partial class NetManager : IClientNetManager, IServerNetManager, IPostInjectInit
    {
        internal const int SharedKeyLength = CryptoAeadXChaCha20Poly1305Ietf.KeyBytes; // 32 bytes

        private static readonly Counter SentPacketsMetrics = Metrics.CreateCounter(
            "robust_net_sent_packets",
            "Number of packets sent since server startup.");

        private static readonly Counter RecvPacketsMetrics = Metrics.CreateCounter(
            "robust_net_recv_packets",
            "Number of packets received since server startup.");

        private static readonly Counter SentMessagesMetrics = Metrics.CreateCounter(
            "robust_net_sent_messages",
            "Number of messages sent since server startup.");

        private static readonly Counter RecvMessagesMetrics = Metrics.CreateCounter(
            "robust_net_recv_messages",
            "Number of messages received since server startup.");

        private static readonly Counter SentBytesMetrics = Metrics.CreateCounter(
            "robust_net_sent_bytes",
            "Number of bytes sent since server startup.");

        private static readonly Counter RecvBytesMetrics = Metrics.CreateCounter(
            "robust_net_recv_bytes",
            "Number of bytes received since server startup.");

        private static readonly Counter MessagesResentDelayMetrics = Metrics.CreateCounter(
            "robust_net_resent_delay",
            "Number of messages that had to be re-sent due to delay.");

        private static readonly Counter MessagesResentHoleMetrics = Metrics.CreateCounter(
            "robust_net_resent_hole",
            "Number of messages that had to be re-sent due to holes.");

        private static readonly Counter MessagesDroppedMetrics = Metrics.CreateCounter(
            "robust_net_dropped",
            "Number of incoming messages that have been dropped.");

        // TODO: Disabled for now since calculating these from Lidgren is way too expensive.
        // Need to go through and have Lidgren properly keep track of counters for these.
        /*
        private static readonly Gauge MessagesStoredMetrics = Metrics.CreateGauge(
            "robust_net_stored",
            "Number of stored messages for reliable resending (if necessary).");

        private static readonly Gauge MessagesUnsentMetrics = Metrics.CreateGauge(
            "robust_net_unsent",
            "Number of queued (unsent) messages that have yet to be sent.");
        */

        /// <summary>
        ///     Looks up the NetChannel that belongs to a NetConnection.
        /// </summary>
        private readonly Dictionary<NetConnection, NetChannel> _channels = new();

        private readonly Dictionary<string, NetConnection> _assignedUsernames = new();

        private readonly Dictionary<NetUserId, NetConnection> _assignedUserIds =
            new();

        // Used for processing incoming net messages.
        private readonly MessageData?[] _netMsgIndices = new MessageData?[256];

        private readonly Dictionary<Type, long> _bandwidthUsage = new();

        [Dependency] private readonly IRobustSerializer _serializer = default!;
        [Dependency] private readonly IConfigurationManagerInternal _config = default!;
        [Dependency] private readonly IAuthManager _authManager = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly ILogManager _logMan = default!;
        [Dependency] private readonly ProfManager _prof = default!;
        [Dependency] private readonly HttpClientHolder _http = default!;
        [Dependency] private readonly IHWId _hwId = default!;
        [Dependency] private readonly ITransferManager _transfer = default!;

        /// <summary>
        ///     Whether we bother to log problematic packets. Set by <see cref="CVars.NetLogging"/>.
        /// </summary>
        private bool _logPacketIssues = false;

        /// <summary>
        ///     Per-message registration data, keyed by the name of the message.
        /// </summary>
        private readonly Dictionary<string, MessageData> _messages = new();

        /// <summary>
        /// Table that translates between the numeric ids used in packets and message names.
        /// </summary>
        private readonly StringTable _strings;

        /// <summary>
        ///     The list of network peers we are listening on.
        /// </summary>
        private readonly List<NetPeerData> _netPeers = new();

        // Client connect happens during status changed and such callbacks, so we need to defer deletion of these.
        private readonly List<NetPeer> _toCleanNetPeers = new();

        private readonly Dictionary<NetConnection, TaskCompletionSource<object?>> _awaitingDisconnect
            = new();

        private readonly HashSet<NetUserId> _awaitingDisconnectToConnect = new HashSet<NetUserId>();

        private ISawmill _logger = default!;
        private ISawmill _loggerPacket = default!;
        private ISawmill _authLogger = default!;

        private readonly ConcurrentDictionary<IPAddress, (int TotalCount, DateTime LastSeen)> _decryptFailCounts = new();
        private DateTime _lastDecryptFailCleanup = DateTime.UtcNow;

        private bool _clientSerializerComplete;
        private bool _clientTransferComplete;
        private bool _clientResetPending;

        /// <inheritdoc />
        public int Port => _config.GetCVar(CVars.NetPort);

        public bool IsAuthEnabled => _config.GetCVar<bool>("auth.enabled");

        public IReadOnlyDictionary<Type, long> MessageBandwidthUsage => _bandwidthUsage;

        internal StringTable StringTable => _strings;

        /// <inheritdoc />
        public bool IsServer { get; private set; }

        /// <inheritdoc />
        public bool IsClient => !IsServer;

        /// <inheritdoc />
        public bool IsConnected
        {
            get
            {
                foreach (var p in _netPeers)
                {
                    if (p.Peer.ConnectionsCount > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool IsRunning => _netPeers.Count != 0;

        public NetworkStats Statistics
        {
            get
            {
                var sentPackets = 0L;
                var sentBytes = 0L;
                var recvPackets = 0L;
                var recvBytes = 0L;

                foreach (var peer in _netPeers)
                {
                    var netPeerStatistics = peer.Peer.Statistics;
                    sentPackets += netPeerStatistics.SentPackets;
                    sentBytes += netPeerStatistics.SentBytes;
                    recvPackets += netPeerStatistics.ReceivedPackets;
                    recvBytes += netPeerStatistics.ReceivedBytes;
                }

                return new NetworkStats(sentBytes, recvBytes, sentPackets, recvPackets);
            }
        }

        /// <inheritdoc />
        [ViewVariables]
        public IEnumerable<INetChannel> Channels => _channels.Values;

        /// <inheritdoc />
        public int ChannelCount => _channels.Count;

        public IReadOnlyDictionary<Type, ProcessMessage> CallbackAudit => _messages
            .Where(e => e.Value.Callback != null)
            .ToDictionary(e => e.Value.Type, e => e.Value.Callback)!;

        /// <inheritdoc />
        public INetChannel? ServerChannel => ServerChannelImpl;

        private NetChannel? ServerChannelImpl
        {
            get
            {
                DebugTools.Assert(IsClient);

                if (_netPeers.Count == 0)
                {
                    return null;
                }

                var peer = _netPeers[0];
                return peer.Channels.Count == 0 ? null : peer.Channels[0];
            }
        }

        private bool _initialized;

        private int _mainThreadId;

        public NetManager()
        {
            _strings = new StringTable(this);
        }

        public void ResetBandwidthMetrics()
        {
            _bandwidthUsage.Clear();
        }

        /// <summary>
        ///     Prepares this manager for use as either a server or a client. Must be called exactly once before
        ///     <see cref="Shutdown"/> is called; after a shutdown it may be called again.
        /// </summary>
        /// <param name="isServer">True to act as a server, false to act as a client.</param>
        /// <exception cref="InvalidOperationException">The manager is already initialized.</exception>
        /// <remarks>
        ///     <see cref="IsServer"/> is assigned before the string table is initialized, because the table
        ///     registers a net message whose registration behavior depends on the side.
        ///     The <see cref="CVars.NetLogging"/> subscription does not fire immediately, so packet-issue logging
        ///     stays off until that CVar changes after initialization.
        /// </remarks>
        public void Initialize(bool isServer)
        {
            if (_initialized)
            {
                throw new InvalidOperationException("NetManager has already been initialized.");
            }

            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            _strings.Sawmill = _logger;

            SynchronizeNetTime();

            IsServer = isServer;

            _config.OnValueChanged(CVars.NetLidgrenLogWarning, LidgrenLogWarningChanged);
            _config.OnValueChanged(CVars.NetLidgrenLogError, LidgrenLogErrorChanged);
            _config.OnValueChanged(CVars.NetVerbose, NetVerboseChanged);
            _config.OnValueChanged(CVars.NetLogging, NetLoggingChanged);

            if (isServer)
            {
                // Immediate invocation so that Auth is valid as soon as Initialize returns.
                _config.OnValueChanged(CVars.AuthMode, OnAuthModeChanged, invokeImmediately: true);
            }

            _config.OnValueChanged(CVars.NetFakeLoss, _fakeLossChanged);
            _config.OnValueChanged(CVars.NetFakeLagMin, _fakeLagMinChanged);
            _config.OnValueChanged(CVars.NetFakeLagRand, _fakeLagRandomChanged);
            _config.OnValueChanged(CVars.NetFakeDuplicates, FakeDuplicatesChanged);

            _strings.Initialize(() => { _logger.Info("Message string table loaded."); }, UpdateNetMessageFunctions);

            _serializer.ClientHandshakeComplete += OnSerializerOnClientHandshakeComplete;
            _transfer.ClientHandshakeComplete += OnTransferOnClientHandshakeComplete;

            _initialized = true;

            if (IsServer)
            {
                SAGenerateKeys();
            }
        }

        private void NetLoggingChanged(bool obj)
        {
            _logPacketIssues = obj;
        }

        private void LidgrenLogWarningChanged(bool newValue)
        {
            foreach (var netPeer in _netPeers)
            {
                netPeer.Peer.Configuration.SetMessageTypeEnabled(NetIncomingMessageType.WarningMessage, newValue);
            }
        }

        private void LidgrenLogErrorChanged(bool newValue)
        {
            foreach (var netPeer in _netPeers)
            {
                netPeer.Peer.Configuration.SetMessageTypeEnabled(NetIncomingMessageType.ErrorMessage, newValue);
            }
        }

        private void OnAuthModeChanged(int mode)
        {
            Auth = (AuthMode)mode;
        }

        private void OnSerializerOnClientHandshakeComplete()
        {
            _logger.Info("Client completed serializer handshake.");
            _clientSerializerComplete = true;
            ClientCheckSwitchToConnected();
        }

        private void OnTransferOnClientHandshakeComplete()
        {
            _logger.Info("Client completed transfer handshake.");
            _clientTransferComplete = true;
            ClientCheckSwitchToConnected();
        }

        private void ClientCheckSwitchToConnected()
        {
            if (_clientSerializerComplete && _clientTransferComplete)
                OnConnected(ServerChannelImpl!);
        }

        private void SynchronizeNetTime()
        {
            // Synchronize Lidgren NetTime with our RealTime.

            for (var i = 0; i < 10; i++)
            {
                // Try and set this in a loop to avoid any JIT hang fuckery or similar.
                // Loop until the time is within acceptable margin.
                // Fixing this "properly" would basically require re-architecturing Lidgren to do DI stuff
                // so we can more sanely wire these together.
                NetTime.SetNow(_timing.RealTime.TotalSeconds);
                var dev = TimeSpan.FromSeconds(NetTime.Now) - _timing.RealTime;

                if (Math.Abs(dev.TotalMilliseconds) < 0.05)
                    break;
            }
        }

        private void UpdateNetMessageFunctions(MsgStringTableEntries.Entry[] entries)
        {
            foreach (var entry in entries)
            {
                if (entry.Id > byte.MaxValue)
                    continue;

                CacheNetMsgIndex(entry.Id, entry.String);
            }
        }

        private void NetVerboseChanged(bool on)
        {
            foreach (var peer in _netPeers)
            {
                peer.Peer.Configuration.SetMessageTypeEnabled(NetIncomingMessageType.VerboseDebugMessage, on);
            }
        }

        /// <summary>
        ///     Binds one Lidgren peer per address listed in the bind-address CVar and starts listening.
        /// </summary>
        /// <exception cref="InvalidOperationException">An entry of the bind-address list is not a valid IP address.</exception>
        /// <remarks>
        ///     There is no rollback: if an entry fails to parse or a socket cannot be bound, the peers created
        ///     for earlier entries stay started and registered. Note that splitting an empty CVar value yields
        ///     one empty entry, which is rejected as an invalid address.
        /// </remarks>
        public void StartServer()
        {
            DebugTools.Assert(IsServer);
            DebugTools.Assert(!IsRunning);

            var bindAddresses = _config.GetCVar(CVars.NetBindTo).Split(',');
            var dualStack = _config.GetCVar(CVars.NetDualStack);
            var useUpnp = _config.GetCVar(CVars.NetUPnP);
            var foundIpv6 = false;

            foreach (var rawAddress in bindAddresses)
            {
                if (!IPAddress.TryParse(rawAddress.Trim(), out var address))
                {
                    throw new InvalidOperationException("Not a valid IPv4 or IPv6 address");
                }

                var config = _getBaseNetPeerConfig();
                config.LocalAddress = address;
                config.Port = Port;

                if (address.AddressFamily == AddressFamily.InterNetworkV6 && dualStack)
                {
                    foundIpv6 = true;
                    config.DualStack = true;
                }

                if (UpnpCompatible(config) && useUpnp)
                {
                    config.EnableUPnP = true;
                }

                NetPeer peer;
                if (IsServer)
                {
                    peer = new NetServer(config);
                }
                else
                {
                    peer = new NetClient(config);
                }

                // Binds the socket. A failure escapes from here, and this peer is then not registered.
                peer.Start();

                _netPeers.Add(new NetPeerData(peer));
            }

            if (_netPeers.Count == 0)
            {
                _logger.Warning("Not binding to any address! Nothing will be able to connect to the server.");
            }

            if (dualStack && !foundIpv6)
            {
                _logger.Warning("Dual stack is enabled, but no IPv6 address was bound. Dual stack will not work.");
            }

            if (useUpnp)
            {
                InitUpnp();
            }
        }

        /// <summary>
        ///     Tears down all transport state: disconnects every channel, shuts down every Lidgren peer (blocking
        ///     until they have stopped) and returns the string table and connect state to their initial values.
        ///     Registered message types and event subscribers are kept.
        /// </summary>
        /// <param name="reason">The reason text that is passed to the remote ends and to Lidgren.</param>
        /// <remarks>
        ///     <para>
        ///         This blocks the calling thread, without a timeout, until no peer is in the shutdown-requested state.
        ///     </para>
        ///     <para>
        ///         The channel tables are not cleared here. Peers are dropped before their disconnect notifications
        ///         could be pumped, so stale entries in the channel tables can outlive a reset. The
        ///         <see cref="Disconnect"/> event is not raised by this method.
        ///     </para>
        ///     <para>
        ///         The message-index cache must be cleared before the string table is reset: on a client the table
        ///         reset reseeds id 0 through the update callback, which writes into that cache.
        ///     </para>
        /// </remarks>
        public void Reset(string reason)
        {
            _logger.Info($"Resetting NetManager: {reason}");

            _clientResetPending = false;

            foreach (var channel in _channels.Values)
            {
                DisconnectChannel(channel, reason);
            }

            foreach (var peerData in _netPeers)
            {
                peerData.Peer.Shutdown(reason);
            }

            // Lidgren has no handle to join on; the network thread moves the status away from
            // ShutdownRequested once it has finished.
            while (true)
            {
                var stillShuttingDown = false;
                foreach (var peerData in _netPeers)
                {
                    if (peerData.Peer.Status == NetPeerStatus.ShutdownRequested)
                    {
                        stillShuttingDown = true;
                        break;
                    }
                }

                if (!stillShuttingDown)
                    break;

                Thread.Sleep(50);
            }

            _netPeers.Clear();

            for (var i = 0; i < _netMsgIndices.Length; i++)
            {
                _netMsgIndices[i] = null;
            }

            _strings.Reset();

            _cancelConnectTokenSource?.Cancel();

            ClientConnectState = ClientConnectionState.NotConnecting;

            _clientSerializerComplete = false;
            _clientTransferComplete = false;
        }

        /// <summary>
        ///     Resets the manager, forgets all registered message types and detaches from CVars and handshake
        ///     events so that the manager can be initialized again.
        /// </summary>
        /// <param name="reason">The reason text, forwarded to <see cref="Reset"/>.</param>
        /// <remarks>
        ///     The <see cref="CVars.NetLogging"/> subscription and the transfer-manager handshake subscription are
        ///     intentionally not removed, so a later <see cref="Initialize"/> subscribes them a second time.
        ///     Subscribers of <see cref="ConnectFailed"/>, <see cref="Connected"/>, <see cref="Disconnect"/> and
        ///     <see cref="Connecting"/> are dropped; subscribers of the client connect-state event are kept.
        /// </remarks>
        public void Shutdown(string reason)
        {
            Reset(reason);

            _messages.Clear();

            _config.UnsubValueChanged(CVars.NetVerbose, NetVerboseChanged);

            if (IsServer)
            {
                _config.UnsubValueChanged(CVars.AuthMode, OnAuthModeChanged);
            }

            _config.UnsubValueChanged(CVars.NetFakeLoss, _fakeLossChanged);
            _config.UnsubValueChanged(CVars.NetFakeLagMin, _fakeLagMinChanged);
            _config.UnsubValueChanged(CVars.NetFakeLagRand, _fakeLagRandomChanged);
            _config.UnsubValueChanged(CVars.NetFakeDuplicates, FakeDuplicatesChanged);
            _config.UnsubValueChanged(CVars.NetLidgrenLogWarning, LidgrenLogWarningChanged);
            _config.UnsubValueChanged(CVars.NetLidgrenLogError, LidgrenLogErrorChanged);

            _serializer.ClientHandshakeComplete -= OnSerializerOnClientHandshakeComplete;

            ConnectFailed = null;
            Connected = null;
            Disconnect = null;
            _connectingEvent.Clear();

            _initialized = false;
        }

        private static IPAddress NormalizeIp(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip;
            var bytes = ip.GetAddressBytes();
            for (var i = 8; i < 16; i++) bytes[i] = 0;
            return new IPAddress(bytes);
        }

        private void CleanupDecryptFailCounts()
        {
            if (!IsServer) return;
            var now = DateTime.UtcNow;
            var intervalMinutes = _config.GetCVar(CVars.NetDecryptFailCleanupInterval);
            if ((now - _lastDecryptFailCleanup).TotalMinutes < intervalMinutes) return;
            _lastDecryptFailCleanup = now;
            foreach (var (ip, (_, lastSeen)) in _decryptFailCounts)
            { if ((now - lastSeen).TotalMinutes >= intervalMinutes) _decryptFailCounts.TryRemove(ip, out _); }
        }

        /// <summary>
        ///     Pumps every Lidgren peer: reads all pending incoming messages, dispatches them, handles connection
        ///     status changes, publishes traffic metrics and finally performs a deferred client reset if one was
        ///     requested. Must be called on the main thread once per tick; it never waits for traffic.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         There is deliberately no per-packet exception handling here. An exception thrown by a status
        ///         change handler (or by the unguarded steps of message dispatch) leaves this method and abandons
        ///         the rest of the pass, including peer cleanup, metrics and the deferred reset. Malformed or
        ///         hostile data packets are isolated inside the dispatcher, which disconnects the sender instead of
        ///         throwing.
        ///     </para>
        ///     <para>
        ///         Continuations of tasks completed from here (client connect, handshakes) run synchronously inside
        ///         this call. Those continuations may append to the peer list, so the peer list is iterated by index.
        ///         Peers that were discarded in the meantime are only removed after all peers were processed.
        ///     </para>
        ///     <para>
        ///         The recycle flag is kept per peer, not per message: once a message was kept by an awaiter, later
        ///         non-data messages of the same peer in this call are not recycled either. This only affects
        ///         Lidgren's buffer pooling.
        ///     </para>
        /// </remarks>
        public void ProcessPackets()
        {
            CleanupDecryptFailCounts();

            var sentMessages = 0L;
            var recvMessages = 0L;
            var sentBytes = 0L;
            var recvBytes = 0L;
            var sentPackets = 0L;
            var recvPackets = 0L;
            var resentDelay = 0L;
            var resentHole = 0L;
            var dropped = 0L;

            var messagesProcessed = 0;
            var dataMessagesProcessed = 0;

            for (var i = 0; i < _netPeers.Count; i++)
            {
                var peer = _netPeers[i];
                var recycle = true;

                NetIncomingMessage? msg;
                while ((msg = peer.Peer.ReadMessage()) != null)
                {
                    messagesProcessed += 1;

                    switch (msg.MessageType)
                    {
                        case NetIncomingMessageType.VerboseDebugMessage:
                            _logger.Debug($"{peer.Peer.Configuration.LocalAddress}: {msg.ReadString()}");
                            break;

                        case NetIncomingMessageType.DebugMessage:
                            _logger.Info($"{peer.Peer.Configuration.LocalAddress}: {msg.ReadString()}");
                            break;

                        case NetIncomingMessageType.WarningMessage:
                            _logger.Warning($"{peer.Peer.Configuration.LocalAddress}: {msg.ReadString()}");
                            break;

                        case NetIncomingMessageType.ErrorMessage:
                            _logger.Error($"{peer.Peer.Configuration.LocalAddress}: {msg.ReadString()}");
                            break;

                        case NetIncomingMessageType.ConnectionApproval:
                            // The asynchronous approval handler keeps using the message.
                            HandleApproval(msg);
                            recycle = false;
                            break;

                        case NetIncomingMessageType.Data:
                            dataMessagesProcessed += 1;
                            recycle = DispatchNetMessage(msg);
                            break;

                        case NetIncomingMessageType.StatusChanged:
                            HandleStatusChanged(peer, msg);
                            break;

                        default:
                            _logger.Warning(
                                $"{peer.Peer.Configuration.LocalAddress}: Unhandled incoming packet type from " +
                                $"{msg.SenderConnection?.RemoteEndPoint}: {msg.MessageType}");
                            break;
                    }

                    if (recycle)
                    {
                        peer.Peer.Recycle(msg);
                    }
                }

                var stats = peer.Peer.Statistics;
                sentMessages += stats.SentMessages;
                recvMessages += stats.ReceivedMessages;
                sentBytes += stats.SentBytes;
                recvBytes += stats.ReceivedBytes;
                sentPackets += stats.SentPackets;
                recvPackets += stats.ReceivedPackets;
                resentDelay += stats.ResentMessagesDueToDelay;
                resentHole += stats.ResentMessagesDueToHole;
                dropped += stats.DroppedMessages;
            }

            if (_toCleanNetPeers.Count != 0)
            {
                foreach (var peerToClean in _toCleanNetPeers)
                {
                    _netPeers.RemoveAll(p => p.Peer == peerToClean);
                }

                _toCleanNetPeers.Clear();
            }

            // IncTo only ever raises a counter, so the metrics stay monotonic even if the sums shrink
            // because a peer was removed above.
            SentMessagesMetrics.IncTo(sentMessages);
            RecvMessagesMetrics.IncTo(recvMessages);
            SentBytesMetrics.IncTo(sentBytes);
            RecvBytesMetrics.IncTo(recvBytes);
            SentPacketsMetrics.IncTo(sentPackets);
            RecvPacketsMetrics.IncTo(recvPackets);
            MessagesResentDelayMetrics.IncTo(resentDelay);
            MessagesResentHoleMetrics.IncTo(resentHole);
            MessagesDroppedMetrics.IncTo(dropped);

            _prof.WriteValue("Count Processed", messagesProcessed);
            _prof.WriteValue("Count Data Processed", dataMessagesProcessed);

            if (_clientResetPending)
            {
                Reset("Channel closed");
            }
        }

        /// <inheritdoc />
        /// <remarks>
        ///     The <see cref="Disconnect"/> event is raised directly, before any teardown, while the server channel
        ///     is still listed in <see cref="Channels"/>. The reset afterwards drops the peers, so the event is not
        ///     raised a second time for the same channel. Exceptions from event handlers skip the reset.
        /// </remarks>
        public void ClientDisconnect(string reason)
        {
            DebugTools.Assert(IsClient, "Should never be called on the server.");

            if (ClientConnectState != ClientConnectionState.NotConnecting)
            {
                // Aborts a connection attempt that is still in progress.
                _cancelConnectTokenSource?.Cancel();
            }

            var serverChannel = ServerChannel;
            if (serverChannel != null)
            {
                Disconnect?.Invoke(this, new NetDisconnectedArgs(serverChannel, reason));
            }

            Reset(reason);
        }

        /// <summary>
        ///     Creates a Lidgren peer configuration that reflects the current CVar values for the side of this
        ///     manager. The local address and port are left for the caller to set.
        /// </summary>
        /// <remarks>
        ///     In debug builds the connection timeout is forced to 30 seconds for both sides, which overrides the
        ///     client timeout CVar. On a server in release builds the connection timeout and the handshake resend
        ///     settings keep Lidgren's defaults.
        /// </remarks>
        private NetPeerConfiguration _getBaseNetPeerConfig()
        {
            var config = new NetPeerConfiguration(_config.GetCVar(CVars.NetLidgrenAppIdentifier));

            config.PingInterval = 1f;

            config.SetMessageTypeEnabled(NetIncomingMessageType.WarningMessage, _config.GetCVar(CVars.NetLidgrenLogWarning));
            config.SetMessageTypeEnabled(NetIncomingMessageType.ErrorMessage, _config.GetCVar(CVars.NetLidgrenLogError));

            var poolSize = _config.GetCVar(CVars.NetPoolSize);
            if (poolSize <= 0)
            {
                config.UseMessageRecycling = false;
            }
            else
            {
                config.RecycledCacheMaxCount = Math.Min(poolSize, 8192);
            }

            config.SendBufferSize = _config.GetCVar(CVars.NetSendBufferSize);
            config.ReceiveBufferSize = _config.GetCVar(CVars.NetReceiveBufferSize);

            // Initial value for both sides; clients override it below.
            config.MaximumHandshakeAttempts = 5;

            config.SetMessageTypeEnabled(NetIncomingMessageType.VerboseDebugMessage, _config.GetCVar(CVars.NetVerbose));

            if (IsServer)
            {
                config.SetMessageTypeEnabled(NetIncomingMessageType.ConnectionApproval, true);
                config.MaximumConnections = _config.GetEffectiveMaxConnections();
            }
            else
            {
                config.ConnectionTimeout = _config.GetCVar(CVars.ConnectionTimeout);
                config.ResendHandshakeInterval = _config.GetCVar(CVars.ResendHandshakeInterval);
                config.MaximumHandshakeAttempts = _config.GetCVar(CVars.MaximumHandshakeAttempts);
            }

            config.SimulatedLoss = _config.GetCVar(CVars.NetFakeLoss);
            config.SimulatedMinimumLatency = _config.GetCVar(CVars.NetFakeLagMin);
            config.SimulatedRandomLatency = _config.GetCVar(CVars.NetFakeLagRand);
            config.SimulatedDuplicatesChance = _config.GetCVar(CVars.NetFakeDuplicates);

#if DEBUG
            config.ConnectionTimeout = 30000f;
#endif

            config.MaximumTransmissionUnit = _config.GetCVar(CVars.NetMtu);
            config.MaximumTransmissionUnitV6 = _config.GetCVar(CVars.NetMtuIpv6);
            config.AutoExpandMTU = _config.GetCVar(CVars.NetMtuExpand);
            config.ExpandMTUFrequency = _config.GetCVar(CVars.NetMtuExpandFrequency);
            config.ExpandMTUFailAttempts = _config.GetCVar(CVars.NetMtuExpandFailAttempts);

            return config;
        }

        private void _fakeLossChanged(float newValue)
        {
            foreach (var peer in _netPeers)
            {
                peer.Peer.Configuration.SimulatedLoss = newValue;
            }
        }

        private void _fakeLagMinChanged(float newValue)
        {
            foreach (var peer in _netPeers)
            {
                peer.Peer.Configuration.SimulatedMinimumLatency = newValue;
            }
        }

        private void _fakeLagRandomChanged(float newValue)
        {
            foreach (var peer in _netPeers)
            {
                peer.Peer.Configuration.SimulatedRandomLatency = newValue;
            }
        }

        private void FakeDuplicatesChanged(float newValue)
        {
            foreach (var peer in _netPeers)
            {
                peer.Peer.Configuration.SimulatedDuplicatesChance = newValue;
            }
        }

        /// <summary>
        ///     Looks up the channel that belongs to a Lidgren connection.
        /// </summary>
        /// <param name="connection">The connection to look up.</param>
        /// <returns>The channel registered for the connection.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="connection"/> is null.</exception>
        /// <exception cref="NetManagerException">No channel exists for the connection.</exception>
        private INetChannel GetChannel(NetConnection connection)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (_channels.TryGetValue(connection, out var channel))
            {
                return channel;
            }

            throw new NetManagerException("There is no NetChannel for this NetConnection.");
        }

        private bool TryGetChannel(NetConnection connection, [NotNullWhen(true)] out INetChannel? channel)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (_channels.TryGetValue(connection, out var channelInstance))
            {
                channel = channelInstance;
                return true;
            }

            channel = default;
            return false;
        }

        /// <summary>
        ///     Handles a Lidgren status-change packet: completes a pending status wait if one exists, otherwise
        ///     starts the server handshake for new connections and tears down channels of finished connections.
        /// </summary>
        /// <param name="peer">The peer that produced the packet.</param>
        /// <param name="msg">The status-change packet. Its payload is the new status byte followed by the reason string.</param>
        /// <remarks>
        ///     A status change that something is waiting for (the client connect flow) is consumed by the waiter
        ///     and gets no further handling. The waiter's continuation runs synchronously inside this call.
        ///     For a disconnect, a pending raw-data wait is failed before the channel is torn down.
        ///     Entries of the disconnect-wait table are completed but never removed.
        /// </remarks>
        private void HandleStatusChanged(NetPeerData peer, NetIncomingMessage msg)
        {
            DebugTools.AssertNotNull(msg.SenderConnection);
            var sender = msg.SenderConnection!;

            var newStatus = (NetConnectionStatus) msg.ReadByte();
            var reason = msg.ReadString();

            _logger.Debug($"{sender.RemoteEndPoint}: Status changed to {newStatus}, reason: {reason}");

            if (_awaitingStatusChange.TryGetValue(sender, out var awaiter))
            {
                _awaitingStatusChange.Remove(sender);
                awaiter.Item1.Dispose();
                awaiter.Item2.SetResult(reason);
                return;
            }

            switch (newStatus)
            {
                case NetConnectionStatus.Connected:
                    if (IsServer)
                    {
                        // Starts the asynchronous login handshake; not awaited.
                        HandleHandshake(peer, sender);
                    }

                    break;

                case NetConnectionStatus.Disconnected:
                    if (_awaitingData.TryGetValue(sender, out var dataAwaiter))
                    {
                        dataAwaiter.Item1.Dispose();
                        dataAwaiter.Item2.TrySetException(new ClientDisconnectedException($"Disconnected: {reason}"));
                        _awaitingData.Remove(sender);
                    }

                    if (_channels.ContainsKey(sender))
                    {
                        HandleDisconnect(peer, sender, reason);
                    }

                    if (_awaitingDisconnect.TryGetValue(sender, out var disconnectAwaiter))
                    {
                        disconnectAwaiter.TrySetResult(null);
                    }

                    break;
            }
        }

        /// <summary>
        ///     Server only. Creates and registers the channel of a client whose login succeeded, sends it the
        ///     string table and runs the serializer and transfer handshakes. Raises <see cref="Connected"/> when
        ///     both finish.
        /// </summary>
        /// <param name="peer">The peer the client is connected to.</param>
        /// <param name="sender">The client's Lidgren connection.</param>
        /// <param name="userData">The authenticated user information.</param>
        /// <param name="encryption">The encryption state for the channel, or null if the client does not encrypt.</param>
        /// <param name="loginType">How the client logged in.</param>
        /// <remarks>
        ///     <para>
        ///         The channel is entered into all lookup tables before the handshakes, so it already receives data
        ///         (only handshake-flagged message types are accepted) while <c>IsHandshakeComplete</c> is still
        ///         false. The user id and name reservations are not undone if the handshake is cancelled; they are
        ///         released when the disconnect is processed.
        ///     </para>
        ///     <para>
        ///         The part before the first await runs inside the caller's try block, so a duplicate-key failure
        ///         there is handled by the caller. Once the method has suspended, only a cancelled handshake (the
        ///         client left) is handled, silently; any other exception escapes through the async-void machinery.
        ///     </para>
        /// </remarks>
        private async void HandleInitialHandshakeComplete(NetPeerData peer,
            NetConnection sender,
            NetUserData userData,
            NetEncryption? encryption,
            LoginType loginType)
        {
            _logger.Verbose($"{sender.RemoteEndPoint}: Initial handshake complete!");

            var channel = new NetChannel(this, sender, userData, loginType);

            _assignedUserIds.Add(channel.UserId, sender);
            _assignedUsernames.Add(channel.UserName, sender);
            _channels.Add(sender, channel);
            peer.AddChannel(channel);

            channel.Encryption = encryption;

            SetupEncryptionChannel(channel);

            // The client must learn the id table before anything else is exchanged.
            _strings.SendFullTable(channel);

            try
            {
                await Task.WhenAll(_serializer.Handshake(channel), _transfer.ServerHandshake(channel));
            }
            catch (TaskCanceledException)
            {
                // The client disconnected while handshaking. Not an error, and no Connected event.
                return;
            }

            _logger.Info($"{sender.RemoteEndPoint}: Connected");

            OnConnected(channel);
        }

        /// <summary>
        ///     Removes the channel of a disconnected connection from all tables and notifies subscribers.
        ///     On a client this also schedules a reset for the end of the current packet pass.
        /// </summary>
        /// <param name="peer">The peer owning the connection.</param>
        /// <param name="connection">The connection that reached the disconnected state. It must have a channel.</param>
        /// <param name="reason">The disconnect reason reported by Lidgren.</param>
        /// <remarks>
        ///     The tables are updated before the event is raised, so handlers never see the dead channel.
        ///     In builds with <c>EXCEPTION_TOLERANCE</c> an exception from a handler is logged and swallowed so the
        ///     client reset flag still gets set; otherwise it propagates and the flag is not set.
        /// </remarks>
        /// <exception cref="KeyNotFoundException">There is no channel for <paramref name="connection"/>.</exception>
        private void HandleDisconnect(NetPeerData peer, NetConnection connection, string reason)
        {
            var channel = _channels[connection];

            _logger.Info($"{connection.RemoteEndPoint}: Disconnected: {reason}");

            _assignedUsernames.Remove(channel.UserName);
            _assignedUserIds.Remove(channel.UserId);

            _channels.Remove(connection);
            peer.RemoveChannel(channel);

            // Stops the per-channel encryption worker.
            channel.EncryptionChannel?.Complete();

#if EXCEPTION_TOLERANCE
            try
            {
                OnDisconnected(channel, reason);
            }
            catch (Exception e)
            {
                _logger.Error($"Exception in Disconnect event handler: {e}");
            }
#else
            OnDisconnected(channel, reason);
#endif

            if (IsClient)
            {
                _clientResetPending = true;
            }
        }

        /// <inheritdoc />
        /// <remarks>
        ///     No event is raised here; <see cref="Disconnect"/> fires later, when Lidgren reports the disconnected
        ///     status during <see cref="ProcessPackets"/>. Does nothing if the channel's connection is not currently
        ///     in the connected state.
        /// </remarks>
        public void DisconnectChannel(INetChannel channel, string reason)
        {
            channel.Disconnect(reason);
        }

        /// <summary>
        ///     Validates, decrypts, deserializes and dispatches one incoming data packet.
        /// </summary>
        /// <param name="msg">The incoming data packet.</param>
        /// <returns>
        ///     True if the caller should recycle the packet (always, including for rejected packets); false if the
        ///     packet was handed to a raw-data waiter and must not be recycled.
        /// </returns>
        /// <remarks>
        ///     <para>
        ///         Rejected packets never throw: offending connections are disconnected without telling the remote
        ///         (no "bye"), and the reason only appears locally. Exceptions from deserialization and from the
        ///         receive callback are contained here. The one unguarded operation is reading the message id byte of
        ///         a decrypted packet that has no payload.
        ///     </para>
        ///     <para>
        ///         Raw-data waiters (used during handshakes) are served before the empty-packet, channel and
        ///         decryption checks, so such data is neither decrypted nor interpreted.
        ///     </para>
        ///     <para>
        ///         Decryption failures on the server are counted per normalized remote IP. A new IP is ignored
        ///         silently once the tracking table is full, and once the ban threshold is reached the sender is only
        ///         disconnected if the kick CVar is enabled.
        ///     </para>
        /// </remarks>
        private bool DispatchNetMessage(NetIncomingMessage msg)
        {
            DebugTools.AssertNotNull(msg.SenderConnection);

            var senderConnection = msg.SenderConnection!;
            var netPeer = senderConnection.Peer;

            if (netPeer.Status == NetPeerStatus.ShutdownRequested)
            {
                if (_logPacketIssues)
                {
                    _logger.Warning($"{senderConnection.RemoteEndPoint}: Received data packet while the peer is shutting down.");
                }

                return true;
            }

            if (netPeer.Status == NetPeerStatus.NotRunning)
            {
                if (_logPacketIssues)
                {
                    _logger.Warning($"{senderConnection.RemoteEndPoint}: Received data packet while the peer is not running.");
                }

                return true;
            }

            if (!IsConnected)
            {
                if (_logPacketIssues)
                {
                    _logger.Warning($"{senderConnection.RemoteEndPoint}: Received data packet while not connected.");
                }

                return true;
            }

            // Raw data that a handshake routine is waiting for. Intentionally precedes every other per-packet check.
            if (_awaitingData.TryGetValue(senderConnection, out var dataAwaiter))
            {
                _awaitingData.Remove(senderConnection);
                dataAwaiter.Item1.Dispose();
                dataAwaiter.Item2.TrySetResult(msg);
                return false;
            }

            if (msg.LengthBytes < 1)
            {
                if (_logPacketIssues)
                {
                    _logger.Warning($"{senderConnection.RemoteEndPoint}: Received empty packet.");
                }

                senderConnection.Disconnect("Received empty/weird packet", false);
                return true;
            }

            if (!_channels.TryGetValue(senderConnection, out var channel))
            {
                if (_logPacketIssues)
                {
                    _logger.Debug($"{senderConnection.RemoteEndPoint}: Received data packet from a connection without a channel.");
                }

                senderConnection.Disconnect("Unexpected packet before handshake completion", false);
                return true;
            }

            if (channel.Encryption != null && !channel.Encryption.TryDecrypt(msg))
            {
                if (IsServer)
                {
                    var ip = NormalizeIp(channel.RemoteEndPoint.Address);
                    var now = DateTime.UtcNow;
                    var maxTracked = _config.GetCVar(CVars.NetDecryptFailMaxTracked);

                    if (_decryptFailCounts.Count >= maxTracked && !_decryptFailCounts.ContainsKey(ip))
                    {
                        // Tracking table is full and this address is new: drop quietly, do not disconnect.
                        return true;
                    }

                    var failCount = _decryptFailCounts.AddOrUpdate(
                        ip,
                        _ => (1, now),
                        (_, old) => (old.TotalCount + 1, now)).TotalCount;

                    if (failCount == 1 && _logPacketIssues)
                    {
                        _logger.Debug($"{channel.RemoteEndPoint}: Packet failed to decrypt.");
                    }

                    var banThreshold = _config.GetCVar(CVars.NetDecryptFailBanThreshold);
                    if (failCount >= banThreshold)
                    {
                        _authLogger.Warning(
                            $"[DECRYPTBAN] {ip} has failed to decrypt {failCount} packets. Consider banning this IP.");

                        if (_config.GetCVar(CVars.NetDecryptFailKick))
                        {
                            senderConnection.Disconnect("Failed to decrypt packet.", false);
                        }

                        return true;
                    }
                }
                else if (_logPacketIssues)
                {
                    _logger.Debug($"{channel.RemoteEndPoint}: Packet failed to decrypt.");
                }

                senderConnection.Disconnect("Failed to decrypt packet.", false);
                return true;
            }

            var id = msg.ReadByte();
            var entry = _netMsgIndices[id];

            if (entry == null)
            {
                if (_logPacketIssues)
                {
                    _logger.Warning($"{channel.RemoteEndPoint}: Got net message with invalid ID {id}.");
                }

                channel.Disconnect("Got NetMessage with invalid ID", false);
                return true;
            }

            DebugTools.Assert(entry.Callback != null, "A message in the receive cache must have a callback.");

            if (!channel.IsHandshakeComplete && !entry.IsHandshake)
            {
                if (_logPacketIssues)
                {
                    _logger.Warning(
                        $"{channel.RemoteEndPoint}: Got net message of type {entry.Type.Name} before handshake completion.");
                }

                channel.Disconnect("Got unacceptable net message before handshake completion", false);
                return true;
            }

            var instance = (NetMessage) Activator.CreateInstance(entry.Type)!;
            instance.MsgChannel = channel;

#if DEBUG
            _bandwidthUsage.TryGetValue(entry.Type, out var previousUsage);
            _bandwidthUsage[entry.Type] = previousUsage + msg.LengthBytes;
#endif

            try
            {
                instance.ReadFromBuffer(msg, _serializer);
            }
            catch (InvalidCastException e)
            {
                if (_logPacketIssues)
                {
                    _logger.Error(
                        $"{channel.RemoteEndPoint}: Failed to cast net message of type {entry.Type.Name}: {e}");
                }

                channel.Disconnect("Failed to deserialize packet.", false);
                return true;
            }
            catch (Exception e)
            {
                // Everything is caught on purpose: the packet content is untrusted.
                if (_logPacketIssues)
                {
                    _logger.Error(
                        $"{channel.RemoteEndPoint}: Failed to deserialize net message of type {entry.Type.Name}: {e}");
                }

                channel.Disconnect("Failed to deserialize packet.", false);
                return true;
            }

            if (_loggerPacket.IsLogLevelEnabled(LogLevel.Verbose))
            {
                _loggerPacket.Verbose($"RECV: {instance.GetType().Name} {msg.LengthBytes}");
            }

            try
            {
                entry.Callback!.Invoke(instance);
            }
            catch (Exception e)
            {
                // A faulty handler must not take the connection or later packets down with it.
                _logger.Error(
                    $"{channel.RemoteEndPoint}: Exception in receive handler of net message {entry.Type.Name}: {e}");
            }

            return true;
        }

        public void DispatchLocalNetMessage(NetMessage message)
        {
            if (!_messages.TryGetValue(message.MsgName, out var msgDat))
                return;

            msgDat.Callback!.Invoke(message);
        }

        private void CacheNetMsgIndex(int id, string name)
        {
            if (!_messages.TryGetValue(name, out var msgDat))
                return;

            if (msgDat.Callback == null)
                return;

            _netMsgIndices[id] = msgDat;
        }

        #region NetMessages

        /// <inheritdoc />
        /// <remarks>
        ///     The name is registered in the string table before the duplicate check on the message table, so a
        ///     duplicate registration still leaves the name in the string table. A callback is only attached (and the
        ///     type only becomes routable for incoming packets) if <paramref name="accept"/> includes this side;
        ///     otherwise the type can be sent but packets carrying its id are treated as having an invalid id.
        /// </remarks>
        public void RegisterNetMessage<T>(ProcessMessage<T>? rxCallback = null,
            NetMessageAccept accept = NetMessageAccept.Both)
            where T : NetMessage, new()
        {
            var name = new T().MsgName;
            var id = _strings.AddString(name);

            var data = new MessageData
            {
                Type = typeof(T),
                IsHandshake = (accept & NetMessageAccept.Handshake) != 0
            };

            _messages.Add(name, data);

            var thisSide = IsServer ? NetMessageAccept.Server : NetMessageAccept.Client;

            if (rxCallback != null && (accept & thisSide) != 0)
            {
                ProcessMessage<T> typedCallback = rxCallback;
                data.Callback = message => typedCallback((T) message);

                if (id != -1)
                {
                    // Must come after the callback is stored: the helper only caches entries that have one.
                    CacheNetMsgIndex(id, name);
                }
            }
        }

        /// <inheritdoc />
        public T CreateNetMessage<T>()
            where T : NetMessage, new()
        {
            return new T();
        }

        /// <summary>
        ///     Creates the outgoing Lidgren packet for a message: a one-byte type id followed by the message payload.
        /// </summary>
        /// <param name="message">The message to serialize.</param>
        /// <param name="peer">The peer that will own the packet.</param>
        /// <returns>The packet. Encryption, delivery method and sequence channel are applied by the caller.</returns>
        /// <exception cref="NetManagerException">The message name has no entry in the string table.</exception>
        /// <remarks>
        ///     Safe to call from any thread, since no manager state is modified. Ids above 255 would wrap when
        ///     narrowed to the single id byte.
        /// </remarks>
        private NetOutgoingMessage BuildMessage(NetMessage message, NetPeer peer)
        {
            var packet = peer.CreateMessage(4);

            if (!_strings.TryFindStringId(message.MsgName, out var id))
            {
                throw new NetManagerException(
                    $"[NET] No string in table with name {message.MsgName}. Was it registered?");
            }

            packet.Write((byte) id);
            message.WriteToBuffer(packet, _serializer);
            return packet;
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Channels whose handshake has not completed are skipped (unlike <see cref="ServerSendToMany"/>).
        ///     The message is serialized once per recipient. If a send throws, the remaining recipients are not served.
        /// </remarks>
        public void ServerSendToAll(NetMessage message)
        {
            DebugTools.Assert(IsServer);

            if (!IsConnected)
                return;

            foreach (var channel in _channels.Values)
            {
                if (!channel.IsHandshakeComplete)
                    continue;

                ServerSendMessage(message, channel);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Silently does nothing if the manager is not initialized. Does not require the recipient's handshake to
        ///     be complete, so handshake-flagged messages may be sent during the handshake window.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="recipient"/> is not a channel created by this class.</exception>
        public void ServerSendMessage(NetMessage message, INetChannel recipient)
        {
            if (!_initialized)
                return;

            DebugTools.Assert(IsServer);

            if (recipient is not NetChannel channel)
            {
                throw new ArgumentException($"Not of type {typeof(NetChannel).FullName}", nameof(recipient));
            }

            CoreSendMessage(channel, message);
        }

        private void LogSend(NetMessage message, NetDeliveryMethod method, NetOutgoingMessage packet)
        {
            if (_loggerPacket.IsLogLevelEnabled(LogLevel.Verbose))
                _loggerPacket.Verbose($"SEND: {message.GetType().Name} {method} {packet.LengthBytes}");
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Does not filter by handshake state and does not de-duplicate. If a send throws, earlier recipients
        ///     have already received the message and later ones will not.
        /// </remarks>
        public void ServerSendToMany(NetMessage message, List<INetChannel> recipients)
        {
            DebugTools.Assert(IsServer);

            if (!IsConnected)
                return;

            foreach (var channel in recipients)
            {
                ServerSendMessage(message, channel);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Not connected: logs an error with a stack trace and returns without throwing. Otherwise the message is
        ///     sent over the first channel of the first peer; that channel is not required to have completed its
        ///     handshake. If the transport is connected but no channel exists yet, the lookup throws.
        /// </remarks>
        public void ClientSendMessage(NetMessage message)
        {
            DebugTools.Assert(IsClient);

            if (!IsConnected)
            {
                _logger.Error($"Tried to send message while not connected: {message}{Environment.NewLine}{Environment.StackTrace}");
                return;
            }

            DebugTools.Assert(_netPeers.Count == 1);
            DebugTools.Assert(_netPeers[0].Channels.Count == 1);

            var peer = _netPeers[0];
            var channel = peer.Channels[0];

            CoreSendMessage(channel, message);
        }

        #endregion NetMessages

        #region Events

        private async Task<NetConnectingArgs> OnConnecting(
            IPEndPoint ip,
            NetUserData userData,
            LoginType loginType)
        {
            var args = new NetConnectingArgs(userData, ip, loginType);
            foreach (var conn in _connectingEvent)
            {
                await conn(args);
            }

            return args;
        }

        private void OnConnectFailed(string reason)
        {
            var args = new NetConnectFailArgs(reason);
            ConnectFailed?.Invoke(this, args);
        }

        private void OnConnected(NetChannel channel)
        {
            channel.IsHandshakeComplete = true;

            Connected?.Invoke(this, new NetChannelArgs(channel));
        }

        private void OnDisconnected(INetChannel channel, string reason)
        {
            Disconnect?.Invoke(this, new NetDisconnectedArgs(channel, reason));
        }

        private readonly List<Func<NetConnectingArgs, Task>> _connectingEvent
            = new();

        /// <inheritdoc />
        public event Func<NetConnectingArgs, Task> Connecting
        {
            add => _connectingEvent.Add(value);
            remove => _connectingEvent.Remove(value);
        }

        /// <inheritdoc />
        public event EventHandler<NetConnectFailArgs>? ConnectFailed;

        /// <inheritdoc />
        public event EventHandler<NetChannelArgs>? Connected;

        /// <inheritdoc />
        public event EventHandler<NetDisconnectedArgs>? Disconnect;

        #endregion Events

        [Serializable]
        [Virtual]
        public class ClientDisconnectedException : Exception
        {
            public ClientDisconnectedException()
            {
            }

            public ClientDisconnectedException(string message) : base(message)
            {
            }

            public ClientDisconnectedException(string message, Exception inner) : base(message, inner)
            {
            }
        }

        private sealed class NetPeerData
        {
            public readonly NetPeer Peer;

            public readonly List<NetChannel> Channels = new();

            // So that we can do ServerSendToAll without a list copy.
            public readonly List<NetConnection> ConnectionsWithChannels = new();

            public NetPeerData(NetPeer peer)
            {
                Peer = peer;
            }

            public void AddChannel(NetChannel channel)
            {
                Channels.Add(channel);
                ConnectionsWithChannels.Add(channel.Connection);
            }

            public void RemoveChannel(NetChannel channel)
            {
                Channels.Remove(channel);
                ConnectionsWithChannels.Remove(channel.Connection);
            }
        }

        private sealed class MessageData
        {
            public bool IsHandshake;
            public Type Type = default!;
            public ProcessMessage? Callback;
        }

        void IPostInjectInit.PostInject()
        {
            _logger = _logMan.GetSawmill("net");
            _loggerPacket = _logMan.GetSawmill("net.packet");
            _authLogger = _logMan.GetSawmill("auth");
        }
    }

    /// <summary>
    ///     Exception type used for failures inside the network manager.
    /// </summary>
    [Virtual]
    public class NetManagerException : Exception
    {
        /// <summary>
        ///     Creates the exception with the given message text.
        /// </summary>
        /// <param name="message">The text exposed through <see cref="Exception.Message"/>.</param>
        public NetManagerException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    ///     Counters describing the traffic that has passed through a channel.
    /// </summary>
    public struct NetworkStats
    {
        /// <summary>
        ///     Bytes sent so far.
        /// </summary>
        public readonly long SentBytes;

        /// <summary>
        ///     Bytes received so far.
        /// </summary>
        public readonly long ReceivedBytes;

        /// <summary>
        ///     Packets sent so far.
        /// </summary>
        public readonly long SentPackets;

        /// <summary>
        ///     Packets received so far.
        /// </summary>
        public readonly long ReceivedPackets;

        public NetworkStats(long sentBytes, long receivedBytes, long sentPackets, long receivedPackets)
        {
            SentBytes = sentBytes;
            ReceivedBytes = receivedBytes;
            SentPackets = sentPackets;
            ReceivedPackets = receivedPackets;
        }

        /// <summary>
        ///     Creates a snapshot of the traffic counters of a single Lidgren peer.
        /// </summary>
        /// <param name="statistics">The statistics to copy. Later changes to it are not reflected in the new value.</param>
        public NetworkStats(NetPeerStatistics statistics)
        {
            SentBytes = statistics.SentBytes;
            ReceivedBytes = statistics.ReceivedBytes;
            SentPackets = statistics.SentPackets;
            ReceivedPackets = statistics.ReceivedPackets;
        }
    }
}
