using System;
using System.Diagnostics.Tracing;
using System.Threading;
using Robust.Shared.Log;
using Robust.Shared.Exceptions;
using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.Profiling;

namespace Robust.Shared.Timing
{
    internal interface IGameLoop
    {
        event EventHandler<FrameEventArgs> Input;
        event EventHandler<FrameEventArgs> Tick;
        event EventHandler<FrameEventArgs> Update;
        event EventHandler<FrameEventArgs> Render;

        /// <summary>
        ///     When enabled, game time is paused after every simulated tick. Unpausing the game then runs
        ///     exactly one more tick before it pauses again.
        /// </summary>
        bool SingleStep { get; set; }

        /// <summary>
        ///     Whether the loop is active. Setting this to false after <see cref="Run"/> has started makes the loop stop.
        /// </summary>
        bool Running { get; set; }

        /// <summary>
        ///     How many ticks the simulation may fall behind real time before it starts to slow down.
        /// </summary>
        int MaxQueuedTicks { get; set; }

        /// <summary>
        /// Minimum frame time to attain if <see cref="SleepMode"/> is set to <see cref="Timing.SleepMode.Limit"/>.
        /// </summary>
        TimeSpan LimitMinFrameTime { get; set; }

        /// <summary>
        ///     The strategy that is currently used to limit the update rate.
        /// </summary>
        SleepMode SleepMode { get; set; }

        /// <summary>
        ///     Starts the loop. Blocks the calling thread for as long as <see cref="Running"/> is true;
        ///     setting <see cref="Running"/> to false makes this method return.
        /// </summary>
        void Run();
    }

    /// <summary>
    ///     Drives the main loop that runs simulation ticks and frame updates.
    /// </summary>
    internal sealed class GameLoop : IGameLoop
    {
        private static readonly TimeSpan DelayTime = TimeSpan.FromMilliseconds(1);

        public const string ProfTextStartFrame = "Start Frame";

        private static readonly Histogram _frameTimeHistogram = Metrics.CreateHistogram(
            "robust_game_loop_frametime",
            "Histogram of frametimes in ms",
            new HistogramConfiguration
            {
                Buckets = Histogram.ExponentialBuckets(.001, 1.5, 10)
            });

        private readonly IGameTiming _timing;
        private TimeSpan _lastKeepUp; // last wall time keep up announcement

        public event EventHandler<FrameEventArgs>? Input;
        public event EventHandler<FrameEventArgs>? Tick;
        public event EventHandler<FrameEventArgs>? Update;
        public event EventHandler<FrameEventArgs>? Render;

        /// <summary>
        ///     When enabled, game time is paused after every simulated tick. Unpausing the game then runs
        ///     exactly one more tick before it pauses again.
        /// </summary>
        public bool SingleStep { get; set; } = false;

        /// <summary>
        ///     Whether the loop is active. <see cref="Run"/> sets this to true; setting it back to false
        ///     makes the loop stop at the start of its next iteration.
        /// </summary>
        public bool Running { get; set; }

        /// <summary>
        ///     How many ticks the simulation may fall behind real time before it starts to slow down.
        /// </summary>
        public int MaxQueuedTicks { get; set; } = 5;

        public TimeSpan LimitMinFrameTime { get; set; }

        /// <summary>
        ///     If true and the same event causes an event 10 times in a row, the game loop will shut itself down.
        /// </summary>
        public bool DetectSoftLock { get; set; }

        /// <summary>
        ///     If true, the dispatch of every <see cref="Tick"/> event is timed into the frame time histogram.
        /// </summary>
        public bool EnableMetrics { get; set; } = false;

        /// <summary>
        ///     The strategy that is currently used to limit the update rate.
        /// </summary>
        public SleepMode SleepMode { get; set; } = SleepMode.Yield;

        // Only read in EXCEPTION_TOLERANCE builds; the field is kept in all builds so the constructor stays the same.
        private readonly IRuntimeLog _runtimeLog;
        private readonly ProfManager _prof;
        private readonly ISawmill _sawmill;

        private readonly PrecisionSleep _precisionSleep;

#if EXCEPTION_TOLERANCE
        private int _tickExceptions;

        private const int MaxSoftLockExceptions = 10;
#endif

        public GameLoop(IGameTiming timing, IRuntimeLog runtimeLog, ProfManager prof, ISawmill sawmill, GameLoopOptions options)
        {
            _timing = timing;
            _runtimeLog = runtimeLog;
            _prof = prof;
            _sawmill = sawmill;

            // The precise sleeper uses OS-specific high-resolution timers; otherwise fall back to the portable one.
            _precisionSleep = options.Precise
                ? PrecisionSleep.Create()
                : new PrecisionSleepUniversal();
        }

        /// <summary>
        ///     Starts the loop. Blocks the calling thread for as long as <see cref="Running"/> is true;
        ///     setting <see cref="Running"/> to false makes this method return.
        /// </summary>
        /// <exception cref="InvalidOperationException">The timing's tick rate is not greater than zero.</exception>
        public void Run()
        {
            if (_timing.TickRate <= 0)
            {
                throw new InvalidOperationException("TickRate must be greater than 0.");
            }

            Running = true;

            // Running is only examined here, so a stop request lets the current iteration (including its sleep) finish.
            while (Running)
            {
                // Profiling prologue. This runs before StartFrame(), so CurFrame is still the previous frame's value.
                var frameStartIndex = _prof.WriteValue(ProfTextStartFrame, (long)_timing.CurFrame);
                var groupStartIndex = _prof.WriteGroupStart();
                var sampler = ProfSampler.StartNew();
                var gen0Start = GC.CollectionCount(0);
                var gen1Start = GC.CollectionCount(1);
                var gen2Start = GC.CollectionCount(2);

                _timing.StartFrame();

                // Real time that has not been converted into simulation ticks yet.
                var maxBacklog = TimeSpan.FromTicks(_timing.TickPeriod.Ticks * MaxQueuedTicks);
                var accumulator = _timing.RealTime - _timing.LastTick;

                if (accumulator > maxBacklog)
                {
                    // We are too far behind: drop the excess time so the game simply runs slower.
                    accumulator = maxBacklog;
                    _timing.LastTick = _timing.RealTime - maxBacklog;

                    // Announce at most once every 15 seconds of real time.
                    if ((_timing.RealTime - _lastKeepUp).TotalSeconds >= 15.0)
                    {
                        GameLoopEventSource.Log.CannotKeepUp();
                        _sawmill.Warning("MainLoop: Cannot keep up!");
                        _lastKeepUp = _timing.RealTime;
                    }
                }

                // Input phase.
                var realFrameEvent = new FrameEventArgs((float)_timing.RealFrameTime.TotalSeconds);

                GameLoopEventSource.Log.InputStart();
#if EXCEPTION_TOLERANCE
                try
                {
#endif
                    using (_prof.Group("Input"))
                    {
                        Input?.Invoke(this, realFrameEvent);
                    }
#if EXCEPTION_TOLERANCE
                }
                catch (Exception exception)
                {
                    _runtimeLog.LogException(exception, "GameLoop Input");
                }
#endif
                GameLoopEventSource.Log.InputStop();

                // Tick phase.
                _timing.InSimulation = true;

                var tickPeriod = _timing.CalcAdjustedTickPeriod();

                using (_prof.Group("Ticks"))
                {
                    var countTicksRan = 0;

                    while (accumulator >= tickPeriod)
                    {
                        // The time is consumed even while paused, so no backlog builds up.
                        accumulator -= tickPeriod;
                        _timing.LastTick += tickPeriod;

                        if (_timing.Paused)
                        {
                            continue;
                        }

                        _timing.TickRemainder = accumulator / _timing.TimeScale;

                        countTicksRan += 1;

                        GameLoopEventSource.Log.TickStart(_timing.CurTick.Value);
                        // Kept open until the end of this loop body.
                        using var tickGroup = _prof.Group("Tick");

#if EXCEPTION_TOLERANCE
                        var threw = false;
                        try
                        {
#endif
                            var simFrameEvent = new FrameEventArgs((float)_timing.FrameTime.TotalSeconds);

                            _prof.WriteValue("Tick", (long)_timing.CurTick.Value);

                            if (EnableMetrics)
                            {
                                using (_frameTimeHistogram.NewTimer())
                                {
                                    Tick?.Invoke(this, simFrameEvent);
                                }
                            }
                            else
                            {
                                Tick?.Invoke(this, simFrameEvent);
                            }

                            GameLoopEventSource.Log.TickStop(_timing.CurTick.Value);
#if EXCEPTION_TOLERANCE
                        }
                        catch (Exception exception)
                        {
                            threw = true;
                            _runtimeLog.LogException(exception, "GameLoop Tick");

                            _tickExceptions += 1;

                            if (_tickExceptions > MaxSoftLockExceptions && DetectSoftLock)
                            {
                                _sawmill.Fatal("MainLoop: 10 consecutive exceptions inside GameLoop Tick, shutting down!");
                                Running = false;
                            }
                        }

                        if (!threw)
                        {
                            _tickExceptions = 0;
                        }
#endif

                        // The tick counter advances even if the tick threw.
                        _timing.CurTick = new GameTick(_timing.CurTick.Value + 1);
                        tickPeriod = _timing.CalcAdjustedTickPeriod();

                        if (SingleStep)
                        {
                            _timing.Paused = true;
                        }
                    }

                    _prof.WriteValue("Tick count", countTicksRan);
                }

                if (!_timing.Paused)
                {
                    // Leftover time, used for render interpolation (also covers frames where no tick ran).
                    _timing.TickRemainder = accumulator / _timing.TimeScale;
                }

                _timing.InSimulation = false;

                // Update phase.
                GameLoopEventSource.Log.UpdateStart();
#if EXCEPTION_TOLERANCE
                try
                {
#endif
                    using (_prof.Group("Update"))
                    {
                        Update?.Invoke(this, realFrameEvent);
                    }
#if EXCEPTION_TOLERANCE
                }
                catch (Exception exception)
                {
                    _runtimeLog.LogException(exception, "GameLoop Update");
                }
#endif
                GameLoopEventSource.Log.UpdateStop();

                // Render phase (has no ETW events).
#if EXCEPTION_TOLERANCE
                try
                {
#endif
                    using (_prof.Group("Render"))
                    {
                        Render?.Invoke(this, realFrameEvent);
                    }
#if EXCEPTION_TOLERANCE
                }
                catch (Exception exception)
                {
                    _runtimeLog.LogException(exception, "GameLoop Render");
                }
#endif

                // Profiling epilogue.
                using (_prof.Group("GC Overview"))
                {
                    _prof.WriteValue("Gen 0 Count", GC.CollectionCount(0) - gen0Start);
                    _prof.WriteValue("Gen 1 Count", GC.CollectionCount(1) - gen1Start);
                    _prof.WriteValue("Gen 2 Count", GC.CollectionCount(2) - gen2Start);
                }

                _prof.WriteGroupEnd(groupStartIndex, "Frame", sampler);
                _prof.MarkIndex(frameStartIndex, ProfIndexType.Frame);

                // Sleep phase.
                GameLoopEventSource.Log.SleepStart();

                switch (SleepMode)
                {
                    case SleepMode.None:
                        break;

                    case SleepMode.Yield:
                        Thread.Sleep(0);
                        break;

                    case SleepMode.Delay:
                    {
                        // Time until the next tick is due, using the nominal period, capped to DelayTime.
                        var timeToSleep = (_timing.LastTick + _timing.TickPeriod) - _timing.RealTime;
                        if (timeToSleep > DelayTime)
                        {
                            timeToSleep = DelayTime;
                        }

                        if (timeToSleep.Ticks > 0)
                        {
                            _precisionSleep.Sleep(timeToSleep);
                        }

                        break;
                    }

                    case SleepMode.Limit:
                    {
                        // How much the previous frame overran the minimum frame time (never negative).
                        var lastFrameOver = _timing.RealFrameTime - LimitMinFrameTime;
                        if (lastFrameOver < TimeSpan.Zero)
                        {
                            lastFrameOver = TimeSpan.Zero;
                        }

                        var timeSpentThisFrame = _timing.RealTime - _timing.FrameStartTime;
                        var timeRemaining = LimitMinFrameTime - timeSpentThisFrame - lastFrameOver;

                        if (timeRemaining > TimeSpan.Zero)
                        {
                            _precisionSleep.Sleep(timeRemaining);
                        }

                        break;
                    }
                }

                GameLoopEventSource.Log.SleepStop();
            }
        }
    }

    internal sealed record GameLoopOptions(bool Precise)
    {
        public static GameLoopOptions FromCVars(IConfigurationManager cfg)
        {
            return new GameLoopOptions(cfg.GetCVar(CVars.SysPreciseSleep));
        }
    }

    /// <summary>
    ///     Ways in which the loop can wait to keep the update rate in check.
    /// </summary>
    public enum SleepMode : sbyte
    {
        /// <summary>
        ///     The thread neither yields nor sleeps and uses 100% CPU. Only use this when the update rate is
        ///     limited elsewhere (for example by vsync).
        /// </summary>
        None = -1,

        /// <summary>
        ///     Like <see cref="None"/>, but after every update the rest of the time slice is handed to other
        ///     OS threads. Friendlier to the scheduler.
        /// </summary>
        Yield = 0,

        /// <summary>
        ///     Sleeps for about 1 ms after every update to reduce CPU usage and save power. Intended for
        ///     dedicated servers.
        /// </summary>
        Delay = 1,

        /// <summary>
        ///     An FPS limiter that keeps the frame time of the loop at least
        ///     <see cref="IGameLoop.LimitMinFrameTime"/>.
        /// </summary>
        Limit = 2,
    }

    [EventSource(Name = "Robust.GameLoop")]
    internal sealed class GameLoopEventSource : EventSource
    {
        public static GameLoopEventSource Log { get; } = new();

        [Event(1)]
        public void CannotKeepUp() => WriteEvent(1);

        [Event(2)]
        public void InputStart() => WriteEvent(2);

        [Event(3)]
        public void InputStop() => WriteEvent(3);

        [Event(4)]
        public void TickStart(uint tick) => WriteEvent(4, tick);

        [Event(5)]
        public void TickStop(uint tick) => WriteEvent(5, tick);

        [Event(6)]
        public void UpdateStart() => WriteEvent(6);

        [Event(7)]
        public void UpdateStop() => WriteEvent(7);

        [Event(8)]
        public void SleepStart() => WriteEvent(8);

        [Event(9)]
        public void SleepStop() => WriteEvent(9);
    }
}
