namespace Robust.Shared.Timing;

/// <summary>
///     Payload of a game-loop frame event (input, tick, update and render): how much time has passed since the same
///     stage last ran.
/// </summary>
/// <remarks>
///     <para>
///         This is a small immutable value type. It is passed by value to frame-update and update methods and is
///         the argument type of the game loop's <c>EventHandler&lt;FrameEventArgs&gt;</c> events, so it must stay a
///         readonly struct with a single <see cref="float"/> constructor. It deliberately does not derive from
///         <see cref="System.EventArgs"/>. Being immutable, it can be copied and read from any thread.
///     </para>
///     <para>
///         Typical producers are the main loop (real measured frame time for the input and render stages, and
///         simulation time for each tick), the loading screen, and the test harness.
///     </para>
///     <para>
///         For long-running accumulators, summing <see cref="DeltaSeconds"/> loses precision over time. Prefer
///         <see cref="System.TimeSpan"/> values together with the current-time property of the game timing service.
///     </para>
/// </remarks>
public readonly struct FrameEventArgs
{
    /// <summary>
    ///     Elapsed time in seconds (not milliseconds or ticks) since the corresponding stage last fired.
    /// </summary>
    /// <remarks>
    ///     The value is fixed at construction. The struct performs no validation or clamping: zero, negative, NaN,
    ///     infinite and very large values are stored as given, so consumers that cannot tolerate such values must
    ///     guard against them. Lag limiting is the game loop's responsibility, not this type's. The default value
    ///     of the struct has a delta of zero.
    /// </remarks>
    public float DeltaSeconds { get; }

    /// <summary>
    ///     Creates frame event data for the given elapsed time.
    /// </summary>
    /// <param name="deltaSeconds">Elapsed time in seconds; stored unchanged and never validated.</param>
    public FrameEventArgs(float deltaSeconds)
    {
        DeltaSeconds = deltaSeconds;
    }
}
