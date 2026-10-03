using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Prometheus;
using Robust.Server.Console;
using Robust.Server.DataMetrics;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Server.Log;
using Robust.Server.Network.Transfer;
using Robust.Server.Placement;
using Robust.Server.Player;
using Robust.Server.Scripting;
using Robust.Server.ServerHub;
using Robust.Server.ServerStatus;
using Robust.Server.Upload;
using Robust.Server.Utility;
using Robust.Server.ViewVariables;
using Robust.Shared;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Exceptions;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Player;
using Robust.Shared.Profiling;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Replays;
using Robust.Shared.Toolshed;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Threading;
using Robust.Shared.Timing;
using Robust.Shared.Upload;
using Robust.Shared.Utility;
using Serilog.Debugging;
using Serilog.Sinks.Loki;

namespace Robust.Server
{
    /// <summary>
    /// The top-level server class: it starts the engine, runs the main loop and shuts everything down.
    /// </summary>
    internal sealed class BaseServer : IBaseServerInternal, IPostInjectInit
    {
        private static readonly Gauge ServerUpTime = Metrics.CreateGauge(
            "robust_server_uptime",
            "The real time the server main loop has been running.");

        private static readonly Gauge ServerCurTime = Metrics.CreateGauge(
            "robust_server_curtime",
            "The IGameTiming.CurTime of the server.");

        private static readonly Gauge ServerCurTick = Metrics.CreateGauge(
            "robust_server_curtick",
            "The IGameTiming.CurTick of the server.");

        private static readonly Histogram TickUsage = Metrics.CreateHistogram(
            "robust_server_update_usage",
            "Time usage of the main loop Update()s",
            new HistogramConfiguration
            {
                LabelNames = new[] {"area"},
                Buckets = Histogram.ExponentialBuckets(0.000_01, 2, 13)
            });

        [Dependency] private readonly INetConfigurationManagerInternal _config = default!;
        [Dependency] private readonly IServerEntityManager _entityManager = default!;
        [Dependency] private readonly ILogManager _log = default!;
        [Dependency] private readonly IRobustSerializer _serializer = default!;
        [Dependency] private readonly IGameTiming _time = default!;
        [Dependency] private readonly IResourceManagerInternal _resources = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly ITimerManager _timerManager = default!;
        [Dependency] private readonly IServerGameStateManager _stateManager = default!;
        [Dependency] private readonly IServerNetManager _network = default!;
        [Dependency] private readonly ISystemConsoleManager _systemConsole = default!;
        [Dependency] private readonly ITaskManager _taskManager = default!;
        [Dependency] private readonly IRuntimeLog _runtimeLog = default!;
        [Dependency] private readonly IModLoaderInternal _modLoader = default!;
        [Dependency] private readonly IWatchdogApiInternal _watchdogApi = default!;
        [Dependency] private readonly HubManager _hubManager = default!;
        [Dependency] private readonly IScriptHost _scriptHost = default!;
        [Dependency] private readonly IMetricsManagerInternal _metricsManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IRobustMappedStringSerializer _stringSerializer = default!;
        [Dependency] private readonly ILocalizationManagerInternal _loc = default!;
        [Dependency] private readonly IServerConsoleHost _consoleHost = default!;
        [Dependency] private readonly IParallelManagerInternal _parallelMgr = default!;
        [Dependency] private readonly ProfManager _prof = default!;
        [Dependency] private readonly IPrototypeManagerInternal _prototype = default!;
        [Dependency] private readonly IPlacementManager _placement = default!;
        [Dependency] private readonly IServerViewVariablesInternal _viewVariables = default!;
        [Dependency] private readonly ISerializationManager _serialization = default!;
        [Dependency] private readonly IStatusHost _statusHost = default!;
        [Dependency] private readonly IComponentFactory _componentFactory = default!;
        [Dependency] private readonly IReplayRecordingManagerInternal _replay = default!;
        [Dependency] private readonly IGamePrototypeLoadManager _protoLoadMan = default!;
        [Dependency] private readonly UploadedContentManager _uploadedContMan = default!;
        [Dependency] private readonly NetworkResourceManager _netResMan = default!;
        [Dependency] private readonly IReflectionManager _refMan = default!;
        [Dependency] private readonly ITransferManager _transfer = default!;
        [Dependency] private readonly ServerTransferTestManager _transferTest = default!;

        private readonly Stopwatch _uptimeStopwatch = new();

        private CommandLineArgs? _commandLineArgs;
        private Func<ILogHandler>? _logHandlerFactory;
        private ILogHandler? _logHandler;
        private IGameLoop _mainLoop = default!;
        private ISawmill _logger = default!;
        private bool _autoPause;

        private string? _shutdownReason;

        private readonly ManualResetEventSlim _shutdownEvent = new(false);

        public ServerOptions Options { get; private set; } = new();

        /// <inheritdoc />
        public int MaxPlayers => _config.GetEffectiveMaxConnections();

        /// <inheritdoc />
        public string ServerName => _config.GetCVar(CVars.GameHostName);

        public bool ContentStart { get; set; }

        /// <inheritdoc />
        public void Restart()
        {
            // Tear everything down, then boot again with the options and log factory used the first time.
            // Known to be fragile: not every service survives a second initialization.
            Cleanup();

            // The success/failure result is deliberately not inspected here.
            Start(Options, _logHandlerFactory);
        }

        /// <inheritdoc />
        public void Shutdown(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                _logger.Info("Shutting down...");
            else
                _logger.Info($"{reason}, shutting down...");

            // Remember why we stopped; Cleanup() forwards this text to disconnecting clients.
            _shutdownReason = reason ?? "Shutting down";

            // Ask the loop to exit. It may not exist yet if shutdown is requested very early.
            if (_mainLoop != null)
                _mainLoop.Running = false;

            // Detach and release the file/remote log handler so it is flushed. The field keeps its value.
            if (_logHandler != null)
            {
                _log.RootSawmill.RemoveHandler(_logHandler);
                (_logHandler as IDisposable)?.Dispose();
            }
        }

        public void SetCommandLineArgs(CommandLineArgs args)
        {
            _commandLineArgs = args;
        }

        /// <inheritdoc />
        public bool Start(ServerOptions options, Func<ILogHandler>? logHandlerFactory = null)
        {
            // ---- Configuration ----
            Options = options;

            _config.Initialize(true);

            _config.LoadCVarsFromAssembly(typeof(BaseServer).Assembly);
            _config.LoadCVarsFromAssembly(typeof(IConfigurationManager).Assembly);

            if (Options.LoadConfigAndUserData)
            {
                // The command line path is used as given, so it is relative to the working directory.
                var configFile = _commandLineArgs?.ConfigFile;
                if (string.IsNullOrEmpty(configFile))
                    configFile = PathHelpers.ExecutableRelativeFile("server_config.toml");

                if (File.Exists(configFile))
                    _config.LoadFromFile(configFile);
                else
                    _config.SetSaveFile(configFile);
            }

            CVarDefaultOverrides.OverrideServer(_config);

            // Precedence, lowest to highest: file, environment, command line.
            _config.OverrideConVars(EnvironmentVariables.GetEnvironmentCVars());

            if (_commandLineArgs != null)
                _config.OverrideConVars(_commandLineArgs.CVars);

            ProfileOptSetup.Setup(_config);

            // ---- Logging ----
            _logHandlerFactory = logHandlerFactory;
            var logHandler = logHandlerFactory?.Invoke();

            var logEnabled = _config.GetCVar(CVars.LogEnabled);
            if (logEnabled && logHandler == null)
            {
                var logPath = _config.GetCVar(CVars.LogPath);
                var logFormat = _config.GetCVar(CVars.LogFormat);
                var now = DateTime.Now;
                var logFilename = logFormat
                    .Replace("%(date)s", now.ToString("yyyy-MM-dd"))
                    .Replace("%(time)s", now.ToString("hh-mm-ss"));

                var fullPath = Path.Combine(logPath, logFilename);
                if (!Path.IsPathRooted(fullPath))
                    fullPath = PathHelpers.ExecutableRelativeFile(fullPath);

                logHandler = new FileLogHandler(fullPath);
            }

            _log.RootSawmill.Level = _config.GetCVar(CVars.LogLevel);

            if (logEnabled && logHandler != null)
            {
                _logHandler = logHandler;
                _log.RootSawmill.AddHandler(_logHandler);
            }

            if (_commandLineArgs != null)
            {
                foreach (var (sawmillName, levelText) in _commandLineArgs.LogLevels)
                {
                    LogLevel? level;
                    if (levelText == "null")
                    {
                        // "null" resets the sawmill to inherit its parent's level.
                        level = null;
                    }
                    else if (Enum.TryParse<LogLevel>(levelText, out var parsed))
                    {
                        level = parsed;
                    }
                    else
                    {
                        System.Console.WriteLine($"LogLevel {levelText} does not exist!");
                        continue;
                    }

                    _log.GetSawmill(sawmillName).Level = level;
                }
            }

            ProgramShared.PrintRuntimeInfo(_log.RootSawmill);

            SelfLog.Enable(message => System.Console.WriteLine("SERILOG ERROR: {0}", message));

            // SetupLoki returns false on misconfiguration; Start reports failure with true.
            if (!SetupLoki())
                return true;

            // ---- Core services (order matters) ----
            // The task manager must come first: it installs the main-thread synchronization context.
            _taskManager.Initialize();
            _parallelMgr.Initialize();

            LoadSettings();

            _metricsManager.Initialize();
            _prof.Initialize();

            try
            {
                _network.Initialize(true);
                _network.StartServer();
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                var port = _network.Port;
                _logger.Fatal(
                    "Unable to setup networking manager. Make sure that you aren't running the server twice and that port {0} is not in use by another application.\n{1}",
                    port, e);
                return true;
            }
            catch (Exception e)
            {
                _logger.Fatal("Unable to setup networking manager!\n{0}", e);
                return true;
            }

            _transfer.Initialize();
            _transferTest.Initialize();

            var dataDir = Options.LoadConfigAndUserData
                ? _commandLineArgs?.DataDir ?? PathHelpers.ExecutableRelativeFile("data")
                : null;
            _resources.Initialize(dataDir, hideUserDataDir: false);

            var mountOptions = _commandLineArgs != null
                ? MountOptions.Merge(_commandLineArgs.MountOptions, Options.MountOptions)
                : Options.MountOptions;

            var startType = ContentStart ? StartType.Content : StartType.Engine;
#if FULL_RELEASE
            if (Options.ResourceMountDisabled)
                startType = StartType.Loader;
#endif

            ProgramShared.DoMounts(
                _resources,
                mountOptions,
                Options.ContentBuildDirectory,
                Options.AssemblyDirectory,
                Options.LoadContentResources,
                startType);

            // When content is the entry executable, a separate load context would load assemblies twice.
            _modLoader.SetUseLoadContext(!ContentStart);
            _modLoader.SetEnableSandboxing(Options.Sandboxing);

            var manifest = ResourceManifestData.LoadResourceManifest(_resources);
            var modulePrefix = manifest.AssemblyPrefix ?? Options.ContentModulePrefix;
            if (!_modLoader.TryLoadModulesFrom(Options.AssemblyDirectory, modulePrefix))
            {
                _logger.Fatal("Errors while loading content assemblies.");
                return true;
            }

            foreach (var loadedModule in _modLoader.LoadedModules)
            {
                _config.LoadCVarsFromAssembly(loadedModule);
            }

            _modLoader.BroadcastRunLevel(ModRunLevel.PreInit);

            // Must come after content assemblies are loaded so content types get serializer ids.
            _serializer.Initialize();

            _loc.Initialize();
            _loc.AddLoadedToStringSerializer(_stringSerializer);

            _prototype.LoadedData += data =>
            {
                if (!_stringSerializer.Locked)
                    _stringSerializer.AddStrings(data.Root);
            };

            // ---- Tier 2 services ----
            _time.InSimulation = true;

            _config.SetupNetworking();

            _playerManager.Initialize(MaxPlayers);
            _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;

            _placement.Initialize();
            _viewVariables.Initialize();

            _modLoader.BroadcastRunLevel(ModRunLevel.Init);

            var badFileCheck = ProgramShared.CheckBadFileExtensions(_resources, _config, _log.GetSawmill("res"));

            _entityManager.Initialize();
            _mapManager.Initialize();
            _serialization.Initialize();

            // Must finish before prototypes are loaded.
            ProgramShared.FinishCheckBadFileExtensions(badFileCheck);

            // Prototype initialization has to follow the last assembly load, or loaded prototypes get cleared.
            _prototype.Initialize();
            _prototype.LoadDefaultPrototypes();
            _refMan.Initialize();

            IoCManager.Resolve<ToolshedManager>().Initialize();
            _consoleHost.Initialize();
            _entityManager.Startup();
            _mapManager.Startup();
            _stateManager.Initialize();
            _replay.Initialize();

            var xformReg = _entityManager.ComponentFactory.GetRegistration<TransformComponent>();
            if (!xformReg.NetID.HasValue)
                throw new InvalidOperationException("TransformComponent does not have a NetId.");

            _stateManager.TransformNetId = xformReg.NetID.Value;

            _scriptHost.Initialize();
            _protoLoadMan.Initialize();
            _netResMan.Initialize();
            _uploadedContMan.Initialize();

            // Must run before PostInit: content (e.g. automatic replay recording) may rely on locked strings.
            AddFinalStringsToSerializer();
            _stringSerializer.LockStrings();

            _modLoader.BroadcastRunLevel(ModRunLevel.PostInit);

            _statusHost.Start();
            _hubManager.Start();

            AppDomain.CurrentDomain.ProcessExit += ProcessExiting;

            _watchdogApi.Initialize();

            if (OperatingSystem.IsWindows())
            {
                var tickPeriod = _config.GetCVar(CVars.SysWinTickPeriod);
                if (tickPeriod >= 0)
                    WindowsTickPeriod.TimeBeginPeriod((uint) tickPeriod);
            }

            _config.CheckUnusedCVars();

            if (_config.GetCVar(CVars.SysGCCollectStart))
                GC.Collect();

            ProgramShared.RunExecCommands(_consoleHost, _commandLineArgs?.ExecCommands);

            return false;
        }

        private void AddFinalStringsToSerializer()
        {
            foreach (var regType in _componentFactory.AllRegisteredTypes)
            {
                var reg = _componentFactory.GetRegistration(regType);
                _stringSerializer.AddString(reg.Name);
            }

            using var extraMappedStrings = typeof(BaseServer).Assembly
                .GetManifestResourceStream("Robust.Server.ExtraMappedSerializerStrings.txt");

            if (extraMappedStrings != null)
            {
                using var sr = new StreamReader(extraMappedStrings);
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    _stringSerializer.AddString(line);
                }
            }
        }

        private bool SetupLoki()
        {
            var enabled = _config.GetCVar(CVars.LokiEnabled);
            if (!enabled)
            {
                return true;
            }

            var logger = _log.GetSawmill("loki");
            var serverName = _config.GetCVar(CVars.LokiName);
            var address = _config.GetCVar(CVars.LokiAddress);
            var username = _config.GetCVar(CVars.LokiUsername);
            var password = _config.GetCVar(CVars.LokiPassword);

            if (string.IsNullOrWhiteSpace(serverName))
            {
                logger.Fatal("Misconfiguration: Server name is not specified/empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                logger.Fatal("Misconfiguration: Loki address is not specified/empty.");
                return false;
            }

            LokiSinkConfiguration cfg = new()
            {
                LokiUrl = address
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                if (string.IsNullOrWhiteSpace(password))
                {
                    logger.Fatal("Misconfiguration: Loki password is not specified/empty but username is.");
                    return false;
                }

                cfg.LokiUsername = username;
                cfg.LokiPassword = password;
            }

            cfg.LogLabelProvider = new LogLabelProvider(serverName);

            logger.Debug("Loki enabled for server {ServerName} loki address {LokiAddress}.", serverName,
                address);

            var handler = new LokiLogHandler(cfg);
            _log.RootSawmill.AddHandler(handler);
            return true;
        }

        private void ProcessExiting(object? sender, EventArgs e)
        {
            // If the main loop is not running the task will never get processed on the main thread
            if (!_mainLoop.Running)
            {
                return;
            }

            _taskManager.RunOnMainThread(() => Shutdown("ProcessExited"));
            // Give the server 10 seconds to shut down.
            // If it still hasn't managed to assume it's stuck or something.
            if (!_shutdownEvent.Wait(10_000))
            {
                System.Console.WriteLine("ProcessExited timeout (10s) has been passed; killing server.");
                // This kills the server right? Returning?
            }
        }

        internal void SetupMainLoop()
        {
            // Create the default loop unless one was injected through OverrideMainLoop.
            if (_mainLoop == null)
            {
                _mainLoop = new GameLoop(
                    _time,
                    _runtimeLog,
                    _prof,
                    _log.GetSawmill("eng"),
                    GameLoopOptions.FromCVars(_config))
                {
                    SleepMode = SleepMode.Delay,
                    DetectSoftLock = true,
                    EnableMetrics = true
                };
            }

            _uptimeStopwatch.Start();

            // Wire the loop up regardless of where it came from.
            _mainLoop.Input += (sender, args) => Input(args);
            _mainLoop.Tick += (sender, args) => Update(args);
            _mainLoop.Update += (sender, args) => FrameUpdate(args);

            _time.Paused = _autoPause;
        }

        internal void FinishMainLoop()
        {
            _time.InSimulation = true;
            Cleanup();

            _shutdownEvent.Set();
        }

        /// <inheritdoc />
        public void MainLoop()
        {
            SetupMainLoop();

            // If the server has been given a reason to shut down before the main loop has started,
            // Don't start the main loop. This only works if a reason is passed to Shutdown(...)
            if (_shutdownReason != null)
            {
                _logger.Fatal("Shutdown has been requested before the main loop has been started, complying. Reason: {0}", _shutdownReason);
            }
            else _mainLoop.Run();

            FinishMainLoop();
        }

        public void OverrideMainLoop(IGameLoop gameLoop)
        {
            _mainLoop = gameLoop;
        }


        /// <summary>
        ///     Loads the server's runtime settings (tick rate, time scale, start offset, auto-pause)
        ///     from the configuration manager and keeps them in sync with later CVar changes.
        /// </summary>
        private void LoadSettings()
        {
            _config.OnValueChanged(CVars.NetTickrate, b =>
            {
                var rate = (ushort) b;
                _time.TickRate = rate;
                _logger.Info($"Tickrate changed to: {rate} on tick {_time.CurTick}");
            });

            // Invoked immediately, i.e. before the time base and tick rate below are applied.
            _config.OnValueChanged(CVars.GameTimeScale, f =>
            {
                if (!GameTiming.IsTimescaleValid(f))
                {
                    _logger.Error($"Invalid time scale set: {f}, ignoring");
                    return;
                }

                _time.TimeScale = f;
                _logger.Info($"Timescale changed to: {f} on tick {_time.CurTick}");
            }, true);

            var startOffset = TimeSpan.FromSeconds(_config.GetCVar(CVars.NetTimeStartOffset));
            _time.TimeBase = (startOffset, GameTick.First);

            _time.TickRate = (ushort) _config.GetCVar(CVars.NetTickrate);

            _logger.Info($"Name: {ServerName}");
            _logger.Info($"TickRate: {_time.TickRate}({_time.TickPeriod.TotalMilliseconds:0.00}ms)");
            _logger.Info($"Max players: {MaxPlayers}");

            _config.OnValueChanged(CVars.GameAutoPauseEmpty, UpdateAutoPause, true);
        }

        private void UpdateAutoPause(bool doAutoPause)
        {
            _autoPause = doAutoPause;
            if (doAutoPause)
            {
                if (!_time.Paused && CheckIfShouldAutoPause())
                {
                    _logger.Debug("game.auto_pause_empty changed, pausing");
                    _time.Paused = true;
                }
            }
            else if (_time.Paused)
            {
                _logger.Debug("game.auto_pause_empty changed, unpausing");
                _time.Paused = false;
            }
        }

        private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
        {
            if (!_autoPause)
                return;

            if (e.NewStatus == SessionStatus.Connected && _time.Paused)
            {
                _logger.Debug("Client connecting, unpausing automatically.");
                _time.Paused = false;
            }

            if (e.NewStatus == SessionStatus.Disconnected && CheckIfShouldAutoPause())
            {
                _logger.Debug("Last client disconnected, pausing automatically.");
                _time.Paused = true;
            }
        }

        private bool CheckIfShouldAutoPause()
        {
            return _playerManager.Sessions.All(s => s.Status == SessionStatus.Disconnected);
        }

        // Last hook before the main loop ends: persist data and release resources here.
        public void Cleanup()
        {
            _replay.Shutdown();
            _modLoader.Shutdown();

            _playerManager.Shutdown();
            _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;

            // Kick every client with the shutdown reason; the redial flag tells them to reconnect.
            var disconnectMessage = new NetDisconnectMessage($"Server shutting down: {_shutdownReason}", true);
            _network.Shutdown(disconnectMessage.Encode());

            _entityManager.Cleanup();

            if (_config.GetCVar(CVars.LogRuntimeLog))
            {
                var logPath = _config.GetCVar(CVars.LogPath);
                if (!Path.IsPathRooted(logPath))
                    logPath = PathHelpers.ExecutableRelativeFile(logPath);

                // Note: the "-T" in the timestamp format is intentional (keeps existing file names stable).
                var logFile = Path.Combine(logPath,
                    "Runtime-" + DateTime.Now.ToString("yyyy-MM-dd-THH-mm-ss") + ".txt");

                Directory.CreateDirectory(logPath);
                File.WriteAllText(logFile, _runtimeLog.Display(), EncodingHelpers.UTF8);
            }

            AppDomain.CurrentDomain.ProcessExit -= ProcessExiting;

            if (OperatingSystem.IsWindows())
            {
                var tickPeriod = _config.GetCVar(CVars.SysWinTickPeriod);
                if (tickPeriod >= 0)
                    WindowsTickPeriod.TimeEndPeriod((uint) tickPeriod);
            }

            // Config goes last so it can still be used (and saved) by everything above.
            _config.Shutdown();
        }

        private void Input(FrameEventArgs args)
        {
            using var _ = TickUsage.WithLabels("Inputs").NewTimer();
            _systemConsole.UpdateInput();

            _network.ProcessPackets();
            _taskManager.ProcessPendingTasks();

            _modLoader.BroadcastUpdate(ModUpdateLevel.InputPostEngine, args);
        }

        private void Update(FrameEventArgs frameEventArgs)
        {
            ServerCurTick.Set(_time.CurTick.Value);
            ServerCurTime.Set(_time.CurTime.TotalSeconds);

            _systemConsole.UpdateTick();

            using (TickUsage.WithLabels("PreEngine").NewTimer())
            {
                _modLoader.BroadcastUpdate(ModUpdateLevel.PreEngine, frameEventArgs);
            }

            using (TickUsage.WithLabels("NetworkedCVar").NewTimer())
            {
                _config.TickProcessMessages();
            }

            using (TickUsage.WithLabels("Timers").NewTimer())
            {
                _consoleHost.CommandBufferExecute();
                _timerManager.UpdateTimers(frameEventArgs);
            }

            using (TickUsage.WithLabels("AsyncTasks").NewTimer())
            {
                _taskManager.ProcessPendingTasks();
            }

            // Not timed here: the entity manager records its own finer-grained labels on the histogram.
            _entityManager.TickUpdate(frameEventArgs.DeltaSeconds, noPredictions: false, TickUsage);

            using (TickUsage.WithLabels("PostEngine").NewTimer())
            {
                _modLoader.BroadcastUpdate(ModUpdateLevel.PostEngine, frameEventArgs);
            }

            using (TickUsage.WithLabels("GameState").NewTimer())
            {
                _stateManager.SendGameStateUpdate();
            }
        }

        private void FrameUpdate(FrameEventArgs frameEventArgs)
        {
            ServerUpTime.Set(_uptimeStopwatch.Elapsed.TotalSeconds);

            _modLoader.BroadcastUpdate(ModUpdateLevel.FramePreEngine, frameEventArgs);

            _watchdogApi.Heartbeat();
            _hubManager.Heartbeat();

            _modLoader.BroadcastUpdate(ModUpdateLevel.FramePostEngine, frameEventArgs);

            _transfer.FrameUpdate();

            _metricsManager.FrameUpdate();
        }

        void IPostInjectInit.PostInject()
        {
            _logger = _log.GetSawmill("srv");
        }
    }
}
