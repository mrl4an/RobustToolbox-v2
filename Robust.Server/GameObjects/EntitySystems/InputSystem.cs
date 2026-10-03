using System;
using System.Collections.Generic;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Player;

namespace Robust.Server.GameObjects;

/// <summary>
///     Server side of the input pipeline. It receives <see cref="FullInputCmdMessage"/>s sent by clients, keeps
///     per-session bookkeeping about them, and dispatches them to the handlers registered in
///     <see cref="SharedInputSystem.BindRegistry"/>.
/// </summary>
/// <remarks>
///     <para>
///         The system is purely event driven: it has no per-frame update and declares no update ordering. It listens
///         to two things, the network event for input commands and the player manager's session status changes.
///     </para>
///     <para>
///         Per session it tracks two pieces of data. The first is an <see cref="IPlayerCommandStates"/>, the state
///         (up or down) that the server last recorded for every key function from that session. The second is the
///         highest input sequence number processed for that session, which the game state sender reads through
///         <see cref="GetLastInputCommand"/> so a predicting client can discard inputs the server has already
///         acknowledged. Both pieces of data always exist for exactly the same set of sessions.
///     </para>
///     <para>
///         Lifecycle: a session is registered when it reaches <see cref="SessionStatus.Connected"/> and forgotten when
///         it reaches <see cref="SessionStatus.Disconnected"/>. Every other status is ignored. Sessions that are
///         already connected at the time <see cref="Initialize"/> runs are not registered retroactively.
///     </para>
///     <para>
///         This system performs no logging and no rate limiting. Limits on message volume belong to the network
///         layer. Exceptions are deliberately not caught anywhere in this class, see
///         <see cref="OnFullInputCmd"/> and <see cref="OnPlayerStatusChanged"/>.
///     </para>
/// </remarks>
public sealed class InputSystem : SharedInputSystem
{
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    // Both dictionaries are keyed with the default equality of ICommonSession and are always modified together,
    // so they hold the same keys at any point in time. Everything runs on the main simulation thread, so no
    // locking is needed.
    private readonly Dictionary<ICommonSession, IPlayerCommandStates> _commandStates = new();
    private readonly Dictionary<ICommonSession, uint> _lastProcessedSequence = new();

    /// <inheritdoc />
    /// <remarks>
    ///     Subscribes to <see cref="FullInputCmdMessage"/> as a network-only event, without any ordering
    ///     constraints, and starts listening to session status changes. The base implementation is not called
    ///     because it does nothing.
    /// </remarks>
    public override void Initialize()
    {
        SubscribeNetworkEvent<FullInputCmdMessage>(OnFullInputCmd);
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The base implementation runs first and removes the event subscriptions made through this system. After
    ///     that the status change listener is detached. The per-session data is intentionally left in place; a new
    ///     instance of the system always starts with empty data.
    /// </remarks>
    public override void Shutdown()
    {
        base.Shutdown();

        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    /// <summary>
    ///     Gets the key function states that the server has recorded for a session.
    /// </summary>
    /// <param name="session">The session to query. It must not be null.</param>
    /// <returns>
    ///     The live tracker object for the session, not a copy. Functions for which no valid command has been
    ///     received read as <see cref="BoundKeyState.Up"/>.
    /// </returns>
    /// <exception cref="KeyNotFoundException">
    ///     The session was never registered, that is, no <see cref="SessionStatus.Connected"/> status change was
    ///     seen for it while this system was alive, or it has been disconnected since.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public IPlayerCommandStates GetInputStates(ICommonSession session)
    {
        return _commandStates[session];
    }

    /// <summary>
    ///     Gets the highest input sequence number that has been processed for a session.
    /// </summary>
    /// <param name="session">The session to query, or null.</param>
    /// <returns>
    ///     The highest sequence number seen so far. This is 0 until the first sequenced command arrives, and also 0
    ///     if <paramref name="session"/> is null or not registered, for example after it disconnected. This method
    ///     never throws.
    /// </returns>
    /// <remarks>
    ///     The game state sender calls this once per session per tick to stamp outgoing states, which lets the
    ///     client drop the predicted inputs that the server has already acknowledged.
    /// </remarks>
    public uint GetLastInputCommand(ICommonSession? session)
    {
        if (session == null)
            return 0;

        return _lastProcessedSequence.TryGetValue(session, out var sequence) ? sequence : 0;
    }

    /// <summary>
    ///     Registers and unregisters per-session data in response to session status changes.
    /// </summary>
    /// <remarks>
    ///     Registration uses strict add semantics: if a session is reported as connected twice, the second add
    ///     throws an <see cref="ArgumentException"/>, which is not caught and propagates to whoever raised the status
    ///     change. Removal is lenient; removing a session that was never registered, such as one that went from
    ///     connecting straight to disconnected, does nothing.
    /// </remarks>
    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        switch (args.NewStatus)
        {
            case SessionStatus.Connected:
                _commandStates.Add(args.Session, new PlayerCommandStates());
                _lastProcessedSequence.Add(args.Session, 0);
                break;

            case SessionStatus.Disconnected:
                _commandStates.Remove(args.Session);
                _lastProcessedSequence.Remove(args.Session);
                break;
        }
    }

    /// <summary>
    ///     Handles an input command sent by a client.
    /// </summary>
    /// <param name="message">The received command.</param>
    /// <param name="args">
    ///     Event arguments whose sender session was taken from the network channel the message arrived on, never
    ///     from data supplied by the client.
    /// </param>
    /// <remarks>
    ///     <para>
    ///         The steps below run in this order, and the order is observable. Everything the client sends is
    ///         untrusted, so the two sanitization steps come first. A message that fails either of them is dropped
    ///         silently, without a log entry and without touching any state; in particular, its sequence number is
    ///         not acknowledged.
    ///     </para>
    ///     <list type="number">
    ///         <item>Translate the function id to a <see cref="BoundKeyFunction"/>. Unknown ids are dropped.</item>
    ///         <item>Check that the state is a defined <see cref="BoundKeyState"/>. A client can send any byte, so
    ///         other values are dropped.</item>
    ///         <item>Raise the session's last processed sequence number to the message's sequence number if the
    ///         latter is strictly greater (unsigned comparison). It never decreases, so repeated or reordered
    ///         messages cannot move it back, and unsequenced messages (sequence 0) never change it. This happens even
    ///         when no handler is bound to the function.</item>
    ///         <item>Record the new state of the function for the session, whether or not it changed and whether or
    ///         not any handler exists. This happens before the handlers run, so they, and later queries through
    ///         <see cref="GetInputStates"/>, observe the new state.</item>
    ///         <item>Offer the message to the handlers bound to the function, in the order given by the registry,
    ///         and stop at the first one that reports the command as handled. Commands without handlers are simply
    ///         ignored. No reply is sent to the client.</item>
    ///     </list>
    ///     <para>
    ///         Nothing is validated beyond that: tick, sub-tick, coordinates and entity id are passed through as
    ///         received, and there is no check that the session is in game or has an attached entity. Handlers are
    ///         responsible for converting network coordinates and ids into local ones.
    ///     </para>
    ///     <para>
    ///         Exceptions are intentionally not handled here. If the session was never registered (for example a
    ///         command from a session that has not reached connected status, or one that connected before this system
    ///         was initialized), the sequence lookup throws <see cref="KeyNotFoundException"/>. An exception thrown by
    ///         a handler skips the remaining handlers. Both propagate to the event bus.
    ///     </para>
    /// </remarks>
    private void OnFullInputCmd(FullInputCmdMessage message, EntitySessionEventArgs args)
    {
        if (!_playerManager.KeyMap.TryGetKeyFunction(message.InputFunctionId, out var function))
            return;

        if (message.State != BoundKeyState.Up && message.State != BoundKeyState.Down)
            return;

        var session = args.SenderSession;

        // Indexer access on purpose: an unregistered session must surface as KeyNotFoundException.
        if (message.InputSequence > _lastProcessedSequence[session])
            _lastProcessedSequence[session] = message.InputSequence;

        _commandStates[session].SetState(function, message.State);

        foreach (var handler in BindRegistry.GetHandlers(function))
        {
            if (handler.HandleCmdMessage(EntityManager, session, message))
                break;
        }
    }
}
