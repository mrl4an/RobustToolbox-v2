using System;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Robust.Shared.Timing
{
    /// <summary>
    ///     Provides the game clock: the current tick, frame timings and related helper members.
    /// </summary>
    [NotContentImplementable]
    public interface IGameTiming
    {
        /// <summary>
        ///     Whether the code that is running right now is inside a simulation step (a tick), as opposed to
        ///     outside of it (rendering, input, UI).
        /// </summary>
        /// <remarks>
        ///     The main loop enables this just before it runs ticks and disables it afterwards.
        ///     While it is set, time values are aligned to the current tick. While it is cleared, they are
        ///     interpolated using <see cref="TickRemainder"/>.
        ///     This is a plain flag; setting it performs no validation and raises no events.
        /// </remarks>
        bool InSimulation { get; set; }

        /// <summary>
        ///     Whether the simulation is currently suspended.
        /// </summary>
        /// <remarks>
        ///     While paused, no ticks are run and simulation clocks such as <see cref="CurTime"/> stand still.
        ///     Real-time clocks such as <see cref="RealTime"/> keep advancing.
        ///     <see cref="CurTime"/> keeps the value it had at the moment of pausing and continues from there
        ///     once the simulation is unpaused.
        ///     Setting the value it already has changes nothing.
        /// </remarks>
        bool Paused { get; set; }

        /// <summary>
        ///     The in-game clock: simulation time elapsed since the simulation started
        ///     (or since the last <see cref="ResetSimTime()"/>).
        /// </summary>
        /// <remarks>
        ///     Use this for gameplay timing such as cooldowns and stored timestamps.
        ///     It is derived from <see cref="TimeBase"/>, <see cref="CurTick"/> and <see cref="TickPeriod"/>.
        ///     Inside the simulation it is the start time of the current tick; outside of it, the partial tick
        ///     <see cref="TickRemainder"/> is included as well.
        ///     It is not guaranteed to be monotonic, because client-side prediction can move <see cref="CurTick"/> backwards.
        ///     It does not advance while <see cref="Paused"/> is true.
        /// </remarks>
        TimeSpan CurTime { get; }

        /// <summary>
        ///     Wall-clock time since the timing stopwatch was started (roughly since process start).
        /// </summary>
        /// <remarks>
        ///     The value is sampled on every access and never decreases.
        ///     It is not affected by pausing, the time scale, tick lag or prediction.
        ///     Use it for UI, animations and anything else outside the game simulation.
        ///     It is the reference scale for <see cref="FrameStartTime"/>, <see cref="LastTick"/> and
        ///     <see cref="ServerTime"/>.
        /// </remarks>
        TimeSpan RealTime { get; }

        /// <summary>
        /// The time (on the <see cref="RealTime"/> scale) when the current frame started.
        /// </summary>
        TimeSpan FrameStartTime { get; }

        /// <summary>
        ///     The <see cref="RealTime"/> of the server.
        /// </summary>
        /// <remarks>
        ///     0 if we are the client and we are not connected to a server.
        ///     <see cref="RealTime"/> if we are the server.
        /// </remarks>
        TimeSpan ServerTime { get; }

        /// <summary>
        ///     The duration of the most recently finished frame, as seen by game logic.
        /// </summary>
        /// <remarks>
        ///     The value is refreshed once per frame by <see cref="StartFrame()"/> and does not change in between.
        ///     It is never negative. For the raw wall-clock duration see <see cref="RealFrameTime"/>.
        /// </remarks>
        TimeSpan FrameTime { get; }

        /// <summary>
        ///     The wall-clock duration of the last finished frame, measured on the <see cref="RealTime"/> scale.
        /// </summary>
        /// <remarks>
        ///     This is the gap between the two most recent calls to <see cref="StartFrame()"/>.
        ///     It is not affected by pausing or by the time scale, and it is only updated by
        ///     <see cref="StartFrame()"/>. It is never negative and is zero before any frame has completed.
        /// </remarks>
        TimeSpan RealFrameTime { get; }

        /// <summary>
        ///     The average of <see cref="RealFrameTime"/> over a sliding window of the 50 most recent frames.
        /// </summary>
        /// <remarks>
        ///     Updated by <see cref="StartFrame()"/>. Intended for diagnostics such as debug overlays and profiling.
        ///     Until 50 frames have been recorded, the average may cover only the frames available so far,
        ///     or be zero right after start-up.
        /// </remarks>
        TimeSpan RealFrameTimeAvg { get; }

        /// <summary>
        ///     The standard deviation of the real frame time over the same 50-frame window used by
        ///     <see cref="RealFrameTimeAvg"/>, expressed as a <see cref="TimeSpan"/>.
        /// </summary>
        /// <remarks>
        ///     The deviation is computed around <see cref="RealFrameTimeAvg"/>. Updated by
        ///     <see cref="StartFrame()"/>. Intended for diagnostics only. It is never negative.
        /// </remarks>
        TimeSpan RealFrameTimeStdDev { get; }

        /// <summary>
        ///     Current graphics frame since init OpenGL which is taken as frame 1. Useful to set a conditional breakpoint on specific frames, and
        ///     synchronize with OGL debugging tools that capture frames. Depending on the tools used, this frame
        ///     number will vary between 1 frame more or less due to how that tool is counting frames,
        ///     i.e. starting from 0 or 1, having a separate counter, etc. Available in timing debug panel.
        /// </summary>
        uint CurFrame { get; set; }

        /// <summary>
        ///     The number of frames per second, averaged over the last 50 frames.
        /// </summary>
        /// <remarks>
        ///     This is one divided by <see cref="RealFrameTimeAvg"/> in seconds. The unit is frames per second.
        ///     Updated by <see cref="StartFrame()"/>. Intended for diagnostics only.
        ///     Before any frame samples exist the average frame time is zero, so the result may be infinite or
        ///     undefined; callers must not rely on it in that case.
        /// </remarks>
        double FramesPerSecondAvg { get; }

        /// <summary>
        ///     The simulation tick that is being processed. Outside of the simulation, it is the latest tick
        ///     that was processed.
        /// </summary>
        /// <remarks>
        ///     It normally increases by one for every simulated tick. The main loop, client state application
        ///     and prediction may also assign it, which can move it backwards or skip forwards.
        ///     The setter only changes the value that <see cref="CurTime"/> is derived from; it does not run any
        ///     tick logic and raises no events.
        /// </remarks>
        GameTick CurTick { get; set; }

        /// <summary>
        ///     Time, relative to <see cref="RealTime"/>, the last tick started at.
        ///     If we're currently in simulation, that's THIS tick.
        /// </summary>
        TimeSpan LastTick { get; set; }

        /// <summary>
        ///     The target number of simulation ticks per second.
        /// </summary>
        /// <remarks>
        ///     This is measured in simulation time and is independent of <see cref="TimeScale"/>.
        ///     Valid values are 1 to 65535; zero is invalid because <see cref="TickPeriod"/> is computed by dividing
        ///     by this value. Changing the rate changes <see cref="TickPeriod"/>.
        ///     On the server it is normally driven by a configuration variable and then replicated to clients.
        ///     See also <see cref="SetTickRateAt(ushort, GameTick)"/>.
        /// </remarks>
        ushort TickRate { get; set; }

        /// <summary>
        /// The scale of simulation time to real time.
        /// </summary>
        /// <remarks>
        /// A scale of 2 means the game should go "twice as slow"
        /// </remarks>
        float TimeScale { get; set; }

        /// <summary>
        /// The baseline time value that CurTime is calculated relatively to.
        /// </summary>
        (TimeSpan, GameTick) TimeBase { get; set; }

        /// <summary>
        ///     The duration of a single tick in simulation time: one second divided by <see cref="TickRate"/>.
        /// </summary>
        /// <remarks>
        ///     For example, 60 ticks per second gives a period of about 16.67 milliseconds.
        ///     The value follows <see cref="TickRate"/> and is always positive.
        ///     This is simulation time and not necessarily real time; for the real time to wait between ticks,
        ///     see <see cref="CalcAdjustedTickPeriod()"/>.
        /// </remarks>
        TimeSpan TickPeriod { get; }

        /// <summary>
        ///     The simulation time left over after the most recent batch of ticks that did not fill a whole tick;
        ///     in other words, how far the clock already is into the next tick that has not run yet.
        /// </summary>
        /// <remarks>
        ///     In normal operation it lies in the range from zero up to (but not including) <see cref="TickPeriod"/>.
        ///     The main loop sets it after running its ticks. It is used for interpolation: it feeds
        ///     <see cref="TickFraction"/> and, outside the simulation, <see cref="CurTime"/>.
        ///     The setter performs no validation.
        /// </remarks>
        TimeSpan TickRemainder { get; set; }

        /// <summary>
        /// <see cref="TickRemainder"/> in real time.
        /// </summary>
        TimeSpan TickRemainderRealtime { get; }

        /// <summary>
        /// Calculate the amount of <b>real time</b> to wait between ticks.
        /// </summary>
        /// <remarks>
        /// This is adjusted for various "out of simulation"
        /// factors such as <see cref="TickTimingAdjustment"/> and <see cref="TimeScale"/>.
        /// </remarks>
        TimeSpan CalcAdjustedTickPeriod();

        /// <summary>
        ///     Fraction of how far into the tick we are. <c>0</c> is 0% and <see cref="ushort.MaxValue"/> is 100%.
        /// </summary>
        ushort TickFraction
        {
            get
            {
                if (InSimulation)
                {
                    return ushort.MaxValue;
                }

                return (ushort)(ushort.MaxValue * TickRemainder.TotalSeconds / TickPeriod.TotalSeconds);
            }
        }

        /// <summary>
        ///     If the client clock is a little behind or ahead of the server, you can
        ///     use the to adjust the timing of the clock speed. The default value is 0,
        ///     and you can run the clock from -1 (almost stopped) to 1 (almost no delay).
        ///     This has no effect on in-simulation timing, and only changes the speed at which
        ///     the simulation progresses in relation to Real time. Don't mess with this unless
        ///     you know what you are doing. DO NOT TOUCH THIS ON SERVER.
        /// </summary>
        float TickTimingAdjustment { get; set; }

        /// <summary>
        ///     Marks the boundary between two frames. The main loop calls this once per frame.
        /// </summary>
        /// <remarks>
        ///     It updates the per-frame statistics: <see cref="RealFrameTime"/>, <see cref="FrameTime"/>,
        ///     <see cref="RealFrameTimeAvg"/>, <see cref="RealFrameTimeStdDev"/> and
        ///     <see cref="FramesPerSecondAvg"/>.
        ///     It must be called from the main thread. Calling it more than once per frame simply produces very
        ///     short frame times.
        /// </remarks>
        void StartFrame();

        /// <summary>
        ///     Is this the first time CurTick has been predicted?
        /// </summary>
        bool IsFirstTimePredicted { get; }

        /// <summary>
        ///     True if CurTick is ahead of LastRealTick, and <see cref="ApplyingState"/> is false.
        /// </summary>
        /// <remarks>
        ///     This means the client is currently running ahead of the server, to fill in the gaps for the player and
        ///     reduce latency while waiting for the next game state to arrive.
        /// </remarks>
        bool InPrediction { get; }

        /// <summary>
        ///     If true, the game is currently in the process of applying a game server-state.
        /// </summary>
        bool ApplyingState { get; }

        string TickStamp => $"{CurTick}, predFirst: {IsFirstTimePredicted}, tickRem: {TickRemainder.TotalSeconds}, sim: {InSimulation}";

        /// <summary>
        ///     Statically-accessible version of <see cref="TickStamp"/>.
        /// </summary>
        /// <remarks>
        ///     This is intended as a debugging aid, and should not be used in regular committed code.
        /// </remarks>
        static string TickStampStatic => IoCManager.Resolve<IGameTiming>().TickStamp;

        /// <summary>
        ///     Resets the simulation time completely. While functional, no mainstream RobustToolbox game currently uses
        ///     this outside of client synchronization with the server and it may have quirks on existing titles.
        /// </summary>
        /// <remarks>
        ///     To avoid potential desynchronization where some entities think they have changes from the far future,
        ///     this should be accompanied by a full ECS reset using <see cref="IEntityManager.FlushEntities"/>.
        /// </remarks>
        void ResetSimTime();
        /// <inheritdoc cref="ResetSimTime()"/>
        void ResetSimTime((TimeSpan, GameTick) timeBase);

        void SetTickRateAt(ushort tickRate, GameTick atTick);

        TimeSpan RealLocalToServer(TimeSpan local);
        TimeSpan RealServerToLocal(TimeSpan server);
    }
}
