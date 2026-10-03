using System;
using System.Threading.Tasks;

namespace Robust.Shared.Network;

/// <summary>
///     The server-side view of the network manager: authentication settings, connection approval hooks and
///     kicking clients.
/// </summary>
/// <remarks>
///     Content must not implement this interface. The engine's net manager is the only real implementation;
///     inject it through dependency injection rather than creating your own.
/// </remarks>
[NotContentImplementable]
public interface IServerNetManager : INetManager
{
    /// <summary>
    ///     Asynchronously decides whether a connection that is waiting for approval may proceed.
    /// </summary>
    /// <param name="eventArgs">Identifies the pending connection.</param>
    /// <returns>
    ///     A task producing the verdict: allowed, or denied together with a reason that is sent to the client.
    /// </returns>
    public delegate Task<NetApproval> NetApprovalDelegate(NetApprovalEventArgs eventArgs);

    /// <summary>
    ///     The server's public key for the encrypted handshake, which clients use to derive the shared secret
    ///     and, when authenticating, the join hash. It is generated once at startup and never changes.
    ///     <see langword="null"/> is possible for implementations without encryption (such as test fakes), so
    ///     callers must check for it.
    /// </summary>
    byte[]? CryptoPublicKey { get; }

    /// <summary>
    ///     The current authentication mode of the server. It is derived from configuration and can change at
    ///     runtime. It controls whether unauthenticated clients are admitted and whether guest name prefixes are
    ///     added.
    /// </summary>
    AuthMode Auth { get; }

    /// <summary>
    ///     Optional hook for choosing the <see cref="NetUserId"/> of an unauthenticated (guest) connection. It
    ///     receives the user name that was already assigned to the connection. A non-null result is used as the
    ///     user id and marks the login as a guest with an assigned id; a null result, or no callback at all,
    ///     makes the engine generate a random id. It is not called for authenticated users. The default is
    ///     <see langword="null"/>.
    /// </summary>
    Func<string, Task<NetUserId?>>? AssignUserIdCallback { get; set; }

    /// <summary>
    ///     Optional hook that is awaited for every connection awaiting approval. A denial rejects the connection
    ///     with the supplied reason. When it is <see langword="null"/>, which is the default, every connection is
    ///     approved.
    /// </summary>
    NetApprovalDelegate? HandleApprovalCallback { get; set; }

    /// <summary>
    ///     Disconnects a channel and tells the remote peer why. This is equivalent to calling
    ///     <see cref="INetChannel.Disconnect(string)"/> on the channel, so it does nothing when the channel is no
    ///     longer connected, and it does not throw for channels that are already disconnected.
    /// </summary>
    /// <param name="channel">The channel to disconnect.</param>
    /// <param name="reason">Text delivered to the peer and logged.</param>
    void DisconnectChannel(INetChannel channel, string reason);
}
