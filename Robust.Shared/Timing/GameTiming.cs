using System;
using System.Linq;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Robust.Shared.Timing
{
    /// <summary>
    ///     Default implementation of the game clock: tick counting, frame timing and related helpers.
    /// </summary>
    [Virtual]
    public class GameTiming : IGameTiming
    {
        // How many recent frames are kept for the timing statistics.
        private const int NumFrames = 60;

        private readonly IStopwatch _realTimer = new Stopwatch();
        private readonly long[] _realFrameTimes = new long[NumFrames];
        private int _frameIdx;
        private TimeSpan _lastRealTime;

        /// <summary>
        ///     Creates a timing object that starts paused, with a tick rate of 60 and the real-time
        ///     stopwatch already running.
        /// </summary>
        public GameTiming()
        {
            _realTimer.Start();
            Paused = true;

            // Goes through the property so the rate is stored via SetTickRateAt.
            // The stored rate is still 0 here, so no time base caching happens.
            TickRate = NumFrames;
        }

        /// <summary>
        ///     True while execution is inside the simulation update, false while rendering.
        /// </summary>
        public bool InSimulation { get; set; }

        /// <summary>
        ///     Whether the simulation is currently paused.
        /// </summary>
        public bool Paused { get; set; }

        // Cached values for making CurTime not jump whenever the tickrate
        // changes. This holds the last calculated value of CurTime, and the
        // tick that the value was calculated for. The next time CurTime is
        // calculated, it should only try to monotonically increase both of
        // these values
        //
        // Notice that it starts from GameTick 1  - the "first tick" has no impact
        // on timing
        /// <summary>
        ///     Anchor of the simulated clock: the clock value (first item) that was valid at the
        ///     given tick (second item). The whole tuple is replaced whenever the tick rate changes
        ///     or the simulation is reset.
        /// </summary>
        public (TimeSpan, GameTick) TimeBase { get; set; } = (TimeSpan.Zero, GameTick.First);

        /// <summary>
        ///     Current synchronized uptime of the simulation, used for in-game timing.
        ///     This value may be rewound (for example by client-side prediction).
        /// </summary>
        public TimeSpan CurTime
        {
            get
            {
                var (baseTime, baseTick) = TimeBase;

                // Not asserted to be non-negative: prediction can leave CurTick behind the anchor.
                var time = baseTime + TickPeriod.Mul(CurTick.Value - baseTick.Value);

                if (!InSimulation)
                {
                    // Rendering between ticks: include the partial tick that has elapsed.
                    var withRemainder = time + TickRemainder;
                    DebugTools.Assert(withRemainder.TotalSeconds >= 0);
                    return withRemainder;
                }

                DebugTools.Assert(time.TotalSeconds >= 0);
                return time;
            }
        }

        /// <summary>
        ///     Real (wall clock) uptime, for UI and other timing outside the game simulation.
        /// </summary>
        public TimeSpan RealTime => _realTimer.Elapsed;

        public TimeSpan FrameStartTime => _lastRealTime;

        public virtual TimeSpan ServerTime => TimeSpan.Zero;

        /// <summary>
        ///     Simulated time that the last frame took.
        /// </summary>
        public TimeSpan FrameTime => CalcFrameTime();

        /// <summary>
        ///     Real time that the last frame took. Zero until the first call to <see cref="StartFrame"/>.
        /// </summary>
        public TimeSpan RealFrameTime { get; private set; }

        /// <summary>
        ///     Average real frame time over the recent frame window (the last <c>NumFrames</c> samples).
        ///     Slots that have not been written yet count as zero.
        /// </summary>
        public TimeSpan RealFrameTimeAvg => TimeSpan.FromTicks((long)_realFrameTimes.Average());

        /// <summary>
        ///     Standard deviation of the real frame time over the recent frame window.
        /// </summary>
        public TimeSpan RealFrameTimeStdDev => CalcRftStdDev();

        /// <summary>
        ///     Average real frames per second over the recent frame window.
        /// </summary>
        public double FramesPerSecondAvg => CalcFpsAvg();

        /// <summary>
        ///     The simulation tick that is currently being processed.
        ///     Time always starts on the first tick.
        /// </summary>
        public GameTick CurTick { get; set; } = new(1);

        /// <summary>
        ///     Timespan of the last tick.
        /// </summary>
        public TimeSpan LastTick { get; set; }

        private ushort _tickRate;
        private TimeSpan _tickRemainder;

        /// <summary>
        ///     Target number of simulation ticks per second.
        /// </summary>
        public ushort TickRate
        {
            get => _tickRate;
            // Delegates so the clock is cached with the old rate before the new one is stored.
            set => SetTickRateAt(value, CurTick);
        }

        public float TimeScale { get; set; } = 1;

        /// <summary>
        ///     Length of one tick at the current rate, i.e. 1 / <see cref="TickRate"/>.
        /// </summary>
        public TimeSpan TickPeriod => TimeSpan.FromTicks((long)(1.0 / TickRate * TimeSpan.TicksPerSecond));

        /// <summary>
        ///     Time left over after the last tick ran.
        /// </summary>
        public TimeSpan TickRemainder
        {
            get => _tickRemainder;
            set
            {
                // Checks the previously stored value, not the incoming one.
                DebugTools.Assert(TickRemainder >= TimeSpan.Zero);
                _tickRemainder = value;
            }
        }

        public TimeSpan TickRemainderRealtime => TickRemainder * TimeScale;

        public TimeSpan CalcAdjustedTickPeriod()
        {
            // ranges from -1 to 1, with 0 being 'default'
            var ratio = MathHelper.Clamp(TickTimingAdjustment, -0.99f, 0.99f);

            // Final period ranges from near 0 (runs very fast to catch up) or 2 * tick period (runs at half speed).
            return TickPeriod * (1-ratio) * TimeScale;
        }

        /// <summary>
        ///     The graphics frame counter. It starts at 1 when graphics are initialized and counts
        ///     from one buffer swap to the next. Useful for conditional breakpoints and for matching
        ///     frames in GPU debugging tools (some of those count from 0, others from 1).
        ///     This class never increments it.
        /// </summary>
        public uint CurFrame { get; set; } = 1;

        /// <inheritdoc />
        public float TickTimingAdjustment { get; set; } = 0;

        /// <summary>
        ///     Ends the stopwatch "lap" and updates the frame time information.
        /// </summary>
        public void StartFrame()
        {
            var now = RealTime;
            RealFrameTime = now - _lastRealTime;
            _lastRealTime = now;

            // Ring buffer: the first sample lands in slot 1 and overwrites the oldest entry once full.
            _frameIdx = (_frameIdx + 1) % _realFrameTimes.Length;
            _realFrameTimes[_frameIdx] = RealFrameTime.Ticks;
        }

        private TimeSpan CalcFrameTime()
        {
            if (InSimulation)
            {
                // Inside the simulation every frame is exactly one tick long.
                return TimeSpan.FromTicks(TickPeriod.Ticks);
            }

            // Rendering: nothing elapses while paused, otherwise use the unscaled real frame time.
            return Paused ? TimeSpan.Zero : RealFrameTime;
        }

        // Calculate and store the current time value, based on the current tick rate.
        // Call this whenever you change the TickRate.
        private void CacheCurTime(GameTick atTick)
        {
            var (cachedTime, cachedTick) = TimeBase;

            // TickPeriod still reflects the rate that applied up to atTick.
            var newTime = cachedTime + TickPeriod.Mul(atTick.Value - cachedTick.Value);
            DebugTools.Assert(newTime >= TimeSpan.Zero);

            TimeBase = (newTime, atTick);
        }

        /// <summary>
        ///     Resets the simulation time.
        /// </summary>
        public void ResetSimTime()
        {
            ResetSimTime((TimeSpan.Zero, GameTick.First));
        }

        public void ResetSimTime((TimeSpan, GameTick) timeBase)
        {
            TimeBase = timeBase;
            CurTick = GameTick.First;
            TickRemainder = TimeSpan.Zero;
            Paused = true;
        }

        public void SetTickRateAt(ushort tickRate, GameTick atTick)
        {
            // Check this, because TickRate is a divisor in the cache calculation
            // The first time TickRate is set, no time will have passed anyways
            if (_tickRate != 0)
                // Cache BEFORE updating the tick rate, because ticks up until
                // now have been on a different rate, so they count for a
                // different amount of time
                CacheCurTime(atTick);

            _tickRate = tickRate;
        }

        public virtual TimeSpan RealLocalToServer(TimeSpan local)
        {
            return TimeSpan.Zero;
        }

        public virtual TimeSpan RealServerToLocal(TimeSpan server)
        {
            return TimeSpan.Zero;
        }

        protected virtual TimeSpan? GetServerOffset()
        {
            return null;
        }

        public bool IsFirstTimePredicted { get; protected set; } = true;

        /// <inheritdoc />
        public virtual bool InPrediction => false;

        /// <inheritdoc />
        public bool ApplyingState {get; protected set; }

        /// <summary>
        ///     Average frames per second over the recent frame window.
        /// </summary>
        private double CalcFpsAvg()
        {
            // Mean frame time in ticks (not truncated), converted to seconds, then inverted.
            var meanTicks = _realFrameTimes.Average();
            return 1 / (meanTicks / TimeSpan.TicksPerSecond);
        }

        /// <summary>
        ///     Standard deviation of the recent real frame times.
        /// </summary>
        private TimeSpan CalcRftStdDev()
        {
            var count = _realFrameTimes.Length;
            var sum = _realFrameTimes.Sum();
            var avg = (double)sum / count;

            // Unwritten (zero) slots add nothing to the deviation, but they still
            // count in the mean and in the divisor below.
            double devSquared = 0;
            for (var i = 0; i < count; i++)
            {
                var slot = _realFrameTimes[i];
                if (slot == 0)
                    continue;

                var d = slot - avg;
                devSquared += d * d;
            }

            var variance = devSquared / (count - 1);
            return TimeSpan.FromTicks((long)Math.Sqrt(variance));
        }

        internal static bool IsTimescaleValid(float scale)
        {
            return scale > 0 && float.IsNormal(scale) && float.IsFinite(scale);
        }
    }
}
