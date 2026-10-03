using Robust.Shared.Input;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Robust.Server.Player;

/// <summary>
///     Keeps track of the sessions of the players connected to the server.
/// </summary>
[NotContentImplementable]
public interface IPlayerManager : ISharedPlayerManager
{
    /// <summary>
    ///     The server's <see cref="BoundKeyMap"/>: the table that maps input key function names to numeric ids
    ///     shared with clients.
    /// </summary>
    BoundKeyMap KeyMap { get; }

    internal void MarkPlayerResourcesSent(INetChannel channel);
}
