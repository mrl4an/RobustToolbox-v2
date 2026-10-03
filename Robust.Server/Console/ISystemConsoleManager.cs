using Robust.Shared.Analyzers;

namespace Robust.Server.Console;

/// <summary>
///     Abstraction over the operating system console (standard input and output window) that the dedicated
///     server process runs in.
/// </summary>
/// <remarks>
///     <para>
///         All members are meant to be called from the main server thread only; no thread safety is promised.
///     </para>
///     <para>
///         The engine provides an interactive implementation and a do-nothing implementation. The do-nothing one is
///         valid for tests and non-interactive scenarios, so every member must be safe to implement as a no-op.
///         Content must not implement this interface.
///     </para>
/// </remarks>
[NotContentImplementable]
public interface ISystemConsoleManager
{
    /// <summary>
    ///     Pumps the system console: polls the terminal for input, maintains the command-line editing state
    ///     (buffer, cursor, history, tab completion), redraws the prompt as needed, and when the user submits a
    ///     line, forwards it to the server console host for execution as the host (with no player session).
    /// </summary>
    /// <remarks>
    ///     The server calls this once per main-loop iteration in the input stage, outside the fixed tick and before
    ///     network packets are processed. Because it is called so often, it must be cheap and must not block when
    ///     there is no input. An implementation may read the terminal on a background task and hand lines over
    ///     through a queue, but lines must be delivered to the console host on the thread that calls this method.
    /// </remarks>
    void UpdateInput();

    /// <summary>
    ///     Writes <paramref name="text"/> to the system console's standard output exactly as given.
    /// </summary>
    /// <remarks>
    ///     No newline is appended and no formatting is applied; callers that want a line break must include it in
    ///     <paramref name="text"/>. The console host uses this to show the output of commands executed as the host,
    ///     and it appends its own trailing newline, so an implementation that added one would produce double line
    ///     breaks. Call it from the main thread. The text must not be <see langword="null"/>.
    /// </remarks>
    /// <param name="text">The text to write.</param>
    void Print(string text);

    /// <summary>
    ///     Per-tick housekeeping hook, called once at the start of every server update (tick) step, before mod
    ///     pre-engine updates run.
    /// </summary>
    /// <remarks>
    ///     The interactive implementation uses it, only when the process runs interactively (a user-interactive
    ///     environment with standard input not redirected), to refresh the console window title at most once per
    ///     second with performance statistics: average frames per second, frame-time standard deviation in
    ///     milliseconds, network send and receive rates in KiB/s since the previous refresh, and the process's
    ///     private memory in KiB. It does nothing when not interactive. A do-nothing implementation does nothing.
    /// </remarks>
    void UpdateTick();
}
