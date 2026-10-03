using System.Text;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Robust.Server.Console.Commands
{
    /*
    // Disabled for now since it doesn't actually work.
    sealed class RestartCommand : LocalizedCommands
    {
        [Dependency] private readonly IBaseServer _server = default!;

        public override string Command => "restart";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            _server.Restart();
        }
    }
    */

    sealed class ShutdownCommand : LocalizedCommands
    {
        [Dependency] private readonly IBaseServer _server = default!;

        public override string Command => "shutdown";

        /// <summary>
        ///     Asks the server to stop. No reason text is supplied and any arguments are ignored.
        /// </summary>
        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            // Request shutdown without a reason; the server main loop handles the actual termination.
            _server.Shutdown(null);
        }
    }

    sealed class NetworkAuditCommand : LocalizedCommands
    {
        [Dependency] private readonly INetManager _netManager = default!;

        public override string Command => "netaudit";
        /// <summary>
        ///     Prints one line per registered net message type, showing the type name and the
        ///     target object of its receive callback. Arguments are ignored.
        /// </summary>
        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            // Unchecked cast on purpose: only the concrete NetManager exposes the audit data.
            var audit = ((NetManager) _netManager).CallbackAudit;

            var builder = new StringBuilder();
            foreach (var (messageType, callback) in audit)
            {
                // Name is padded to at least 16 chars, then one space, then "Call:" with no space after it.
                builder.AppendLine($"Type: {messageType.Name,-16} Call:{callback.Target}");
            }

            // Every entry already ends with a newline, so the output finishes with a blank line.
            shell.WriteLine(builder.ToString());
        }
    }

    sealed class ShowTimeCommand : LocalizedCommands
    {
        [Dependency] private readonly IGameTiming _timing = default!;

        public override string Command => "showtime";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            shell.WriteLine($"Paused: {_timing.Paused}, CurTick: {_timing.CurTick}, CurTime: {_timing.CurTime}, RealTime: {_timing.RealTime}");
        }
    }
}
