using System.Linq;
using System.Text;
using Robust.Shared.Localization;

namespace Robust.Shared.Console.Commands;

internal sealed class ListCommands : LocalizedCommands
{
    public override string Command => "list";

    /// <summary>
    ///     Writes a table of all known console commands to the shell. A single argument acts as a case-sensitive
    ///     substring filter on command names; any other argument count lists everything.
    /// </summary>
    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var filter = args.Length == 1 ? args[0] : string.Empty;

        var host = (IConsoleHostInternal) shell.ConsoleHost;
        var builder = new StringBuilder(Loc.GetString("cmd-list-heading"));

        // TODO: permission checks are not applied here yet.
        var commands = host.AvailableCommands.Values
            .Where(cmd => cmd.Command.Contains(filter))
            .OrderBy(cmd => cmd.Command);

        foreach (var cmd in commands)
        {
            // 'S' marks server-side commands, 'C' client-side ones.
            var side = host.IsCmdServer(cmd) ? 'S' : 'C';
            builder.AppendLine($"{side} {cmd.Command,-32}{cmd.Description}");
        }

        shell.WriteLine(builder.ToString().Trim(' ', '\n'));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHint(Loc.GetString("cmd-list-arg-filter"));

        return CompletionResult.Empty;
    }
}
