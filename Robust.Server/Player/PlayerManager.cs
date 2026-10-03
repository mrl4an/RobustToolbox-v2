using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Prometheus;
using Robust.Server.Configuration;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Player;
using Robust.Shared.Reflection;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Robust.Server.Player
{
    /// <summary>
    ///     Server-side implementation of player session management.
    /// </summary>
    internal sealed class PlayerManager : SharedPlayerManager, IPlayerManager
    {
        private static readonly Gauge PlayerCountMetric = Metrics
            .CreateGauge("robust_player_count", "Number of players on the server.");

        [Dependency] private readonly IBaseServer _baseServer = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly IServerNetManager _network = default!;
        [Dependency] private readonly IReflectionManager _reflectionManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IServerNetConfigurationManager _cfg = default!;

        /// <summary>
        ///     Maps bound key functions to their keys. Created in <see cref="Initialize"/> and cleared
        ///     again in <see cref="Shutdown"/>.
        /// </summary>
        public BoundKeyMap KeyMap { get; private set; } = default!;

        /// <inheritdoc />
        public override void Initialize(int maxPlayers)
        {
            base.Initialize(maxPlayers);

            // Build the key map and fill in its key-function lookup.
            KeyMap = new BoundKeyMap(_reflectionManager);
            KeyMap.PopulateKeyFunctionsMap();

            // Net messages must be registered before any handler can fire.
            _network.RegisterNetMessage<MsgPlayerListReq>(HandlePlayerListReq);
            // The server only ever sends these two, so they need no receive callback.
            _network.RegisterNetMessage<MsgPlayerList>();
            _network.RegisterNetMessage<MsgSyncTimeBase>();

            _network.Connecting += OnConnecting;
            _network.Connected += NewSession;
            _network.Disconnect += EndSession;
        }

        public override void Shutdown()
        {
            base.Shutdown();
            KeyMap = default!;

            _network.Connecting -= OnConnecting;
            _network.Connected -= NewSession;
            _network.Disconnect -= EndSession;
        }

        private Task OnConnecting(NetConnectingArgs args)
        {
            // Refuse the connection when every slot is already taken.
            if (PlayerCount >= _baseServer.MaxPlayers)
            {
                args.Deny("The server is full.");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Handles a freshly connected network channel by giving it a session and sending the
        ///     initial time base and replicated config values.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="args">Details about the channel that connected.</param>
        private void NewSession(object? sender, NetChannelArgs args)
        {
            CreateAndAddSession(args.Channel);

            PlayerCountMetric.Set(PlayerCount);

            var syncMsg = new MsgSyncTimeBase();
            (syncMsg.Time, syncMsg.Tick) = _timing.TimeBase;
            _network.ServerSendMessage(syncMsg, args.Channel);

            _cfg.SyncConnectingClient(args.Channel);
        }

        private void EndSession(object? sender, NetChannelArgs args)
        {
            EndSession(args.Channel.UserId);
        }

        /// <summary>
        ///     Ends the session of the given user, if one exists, and detaches them from the world.
        /// </summary>
        /// <param name="user">The user whose session should end.</param>
        internal void EndSession(NetUserId user)
        {
            if (!TryGetSessionById(user, out var session))
                return;

            // Take the session out of the live collection first.
            RemoveSession(session.UserId);
            SetStatus(session, SessionStatus.Disconnected);

            // Release the controlled entity.
            SetAttachedEntity(session, null, out _, true);

            // Drop all view subscriptions; iterate over a copy because removal edits the collection.
            var viewSystem = EntManager.System<ViewSubscriberSystem>();
            foreach (var eye in session.ViewSubscriptions.ToArray())
            {
                viewSystem.RemoveViewSubscriber(eye, session);
            }

            PlayerCountMetric.Set(PlayerCount);
            Dirty();
        }

        private void HandlePlayerListReq(MsgPlayerListReq message)
        {
            var channel = message.MsgChannel;
            var session = (CommonSession) GetSessionByChannel(channel);
            session.InitialPlayerListReqDone = true;

            if (!session.InitialResourcesDone)
                return;

            SendPlayerList(channel, session);
        }

        public void MarkPlayerResourcesSent(INetChannel channel)
        {
            var session = (CommonSession) GetSessionByChannel(channel);
            session.InitialResourcesDone = true;

            if (!session.InitialPlayerListReqDone)
                return;

            SendPlayerList(channel, session);
        }

        private void SendPlayerList(INetChannel channel, CommonSession session)
        {
            var roster = Sessions;
            var msg = new MsgPlayerList();

            // The receiving client must already appear as connected in the roster it gets.
            session.ConnectedTime = DateTime.UtcNow;
            SetStatus(session, SessionStatus.Connected);

            var entries = new List<SessionState>();
            foreach (var other in roster)
            {
                entries.Add(new SessionState
                {
                    UserId = other.UserId,
                    Name = other.Name,
                    Status = other.Status
                });
            }

            msg.Plyrs = entries;
            channel.SendMessage(msg);
        }

        public override bool TryGetSessionByEntity(EntityUid uid, [NotNullWhen(true)] out ICommonSession? session)
        {
            if (!_entityManager.TryGetComponent(uid, out ActorComponent? actor))
            {
                session = null;
                return false;
            }

            session = actor.PlayerSession;
            return true;
        }

    internal ICommonSession AddDummySession(NetUserId user, string name)
    {
#if FULL_RELEASE
        // Lets not make it completely trivial to fake player counts.
        throw new NotSupportedException();
#endif
        Lock.EnterWriteLock();
        DummySession session;
        try
        {
            UserIdMap[name] = user;
            if (!PlayerData.TryGetValue(user, out var data))
                PlayerData[user] = data = new(user, name);

            session = new DummySession(user, name, data);
            InternalSessions.Add(user, session);
        }
        finally
        {
            Lock.ExitWriteLock();
        }

        UpdateState(session);

        return session;
    }
    }
}
