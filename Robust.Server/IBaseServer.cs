using System;
using Robust.Shared.Log;
using Robust.Shared.Timing;

namespace Robust.Server;

/// <summary>
///     The top-level host service of a dedicated server process. It exposes the server's identity and capacity
///     and the process-wide lifecycle operations: start-up, the main loop, restart and shutdown.
/// </summary>
/// <remarks>
///     <para>
///         The engine entry point drives the lifecycle in a fixed order: <see cref="Start"/> once, then
///         <see cref="MainLoop"/> once. Both are expected to run on the main thread. Game content normally only
///         reads <see cref="ServerName"/> and requests <see cref="Shutdown"/>; it should never call
///         <see cref="Start"/>, <see cref="MainLoop"/> or <see cref="Restart"/>.
///     </para>
///     <para>
///         This interface is not meant to be implemented by game content, and members may be added to it in the
///         future.
///     </para>
/// </remarks>
[NotContentImplementable]
public interface IBaseServer
{
    /// <summary>
    ///     The maximum number of players that may be connected to this server at the same time.
    /// </summary>
    /// <remarks>
    ///     The value is derived from the current network configuration every time it is read rather than being
    ///     cached, so runtime configuration changes are reflected. It is used by the player manager to size its
    ///     tracking and to refuse incoming connections once the server is full.
    /// </remarks>
    int MaxPlayers { get; }

    /// <summary>
    ///     The human-readable name of this server, intended for display purposes such as status pages and
    ///     chat or webhook messages.
    /// </summary>
    /// <remarks>
    ///     The value is read from the host-name configuration variable on every access, so changes made at
    ///     runtime are visible immediately. It is never <see langword="null"/>, but it may be empty.
    /// </remarks>
    string ServerName { get; }

    /// <summary>
    ///     Performs the full start-up sequence of the server: configuration, logging, networking, resource
    ///     mounting, content loading and initialization of all engine services.
    /// </summary>
    /// <param name="options">
    ///     Options that control how the server starts, such as sandboxing, resource mounting, the content
    ///     module prefix and whether configuration and user data are loaded from disk.
    /// </param>
    /// <param name="logHandler">
    ///     An optional factory that creates an additional log handler. It is invoked while logging is being
    ///     configured. When it is <see langword="null"/> or produces no handler and logging to file is
    ///     enabled, a default file-based handler is used instead.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> if start-up <b>failed</b> and the server cannot run, and
    ///     <see langword="false"/> if it succeeded. Note that this is the opposite of the usual
    ///     "returns true on success" convention. Typical failures are a misconfigured remote logging sink, the
    ///     network layer being unable to bind its port, and content assemblies that fail to load. When a
    ///     failure is reported the cause has already been written to the log, and the caller is expected to
    ///     abandon the process rather than call <see cref="MainLoop"/>.
    /// </returns>
    /// <remarks>
    ///     Must be called on the main thread, before <see cref="MainLoop"/>. For inputs that can only be
    ///     supplied through the internal host interface, such as parsed command-line arguments, those must be
    ///     provided before this method is called.
    /// </remarks>
    bool Start(ServerOptions options, Func<ILogHandler>? logHandler = null);

    /// <summary>
    ///     Performs a hard restart of the server: all engine services are torn down in the same way as at the
    ///     end of the main loop, and <see cref="Start"/> is then invoked again with the options and log handler
    ///     factory that were used originally.
    /// </summary>
    /// <remarks>
    ///     This is called by the <c>restart</c> console command. It is not a reliable operation: tearing down
    ///     and rebuilding every service in-process can leave the server in a broken state, and the result of
    ///     the second start-up is not inspected. Content must not depend on it, and a clean restart is better
    ///     achieved by shutting the process down and letting an external supervisor launch it again.
    /// </remarks>
    void Restart();

    /// <summary>
    ///     Requests an orderly shutdown of the server. This call does not terminate the process by itself; it
    ///     signals the main loop to stop, and the actual teardown happens after <see cref="MainLoop"/> returns.
    /// </summary>
    /// <param name="reason">
    ///     A human-readable explanation, written to the log and included in the message sent to connected
    ///     clients when they are disconnected. May be <see langword="null"/> or whitespace, in which case a
    ///     generic message is used.
    /// </param>
    /// <remarks>
    ///     <para>
    ///         Calling this more than once is harmless. Calling it before <see cref="MainLoop"/> has started
    ///         stops the main loop from running at all, even if <paramref name="reason"/> is
    ///         <see langword="null"/>.
    ///     </para>
    ///     <para>
    ///         This method is not thread-safe. It must be called on the main thread; code running on other
    ///         threads should first marshal the call onto the main thread, for example through the task
    ///         manager.
    ///     </para>
    ///     <para>
    ///         The additional log handler supplied to <see cref="Start"/> is detached and disposed as part of
    ///         this call, so messages logged during the subsequent teardown no longer reach it.
    ///     </para>
    /// </remarks>
    void Shutdown(string? reason);

    /// <summary>
    ///     Runs the server's fixed-timestep game loop on the calling thread. This method blocks until
    ///     <see cref="Shutdown"/> has been requested, then performs the final cleanup before returning.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It is called once by the entry point, on the main thread, after <see cref="Start"/> has
    ///         reported success. If a shutdown was already requested before the loop begins, the loop is
    ///         skipped (a fatal message is logged) and only the cleanup is performed.
    ///     </para>
    ///     <para>
    ///         Each iteration processes incoming input and network traffic, advances the simulation tick, and
    ///         runs per-frame updates. When this method returns, all services have been shut down.
    ///     </para>
    /// </remarks>
    void MainLoop();
}

/// <summary>
///     Engine-internal extension of <see cref="IBaseServer"/>. It carries the members that the entry point and
///     test harnesses use to configure the server but that game content has no business touching.
/// </summary>
internal interface IBaseServerInternal : IBaseServer
{
    /// <summary>
    ///     The options that were most recently passed to <see cref="IBaseServer.Start"/>. Before the server has
    ///     been started this is a default-constructed <see cref="ServerOptions"/> instance.
    /// </summary>
    ServerOptions Options { get; }

    /// <summary>
    ///     Set by the entry point before <see cref="IBaseServer.Start"/> to indicate that the process was
    ///     launched through the content executable rather than through the engine's own loader.
    /// </summary>
    /// <remarks>
    ///     When enabled, content assemblies are not loaded into a separate load context, which prevents them
    ///     from being loaded twice, and resource mounting uses the content start mode.
    /// </remarks>
    bool ContentStart { set; }

    /// <summary>
    ///     Replaces the game loop that <see cref="IBaseServer.MainLoop"/> runs, so that tests and headless
    ///     harnesses can supply their own. When a loop has been provided, no default loop is created.
    /// </summary>
    /// <param name="gameLoop">The game loop to use in place of the default one.</param>
    /// <remarks>Must be called before <see cref="IBaseServer.MainLoop"/>.</remarks>
    void OverrideMainLoop(IGameLoop gameLoop);

    /// <summary>
    ///     Provides the parsed command-line arguments that <see cref="IBaseServer.Start"/> consumes, such as
    ///     the configuration file path, data directory, configuration variable overrides, log level overrides,
    ///     mount options and commands to execute on start-up.
    /// </summary>
    /// <param name="args">The parsed arguments. A missing value is treated the same as no arguments.</param>
    /// <remarks>Must be called before <see cref="IBaseServer.Start"/>.</remarks>
    void SetCommandLineArgs(CommandLineArgs args);
}
