using System;

namespace Robust.Shared.Network;

/// <summary>
///     The client-side view of the network manager: connecting to a server, tracking the connection state and
///     injecting locally created messages.
/// </summary>
/// <remarks>
///     Content must not implement this interface. The engine's net manager is the only real implementation;
///     inject it through dependency injection rather than creating your own.
/// </remarks>
[NotContentImplementable]
public interface IClientNetManager : INetManager
{
    /// <summary>
    ///     The channel to the server, or <see langword="null"/> when no channel is established (not
    ///     connected, still connecting, or after a reset). Only meaningful on a client.
    /// </summary>
    INetChannel? ServerChannel { get; }

    /// <summary>
    ///     The current phase of the client connection state machine.
    /// </summary>
    ClientConnectionState ClientConnectState { get; }

    /// <summary>
    ///     Raised with the new state every time <see cref="ClientConnectState"/> is assigned. It may fire again
    ///     with an unchanged value, so subscribers must tolerate duplicate notifications. It runs on the thread
    ///     that performs the transition.
    /// </summary>
    event Action<ClientConnectionState> ClientConnectStateChanged;

    /// <summary>
    ///     Raised when a connection attempt that is in progress fails, for example because the host cannot be
    ///     resolved, every address fails, the attempt is cancelled or the handshake is rejected. The event
    ///     arguments carry a readable reason and a redial flag. It is not raised when an already established
    ///     connection ends; use <see cref="INetManager.Disconnect"/> for that.
    /// </summary>
    event EventHandler<NetConnectFailArgs> ConnectFailed;

    /// <summary>
    ///     Starts connecting to a server and returns immediately. Watch <see cref="ClientConnectStateChanged"/>,
    ///     <see cref="ConnectFailed"/> and <see cref="INetManager.Connected"/> to follow the outcome.
    /// </summary>
    /// <param name="host">DNS name or IP address of the server. It is resolved asynchronously.</param>
    /// <param name="port">UDP port of the server.</param>
    /// <param name="userNameRequest">
    ///     The preferred user name. This is only a request; the server may assign a different name.
    /// </param>
    /// <remarks>
    ///     Networking must have been started with <see cref="INetManager.Initialize"/> in client mode first. It
    ///     must not be called on a server.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the client is already connected, or when another connection attempt is still in progress
    ///     and has to be cancelled first.
    /// </exception>
    void ClientConnect(string host, int port, string userNameRequest);

    /// <summary>
    ///     Disconnects from the server and cancels any connection attempt in progress.
    /// </summary>
    /// <param name="reason">Text that is logged and sent to the server as the disconnect reason.</param>
    /// <remarks>
    ///     If a server channel exists, <see cref="INetManager.Disconnect"/> is raised before the network layer is
    ///     reset, so subscribers can still use the channel. Because the reset is part of this call, networking
    ///     has to be initialized again before reconnecting.
    /// </remarks>
    void ClientDisconnect(string reason);

    /// <summary>
    ///     Hard-resets the network layer: disconnects every channel, stops all underlying network peers, clears
    ///     the registered handler table and the message string table, cancels any pending connection attempt and
    ///     sets the state back to not connecting.
    /// </summary>
    /// <param name="reason">Disconnect reason given to the channels.</param>
    /// <remarks>
    ///     This blocks the calling thread until the peers have stopped, so call it from the main thread. The
    ///     string table is cleared, which means message ids are downloaded again on the next connect. External
    ///     event subscribers are not removed.
    /// </remarks>
    void Reset(string reason);

    /// <summary>
    ///     Delivers a locally created message to the receive callback registered for its type, exactly as if it
    ///     had arrived from the network but without serializing it. The lookup uses the message name; when no
    ///     type is registered under that name nothing happens. The message channel is not set.
    /// </summary>
    /// <param name="message">The message to dispatch. It is not sent over the network.</param>
    /// <remarks>
    ///     Called on the main thread, for example by replay playback to inject recorded messages.
    /// </remarks>
    void DispatchLocalNetMessage(NetMessage message);
}
