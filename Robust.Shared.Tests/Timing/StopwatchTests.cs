using System.Threading;
using NUnit.Framework;
using Robust.Shared.Timing;
using Stopwatch = Robust.Shared.Timing.Stopwatch;

namespace Robust.Shared.Tests.Timing;

/// <summary>
///     Tests for <see cref="Stopwatch"/> and its <see cref="IStopwatch"/> contract.
/// </summary>
/// <remarks>
///     These tests measure real time, so they only use lower bounds with a wide safety margin
///     (sleep 20 ms, expect at least 10 ms) and never assert an upper bound on elapsed time, which would be
///     flaky on a loaded CI machine.
/// </remarks>
[TestFixture, Parallelizable, TestOf(typeof(Stopwatch))]
internal sealed class StopwatchTests
{
    private static readonly TimeSpan SleepTime = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MinimumExpected = TimeSpan.FromMilliseconds(10);

    [Test]
    public void ImplementsIStopwatch()
    {
        Assert.That(new Stopwatch(), Is.InstanceOf<IStopwatch>());
    }

    [Test]
    public void Elapsed_BeforeStart_IsZero()
    {
        var stopwatch = new Stopwatch();

        Assert.That(stopwatch.Elapsed, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Elapsed_NeverStarted_StaysZeroAsTimePasses()
    {
        // Construction must not start the clock: even after real time has passed, nothing has been measured.
        var stopwatch = new Stopwatch();

        Thread.Sleep(SleepTime);

        Assert.That(stopwatch.Elapsed, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Elapsed_AfterStartAndWait_IsPositiveAndAtLeastTheWaitedTime()
    {
        var stopwatch = new Stopwatch();

        stopwatch.Start();
        Thread.Sleep(SleepTime);

        Assert.That(stopwatch.Elapsed, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(MinimumExpected));
    }

    [Test]
    public void Elapsed_WhileRunning_NeverDecreases()
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var previous = stopwatch.Elapsed;
        for (var i = 0; i < 1000; i++)
        {
            var current = stopwatch.Elapsed;
            Assert.That(current, Is.GreaterThanOrEqualTo(previous));
            Assert.That(current, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
            previous = current;
        }
    }

    [Test]
    public void Elapsed_ReadingItDoesNotStopTheTimer()
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        Thread.Sleep(SleepTime);

        var first = stopwatch.Elapsed;
        Thread.Sleep(SleepTime);
        var second = stopwatch.Elapsed;

        // The timer kept running between the two reads, so a further ~20 ms has been accumulated.
        Assert.That(second - first, Is.GreaterThanOrEqualTo(MinimumExpected));
    }

    [Test]
    public void Start_WhileAlreadyRunning_DoesNotResetElapsedTime()
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        Thread.Sleep(SleepTime);
        var beforeSecondStart = stopwatch.Elapsed;
        Assert.That(beforeSecondStart, Is.GreaterThanOrEqualTo(MinimumExpected));

        stopwatch.Start();

        // A reset would bring Elapsed back to roughly zero, below the value observed before the second Start().
        Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(beforeSecondStart));
    }

    [Test]
    public void Restart_AfterRunning_ResetsElapsedTimeAndKeepsRunning()
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        Thread.Sleep(TimeSpan.FromMilliseconds(100));
        var beforeRestart = stopwatch.Elapsed;
        Assert.That(beforeRestart, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(50)));

        stopwatch.Restart();
        var justAfterRestart = stopwatch.Elapsed;

        // The ~100 ms accumulated before the restart must have been discarded.
        Assert.That(justAfterRestart, Is.LessThan(beforeRestart));

        // The timer is running again: it keeps measuring after the restart.
        Thread.Sleep(SleepTime);
        Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(MinimumExpected));
    }

    [Test]
    public void Restart_OnStopwatchThatWasNeverStarted_StartsIt()
    {
        var stopwatch = new Stopwatch();

        stopwatch.Restart();
        Thread.Sleep(SleepTime);

        Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(MinimumExpected));
    }

    [Test]
    public void IStopwatch_Usage_BehavesLikeTheConcreteType()
    {
        // Consumers such as GameTiming and JobQueue only know the interface, so exercise the whole contract
        // through it: zero before start, positive after start, reset and still running after Restart().
        IStopwatch stopwatch = new Stopwatch();
        Assert.That(stopwatch.Elapsed, Is.EqualTo(TimeSpan.Zero));

        stopwatch.Start();
        Thread.Sleep(SleepTime);
        Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(MinimumExpected));

        stopwatch.Restart();
        Thread.Sleep(SleepTime);
        Assert.That(stopwatch.Elapsed, Is.GreaterThanOrEqualTo(MinimumExpected));
    }

    [Test]
    public void Instances_AreIndependent()
    {
        // Each Stopwatch owns its own timer: starting one must not affect another.
        var running = new Stopwatch();
        var idle = new Stopwatch();

        running.Start();
        Thread.Sleep(SleepTime);

        Assert.That(running.Elapsed, Is.GreaterThanOrEqualTo(MinimumExpected));
        Assert.That(idle.Elapsed, Is.EqualTo(TimeSpan.Zero));
    }
}
