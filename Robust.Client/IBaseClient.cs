using System;
using System.Net;

namespace Robust.Client;

/// <summary>
///     The client-side counterpart of the server host: it owns the client's connection state machine and lets
///     the rest of the engine and game content connect to servers, run offline single-player sessions, and
///     observe how the session changes over time.
/// </summary>
/// <remarks>
///     <para>
///         The state of the client is summarized by <see cref="RunLevel"/>, which moves from
///         <see cref="ClientRunLevel.Initialize"/> (main menu) through <see cref="ClientRunLevel.Connecting"/>
///         and <see cref="ClientRunLevel.Connected"/> to <see cref="ClientRunLevel.InGame"/> for networked
///         sessions, or directly to <see cref="ClientRunLevel.SinglePlayerGame"/> for offline ones. Any
///         disconnect, failure or stop returns the client to <see cref="ClientRunLevel.Initialize"/>.
///     </para>
///     <para>
///         The client is not synchronized for multi-threaded use; all members are meant to be called, and all
///         events are raised, on the main thread.
///     </para>
///     <para>
///         This interface is not meant to be implemented by game content, and members may be added to it in
///         the future.
///     </para>
/// </remarks>
[NotContentImplementable]
public interface IBaseClient
{
    /// <summary>
    ///     The network port to use when the user did not specify one, for example when parsing an address typed
    ///     into a connection dialog.
    /// </summary>
    ushort DefaultPort { get; }

    /// <summary>
    ///     The current stage of the client's connection state machine. The value is
    ///     <see cref="ClientRunLevel.Error"/> until <see cref="Initialize"/> has run.
    /// </summary>
    /// <remarks>
    ///     Outside code cannot assign this; it changes only as a result of connecting, disconnecting,
    ///     single-player operations and network notifications. Every change raises
    ///     <see cref="RunLevelChanged"/>.
    /// </remarks>
    ClientRunLevel RunLevel { get; }

    /// <summary>
    ///     Information about the server the client is, or was most recently, connected to, such as its name and
    ///     tick rate. It is <see langword="null"/> until the first successful multiplayer handshake after
    ///     <see cref="Initialize"/>.
    /// </summary>
    /// <remarks>
    ///     Once created, the same instance is kept and updated by later connections; it is not reset on
    ///     disconnect, so it can describe the previous server while the client is back at the main menu.
    ///     Single-player sessions never populate it.
    /// </remarks>
    ServerInfo? GameInfo { get; }

    /// <summary>
    ///     When non-<see langword="null"/>, this name is used instead of the configured player name, both as
    ///     the user name sent to a server when connecting and as the name of the local player in single-player.
    ///     The value is not validated. The default is <see langword="null"/>.
    /// </summary>
    string? PlayerNameOverride { get; set; }

    /// <summary>
    ///     The reason text that accompanied the most recent network disconnect, or <see langword="null"/> if
    ///     the client has not been disconnected yet.
    /// </summary>
    /// <remarks>
    ///     It is replaced on every disconnect, including ones initiated by the client itself, and is not
    ///     cleared when a new connection attempt starts. It is assigned after
    ///     <see cref="PlayerLeaveServer"/> handlers have run, so those handlers still see the previous value.
    /// </remarks>
    string? LastDisconnectReason { get; }

    /// <summary>
    ///     Raised each time <see cref="RunLevel"/> is assigned. The event is raised synchronously on the
    ///     thread that triggered the change, after <see cref="RunLevel"/> already returns the new value.
    /// </summary>
    /// <remarks>
    ///     The event can be raised even when the old and new levels are equal, for instance when the client
    ///     resets to <see cref="ClientRunLevel.Initialize"/> while already there, so handlers must tolerate
    ///     that. The event arguments carry both levels, which lets handlers tell a disconnect from a plain
    ///     return to the main menu.
    /// </remarks>
    event EventHandler<RunLevelChangedEventArgs> RunLevelChanged;

    /// <summary>
    ///     Raised when the local player's session completes its connection handshake with a server, that is,
    ///     when the session leaves the connecting status.
    /// </summary>
    /// <remarks>
    ///     At this point <see cref="RunLevel"/> has just become <see cref="ClientRunLevel.Connected"/>, the
    ///     entity and map managers have been started, and the simulation clock has been synchronized with
    ///     the server and unpaused.
    /// </remarks>
    event EventHandler<PlayerEventArgs> PlayerJoinedServer;

    /// <summary>
    ///     Raised when the local player's session enters the game proper, after the handshake has completed.
    /// </summary>
    /// <remarks>
    ///     At this point <see cref="RunLevel"/> has just become <see cref="ClientRunLevel.InGame"/>.
    /// </remarks>
    event EventHandler<PlayerEventArgs> PlayerJoinedGame;

    /// <summary>
    ///     Raised when the network connection to the server is lost or closed, immediately before the client
    ///     tears down its game state (entity and map managers, player manager and game state buffers). It is
    ///     the place to release anything that depends on that state.
    /// </summary>
    /// <remarks>
    ///     The event is only raised if <see cref="RunLevel"/> was above <see cref="ClientRunLevel.Connecting"/>
    ///     at the time. A connection that failed or dropped while still connecting does not raise it, and
    ///     neither does <see cref="StopSinglePlayer"/>, since single-player has no network disconnect.
    /// </remarks>
    event EventHandler<PlayerEventArgs> PlayerLeaveServer;

    /// <summary>
    ///     Performs one-time setup of the client: hooks the network notifications, registers the time
    ///     synchronization message, applies and watches the tick rate and time scale configuration variables,
    ///     initializes the player manager, and puts the client into its initial state.
    /// </summary>
    /// <remarks>
    ///     Call this exactly once during client start-up. It sets <see cref="RunLevel"/> to
    ///     <see cref="ClientRunLevel.Initialize"/>, which raises <see cref="RunLevelChanged"/>. Calling it a
    ///     second time is not supported.
    /// </remarks>
    void Initialize();

    /// <summary>
    ///     Begins connecting to a remote server, given as a host name or IP address and a port.
    /// </summary>
    /// <param name="ip">
    ///     The host name or IP address literal of the server. Despite the parameter name, host names are
    ///     accepted; resolution is left to the network layer.
    /// </param>
    /// <param name="port">The port of the server.</param>
    /// <remarks>
    ///     This is a convenience overload that creates a <see cref="DnsEndPoint"/> from the two values and
    ///     forwards to <see cref="ConnectToServer(DnsEndPoint)"/>, which describes the behavior. No further
    ///     validation is performed here.
    /// </remarks>
    void ConnectToServer(string ip, ushort port)
    {
        ConnectToServer(new DnsEndPoint(ip, port));
    }

    /// <summary>
    ///     Begins connecting to a remote server. The call returns immediately; the outcome is reported
    ///     asynchronously through <see cref="RunLevelChanged"/>.
    /// </summary>
    /// <param name="endPoint">The host and port of the server to connect to.</param>
    /// <remarks>
    ///     <para>
    ///         If a connection attempt is already in progress it is aborted and the client resets to
    ///         <see cref="ClientRunLevel.Initialize"/> before the new attempt starts. The run level then
    ///         becomes <see cref="ClientRunLevel.Connecting"/>, and the network layer is asked to connect,
    ///         using <see cref="PlayerNameOverride"/> as the user name if it is set and the configured player
    ///         name otherwise.
    ///     </para>
    ///     <para>
    ///         On failure the client returns to <see cref="ClientRunLevel.Initialize"/> without raising
    ///         <see cref="PlayerLeaveServer"/>. On success it proceeds through configuration sync and session
    ///         setup to <see cref="ClientRunLevel.Connected"/>.
    ///     </para>
    ///     <para>
    ///         Calling this while the client is already connected, in game, or in single-player violates the
    ///         contract. Debug builds assert on it, and the behavior in release builds is undefined.
    ///     </para>
    /// </remarks>
    void ConnectToServer(DnsEndPoint endPoint);

    /// <summary>
    ///     Asks the network layer to disconnect from the current server, sending the given reason.
    /// </summary>
    /// <param name="reason">The reason text to send to the server as the disconnect reason.</param>
    /// <remarks>
    ///     The call does not tear anything down by itself. The consequences, namely
    ///     <see cref="PlayerLeaveServer"/>, state teardown, the update of <see cref="LastDisconnectReason"/>
    ///     and the return to <see cref="ClientRunLevel.Initialize"/>, happen when the network layer reports
    ///     the disconnection. If there is no connection, nothing happens.
    /// </remarks>
    void DisconnectFromServer(string reason);

    /// <summary>
    ///     Starts an offline single-player session, also used for replay playback.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The local session is created using <see cref="PlayerNameOverride"/> if set and the configured
    ///         player name otherwise, <see cref="RunLevel"/> becomes
    ///         <see cref="ClientRunLevel.SinglePlayerGame"/>, the entity and map managers are started, and the
    ///         simulation clock is reset and unpaused.
    ///     </para>
    ///     <para>
    ///         The client must be below <see cref="ClientRunLevel.Connecting"/> with no network connection;
    ///         debug builds assert on this. <see cref="PlayerJoinedServer"/> and
    ///         <see cref="PlayerJoinedGame"/> are not raised for single-player sessions.
    ///     </para>
    /// </remarks>
    void StartSinglePlayer();

    /// <summary>
    ///     Ends an offline single-player session and returns the client to
    ///     <see cref="ClientRunLevel.Initialize"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The client must currently be in <see cref="ClientRunLevel.SinglePlayerGame"/>; debug builds
    ///         assert on this. Pending configuration messages are flushed, game state is reset, and the
    ///         player, entity and map managers are shut down.
    ///     </para>
    ///     <para>
    ///         <see cref="PlayerLeaveServer"/> is not raised, because it only reports network disconnects.
    ///     </para>
    /// </remarks>
    void StopSinglePlayer();
}
