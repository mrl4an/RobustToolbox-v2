using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Asynchronous;
using Robust.Shared.Reflection;
using Robust.Shared.Utility;

namespace Robust.Server
{

    internal static class Program
    {

        private static bool _hasStarted;

        internal static void Main(string[] args)
        {
            Start(args, new ServerOptions());
        }

        internal static void Start(string[] args, ServerOptions options, bool contentStart = false)
        {
            if (_hasStarted)
            {
                throw new InvalidOperationException("Cannot start twice!");
            }

            _hasStarted = true;

            if (!CommandLineArgs.TryParse(args, out var parsed))
            {
                return;
            }

            ParsedMain(parsed, contentStart, options);
        }

        /// <summary>
        ///     Runs the server with already parsed command line arguments, blocking until shutdown.
        /// </summary>
        private static void ParsedMain(CommandLineArgs args, bool contentStart, ServerOptions options)
        {
            ServerWarmup.RunWarmup();

            Thread.CurrentThread.Name = "Main Thread";

            var deps = IoCManager.InitThread();
            ServerIoC.RegisterIoC(deps);
            deps.BuildGraph();
            SetupLogging(deps);
            InitReflectionManager(deps);

            var server = deps.Resolve<IBaseServerInternal>();
            var logger = deps.Resolve<ILogManager>().RootSawmill;

            server.ContentStart = contentStart;
            server.SetCommandLineArgs(args);

            logger.Info("Server -> Starting");

            // Start returns true on failure.
            if (server.Start(options))
            {
                logger.Fatal("Server -> Can not start server");
                return;
            }

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            logger.Info("Server Version " + version + " -> Ready");

            server.MainLoop();

            logger.Info("Goodbye.");

            // Disposes services such as the log manager.
            deps.Clear();
        }

        /// <summary>
        ///     Registers the shared and server assemblies with the reflection manager.
        /// </summary>
        internal static void InitReflectionManager(IDependencyCollection deps)
        {
            var reflection = deps.Resolve<IReflectionManager>();

            reflection.LoadAssemblies(new List<Assembly>(2)
            {
                AppDomain.CurrentDomain.GetAssemblyByName("Robust.Shared"),
                Assembly.GetExecutingAssembly(),
            });
        }

        /// <summary>
        ///     Configures console logging, default sawmill levels and logging of unhandled exceptions.
        /// </summary>
        internal static void SetupLogging(IDependencyCollection deps)
        {
            if (OperatingSystem.IsWindows())
            {
#if WINDOWS_USE_UTF8_CONSOLE
                System.Console.OutputEncoding = Encoding.UTF8;
#else
                System.Console.OutputEncoding = Encoding.Unicode;
#endif
            }

            var mgr = deps.Resolve<ILogManager>();
            var handler = new ConsoleLogHandler();
            mgr.RootSawmill.AddHandler(handler);

            mgr.GetSawmill("res.typecheck").Level = LogLevel.Info;
            mgr.GetSawmill("go.sys").Level = LogLevel.Info;
            mgr.GetSawmill("loc").Level = LogLevel.Error;

            var uncaughtLogger = mgr.GetSawmill("unhandled");
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var message = ((Exception) args.ExceptionObject).ToString();
                try
                {
                    uncaughtLogger.Log(args.IsTerminating ? LogLevel.Fatal : LogLevel.Error, message);
                }
                catch (ObjectDisposedException)
                {
                    // The log system is already gone during shutdown; fall back to the console.
                    System.Console.WriteLine("UnhandledException but sawmill is disposed! " + message);
                }
            };

            var unobservedLogger = mgr.GetSawmill("unobserved");
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                try
                {
                    unobservedLogger.Log(LogLevel.Error, args.Exception!.ToString());
                }
                catch (ObjectDisposedException)
                {
                    System.Console.WriteLine("UnobservedTaskException but sawmill is disposed! " + args.Exception);
                }
#if EXCEPTION_TOLERANCE
                // Keep the process alive.
                args.SetObserved();
#endif
            };
        }

    }

}
