using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Robust.Client.Log;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Player;
using Robust.Shared.Reflection;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;

namespace Robust.Client.Console
{
    public sealed class AddStringArgs : EventArgs
    {
        public FormattedMessage Text { get; }

        public bool Local { get; }

        public bool Error { get; }

        public AddStringArgs(FormattedMessage text, bool local, bool error)
        {
            Text = text;
            Local = local;
            Error = error;
        }
    }

    public sealed class AddFormattedMessageArgs : EventArgs
    {
        public readonly FormattedMessage Message;

        public AddFormattedMessageArgs(FormattedMessage message)
        {
            Message = message;
        }
    }

    /// <inheritdoc cref="IClientConsoleHost" />
    [Virtual]
    internal partial class ClientConsoleHost : ConsoleHost, IClientConsoleHost, IConsoleHostInternal, IPostInjectInit
    {
        [Dependency] private readonly IClientConGroupController _conGroup = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IPlayerManager _player = default!;
        [Dependency] private readonly IBaseClient _client = default!;
        [Dependency] private readonly ILogManager _logMan = default!;

        [ViewVariables] private readonly Dictionary<string, IConsoleCommand> _availableServerCommands = new();

        private bool _requestedCommands;
        private ISawmill _logger = default!;
        private ISawmill _conLogger = default!;

        public ClientConsoleHost() : base(isServer: false) {}

        /// <inheritdoc />
        public void Initialize()
        {
            NetManager.RegisterNetMessage<MsgConCmdReg>(HandleConCmdReg);
            NetManager.RegisterNetMessage<MsgConCmdAck>(HandleConCmdAck);
            NetManager.RegisterNetMessage<MsgConCmd>(ProcessCommand);
            NetManager.RegisterNetMessage<MsgConCompletion>();
            NetManager.RegisterNetMessage<MsgConCompletionResp>(ProcessCompletionResp);

            _requestedCommands = false;
            NetManager.Connected += OnNetworkConnected;
            NetManager.Disconnect += OnNetworkDisconnected;
            _client.RunLevelChanged += OnLevelChanged;

            LoadConsoleCommands();
            SendServerCommandRequest();
            LogManager.RootSawmill.AddHandler(new DebugConsoleLogHandler(this));
        }

        private readonly Dictionary<string, IConsoleCommand> _availableCommands = new();
        public override IReadOnlyDictionary<string, IConsoleCommand> AvailableCommands => _availableCommands;

        private void OnLevelChanged(object? sender, RunLevelChangedEventArgs e)
        {
            UpdateAvailableCommands();
        }

        private void OnNetworkDisconnected(object? sender, NetDisconnectedArgs e)
        {
            _availableServerCommands.Clear();
            _requestedCommands = false;
            UpdateAvailableCommands();
        }

        protected override void UpdateAvailableCommands()
        {
            _availableCommands.Clear();

            foreach (var (name, cmd) in RegisteredCommands)
            {
                if (!cmd.RequireServerOrSingleplayer || (_client.RunLevel is ClientRunLevel.Initialize or ClientRunLevel.SinglePlayerGame))
                    _availableCommands.Add(name, cmd);
            }

            foreach (var (name, cmd) in _availableServerCommands)
            {
                _availableCommands.TryAdd(name, cmd);
            }
        }

        /// <summary>
        ///     Handles a request from the server to run a command on this client.
        ///     The command is logged and then executed exactly as if the player had typed it.
        /// </summary>
        private void ProcessCommand(MsgConCmd message)
        {
            var commandText = message.Text;

            LogManager.GetSawmill(SawmillName).Info("SERVER:" + commandText);

            ExecuteCommand(null, commandText);
        }

        /// <inheritdoc />
        public event EventHandler<AddStringArgs>? AddString;

        /// <inheritdoc />
        public event EventHandler<AddFormattedMessageArgs>? AddFormatted;

        /// <inheritdoc />
        public void AddFormattedLine(FormattedMessage message)
        {
            AddFormatted?.Invoke(this, new AddFormattedMessageArgs(message));
        }

        public override void WriteLine(ICommonSession? session, FormattedMessage msg)
        {
            AddFormattedLine(msg);
        }

        /// <inheritdoc />
        public override void WriteError(ICommonSession? session, string text)
        {
            var msg = new FormattedMessage();
            msg.AddText(text);
            OutputText(msg, true, true);
        }

        public bool IsCmdServer(IConsoleCommand cmd)
        {
            return cmd is ServerDummyCommand;
        }

        public override event ConAnyCommandCallback? AnyCommandExecuted;

        /// <inheritdoc />
        public override void ExecuteCommand(ICommonSession? session, string command)
        {
            // Nothing to do for blank input.
            if (string.IsNullOrWhiteSpace(command))
                return;

            // Visually separate this command from earlier output, then echo it back to the user.
            WriteLine(null, "");

            var echo = new FormattedMessage();
            echo.PushColor(Color.Gold);
            echo.AddText("> " + command);
            echo.Pop();
            OutputText(echo, local: true, error: false);

            // Split into words; the first word names the command.
            var words = new List<string>();
            CommandParsing.ParseArguments(command, words);
            var name = words[0];

            if (!AvailableCommands.TryGetValue(name, out var cmd))
            {
                WriteError(null, "Unknown command: " + name);
                return;
            }

            if (!CanExecute(name))
            {
                WriteError(null, "Insufficient perms for command: " + name);
                return;
            }

            // Everything after the name is the argument list.
            words.RemoveAt(0);
            var args = words.ToArray();

            // Without an explicit session the command runs as the local player.
            var shell = new ConsoleShell(this, session ?? _player.LocalSession, session == null);

            try
            {
                AnyCommandExecuted?.Invoke(shell, name, command, args);
                cmd.Execute(shell, command, args);
            }
            catch (Exception e)
            {
                var details = e.ToString();
                _conLogger.Error("ExecuteError - " + command + ":\n" + details);
                shell.WriteError("There was an error while executing the command: " + details);
            }
        }

        private bool CanExecute(string cmdName)
        {
            // When not connected to a server, you can run all local commands.
            // When connected to a server, you can only run commands according to the con group controller.

            return _player.LocalSession is not { Status: > SessionStatus.Connecting }
                   || _conGroup.CanCommand(cmdName);
        }

        /// <inheritdoc />
        public override void RemoteExecuteCommand(ICommonSession? session, string command)
        {
            if (!NetManager.IsConnected) // we don't care about session on client
                return;

            var msg = new MsgConCmd();
            msg.Text = command;
            NetManager.ClientSendMessage(msg);
        }

        /// <inheritdoc />
        public override void WriteLine(ICommonSession? session, string text)
        {
            var msg = new FormattedMessage();
            msg.AddText(text);
            OutputText(msg, true, false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // We don't have anything to dispose.
        }

        private void OutputText(FormattedMessage text, bool local, bool error)
        {
            AddString?.Invoke(this, new AddStringArgs(text, local, error));

            var level = error ? LogLevel.Warning : LogLevel.Info;
            _conLogger.Log(level, text.ToString());
        }

        private void OnNetworkConnected(object? sender, NetChannelArgs netChannelArgs)
        {
            // Once connected, ask the server which commands it offers.
            SendServerCommandRequest();
        }

        private void HandleConCmdAck(MsgConCmdAck msg)
        {
            // Text produced by the server is shown as non-local console output.
            OutputText(msg.Text, local: false, error: msg.Error);
        }

        private void HandleConCmdReg(MsgConCmdReg msg)
        {
            foreach (var entry in msg.Commands)
            {
                var name = entry.Name;

                if (_availableServerCommands.ContainsKey(name))
                {
                    // Keep the dummy we already have.
                    _logger.Error("Server sent duplicate console command " + name);
                    continue;
                }

                _availableServerCommands[name] = new ServerDummyCommand(name, entry.Help, entry.Description);
            }

            // Always refresh the merged command table, even if nothing was added.
            UpdateAvailableCommands();
        }

        /// <summary>
        /// Asks the server for the list of commands it provides.
        /// </summary>
        private void SendServerCommandRequest()
        {
            if (_requestedCommands)
                return;

            // Leave the flag unset so a later connection can retry.
            if (!NetManager.IsConnected)
                return;

            // An empty registration message acts as the request.
            var request = new MsgConCmdReg();
            NetManager.ClientSendMessage(request);

            _requestedCommands = true;
        }

        void IPostInjectInit.PostInject()
        {
            _logger = _logMan.GetSawmill("console");
            _conLogger = _logMan.GetSawmill("CON");
        }

        /// <summary>
        /// Stand-in for a command that only exists on the server; having it registered lets the list and help commands show it.
        /// </summary>
        [Reflect(false)]
        private sealed class ServerDummyCommand : IConsoleCommand
        {
            /// <summary>
            ///     Creates a placeholder for a command that only exists on the server.
            /// </summary>
            /// <param name="command">Name of the command.</param>
            /// <param name="help">Usage text.</param>
            /// <param name="description">Short description.</param>
            internal ServerDummyCommand(string command, string help, string description)
            {
                Command = command;
                Help = help;
                Description = description;
            }

            public string Command { get; }

            public string Description { get; }

            public string Help { get; }

            // Always forward to server.
            public void Execute(IConsoleShell shell, string argStr, string[] args)
            {
                shell.RemoteExecuteCommand(argStr);
            }

            public async ValueTask<CompletionResult> GetCompletionAsync(
                IConsoleShell shell,
                string[] args,
                string argStr,
                CancellationToken cancel)
            {
                var host = (ClientConsoleHost)shell.ConsoleHost;
                var argsList = args.ToList();
                argsList.Insert(0, Command);

                return await host.DoServerCompletions(argsList, argStr, cancel);
            }
        }

        private sealed class RemoteExecCommand : LocalizedCommands
        {
            public override string Command => ">";
            public override string Description => LocalizationManager.GetString("cmd-remoteexec-desc");
            public override string Help => LocalizationManager.GetString("cmd-remoteexec-help");

            public override void Execute(IConsoleShell shell, string argStr, string[] args)
            {
                shell.RemoteExecuteCommand(argStr[">".Length..]);
            }

            public override async ValueTask<CompletionResult> GetCompletionAsync(
                IConsoleShell shell,
                string[] args,
                string argStr,
                CancellationToken cancel)
            {
                var host = (ClientConsoleHost)shell.ConsoleHost;
                return await host.DoServerCompletions(args.ToList(), argStr[">".Length..], cancel);
            }
        }
    }
}
