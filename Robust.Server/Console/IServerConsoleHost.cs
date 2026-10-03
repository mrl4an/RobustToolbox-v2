using Robust.Shared.Analyzers;
using Robust.Shared.Console;

namespace Robust.Server.Console;

/// <summary>
///     The server flavour of the console host. It adds only <see cref="Initialize"/> on top of
///     <see cref="IConsoleHost"/>; command registration, execution, output and everything else is inherited.
/// </summary>
/// <remarks>
///     <para>
///         Content obtains this service through dependency injection and uses the inherited members, for example to
///         run a command as a player session or as the host, to write lines, or to register commands. The engine's
///         sole implementation reports <c>IsServer</c> as <see langword="true"/>. Tests may register a testing
///         implementation in its place.
///     </para>
///     <para>
///         Content must not implement this interface.
///     </para>
/// </remarks>
[NotContentImplementable]
public interface IServerConsoleHost : IConsoleHost
{
    /// <summary>
    ///     One-time startup call that brings the console host into working state. It registers the console
    ///     commands the server host provides itself (for example <c>sudo</c>, which runs a command with host
    ///     privileges) so they show up among the available commands.
    /// </summary>
    /// <remarks>
    ///     The server start-up sequence calls this exactly once, after the dependency container has been populated
    ///     and before the server starts running exec-commands from the command line or processing the command
    ///     buffer each tick. It runs on the main server thread and is not thread safe. Content has no reason to
    ///     call it, and it must not be called twice.
    /// </remarks>
    void Initialize();
}
