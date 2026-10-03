using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Robust.Shared.Player;
using Robust.Shared.Reflection;
using Robust.Shared.Utility;

namespace Robust.Shared.Console
{
    /// <summary>
    ///     Signature of the handler invoked when a registered console command runs.
    /// </summary>
    /// <param name="shell">The shell the command was run from. Gives access to the executing session and to output.</param>
    /// <param name="argStr">The full raw, unparsed command text, including the command name and its arguments.</param>
    /// <param name="args">The parsed arguments, one element per argument, not including the command name.</param>
    public delegate void ConCommandCallback(IConsoleShell shell, string argStr, string[] args);

    /// <summary>
    /// Called to fetch completions for a console command. See <see cref="IConsoleCommand.GetCompletion"/> for details.
    /// </summary>
    public delegate CompletionResult ConCommandCompletionCallback(IConsoleShell shell, string[] args);

    /// <summary>
    /// Called to fetch completions for a console command (async). See <see cref="IConsoleCommand.GetCompletionAsync"/> for details.
    /// </summary>
    public delegate ValueTask<CompletionResult> ConCommandCompletionAsyncCallback(IConsoleShell shell, string[] args, string argStr);

    public delegate void ConAnyCommandCallback(IConsoleShell shell, string commandName, string argStr, string[] args);

    /// <summary>
    /// The console host exists as a singleton subsystem that provides all of the features of the console API.
    /// It will register console commands, spawn console shells and execute command strings.
    /// </summary>
    [NotContentImplementable]
    public interface IConsoleHost
    {
        /// <summary>
        /// Is the shell running on the client?
        /// </summary>
        bool IsClient => !IsServer;

        /// <summary>
        /// Is the shell running on the server?
        /// </summary>
        bool IsServer { get; }

        /// <summary>
        /// The local shell of the peer that is always available.
        /// </summary>
        IConsoleShell LocalShell { get; }

        /// <summary>
        /// A map of (commandName -> ICommand) of every registered command in the shell.
        /// </summary>
        IReadOnlyDictionary<string, IConsoleCommand> AvailableCommands { get; }

        /// <summary>
        /// Invoked before any console command is executed.
        /// </summary>
        event ConAnyCommandCallback AnyCommandExecuted;
        event EventHandler ClearText;

        /// <summary>
        ///     Scans all loaded assemblies for types implementing the console command interface and registers
        ///     each of them.
        /// </summary>
        /// <remarks>
        ///     This does not notify or synchronize with clients that are already connected, so it is intended
        ///     for initialization only (such as server startup). Calling it later leaves clients unaware of any
        ///     newly found commands.
        /// </remarks>
        void LoadConsoleCommands();

        #region RegisterCommand
        /// <summary>
        /// Registers a console command into the console system. This is an alternative to
        /// creating an <see cref="IConsoleCommand"/> class.
        /// </summary>
        /// <param name="command">A string as identifier for this command.</param>
        /// <param name="description">Short one sentence description of the command.</param>
        /// <param name="help">Command format string.</param>
        /// <param name="callback">
        /// Callback to invoke when this command is executed.
        /// </param>
        void RegisterCommand(
            string command,
            string description,
            string help,
            ConCommandCallback callback,
            bool requireServerOrSingleplayer = false);

        /// <summary>
        /// Registers a console command into the console system. This is an alternative to
        /// creating an <see cref="IConsoleCommand"/> class.
        /// </summary>
        /// <param name="command">A string as identifier for this command.</param>
        /// <param name="description">Short one sentence description of the command.</param>
        /// <param name="help">Command format string.</param>
        /// <param name="callback">
        /// Callback to invoke when this command is executed.
        /// </param>
        /// <param name="completionCallback">
        /// Callback to fetch completions with.
        /// </param>
        void RegisterCommand(
            string command,
            string description,
            string help,
            ConCommandCallback callback,
            ConCommandCompletionCallback completionCallback,
            bool requireServerOrSingleplayer = false);

        /// <summary>
        /// Registers a console command into the console system. This is an alternative to
        /// creating an <see cref="IConsoleCommand"/> class.
        /// </summary>
        /// <param name="command">A string as identifier for this command.</param>
        /// <param name="description">Short one sentence description of the command.</param>
        /// <param name="help">Command format string.</param>
        /// <param name="callback">
        /// Callback to invoke when this command is executed.
        /// </param>
        /// <param name="completionCallback">
        /// Callback to fetch completions with (async variant).
        /// </param>
        void RegisterCommand(
            string command,
            string description,
            string help,
            ConCommandCallback callback,
            ConCommandCompletionAsyncCallback completionCallback,
            bool requireServerOrSingleplayer = false);

        /// <summary>
        /// Registers a console command into the console system. This is an alternative to creating an <see
        /// cref="IConsoleCommand"/> class. This override will try to automatically resolve localized help & description
        /// strings based on the command name.
        /// </summary>
        /// <param name="command">A string as identifier for this command.</param>
        /// <param name="callback">
        /// Callback to invoke when this command is executed.
        /// </param>
        void RegisterCommand(
            string command,
            ConCommandCallback callback,
            bool requireServerOrSingleplayer = false);

        /// <summary>
        /// Registers a console command into the console system. This is an alternative to creating an <see
        /// cref="IConsoleCommand"/> class. This override will try to automatically resolve localized help & description
        /// strings based on the command name.
        /// </summary>
        /// <param name="command">A string as identifier for this command.</param>
        /// <param name="callback">
        /// Callback to invoke when this command is executed.
        /// </param>
        /// <param name="completionCallback">
        /// Callback to fetch completions with.
        /// </param>
        void RegisterCommand(
            string command,
            ConCommandCallback callback,
            ConCommandCompletionCallback completionCallback,
            bool requireServerOrSingleplayer = false);

        /// <summary>
        /// Registers a console command into the console system. This is an alternative to creating an <see
        /// cref="IConsoleCommand"/> class. This override will try to automatically resolve localized help & description
        /// strings based on the command name.
        /// </summary>
        /// <param name="command">A string as identifier for this command.</param>
        /// <param name="callback">
        /// Callback to invoke when this command is executed.
        /// </param>
        /// <param name="completionCallback">
        /// Callback to fetch completions with (async variant).
        /// </param>
        void RegisterCommand(
            string command,
            ConCommandCallback callback,
            ConCommandCompletionAsyncCallback completionCallback,
            bool requireServerOrSingleplayer = false);

        /// <summary>
        /// Register an existing console command instance directly.
        /// </summary>
        /// <remarks>
        /// For this to be useful, the command has to be somehow excluded from automatic registration,
        /// such as by using the <see cref="ReflectAttribute"/>.
        /// </remarks>
        /// <param name="command">The command to register.</param>
        /// <seealso cref="BeginRegistrationRegion"/>
        void RegisterCommand(IConsoleCommand command);

        /// <summary>
        /// Begin a region for registering many console commands in one go.
        /// The region can be ended with <see cref="EndRegistrationRegion"/>.
        /// </summary>
        /// <remarks>
        /// Commands registered inside this region temporarily suppress some updating
        /// logic that would cause significant wasted work. This logic runs when the region is ended instead.
        /// </remarks>
        void BeginRegistrationRegion();

        /// <summary>
        /// End a registration region started with <see cref="BeginRegistrationRegion"/>.
        /// </summary>
        void EndRegistrationRegion();

        #endregion

        /// <summary>
        /// Unregisters a console command that has been registered previously with <see cref="RegisterCommand(string,string,string,Robust.Shared.Console.ConCommandCallback)"/>.
        /// If the specified command was registered automatically or isn't registered at all, the method will throw.
        /// </summary>
        /// <param name="command">The string identifier for the command.</param>
        void UnregisterCommand(string command);

        /// <summary>
        /// Returns the console shell for a given active session.
        /// </summary>
        /// <remarks>
        /// On the client this will always return the local shell, on the server this will return the shell of the active
        /// session.
        /// </remarks>
        /// <param name="session">Session to get the shell of.</param>
        /// <returns>Shell of the specified session.</returns>
        IConsoleShell GetSessionShell(ICommonSession session);

        /// <summary>
        ///     Runs a command on the local shell immediately and synchronously, bypassing the command buffer.
        ///     Anything already queued in the buffer stays queued.
        /// </summary>
        /// <param name="command">The full command line, with the command name first.</param>
        void ExecuteCommand(string command);

        /// <summary>
        ///     Adds a command to the end of the local command buffer. It does not run immediately; it runs after
        ///     everything already queued, and only when the buffer is drained, which pending waits can delay.
        /// </summary>
        /// <param name="command">The command line to queue.</param>
        void AppendCommand(string command);

        /// <summary>
        ///     Adds a command to the front of the local command buffer, so it is the next one executed when the
        ///     buffer is drained, ahead of everything already queued. It does not run immediately.
        /// </summary>
        /// <param name="command">The command line to queue.</param>
        void InsertCommand(string command);

        /// <summary>
        /// Processes any contents of the command buffer on the local shell. This needs to be called regularly (once a tick),
        /// inside the simulation. Pausing the server should prevent the buffer from being processed.
        /// </summary>
        void CommandBufferExecute();

        /// <summary>
        /// Executes a command string on this specific session shell. If the command does not exist, the command will be forwarded
        /// to the
        /// remote shell.
        /// </summary>
        /// <param name="session">Session of the client to execute the command.</param>
        /// <param name="command">command line string to execute.</param>
        void ExecuteCommand(ICommonSession? session, string command);

        /// <summary>
        /// Executes the command string on the remote peer. This is mainly used to forward commands from the client to the server.
        /// If there is no remote peer (this is a local shell), this function does nothing.
        /// </summary>
        /// <param name="session">Session of the remote peer to execute the command on.</param>
        /// <param name="command">Command line string to execute at the remote endpoint.</param>
        void RemoteExecuteCommand(ICommonSession? session, string command);

        /// <summary>
        /// Sends a text string to the remote session.
        /// </summary>
        /// <param name="session">
        /// Remote session to send the text message to. If this is null, the text is sent to the local
        /// console.
        /// </param>
        /// <param name="text">Text message to send.</param>
        void WriteLine(ICommonSession? session, string text);

        void WriteLine(ICommonSession? session, FormattedMessage msg);

        /// <summary>
        /// Sends a foreground colored text string to the remote session.
        /// </summary>
        /// <param name="session">
        /// Remote session to send the text message to. If this is null, the text is sent to the local
        /// console.
        /// </param>
        /// <param name="text">Text message to send.</param>
        void WriteError(ICommonSession? session, string text);

        /// <summary>
        /// Removes all text from the local console.
        /// </summary>
        void ClearLocalConsole();
    }

    internal interface IConsoleHostInternal : IConsoleHost
    {
        /// <summary>
        /// Is this command executed on the server?
        /// Always true when ran from server, true for server-proxy commands on the client.
        /// </summary>
        bool IsCmdServer(IConsoleCommand cmd);
    }
}
