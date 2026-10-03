using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime;
using System.Threading.Tasks;
using Robust.Client.Audio;
using Robust.Client.Audio.Midi;
using Robust.Client.Console;
using Robust.Client.GameObjects;
using Robust.Client.GameStates;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Network.Transfer;
using Robust.Client.Placement;
using Robust.Client.Replays.Loading;
using Robust.Client.Replays.Playback;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Client.Upload;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.RichText;
using Robust.Client.UserInterface.XAML.Proxy;
using Robust.Client.Utility;
using Robust.Client.ViewVariables;
using Robust.Client.WebViewHook;
using Robust.LoaderApi;
using Robust.Shared;
using Robust.Shared.Asynchronous;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Exceptions;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Profiling;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Replays;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Threading;
using Robust.Shared.Timing;
using Robust.Shared.Upload;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Robust.Client
{
    internal sealed partial class GameController : IGameControllerInternal
    {
        [Dependency] private readonly INetConfigurationManagerInternal _configurationManager = default!;
        [Dependency] private readonly IResourceCacheInternal _resourceCache = default!;
        [Dependency] private readonly IResourceManagerInternal _resManager = default!;
        [Dependency] private readonly IRobustSerializer _serializer = default!;
        [Dependency] private readonly IXamlProxyManager _xamlProxyManager = default!;
        [Dependency] private readonly IXamlHotReloadManager _xamlHotReloadManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IClientNetManager _networkManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IStateManager _stateManager = default!;
        [Dependency] private readonly IUserInterfaceManagerInternal _userInterfaceManager = default!;
        [Dependency] private readonly IBaseClient _client = default!;
        [Dependency] private readonly IInputManager _inputManager = default!;
        [Dependency] private readonly IClientConsoleHost _console = default!;
        [Dependency] private readonly ITimerManager _timerManager = default!;
        [Dependency] private readonly IClientEntityManager _entityManager = default!;
        [Dependency] private readonly IPlacementManager _placementManager = default!;
        [Dependency] private readonly IClientGameStateManager _gameStateManager = default!;
        [Dependency] private readonly IOverlayManagerInternal _overlayManager = default!;
        [Dependency] private readonly ILogManager _logManager = default!;
        [Dependency] private readonly ITaskManager _taskManager = default!;
        [Dependency] private readonly IClientViewVariablesManagerInternal _viewVariablesManager = default!;
        [Dependency] private readonly IDiscordRichPresence _discord = default!;
        [Dependency] private readonly IClydeInternal _clyde = default!;
        [Dependency] private readonly IAudioInternal _audio = default!;
        [Dependency] private readonly IFontManagerInternal _fontManager = default!;
        [Dependency] private readonly IModLoaderInternal _modLoader = default!;
        [Dependency] private readonly IScriptClient _scriptClient = default!;
        [Dependency] private readonly IRobustMappedStringSerializer _stringSerializer = default!;
        [Dependency] private readonly IAuthManager _authManager = default!;
        [Dependency] private readonly IMidiManager _midiManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IParallelManagerInternal _parallelMgr = default!;
        [Dependency] private readonly ProfManager _prof = default!;
        [Dependency] private readonly IRuntimeLog _runtimeLog = default!;
        [Dependency] private readonly ISerializationManager _serializationManager = default!;
        [Dependency] private readonly MarkupTagManager _tagManager = default!;
        [Dependency] private readonly IGamePrototypeLoadManager _protoLoadMan = default!;
        [Dependency] private readonly NetworkResourceManager _netResMan = default!;
        [Dependency] private readonly IReplayLoadManager _replayLoader = default!;
        [Dependency] private readonly IReplayPlaybackManager _replayPlayback = default!;
        [Dependency] private readonly IReplayRecordingManagerInternal _replayRecording = default!;
        [Dependency] private readonly IReflectionManager _reflectionManager = default!;
        [Dependency] private readonly IReloadManager _reload = default!;
        [Dependency] private readonly ILocalizationManager _loc = default!;
        [Dependency] private readonly ISystemFontManagerInternal _systemFontManager = default!;
        [Dependency] private readonly LoadingScreenManager _loadscr = default!;
        [Dependency] private readonly ITransferManager _transfer = default!;
        [Dependency] private readonly ClientTransferTestManager _transferTest = default!;

        private IWebViewManagerHook? _webViewHook;

        private CommandLineArgs? _commandLineArgs;

        // Arguments for loader-load. Not used otherwise.
        private IMainArgs? _loaderArgs;

        public bool ContentStart { get; set; } = false;
        public StartType StartTypeValue { get; private set; }
        public GameControllerOptions Options { get; private set; } = new();
        public InitialLaunchState LaunchState { get; private set; } = default!;

        private ResourceManifestData? _resourceManifest;

        private DisplayMode _displayMode;

        public void SetCommandLineArgs(CommandLineArgs args)
        {
            _commandLineArgs = args;
        }

        public string GameTitle()
        {
            return Options.DefaultWindowTitle ?? _resourceManifest!.DefaultWindowTitle ?? "RobustToolbox";
        }

        public string WindowIconSet()
        {
            return Options.WindowIconSet?.ToString() ?? _resourceManifest!.WindowIconSet ?? "";
        }

        public string SplashLogo()
        {
            return Options.SplashLogo?.ToString() ?? _resourceManifest!.SplashLogo ?? "";
        }

        public bool ShowLoadingBar()
        {
            return _resourceManifest!.ShowLoadingBar ?? _configurationManager.GetCVar(CVars.LoadingShowBar);
        }

        /// <summary>
        ///     Second startup phase, run once the window system is up: initialises every manager as a counted
        ///     loading step, loads content modules, and finally creates and wires the main loop.
        /// </summary>
        /// <param name="displayMode">Whether the client runs headless or with a real window.</param>
        /// <returns>False if content modules failed to load and startup must abort; otherwise true.</returns>
        internal bool StartupContinue(DisplayMode displayMode)
        {
            DebugTools.AssertNotNull(_resourceManifest);

            // 42 is the number of counted loading steps below.
            _loadscr.Initialize(42);

            _loadscr.BeginLoadingSection("Init graphics", dontRender: true);
            _clyde.InitializePostWindowing();
            _clyde.SetWindowTitle(GameTitle());
            _loadscr.EndLoadingSection();

            _loadscr.LoadingStep(_audio.InitializePostWindowing, "Init audio");

            _loadscr.LoadingStep(_taskManager.Initialize, _taskManager);
            _loadscr.LoadingStep(_parallelMgr.Initialize, _parallelMgr);

            _fontManager.SetFontDpi((uint) _configurationManager.GetCVar(CVars.DisplayFontDpi));

            _loadscr.LoadingStep(_systemFontManager.Initialize, "System fonts");

            _loadscr.LoadingStep(() => LoadOptionalRobustModules(displayMode, _resourceManifest!), "Robust Modules");

            // Content module loading.
            _loadscr.BeginLoadingSection(_modLoader);
            // Load contexts are not used when the client was started from content.
            _modLoader.SetUseLoadContext(!ContentStart);
            var sandboxDisabledByEnv = Environment.GetEnvironmentVariable("ROBUST_DISABLE_SANDBOX") == "1";
            _modLoader.SetEnableSandboxing(!sandboxDisabledByEnv && Options.Sandboxing);
            if (!LoadModules())
            {
                return false;
            }

            foreach (var loadedAssembly in _modLoader.LoadedModules)
            {
                _configurationManager.LoadCVarsFromAssembly(loadedAssembly);
            }

            _loadscr.EndLoadingSection();

            _loadscr.LoadingStep(_serializationManager.Initialize, _serializationManager);
            _loadscr.LoadingStep(_loc.Initialize, _loc);

            _loadscr.LoadingStep(() => _modLoader.BroadcastRunLevel(ModRunLevel.PreInit), "Content PreInit");

            _loadscr.LoadingStep(() => _webViewHook?.Initialize(), "WebView init");

            _loadscr.LoadingStep(() => _modLoader.BroadcastRunLevel(ModRunLevel.Init), "Content Init");

            // Started after content init, since content may modify the VFS.
            var badFileExtensionCheck = ProgramShared.CheckBadFileExtensions(
                _resManager,
                _configurationManager,
                _logManager.GetSawmill("res"));

            _loadscr.LoadingStep(_resourceCache.PreloadTextures, "Texture preload");

            _loadscr.LoadingStep(() =>
            {
                _networkManager.Initialize(false);
                _transfer.Initialize();
                _transferTest.Initialize();
            }, _networkManager);

            _loadscr.LoadingStep(_configurationManager.SetupNetworking, _configurationManager);
            _loadscr.LoadingStep(_serializer.Initialize, _serializer);
            _loadscr.LoadingStep(_inputManager.Initialize, _inputManager);
            _loadscr.LoadingStep(_console.Initialize, _console);

            // Must finish before prototypes are loaded.
            _loadscr.LoadingStep(() => ProgramShared.FinishCheckBadFileExtensions(badFileExtensionCheck),
                "Check bad file extensions");

            _loadscr.LoadingStep(_reload.Initialize, _reload);
            _loadscr.LoadingStep(_reflectionManager.Initialize, _reflectionManager);
            _loadscr.LoadingStep(_xamlProxyManager.Initialize, _xamlProxyManager);
            _loadscr.LoadingStep(_xamlHotReloadManager.Initialize, _xamlHotReloadManager);

            _loadscr.BeginLoadingSection(_prototypeManager);
            _prototypeManager.Initialize();
            _prototypeManager.LoadDefaultPrototypes();
            _loadscr.EndLoadingSection();

            _loadscr.LoadingStep(_userInterfaceManager.Initialize, "UI init");

            _loadscr.LoadingStep(_eyeManager.Initialize, _eyeManager);
            _loadscr.LoadingStep(_entityManager.Initialize, _entityManager);
            _loadscr.LoadingStep(_mapManager.Initialize, _mapManager);
            _loadscr.LoadingStep(_gameStateManager.Initialize, _gameStateManager);
            _loadscr.LoadingStep(_placementManager.Initialize, _placementManager);
            _loadscr.LoadingStep(_viewVariablesManager.Initialize, _viewVariablesManager);
            _loadscr.LoadingStep(_scriptClient.Initialize, _scriptClient);
            _loadscr.LoadingStep(_client.Initialize, _client);
            _loadscr.LoadingStep(_discord.Initialize, _discord);
            _loadscr.LoadingStep(_tagManager.Initialize, _tagManager);
            _loadscr.LoadingStep(_protoLoadMan.Initialize, _protoLoadMan);
            _loadscr.LoadingStep(_netResMan.Initialize, _netResMan);
            _loadscr.LoadingStep(_replayLoader.Initialize, _replayLoader);
            _loadscr.LoadingStep(_replayPlayback.Initialize, _replayPlayback);
            _loadscr.LoadingStep(_replayRecording.Initialize, _replayRecording);

            _loadscr.LoadingStep(_userInterfaceManager.PostInitialize, "UI postinit");

            _loadscr.LoadingStep(() => _modLoader.BroadcastRunLevel(ModRunLevel.PostInit), "Content PostInit");
            _loadscr.Finish();

            if (_commandLineArgs?.Username != null)
            {
                _client.PlayerNameOverride = _commandLineArgs.Username;
            }

            _authManager.LoadFromEnv();

            if (_configurationManager.GetCVar(CVars.SysGCCollectStart))
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
            }

            // An existing main loop is reused as-is.
            _mainLoop ??= new GameLoop(
                _gameTiming,
                _runtimeLog,
                _prof,
                _logManager.GetSawmill("eng"),
                GameLoopOptions.FromCVars(_configurationManager))
            {
                SleepMode = displayMode == DisplayMode.Headless ? SleepMode.Delay : SleepMode.None
            };

            _mainLoop.Tick += (sender, args) =>
            {
                if (_mainLoop.Running)
                    Tick(args);
            };

            _mainLoop.Render += (sender, args) =>
            {
                if (_mainLoop.Running)
                {
                    _gameTiming.CurFrame++;
                    _clyde.Render();
                }
            };

            _mainLoop.Input += (sender, args) =>
            {
                if (_mainLoop.Running)
                    Input(args);
            };

            _mainLoop.Update += (sender, args) =>
            {
                if (_mainLoop.Running)
                    Update(args);
            };

            _configurationManager.OnValueChanged(CVars.DisplayMaxFPS, _ => UpdateVsyncConfig());
            _configurationManager.OnValueChanged(CVars.DisplayVSync, _ => UpdateVsyncConfig(), invokeImmediately: true);

            _clyde.Ready();

            if (_resourceManifest!.AutoConnect
                && (_commandLineArgs?.Connect == true || _commandLineArgs?.Launcher == true)
                && LaunchState.ConnectEndpoint != null)
            {
                _client.ConnectToServer(LaunchState.ConnectEndpoint);
            }

            ProgramShared.RunExecCommands(_console, _commandLineArgs?.ExecCommands);

            return true;
        }

        private bool LoadModules()
        {
            DebugTools.Assert(_resourceManifest != null);

            var assemblyPrefix = Options.ContentModulePrefix ?? _resourceManifest!.AssemblyPrefix ?? "Content.";
            var assemblyDir = Options.AssemblyDirectory;

            bool result;
            if (_resourceManifest.ClientAssemblies is { } clientAssemblies)
            {
                // We have client assemblies. Load only the assemblies listed in the content manifest.
                var paths = clientAssemblies.Select(p => assemblyDir / $"{p}.dll");
                result = _modLoader.TryLoadModules(paths);
            }
            else
            {
                result = _modLoader.TryLoadModulesFrom(assemblyDir, assemblyPrefix);
            }

            if (!result)
            {
                _logger.Fatal("Errors while loading content assemblies.");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     First startup phase: logging, configuration, resource mounting and bringing up the display
        ///     as far as possible before the window exists.
        /// </summary>
        /// <param name="options">Options supplied by the host program.</param>
        /// <param name="logHandlerFactory">Creates the root log handler; a console handler is used when null.</param>
        /// <param name="globalExceptionLog">Whether unhandled exceptions should be logged globally.</param>
        /// <returns>True if the display could be initialised; false if startup failed.</returns>
        internal bool StartupSystemSplash(
            GameControllerOptions options,
            Func<ILogHandler>? logHandlerFactory,
            bool globalExceptionLog = false)
        {
            Options = options;
            ReadInitialLaunchState();

            SetupLogging(_logManager, logHandlerFactory ?? (() => new ConsoleLogHandler()), globalExceptionLog);

            if (_commandLineArgs != null)
            {
                foreach (var (sawmillName, levelText) in _commandLineArgs.LogLevels)
                {
                    LogLevel? level;
                    if (levelText == "null")
                    {
                        level = null;
                    }
                    else
                    {
                        if (!Enum.TryParse<LogLevel>(levelText, out var parsed))
                        {
                            System.Console.WriteLine($"LogLevel {levelText} does not exist!");
                            continue;
                        }

                        level = parsed;
                    }

                    _logManager.GetSawmill(sawmillName).Level = level;
                }
            }

            ProgramShared.PrintRuntimeInfo(_logManager.RootSawmill);

            var userDataDir = GetUserDataDir();

            _configurationManager.Initialize(false);

            // Client CVars first, then the shared ones, then client-specific default overrides.
            _configurationManager.LoadCVarsFromAssembly(typeof(GameController).Assembly);
            _configurationManager.LoadCVarsFromAssembly(typeof(IConfigurationManager).Assembly);
            CVarDefaultOverrides.OverrideClient(_configurationManager);

            if (Options.LoadConfigAndUserData)
            {
                var configPath = Path.Combine(userDataDir, Options.ConfigFileName);
                if (File.Exists(configPath))
                {
                    _configurationManager.LoadFromFile(configPath);
                }
                else
                {
                    // Defaults are used; the file gets written once something changes.
                    _configurationManager.SetSaveFile(configPath);
                }
            }

            // Command line is applied last so that it takes priority.
            _configurationManager.OverrideConVars(EnvironmentVariables.GetEnvironmentCVars());

            if (_commandLineArgs != null)
            {
                _configurationManager.OverrideConVars(_commandLineArgs.CVars);
            }

            ProfileOptSetup.Setup(_configurationManager);
            _prof.Initialize();

            _resManager.Initialize(Options.LoadConfigAndUserData ? userDataDir : null, hideUserDataDir: true);

            var mountOptions = _commandLineArgs != null
                ? MountOptions.Merge(_commandLineArgs.MountOptions, Options.MountOptions)
                : Options.MountOptions;

            StartTypeValue = ContentStart ? StartType.Content : StartType.Engine;
#if FULL_RELEASE
            if (_loaderArgs != null || Options.ResourceMountDisabled)
            {
                StartTypeValue = StartType.Loader;
            }
#else
            if (StartTypeValue == StartType.Content
                && Path.GetFileName(PathHelpers.GetExecutableDirectory()) == "MacOS")
            {
                StartTypeValue = StartType.ContentAppBundle;
            }
#endif

            ProgramShared.DoMounts(
                _resManager,
                mountOptions,
                Options.ContentBuildDirectory,
                Options.AssemblyDirectory,
                Options.LoadContentResources,
                StartTypeValue);

            if (_loaderArgs != null)
            {
                if (_loaderArgs.ApiMounts != null)
                {
                    foreach (var (api, prefix) in _loaderArgs.ApiMounts)
                    {
                        _resourceCache.MountLoaderApi(_resManager, api, "", new(prefix));
                    }
                }

                _stringSerializer.EnableCaching = false;
                _resourceCache.MountLoaderApi(_resManager, _loaderArgs.FileApi, "Resources/");
                _modLoader.VerifierExtraLoadHandler = VerifierExtraLoadHandler;
            }

            _resourceManifest = ResourceManifestData.LoadResourceManifest(_resManager);

            _configurationManager.OverrideConVars(new[]
            {
                (CVars.DisplayWindowIconSet.Name, WindowIconSet()),
                (CVars.DisplaySplashLogo.Name, SplashLogo()),
                (CVars.LoadingShowBar.Name, ShowLoadingBar().ToString())
            });

            _clyde.TextEntered += TextEntered;
            _clyde.TextEditing += TextEditing;
            _clyde.MouseMove += MouseMove;
            _clyde.KeyUp += KeyUp;
            _clyde.KeyDown += KeyDown;
            _clyde.MouseWheel += MouseWheel;
            _clyde.CloseWindow += args =>
            {
                if (args.Window == _clyde.MainWindow)
                {
                    Shutdown("Main window closed");
                }
            };

            return _clyde.InitializePreWindowing();
        }

        private Stream? VerifierExtraLoadHandler(string arg)
        {
            DebugTools.AssertNotNull(_loaderArgs);

            if (_loaderArgs!.FileApi.TryOpen(arg, out var stream))
            {
                return stream;
            }

            return null;
        }

        private void ReadInitialLaunchState()
        {
            if (_commandLineArgs == null)
            {
                LaunchState = new InitialLaunchState(false, null, null, null);
            }
            else
            {
                var addr = _commandLineArgs.ConnectAddress;
                if (!addr.Contains("://"))
                {
                    addr = "udp://" + addr;
                }

                var uri = new Uri(addr);

                if (uri.Scheme != "udp")
                {
                    _logger.Warning($"connect-address '{uri}' does not have URI scheme of udp://..");
                }

                LaunchState = new InitialLaunchState(
                    _commandLineArgs.Launcher,
                    _commandLineArgs.ConnectAddress,
                    _commandLineArgs.Ss14Address,
                    new DnsEndPoint(uri.Host, uri.IsDefaultPort ? 1212 : uri.Port));
            }
        }

        /// <summary>
        ///     Requests that the client stops. The main loop finishes its current iteration and then exits.
        ///     Calling this before the main loop exists only records that startup must not proceed.
        /// </summary>
        /// <param name="reason">Optional human-readable reason, written to the log.</param>
        public void Shutdown(string? reason = null)
        {
            if (_mainLoop == null)
            {
                // Client init has not finished yet; only the first call logs.
                if (!_dontStart)
                {
                    _logger.Info($"Shutdown called before client init completed: {reason ?? "No reason provided"}");
                    _dontStart = true;
                }

                return;
            }

            if (!_mainLoop.Running)
            {
                return;
            }

            _logger.Info(reason == null ? "Shutting down!" : $"Shutting down! Reason: {reason}");

            _mainLoop.Running = false;
        }

        /// <summary>
        ///     Input phase of a frame: window input, network packets, pending async tasks, then content.
        /// </summary>
        /// <param name="frameEventArgs">Timing information for this frame.</param>
        private void Input(FrameEventArgs frameEventArgs)
        {
            using (_prof.Group("Input Events"))
            {
                _clyde.ProcessInput(frameEventArgs);
            }

            using (_prof.Group("Network"))
            {
                _networkManager.ProcessPackets();
            }

            // Completes things such as connection tasks.
            using (_prof.Group("Async"))
            {
                _taskManager.ProcessPendingTasks();
            }

            using (_prof.Group("Content post engine"))
            {
                _modLoader.BroadcastUpdate(ModUpdateLevel.InputPostEngine, frameEventArgs);
            }
        }

        /// <summary>
        ///     Fixed-rate simulation tick. Runs content hooks, console, timers and async work, then advances
        ///     the simulation according to the current client run level.
        /// </summary>
        /// <param name="frameEventArgs">Timing information for this tick.</param>
        private void Tick(FrameEventArgs frameEventArgs)
        {
            using (_prof.Group("Content pre engine"))
            {
                _modLoader.BroadcastUpdate(ModUpdateLevel.PreEngine, frameEventArgs);
            }

            using (_prof.Group("Console"))
            {
                _console.CommandBufferExecute();
            }

            using (_prof.Group("Timers"))
            {
                _timerManager.UpdateTimers(frameEventArgs);
            }

            using (_prof.Group("Async"))
            {
                _taskManager.ProcessPendingTasks();
            }

            switch (_client.RunLevel)
            {
                case ClientRunLevel.InGame:
                case ClientRunLevel.Connected:
                    // The game state manager drives the simulation.
                    using (_prof.Group("Game state"))
                    {
                        _gameStateManager.ApplyGameState();
                    }

                    break;

                case ClientRunLevel.SinglePlayerGame:
                    using (_prof.Group("Entity"))
                    {
                        if (TickUpdateOverride != null)
                        {
                            TickUpdateOverride.Invoke(frameEventArgs);
                        }
                        else
                        {
                            // Keep the client out of prediction mode.
                            _gameTiming.LastRealTick = _gameTiming.LastProcessedTick = _gameTiming.CurTick;
                            _entityManager.TickUpdate(frameEventArgs.DeltaSeconds, noPredictions: false);
                        }
                    }

                    break;
            }

            using (_prof.Group("Content post engine"))
            {
                _modLoader.BroadcastUpdate(ModUpdateLevel.PostEngine, frameEventArgs);
            }
        }

        private void Update(FrameEventArgs frameEventArgs)
        {
            if (_webViewHook != null)
            {
                using (_prof.Group("WebView"))
                {
                    _webViewHook?.Update();
                }
            }

            using (_prof.Group("Clyde"))
            {
                _clyde.FrameProcess(frameEventArgs);
            }

            using (_prof.Group("Content Pre Engine"))
            {
                _modLoader.BroadcastUpdate(ModUpdateLevel.FramePreEngine, frameEventArgs);
            }

            using (_prof.Group("State"))
            {
                _stateManager.FrameUpdate(frameEventArgs);
            }

            if (_client.RunLevel >= ClientRunLevel.Connected)
            {
                using (_prof.Group("Placement"))
                {
                    _placementManager.FrameUpdate(frameEventArgs);
                }

                using (_prof.Group("Entity"))
                {
                    _entityManager.FrameUpdate(frameEventArgs.DeltaSeconds);
                }
            }

            using (_prof.Group("Overlay"))
            {
                _overlayManager.FrameUpdate(frameEventArgs);
            }

            using (_prof.Group("UI"))
            {
                _userInterfaceManager.FrameUpdate(frameEventArgs);
            }

            using (_prof.Group("Content Post Engine"))
            {
                _modLoader.BroadcastUpdate(ModUpdateLevel.FramePostEngine, frameEventArgs);
            }

            using (_prof.Group("Transfer"))
            {
                _transfer.FrameUpdate();
            }

            _audio.FlushALDisposeQueues();
        }

        internal static void SetupLogging(
            ILogManager logManager,
            Func<ILogHandler> logHandlerFactory,
            bool globalExceptionLog)
        {
            logManager.RootSawmill.AddHandler(logHandlerFactory());

            //logManager.GetSawmill("res.typecheck").Level = LogLevel.Info;
            logManager.GetSawmill("res.tex").Level = LogLevel.Info;
            logManager.GetSawmill("console").Level = LogLevel.Info;
            logManager.GetSawmill("go.sys").Level = LogLevel.Info;
            logManager.GetSawmill("ogl.debug.performance").Level = LogLevel.Fatal;
            // Stupid nvidia driver spams buffer info on DebugTypeOther every time you re-allocate a buffer.
            logManager.GetSawmill("ogl.debug.other").Level = LogLevel.Warning;
            logManager.GetSawmill("gdparse").Level = LogLevel.Error;
            logManager.GetSawmill("discord").Level = LogLevel.Warning;
            logManager.GetSawmill("szr").Level = LogLevel.Info;
            logManager.GetSawmill("loc").Level = LogLevel.Warning;

            if (globalExceptionLog)
            {
#if DEBUG_ONLY_FCE_INFO
#if DEBUG_ONLY_FCE_LOG
                var fce = logManager.GetSawmill("fce");
#endif
                AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
                {
                    // TODO: record FCE stats
#if DEBUG_ONLY_FCE_LOG
                    fce.Fatal(message);
#endif
                }
#endif

                var uh = logManager.GetSawmill("unhandled");
                AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                {
                    var message = ((Exception)args.ExceptionObject).ToString();
                    uh.Log(args.IsTerminating ? LogLevel.Fatal : LogLevel.Error, message);
                };

                var uo = logManager.GetSawmill("unobserved");
                TaskScheduler.UnobservedTaskException += (sender, args) =>
                {
                    uo.Error(args.Exception!.ToString());
#if EXCEPTION_TOLERANCE
                    args.SetObserved(); // don't crash
#endif
                };
            }
        }

        private string GetUserDataDir()
        {
            if (_commandLineArgs?.SelfContained == true)
            {
                // Self contained mode. Data is stored in a directory called user_data next to Robust.Client.exe.
                var exeDir = typeof(GameController).Assembly.Location;
                if (string.IsNullOrEmpty(exeDir))
                {
                    throw new Exception("Unable to locate client exe");
                }

                exeDir = Path.GetDirectoryName(exeDir);
                return Path.Combine(exeDir ?? throw new InvalidOperationException(), "user_data");
            }

            return UserDataDir.GetUserDataDir(this);
        }


        private void UpdateVsyncConfig()
        {
            if (_displayMode == DisplayMode.Headless)
                return;

            var vsync = _configurationManager.GetCVar(CVars.DisplayVSync);
            var maxFps = Math.Clamp(_configurationManager.GetCVar(CVars.DisplayMaxFPS), 0, 10_000);

            _clyde.VsyncEnabled = vsync;

            if (_mainLoop == null)
                return;

            if (vsync || maxFps == 0)
            {
                _mainLoop.SleepMode = SleepMode.None;
            }
            else
            {
                _mainLoop.SleepMode = SleepMode.Limit;
                _mainLoop.LimitMinFrameTime = TimeSpan.FromSeconds(1.0 / maxFps);
            }
        }

        internal enum DisplayMode : byte
        {
            Headless,
            Clyde,
        }

        internal void CleanupGameThread()
        {
            _replayRecording.Shutdown();

            _modLoader.Shutdown();

            // CEF specifically makes a massive silent stink of it if we don't shut it down from the correct thread.
            _webViewHook?.Shutdown();

            _networkManager.Shutdown("Client shutting down");
            _midiManager.Shutdown();
            _entityManager.Shutdown();
        }

        internal void CleanupWindowThread()
        {
            _clyde.Shutdown();
            _audio.Shutdown();
        }

        public event Action<FrameEventArgs>? TickUpdateOverride;
    }
}
