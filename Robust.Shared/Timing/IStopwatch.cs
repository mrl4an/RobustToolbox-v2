using System;

namespace Robust.Shared.Timing;

/// <summary>
///     A minimal abstraction over a device that measures elapsed time.
/// </summary>
/// <remarks>
///     <para>
///         This is a legacy interface with very little practical use. It only exists so that code consuming a
///         stopwatch can be handed a substitute (such as a fake or mock) in tests.
///     </para>
///     <para>
///         For new code, prefer <see cref="RStopwatch"/>. The standard implementation of this interface is
///         <see cref="Stopwatch"/>.
///     </para>
/// </remarks>
public interface IStopwatch
{
    /// <summary>
    ///     The total time measured so far, accumulated over every period in which the timer was running.
    /// </summary>
    /// <remarks>
    ///     This is <see cref="TimeSpan.Zero"/> for a timer that has never been started. Reading it neither stops
    ///     nor otherwise affects the timer.
    /// </remarks>
    TimeSpan Elapsed { get; }

    /// <summary>
    ///     Discards any accumulated time and begins measuring again from zero.
    ///     The timer is running once this returns.
    /// </summary>
    void Restart();

    /// <summary>
    ///     Begins measuring time, or continues measuring if the timer was already started before.
    /// </summary>
    /// <remarks>
    ///     Accumulated time is kept, and calling this on a timer that is already running does nothing.
    ///     Use <see cref="Restart"/> to begin again from zero.
    /// </remarks>
    void Start();
}
