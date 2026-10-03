using System;
using System.Net;
using Robust.Client.Configuration;
using Robust.Client.GameObjects;
using Robust.Client.GameStates;
using Robust.Client.Player;
using Robust.Client.Utility;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Robust.Client
{
    /// <inheritdoc />
    public sealed class BaseClient : IBaseClient, IPostInjectInit
    {
        // Services injected by the IoC container.
        [Dependency] private readonly IClientNetManager _net = default!;
        [Dependency] private readonly IPlayerManager _playMan = default!;
        [Dependency] private readonly IClientNetConfigurationManager _configManager = default!;
        [Dependency] private readonly IClientEntityManager _entityManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IDiscordRichPresence _discord = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly IClientGameStateManager _gameStates = default!;
        [Dependency] private readonly ILogManager _logMan = default!;

        /// <inheritdoc />
        public ushort DefaultPort { get; } = 1212;

        /// <inheritdoc />
        public ClientRunLevel RunLevel { get; private set; }

        /// <inheritdoc />
        public ServerInfo? GameInfo { get; private set; }

        /// <inheritdoc />
        public string? PlayerNameOverride { get; set; }

        public string? LastDisconnectReason { get; private set; }

        private (TimeSpan, GameTick) _timeBase;
        private ISawmill _logger = default!;

        /// <inheritdoc />
        public void Initialize()
        {
            // Hook the connection lifecycle events of the net manager.
            _net.Connected += OnConnected;
            _net.ConnectFailed += OnConnectFailed;
            _net.Disconnect += OnNetDisconnect;

            // Time base sync is needed during the handshake as well as afterwards.
            _net.RegisterNetMessage<MsgSyncTimeBase>(SyncTimeBase, NetMessageAccept.Handshake | NetMessageAccept.Client);

            // Both handlers run right away with the current CVar values.
            _configManager.OnValueChanged(CVars.NetTickrate, TickRateChanged, invokeImmediately: true);
            _configManager.OnValueChanged(CVars.GameTimeScale, TimeScaleChanged, invokeImmediately: true);

            // A client has no player cap of its own, hence zero.
            _playMan.Initialize(0);
            _playMan.PlayerListUpdated += OnPlayerListUpdated;

            // Puts the client into the Initialize run level.
            Reset();
        }

        private void OnPlayerListUpdated()
        {
            var serverPlayers = _playMan.PlayerCount;
            if (_net.ServerChannel != null && GameInfo != null && _net.IsConnected)
                _discord.Update(GameInfo.ServerName, _net.ServerChannel.UserName, GameInfo.ServerMaxPlayers.ToString(), serverPlayers.ToString());
        }

        private void SyncTimeBase(MsgSyncTimeBase message)
        {
            _logger.Debug($"Synchronized time base: {message.Tick}: {message.Time}");

            if (RunLevel >= ClientRunLevel.Connected)
                _timing.TimeBase = (message.Time, message.Tick);
            else
                _timeBase = (message.Time, message.Tick);
        }

        private void TickRateChanged(int tickrate, in CVarChangeInfo info)
        {
            if (GameInfo != null)
            {
                GameInfo.TickRate = (ushort) tickrate;
            }

            _timing.SetTickRateAt((ushort) tickrate, info.TickChanged);
            _logger.Info($"Tickrate changed to: {tickrate} on tick {_timing.CurTick}");
        }

        private void TimeScaleChanged(float timeScale, in CVarChangeInfo info)
        {
            if (!GameTiming.IsTimescaleValid(timeScale))
            {
                _logger.Error($"Invalid time scale set: {timeScale}, ignoring");
                return;
            }

            _timing.TimeScale = timeScale;
            _logger.Info($"Tickrate changed to: {timeScale} on tick {_timing.CurTick}");
        }

        /// <inheritdoc />
        public void ConnectToServer(DnsEndPoint endPoint)
        {
            // A connection attempt is already running: abort it and start over.
            if (RunLevel == ClientRunLevel.Connecting)
            {
                _net.Reset("Client mashing that connect button.");
                Reset();
            }

            DebugTools.Assert(RunLevel < ClientRunLevel.Connecting);
            DebugTools.Assert(!_net.IsConnected);

            // Announce the new level before any network traffic is produced.
            OnRunLevelChanged(ClientRunLevel.Connecting);
            _net.ClientConnect(endPoint.Host, endPoint.Port, PlayerNameOverride ?? _configManager.GetCVar(CVars.PlayerName));
        }

        /// <inheritdoc />
        public void DisconnectFromServer(string reason)
        {
            // Cleanup is deferred to OnNetDisconnect, which runs once the net layer reports the disconnect.
            _net.ClientDisconnect(reason);
        }

        /// <inheritdoc />
        public void StartSinglePlayer()
        {
            DebugTools.Assert(RunLevel < ClientRunLevel.Connecting);
            DebugTools.Assert(!_net.IsConnected);
            var name = PlayerNameOverride ?? _configManager.GetCVar(CVars.PlayerName);
            _playMan.SetupSinglePlayer(name);
            OnRunLevelChanged(ClientRunLevel.SinglePlayerGame);
            _playMan.JoinGame(_playMan.LocalSession!);
            GameStartedSetup();
        }

        /// <inheritdoc />
        public void StopSinglePlayer()
        {
            DebugTools.Assert(RunLevel == ClientRunLevel.SinglePlayerGame);
            DebugTools.Assert(!_net.IsConnected);
            GameStoppedReset();
        }

        /// <inheritdoc />
        public event EventHandler<RunLevelChangedEventArgs>? RunLevelChanged;

        public event EventHandler<PlayerEventArgs>? PlayerJoinedServer;
        public event EventHandler<PlayerEventArgs>? PlayerJoinedGame;
        public event EventHandler<PlayerEventArgs>? PlayerLeaveServer;

        private void OnConnected(object? sender, NetChannelArgs args)
        {
            _configManager.SyncWithServer();
            _configManager.ReceivedInitialNwVars += OnReceivedClientData;
        }

        private void OnReceivedClientData(object? sender, EventArgs e)
        {
            _configManager.ReceivedInitialNwVars -= OnReceivedClientData;

            var info = GameInfo;

            var serverName = _configManager.GetCVar<string>("game.hostname");
            if (info == null)
            {
                GameInfo = info = new ServerInfo(serverName);
            }
            else
            {
                info.ServerName = serverName;
            }

            var channel = _net.ServerChannel!;

            // Put the player manager into multiplayer mode on this channel and watch for status changes.
            _playMan.SetupMultiplayer(channel);
            _playMan.PlayerStatusChanged += OnStatusChanged;

            var serverPlayers = _playMan.PlayerCount;
            _discord.Update(info.ServerName, channel.UserName, info.ServerMaxPlayers.ToString(), serverPlayers.ToString());

        }

        /// <summary>
        ///     Called when the local session finishes connecting. Moves to the Connected run level,
        ///     starts the game systems and then notifies listeners.
        /// </summary>
        /// <param name="session">The local player session.</param>
        private void OnPlayerJoinedServer(ICommonSession session)
        {
            DebugTools.Assert(RunLevel < ClientRunLevel.Connected);
            OnRunLevelChanged(ClientRunLevel.Connected);
            GameStartedSetup();
            PlayerJoinedServer?.Invoke(this, new PlayerEventArgs(session));
        }

        /// <summary>
        ///     Called when the local session enters the game proper. Moves to the InGame run level
        ///     and notifies listeners. Setup was already done when the server was joined.
        /// </summary>
        /// <param name="session">The local player session.</param>
        private void OnPlayerJoinedGame(ICommonSession session)
        {
            DebugTools.Assert(RunLevel >= ClientRunLevel.Connected);
            OnRunLevelChanged(ClientRunLevel.InGame);
            PlayerJoinedGame?.Invoke(this, new PlayerEventArgs(session));
        }

        private void Reset()
        {
            // Unsubscribing something that is not subscribed is harmless, so this is safe to repeat.
            _configManager.ReceivedInitialNwVars -= OnReceivedClientData;
            _playMan.PlayerStatusChanged -= OnStatusChanged;
            _configManager.ClearReceivedInitialNwVars();
            OnRunLevelChanged(ClientRunLevel.Initialize);
        }

        private void OnConnectFailed(object? sender, NetConnectFailArgs args)
        {
            DebugTools.Assert(RunLevel == ClientRunLevel.Connecting);
            // Game setup has not happened yet, so only the local state needs resetting.
            Reset();
        }

        private void OnNetDisconnect(object? sender, NetDisconnectedArgs args)
        {
            DebugTools.Assert(RunLevel > ClientRunLevel.Initialize);

            // If we never got past Connecting, the join/setup never happened,
            // so there is nothing to announce as having been left.
            if (RunLevel > ClientRunLevel.Connecting)
            {
                PlayerLeaveServer?.Invoke(this, new PlayerEventArgs(_playMan.LocalSession));
            }

            LastDisconnectReason = args.Reason;

            GameStoppedReset();
        }

        private void GameStartedSetup()
        {
            _entityManager.Startup();
            _mapManager.Startup();

            _timing.ResetSimTime(_timeBase);
            _timing.Paused = false;
        }

        private void GameStoppedReset()
        {
            // Order matters: game state and players go before entities and maps.
            _configManager.FlushMessages();
            _gameStates.Reset();
            _playMan.Shutdown();
            _entityManager.Shutdown();
            _mapManager.Shutdown();
            _discord.ClearPresence();
            Reset();
        }

        private void OnStatusChanged(object? sender, SessionStatusEventArgs e)
        {
            // Only the local session's transitions matter here.
            if (e.Session != _playMan.LocalSession)
                return;

            if (e.OldStatus == SessionStatus.Connecting)
            {
                // Takes priority even if the new status is already InGame.
                OnPlayerJoinedServer(e.Session);
            }
            else if (e.NewStatus == SessionStatus.InGame)
            {
                OnPlayerJoinedGame(e.Session);
            }
        }

        private void OnRunLevelChanged(ClientRunLevel newRunLevel)
        {
            _logger.Debug($"Runlevel changed to: {newRunLevel}");

            // Capture the old level before overwriting it.
            var args = new RunLevelChangedEventArgs(RunLevel, newRunLevel);
            RunLevel = newRunLevel;
            RunLevelChanged?.Invoke(this, args);
        }

        void IPostInjectInit.PostInject()
        {
            _logger = _logMan.GetSawmill("client");
        }
    }

    /// <summary>
    ///     The stages of the client's life cycle, from initialization to being in a game.
    /// </summary>
    /// <seealso cref="ClientRunLevelExt"/>
    public enum ClientRunLevel : byte
    {
        Error = 0,

        /// <summary>
        ///     The client is not connected and not in a game (for example at the main menu).
        /// </summary>
        Initialize,

        /// <summary>
        ///     The client has started connecting and is building up its session.
        /// </summary>
        Connecting,

        /// <summary>
        ///     The connection is established and the session is set up.
        /// </summary>
        Connected,

        /// <summary>
        ///     The client is playing on a server.
        /// </summary>
        InGame,

        /// <summary>
        ///     The client is playing a singleplayer game.
        /// </summary>
        SinglePlayerGame,
    }

    /// <summary>
    ///     Helper extension methods for <see cref="ClientRunLevel"/>.
    /// </summary>
    public static class ClientRunLevelExt
    {
        /// <summary>
        ///     Returns true if the run level is one in which a game is actually being played
        ///     (<see cref="ClientRunLevel.InGame"/> or <see cref="ClientRunLevel.SinglePlayerGame"/>).
        /// </summary>
        public static bool IsInGameLike(this ClientRunLevel runLevel)
        {
            return runLevel == ClientRunLevel.InGame || runLevel == ClientRunLevel.SinglePlayerGame;
        }
    }

    /// <summary>
    ///     Data for events that report a change related to a player session.
    /// </summary>
    public sealed class PlayerEventArgs : EventArgs
    {
        /// <summary>
        ///         The player session the event is about.
        /// </summary>
        private ICommonSession? Session { get; }

        /// <summary>
        ///     Constructs a new instance of <see cref="PlayerEventArgs"/>.
        /// </summary>
        /// <param name="session">The session that triggered the event; may be null.</param>
        public PlayerEventArgs(ICommonSession? session)
        {
            Session = session;
        }
    }

    /// <summary>
    ///     Data for the event raised when the client moves to another run level.
    /// </summary>
    public sealed class RunLevelChangedEventArgs : EventArgs
    {
        /// <summary>
        ///     The run level the client switched from.
        /// </summary>
        public ClientRunLevel OldLevel { get; }

        /// <summary>
        ///     The run level the client switched to.
        /// </summary>
        public ClientRunLevel NewLevel { get; }

        /// <summary>
        ///     Constructs a new instance of <see cref="RunLevelChangedEventArgs"/>.
        /// </summary>
        /// <param name="oldLevel">The run level the client switched from.</param>
        /// <param name="newLevel">The run level the client switched to.</param>
        public RunLevelChangedEventArgs(ClientRunLevel oldLevel, ClientRunLevel newLevel)
        {
            OldLevel = oldLevel;
            NewLevel = newLevel;
        }
    }

    /// <summary>
    ///     Details about the server and the local player that the server sends while the client connects.
    /// </summary>
    public sealed class ServerInfo
    {
        public ServerInfo(string serverName)
        {
            ServerName = serverName;
        }

        /// <summary>
        ///     The current name of the server.
        /// </summary>
        public string ServerName { get; set; }

        /// <summary>
        ///     The maximum number of players allowed on the server at once.
        /// </summary>
        public int ServerMaxPlayers { get; set; }

        public uint TickRate { get; internal set; }
    }
}
