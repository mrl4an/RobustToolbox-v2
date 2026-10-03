using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Reflection;
using Robust.Shared.Utility;

namespace Robust.Shared.Console;

/// <summary>
/// Manages registration for "entity" console commands.
/// </summary>
/// <remarks>
/// See <see cref="LocalizedEntityCommands"/> for details on what "entity" console commands are.
/// </remarks>
internal sealed class EntityConsoleHost
{
    [Dependency] private readonly IConsoleHost _consoleHost = default!;
    [Dependency] private readonly IReflectionManager _reflectionManager = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;

    private readonly HashSet<string> _entityCommands = [];

    /// <summary>
    /// Whether <see cref="Startup"/> scans the loaded assemblies for entity commands.
    /// Defaults to true; some unit tests turn it off to skip reflection-based discovery.
    /// </summary>
    public bool DiscoverCommands { get; set; } = true;

    /// <summary>
    /// Finds every entity console command, gives each one its dependencies and registers it with the console host.
    /// Does nothing when <see cref="DiscoverCommands"/> is false.
    /// </summary>
    /// <remarks>
    /// An exception thrown while registering (for example a duplicate command name) leaves the registration
    /// region open.
    /// </remarks>
    public void Startup()
    {
        DebugTools.Assert(_entityCommands.Count == 0);

        if (!DiscoverCommands)
            return;

        var deps = ((EntitySystemManager) _entitySystemManager).SystemDependencyCollection;

        _consoleHost.BeginRegistrationRegion();

        foreach (var type in _reflectionManager.GetAllChildren<IEntityConsoleCommand>())
        {
            var command = (IConsoleCommand) Activator.CreateInstance(type)!;
            deps.InjectDependencies(command, oneOff: true);

            _entityCommands.Add(command.Command);
            _consoleHost.RegisterCommand(command);
        }

        _consoleHost.EndRegistrationRegion();
    }

    public void Shutdown()
    {
        foreach (var command in _entityCommands)
        {
            _consoleHost.UnregisterCommand(command);
        }

        _entityCommands.Clear();
    }
}
