using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;

namespace Robust.Shared.Console.Commands;

[UsedImplicitly]
public sealed class SaveConfig : LocalizedCommands
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override string Command => "saveconfig";

    /// <summary>
    ///     Writes the current persistent configuration (CVars) to the config file.
    ///     All arguments are ignored and nothing is printed.
    /// </summary>
    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _cfg.SaveToFile();
    }
}
