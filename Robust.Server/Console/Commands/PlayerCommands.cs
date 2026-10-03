using System;
using System.Linq;
using System.Text;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Robust.Server.Console.Commands
{
    public sealed class ListPlayers : LocalizedCommands
    {
        [Dependency] private readonly IPlayerManager _players = default!;

        public override string Command => "listplayers";
        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            // Build the whole table first and send it as a single console write.
            var table = new StringBuilder();

            // Column titles, right-aligned to the same widths as the data rows below.
            table.AppendLine($"{"Player Name",20} {"Status",12} {"Playing Time",14} {"Ping",9} {"IP EndPoint",20}");
            table.AppendLine(new string('-', 79));

            foreach (var session in _players.Sessions)
            {
                // Time since the session connected, shown as hours:minutes:seconds only.
                var playTime = (DateTime.UtcNow - session.ConnectedTime).ToString(@"hh\:mm\:ss");
                var ping = session.Channel.Ping + "ms";

                table.AppendLine(
                    $"{session.Name,20} {session.Status.ToString(),12} {playTime,14} {ping,9} {session.Channel.RemoteEndPoint,20}");
            }

            shell.WriteLine(table.ToString());
        }
    }

    internal sealed class KickCommand : LocalizedCommands
    {
        [Dependency] private readonly IPlayerManager _players = default!;
        [Dependency] private readonly IServerNetManager _netManager = default!;

        public override string Command => "kick";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length < 1)
            {
                // No target given: explain usage, suggesting a real player name when one is available.
                var example = shell.Player ?? _players.Sessions.FirstOrDefault();

                if (example == null)
                {
                    shell.WriteLine("You need to provide a player to kick.");
                }
                else
                {
                    shell.WriteLine("You need to provide a player to kick. Try running 'kick " + example.Name + "' as an example.");
                }

                return;
            }

            // An unknown user name is silently ignored.
            if (!_players.TryGetSessionByUsername(args[0], out var target))
                return;

            // Any words after the user name become the custom part of the reason.
            var reason = args.Length == 1
                ? "Kicked by console"
                : "Kicked by console: " + string.Join(' ', args.Skip(1));

            _netManager.DisconnectChannel(target.Channel, reason);
        }

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            if (args.Length == 1)
            {
                var options = _players.Sessions.OrderBy(c => c.Name).Select(c => c.Name).ToArray();

                return CompletionResult.FromHintOptions(options, "<PlayerIndex>");
            }

            if (args.Length > 1)
            {
                return CompletionResult.FromHint("[<Reason>]");
            }

            return CompletionResult.Empty;
        }
    }
}
