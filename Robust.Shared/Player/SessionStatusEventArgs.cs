using System;
using Robust.Shared.Enums;

namespace Robust.Shared.Player;

public sealed class SessionStatusEventArgs : EventArgs
{
    /// <summary>
    ///     Creates the event data describing a session's status transition.
    /// </summary>
    /// <param name="session">The session whose status changed.</param>
    /// <param name="oldStatus">The status before the change.</param>
    /// <param name="newStatus">The status after the change.</param>
    public SessionStatusEventArgs(ICommonSession session, SessionStatus oldStatus, SessionStatus newStatus)
    {
        Session = session;
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }

    public readonly ICommonSession Session;
    public readonly SessionStatus OldStatus;
    public readonly SessionStatus NewStatus;
}