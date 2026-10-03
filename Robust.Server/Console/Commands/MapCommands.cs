using System.Linq;
using System.Numerics;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.GameSaves;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Robust.Server.Console.Commands
{
    public sealed class SaveGridCommand : LocalizedCommands
    {
        [Dependency] private readonly IEntityManager _ent = default!;
        [Dependency] private readonly IResourceManager _resource = default!;

        public override string Command => "savegrid";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            // Needs an entity id and a destination path; anything extra is ignored.
            if (args.Length < 2)
            {
                shell.WriteError("Not enough arguments.");
                return;
            }

            if (!NetEntity.TryParse(args[0], out var netEntity))
            {
                shell.WriteError("Not a valid entity ID.");
                return;
            }

            var entity = _ent.GetEntity(netEntity);

            // Only existence is checked here; the saver itself deals with non-grid entities.
            if (!_ent.EntityExists(entity))
            {
                shell.WriteError("That grid does not exist.");
                return;
            }

            var saved = _ent.System<MapLoaderSystem>().TrySaveGrid(entity, new ResPath(args[1]));

            if (saved)
                shell.WriteLine("Save successful. Look in the user data directory.");
            else
                shell.WriteError("Save unsuccessful!");
        }

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            switch (args.Length)
            {
                case 1:
                    return CompletionResult.FromHintOptions(CompletionHelper.Components<MapGridComponent>(args[0], _ent), Loc.GetString("cmd-hint-savebp-id"));
                case 2:
                    var opts = CompletionHelper.UserFilePath(args[1], _resource.UserData);
                    return CompletionResult.FromHintOptions(opts, Loc.GetString("cmd-hint-savemap-path"));
            }
            return CompletionResult.Empty;
        }
    }

    public sealed class LoadGridCommand : LocalizedCommands
    {
        [Dependency] private readonly IEntitySystemManager _system = default!;
        [Dependency] private readonly IResourceManager _resource = default!;

        public override string Command => "loadgrid";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            // Accepted argument counts: 2, 4, 5 or 6 (a lone x without y is not allowed).
            if (args.Length < 2 || args.Length == 3 || args.Length > 6)
            {
                shell.WriteError("Must have either 2, 4, 5, or 6 arguments.");
                return;
            }

            if (!int.TryParse(args[0], out var intMapId))
            {
                shell.WriteError($"{args[0]} is not a valid integer.");
                return;
            }

            var mapId = new MapId(intMapId);

            if (mapId == MapId.Nullspace)
            {
                shell.WriteError("Cannot load into nullspace.");
                return;
            }

            var mapSystem = _system.GetEntitySystem<SharedMapSystem>();

            // Unlike loadmap, a missing map is created on demand (without map init).
            // This is reported through the error channel even though execution continues, and it
            // happens before the remaining arguments are validated.
            if (!mapSystem.MapExists(mapId))
            {
                shell.WriteError($"MapID {intMapId} did not exist, creating without map init");
                mapSystem.CreateMap(mapId, false);
            }

            var offset = Vector2.Zero;
            if (args.Length >= 4)
            {
                if (!float.TryParse(args[2], out var x))
                {
                    shell.WriteError($"{args[2]} is not a valid float.");
                    return;
                }

                if (!float.TryParse(args[3], out var y))
                {
                    shell.WriteError($"{args[3]} is not a valid float.");
                    return;
                }

                offset = new Vector2(x, y);
            }

            // Rotation is given in degrees here.
            var rotation = Angle.Zero;
            if (args.Length >= 5)
            {
                if (!float.TryParse(args[4], out var degrees))
                {
                    shell.WriteError($"{args[4]} is not a valid float.");
                    return;
                }

                rotation = Angle.FromDegrees(degrees);
            }

            var options = DeserializationOptions.Default;
            if (args.Length >= 6)
            {
                if (!bool.TryParse(args[5], out var storeUids))
                {
                    shell.WriteError($"{args[5]} is not a valid boolean.");
                    return;
                }

                options.StoreYamlUids = storeUids;
            }

            // The outcome is intentionally not reported.
            _system.GetEntitySystem<MapLoaderSystem>()
                .TryLoadGrid(mapId, new ResPath(args[1]), out _, options, offset, rotation);
        }

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            return LoadMap.GetCompletionResult(shell, args, _resource, Loc);
        }
    }

    public sealed class SaveMap : LocalizedCommands
    {
        [Dependency] private readonly IEntityManager _entManager = default!;
        [Dependency] private readonly IEntitySystemManager _system = default!;
        [Dependency] private readonly IResourceManager _resource = default!;

        public override string Command => "savemap";

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            switch (args.Length)
            {
                case 1:
                    return CompletionResult.FromHintOptions(CompletionHelper.MapIds(_entManager), Loc.GetString("cmd-hint-savemap-id"));
                case 2:
                    var opts = CompletionHelper.UserFilePath(args[1], _resource.UserData);
                    return CompletionResult.FromHintOptions(opts, Loc.GetString("cmd-hint-savemap-path"));
                case 3:
                    return CompletionResult.FromHint(Loc.GetString("cmd-hint-savemap-force"));
            }
            return CompletionResult.Empty;
        }

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length < 2)
            {
                shell.WriteLine(Help);
                return;
            }

            if (!int.TryParse(args[0], out var intMapId))
            {
                shell.WriteLine(Help);
                return;
            }

            var mapId = new MapId(intMapId);

            // Nullspace cannot be saved; quietly do nothing.
            if (mapId == MapId.Nullspace)
                return;

            var mapSystem = _system.GetEntitySystem<SharedMapSystem>();

            if (!mapSystem.MapExists(mapId))
            {
                shell.WriteError(Loc.GetString("cmd-savemap-not-exist"));
                return;
            }

            // An already initialised map is only saved when the optional third argument parses to true.
            // A missing or unparsable value counts as "not forced".
            if (mapSystem.IsInitialized(mapId))
            {
                var forced = args.Length >= 3 && bool.TryParse(args[2], out var parsed) && parsed;
                if (!forced)
                {
                    shell.WriteError(Loc.GetString("cmd-savemap-init-warning"));
                    return;
                }
            }

            shell.WriteLine(Loc.GetString("cmd-savemap-attempt", ("mapId", mapId), ("path", args[1])));

            var saved = _system.GetEntitySystem<MapLoaderSystem>().TrySaveMap(mapId, new ResPath(args[1]));

            if (saved)
                shell.WriteLine(Loc.GetString("cmd-savemap-success"));
            else
                shell.WriteError(Loc.GetString("cmd-savemap-error"));
        }
    }

    public sealed class LoadMap : LocalizedCommands
    {
        [Dependency] private readonly IEntitySystemManager _system = default!;
        [Dependency] private readonly IResourceManager _resource = default!;

        public override string Command => "loadmap";

        public static CompletionResult GetCompletionResult(IConsoleShell shell, string[] args, IResourceManager resource, ILocalizationManager loc)
        {
            switch (args.Length)
            {
                case 1:
                    return CompletionResult.FromHint(loc.GetString("cmd-hint-savemap-id"));
                case 2:
                    var opts = CompletionHelper.UserFilePath(args[1], resource.UserData)
                        .Concat(CompletionHelper.ContentFilePath(args[1], resource));
                    return CompletionResult.FromHintOptions(opts, loc.GetString("cmd-hint-savemap-path"));
                case 3:
                    return CompletionResult.FromHint(loc.GetString("cmd-hint-loadmap-x-position"));
                case 4:
                    return CompletionResult.FromHint(loc.GetString("cmd-hint-loadmap-y-position"));
                case 5:
                    return CompletionResult.FromHint(loc.GetString("cmd-hint-loadmap-rotation"));
                case 6:
                    return CompletionResult.FromHint(loc.GetString("cmd-hint-loadmap-uids"));
            }

            return CompletionResult.Empty;
        }

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            return GetCompletionResult(shell, args, _resource, Loc);
        }

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length < 2 || args.Length > 6)
            {
                shell.WriteLine(Help);
                return;
            }

            if (!int.TryParse(args[0], out var intMapId))
            {
                shell.WriteError(Loc.GetString("cmd-parse-failure-integer", ("arg", args[0])));
                return;
            }

            var mapId = new MapId(intMapId);

            if (mapId == MapId.Nullspace)
            {
                shell.WriteError(Loc.GetString("cmd-loadmap-nullspace"));
                return;
            }

            var mapSystem = _system.GetEntitySystem<SharedMapSystem>();

            // This command needs a fresh map id: refuse if the map is already there.
            if (mapSystem.MapExists(mapId))
            {
                shell.WriteError(Loc.GetString("cmd-loadmap-exists", ("mapId", mapId)));
                return;
            }

            float x = 0;
            if (args.Length >= 3 && !float.TryParse(args[2], out x))
            {
                shell.WriteError(Loc.GetString("cmd-parse-failure-float", ("arg", args[2])));
                return;
            }

            float y = 0;
            if (args.Length >= 4 && !float.TryParse(args[3], out y))
            {
                shell.WriteError(Loc.GetString("cmd-parse-failure-float", ("arg", args[3])));
                return;
            }

            var offset = new Vector2(x, y);

            // Rotation is given in radians here (unlike loadgrid, which uses degrees).
            float rotationRadians = 0;
            if (args.Length >= 5 && !float.TryParse(args[4], out rotationRadians))
            {
                shell.WriteError(Loc.GetString("cmd-parse-failure-float", ("arg", args[4])));
                return;
            }

            var rotation = new Angle(rotationRadians);

            var storeUids = false;
            if (args.Length >= 6 && !bool.TryParse(args[5], out storeUids))
            {
                shell.WriteError(Loc.GetString("cmd-parse-failure-bool", ("arg", args[5])));
                return;
            }

            // Note: built from a fresh options value, not from DeserializationOptions.Default.
            var options = new DeserializationOptions { StoreYamlUids = storeUids };

            // The return value is ignored; success is judged by whether the map now exists.
            _system.GetEntitySystem<MapLoaderSystem>()
                .TryLoadMapWithId(mapId, new ResPath(args[1]), out _, out _, options, offset, rotation);

            if (mapSystem.MapExists(mapId))
                shell.WriteLine(Loc.GetString("cmd-loadmap-success", ("mapId", mapId), ("path", args[1])));
            else
                shell.WriteLine(Loc.GetString("cmd-loadmap-error", ("path", args[1])));
        }
    }

    public sealed class SaveGame : LocalizedCommands
    {
        [Dependency] private readonly IEntitySystemManager _system = default!;
        [Dependency] private readonly IResourceManager _resource = default!;
        [Dependency] private readonly IConfigurationManager _config = default!;

        public override string Command => "savegame";

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            if (args.Length == 1)
            {
                var opts = CompletionHelper.UserFilePath(args[0], _resource.UserData);
                return CompletionResult.FromHintOptions(opts, Loc.GetString("cmd-hint-savemap-path"));
            }
            return CompletionResult.Empty;
        }

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (!_config.GetCVar(CVars.GameSavesEnabled))
            {
                shell.WriteLine(Loc.GetString("cmd-savegame-disabled"));
                return;
            }
            if (args.Length < 1)
            {
                shell.WriteLine(Help);
                return;
            }
            shell.WriteLine(Loc.GetString("cmd-savegame-attempt", ("path", args[0])));
            var success = _system.GetEntitySystem<GameSavesSystem>().TrySaveGame(new ResPath(args[0]));
            if (success)
                shell.WriteLine(Loc.GetString("cmd-savegame-success"));
            else
                shell.WriteError(Loc.GetString("cmd-savegame-error"));
        }
    }

    public sealed class LoadGame : LocalizedCommands
    {
        [Dependency] private readonly IEntityManager _entMan = default!;
        [Dependency] private readonly IEntitySystemManager _system = default!;
        [Dependency] private readonly IResourceManager _resource = default!;
        [Dependency] private readonly IConfigurationManager _config = default!;

        public override string Command => "loadgame";

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            switch (args.Length)
            {
                case 1:
                    var opts = CompletionHelper.UserFilePath(args[0], _resource.UserData);
                    return CompletionResult.FromHintOptions(opts, Loc.GetString("cmd-hint-savemap-path"));
                case 2:
                    return CompletionResult.FromHint(Loc.GetString("cmd-hint-loadgame-flush"));
            }
            return CompletionResult.Empty;
        }

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (!_config.GetCVar(CVars.GameSavesEnabled))
            {
                shell.WriteLine(Loc.GetString("cmd-loadgame-disabled"));
                return;
            }
            if (args.Length < 1)
            {
                shell.WriteLine(Help);
                return;
            }
            var flush = false;
            if (args.Length == 2 && !bool.TryParse(args[1], out flush))
            {
                shell.WriteError(Loc.GetString("cmd-parse-failure-bool", ("arg", args[1])));
                return;
            }
            shell.WriteLine(Loc.GetString("cmd-loadgame-attempt", ("path", args[0])));
            if (flush)
                _entMan.FlushEntities();
            var success = _system.GetEntitySystem<GameSavesSystem>().TryLoadGame(new ResPath(args[0]));
            if (success)
                shell.WriteLine(Loc.GetString("cmd-loadgame-success"));
            else
                shell.WriteError(Loc.GetString("cmd-loadgame-error"));
        }
    }
}
