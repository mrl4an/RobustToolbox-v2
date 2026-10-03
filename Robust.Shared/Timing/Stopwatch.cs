using System;

namespace Robust.Shared.Timing;

/// <summary>
///     Default <see cref="IStopwatch"/> implementation, backed by a single
///     <see cref="System.Diagnostics.Stopwatch"/>.
/// </summary>
/// <remarks>
///     Newly constructed instances are not running; call <see cref="Start"/> or <see cref="Restart"/> first.
///     This type does not synchronize access, so it must not be mutated from several threads at once.
///     For new code, prefer <see cref="RStopwatch"/>.
/// </remarks>
public sealed class Stopwatch : IStopwatch
{
    // Fully qualified so it cannot be confused with this class.
    private readonly System.Diagnostics.Stopwatch _inner = new();

    /// <inheritdoc />
    public TimeSpan Elapsed => _inner.Elapsed;

    /// <inheritdoc />
    public void Start()
    {
        _inner.Start();
    }

    /// <inheritdoc />
    public void Restart()
    {
        _inner.Restart();
    }
}
