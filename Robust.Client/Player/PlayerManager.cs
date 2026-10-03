using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Robust.Client.Player
{
    /// <summary>
    ///     The client-side player manager. It connects the user interface and input to the entity the local player
    ///     is controlling, which keeps that wiring in one place instead of spreading it across the code base.
    ///     It also exchanges messages with the server, because the server decides which entity the player controls.
    /// </summary>
    internal sealed class PlayerManager : SharedPlayerManager, IPlayerManager
    {
        [Dependency] private readonly IClientNetManager _network = default!;
        [Dependency] private readonly IBaseClient _client = default!;

        /// <summary>
        ///     Player states received from the server whose controlled <see cref="NetEntity"/> could not be
        ///     resolved to a local entity yet, keyed by user. They are kept so the attachment can be retried later.
        /// </summary>
        private Dictionary<NetUserId, SessionState> _pendingStates = new ();
        private List<SessionState> _pending = new();

        /// <inheritdoc />
        public override ICommonSession[] NetworkedSessions
        {
            get
            {
                return LocalSession != null
                    ? new [] { LocalSession }
                    : Array.Empty<ICommonSession>();
            }
        }

        /// <inheritdoc />
        public override int MaxPlayers => _client.GameInfo?.ServerMaxPlayers ?? -1;

        public LocalPlayer? LocalPlayer { get; private set; }

        public event Action<SessionStatusEventArgs>? LocalStatusChanged;
        public event Action? PlayerListUpdated;
        public event Action<EntityUid>? LocalPlayerDetached;
        public event Action<EntityUid>? LocalPlayerAttached;
        public event Action<(ICommonSession? Old, ICommonSession? New)>? LocalSessionChanged;

        /// <inheritdoc />
        public override void Initialize(int maxPlayers)
        {
            base.Initialize(maxPlayers);
            _network.RegisterNetMessage<MsgPlayerListReq>();
            _network.RegisterNetMessage<MsgPlayerList>(HandlePlayerList);
            PlayerStatusChanged += StatusChanged;
        }

        private void StatusChanged(object? sender, SessionStatusEventArgs e)
        {
            if (e.Session == LocalPlayer?.Session)
                LocalStatusChanged?.Invoke(e);
        }

        public void SetupSinglePlayer(string name)
        {
            if (LocalSession != null)
                throw new InvalidOperationException($"Player manager already running?");

            var session = CreateAndAddSession(default, name);
            session.ClientSide = true;
            SetLocalSession(session);
            Startup();
            PlayerListUpdated?.Invoke();
        }

        /// <summary>
        ///     Starts the manager for a networked game. Call once the client is connected and owns a net channel.
        ///     The player list is requested from the server; <see cref="PlayerListUpdated"/> fires when it arrives.
        /// </summary>
        /// <param name="channel">The channel connected to the server.</param>
        /// <exception cref="InvalidOperationException">A local session already exists.</exception>
        public void SetupMultiplayer(INetChannel channel)
        {
            if (LocalSession != null)
                throw new InvalidOperationException("Player manager already running?");

            var localSession = CreateAndAddSession(channel);
            SetLocalSession(localSession);

            Startup();

            _network.ClientSendMessage(new MsgPlayerListReq());
        }

        public void SetLocalSession(ICommonSession? session)
        {
            if (session == LocalSession)
                return;

            var old = LocalSession;

            if (old?.AttachedEntity is {} oldUid)
            {
                LocalSession = null;
                LocalPlayer = null;
                Sawmill.Info($"Detaching local player from {EntManager.ToPrettyString(oldUid)}.");
                EntManager.EventBus.RaiseLocalEvent(oldUid, new LocalPlayerDetachedEvent(oldUid), true);
                LocalPlayerDetached?.Invoke(oldUid);
            }

            LocalSession = session;
            LocalPlayer = session == null ? null : new LocalPlayer(session);
            Sawmill.Info($"Changing local session from {old?.ToString() ?? "null"} to {session?.ToString() ?? "null"}.");
            LocalSessionChanged?.Invoke((old, LocalSession));

            if (session?.AttachedEntity is {} newUid)
            {
                Sawmill.Info($"Attaching local player to {EntManager.ToPrettyString(newUid)}.");
                EntManager.EventBus.RaiseLocalEvent(newUid, new LocalPlayerAttachedEvent(newUid), true);
                LocalPlayerAttached?.Invoke(newUid);
            }
        }

        /// <inheritdoc />
        public override void Shutdown()
        {
            // Run the normal detach path first so events fire for the entity we are leaving.
            SetAttachedEntity(LocalSession, null, out _);

            // Written directly on purpose: LocalSessionChanged must not be raised during shutdown.
            LocalPlayer = null;
            LocalSession = null;

            _pendingStates.Clear();

            base.Shutdown();

            PlayerListUpdated?.Invoke();
        }

        public override bool SetAttachedEntity(ICommonSession? session, EntityUid? uid, out ICommonSession? kicked, bool force = false)
        {
            kicked = null;

            if (session == null)
                return false;

            // Nothing to do if the session already has exactly this attachment.
            if (session.AttachedEntity == uid)
                return true;

            var previous = session.AttachedEntity;

            if (!base.SetAttachedEntity(session, uid, out kicked, force))
                return false;

            // Remote sessions only receive the shared behaviour.
            if (session != LocalSession)
                return true;

            // Local session: the detach half always runs before any attach validation.
            if (previous is { } oldUid)
            {
                Sawmill.Info($"Detaching local player from {EntManager.ToPrettyString(oldUid)}.");
                EntManager.EventBus.RaiseLocalEvent(oldUid, new LocalPlayerDetachedEvent(oldUid), true);
                LocalPlayerDetached?.Invoke(oldUid);
            }

            if (uid == null)
            {
                Sawmill.Info("Local player is no longer attached to any entity.");
                return true;
            }

            if (!EntManager.EntityExists(uid))
            {
                Sawmill.Error($"Attempted to attach player to non-existent entity {uid}!");
                return true;
            }

            if (!EntManager.HasComponent<EyeComponent>(uid))
            {
                if (_client.RunLevel != ClientRunLevel.SinglePlayerGame)
                {
                    Sawmill.Warning($"Attaching local player to an entity {EntManager.ToPrettyString(uid)} without an eye. This eye will not be netsynced and may cause issues.");
                }

                var eye = Factory.GetComponent<EyeComponent>();
                eye.NetSyncEnabled = false;
                EntManager.AddComponent(uid.Value, eye);
            }

            Sawmill.Info($"Attaching local player to {EntManager.ToPrettyString(uid)}.");
            EntManager.EventBus.RaiseLocalEvent(uid.Value, new LocalPlayerAttachedEvent(uid.Value), true);
            LocalPlayerAttached?.Invoke(uid.Value);
            return true;
        }

        /// <summary>
        ///     Applies a complete player list received from the server. Also used when replaying recordings.
        /// </summary>
        /// <param name="list">The full set of player states.</param>
        public void ApplyPlayerStates(IReadOnlyCollection<SessionState> list)
        {
            var dirty = ApplyStates(list, true);

            // Retry pass for states that could not be resolved earlier. Note the guard is an equality with zero,
            // so this normally runs on an empty collection (ApplyStates returns false for that); kept as-is.
            if (_pendingStates.Count == 0)
            {
                _pending.Clear();
                _pending.AddRange(_pendingStates.Values);
                _pendingStates.Clear();

                // Partial list: sessions missing from it must not be removed.
                dirty |= ApplyStates(_pending, false);
            }

            if (dirty)
                PlayerListUpdated?.Invoke();
        }

        private bool ApplyStates(IReadOnlyCollection<SessionState> list, bool fullList)
        {
            if (list.Count == 0)
                return false;

            DebugTools.Assert(_network.IsConnected ||  _client.RunLevel == ClientRunLevel.SinglePlayerGame // replays use state application.
                , "Received player state without being connected?");
            DebugTools.Assert(LocalSession != null, "Received player state before Session finished setup.");

            var state = list.FirstOrDefault(s => s.UserId == LocalSession.UserId);

            bool dirty = false;
            if (state != null)
            {
                dirty = true;
                if (!EntManager.TryGetEntity(state.ControlledEntity, out var uid)
                    && state.ControlledEntity is { Valid:true } )
                {
                    Sawmill.Error($"Received player state for local player with an unknown net entity!");
                    _pendingStates[state.UserId] = state;
                }
                else
                {
                    _pendingStates.Remove(state.UserId);
                }

                SetAttachedEntity(LocalSession, uid, out _, true);
                SetStatus(LocalSession, state.Status);
            }

            return UpdatePlayerList(list, fullList) || dirty;
        }

        /// <summary>
        ///     Net message handler for <see cref="MsgPlayerList"/>; applies the contained player states.
        /// </summary>
        /// <param name="msg">The received player list message.</param>
        private void HandlePlayerList(MsgPlayerList msg)
        {
            ApplyPlayerStates(msg.Plyrs);
        }

        /// <summary>
        ///     Reconciles the session table with received player states.
        /// </summary>
        /// <param name="remotePlayers">The received states.</param>
        /// <param name="fullList">
        ///     True if the states are the complete player list, in which case sessions absent from it are removed.
        /// </param>
        /// <returns>True if any session was added, changed or removed.</returns>
        private bool UpdatePlayerList(IEnumerable<SessionState> remotePlayers, bool fullList)
        {
            var dirty = false;
            var seen = new List<NetUserId>();

            foreach (var state in remotePlayers)
            {
                seen.Add(state.UserId);

                // Keep unresolved (but valid) net entities around so they can be retried later.
                if (!EntManager.TryGetEntity(state.ControlledEntity, out var controlled)
                    && state.ControlledEntity is { Valid: true })
                {
                    _pendingStates[state.UserId] = state;
                }
                else
                {
                    _pendingStates.Remove(state.UserId);
                }

                if (!InternalSessions.TryGetValue(state.UserId, out var existing))
                {
                    // Brand new user.
                    DebugTools.Assert(state.UserId != LocalPlayer?.UserId);
                    var created = (ICommonSessionInternal) CreateAndAddSession(state.UserId, state.Name);
                    SetStatus(created, state.Status);
                    SetAttachedEntity(created, controlled, out _, true);
                    dirty = true;
                    continue;
                }

                if (existing.Name == state.Name
                    && existing.Status == state.Status
                    && existing.AttachedEntity == controlled)
                {
                    continue;
                }

                dirty = true;
                var internalSession = (ICommonSessionInternal) existing;
                internalSession.SetName(state.Name);
                SetStatus(internalSession, state.Status);
                SetAttachedEntity(internalSession, controlled, out _, true);
            }

            if (fullList)
            {
                foreach (var oldUser in InternalSessions.Keys.ToArray())
                {
                    if (seen.Contains(oldUser))
                        continue;

                    // Client-side sessions are never known to the server.
                    if (InternalSessions[oldUser].ClientSide)
                        continue;

                    DebugTools.Assert(
                        LocalUser == null || LocalUser == default(NetUserId) || oldUser != LocalUser,
                        "Client is still connected to the server but not in the list of players?");

                    RemoveSession(oldUser);
                    _pendingStates.Remove(oldUser);
                    dirty = true;
                }
            }

            return dirty;
        }

        public override bool TryGetSessionByEntity(EntityUid uid, [NotNullWhen(true)] out ICommonSession? session)
        {
            if (LocalEntity == uid)
            {
                session = LocalSession!;
                return true;
            }

            session = null;
            return false;
        }
    }
}
