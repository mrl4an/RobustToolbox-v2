using System;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.Exceptions;
using Robust.Shared.IoC;

namespace Robust.Shared.Timing
{
    /// <summary>
    ///     Non-serializable, but async friendly timers.
    /// </summary>
    /// <remarks>
    ///     Using these in Space Station 14 is discouraged, it has its own idioms that are all serialization friendly.
    /// </remarks>
    /// <seealso cref="ITimerManager"/>
    public sealed class Timer
    {
        /// <summary>
        ///     Milliseconds left until the next firing. Can drop to zero or below, in which case the
        ///     negative part is overshoot that carries over into the next period of a repeating timer.
        /// </summary>
        private float _timeCounter;

        /// <summary>
        ///     Interval in milliseconds between firings (the one-shot delay, or the repeat period).
        /// </summary>
        public int Time { get; }

        /// <summary>
        ///     Whether the timer re-arms itself after every firing instead of firing only once.
        /// </summary>
        public bool IsRepeating { get; }

        /// <summary>
        ///     Whether the timer is still running. Only <see cref="Update"/> may change it, and only
        ///     from true to false, which happens once a non-repeating timer has fired.
        /// </summary>
        public bool IsActive { get; private set; } = true;

        /// <summary>
        ///     The callback invoked each time the timer fires.
        /// </summary>
        public Action OnFired { get; }

        /// <summary>
        ///     Creates a timer. Nothing is scheduled here; the timer manager drives it through <see cref="Update"/>.
        /// </summary>
        /// <param name="milliseconds">The interval in milliseconds, stored without validation.</param>
        /// <param name="isRepeating">True if the timer should keep firing every interval.</param>
        /// <param name="onFired">The callback to invoke when the timer fires.</param>
        public Timer(int milliseconds, bool isRepeating, Action onFired)
        {
            Time = milliseconds;
            IsRepeating = isRepeating;
            OnFired = onFired;
            _timeCounter = milliseconds;
        }

        // Parameter is used only on release.
        // ReSharper disable once UnusedParameter.Global
        /// <summary>
        ///     Advances the countdown and fires the callback when it runs out. Fires at most once per call.
        /// </summary>
        /// <param name="frameTime">Elapsed time since the previous update, in seconds.</param>
        /// <param name="runtimeLog">Receives exceptions thrown by the callback when EXCEPTION_TOLERANCE is defined.</param>
        public void Update(float frameTime, IRuntimeLog runtimeLog)
        {
            if (!IsActive)
            {
                return;
            }

            // The countdown is in milliseconds, the frame time in seconds.
            _timeCounter -= frameTime * 1000;

            if (_timeCounter > 0)
            {
                return;
            }

#if EXCEPTION_TOLERANCE
            try
            {
                OnFired();
            }
            catch (Exception exception)
            {
                runtimeLog.LogException(exception, "Timer Callback");
            }
#else
            OnFired();
#endif

            if (IsRepeating)
            {
                // Keep the overshoot so the long-run rate matches the interval.
                _timeCounter += Time;
            }
            else
            {
                IsActive = false;
            }
        }

        /// <summary>
        ///     Creates a task that will complete after a given delay.
        ///     The task is resumed on the main game logic thread.
        /// </summary>
        /// <param name="milliseconds">The length of time, in milliseconds, to delay for.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The task that can be awaited.</returns>
        public static Task Delay(int milliseconds, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<object?>();
            Spawn(milliseconds, () => tcs.SetResult(null), cancellationToken);
            return tcs.Task;
        }

        /// <summary>
        ///     Creates a task that will complete after a given delay.
        ///     The task is resumed on the main game logic thread.
        /// </summary>
        /// <param name="duration">The length of time to delay for.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The task that can be awaited.</returns>
        public static Task Delay(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            return Delay((int)duration.TotalMilliseconds, cancellationToken);
        }

        /// <summary>
        ///     Schedule an action to be fired after a certain delay.
        ///     The action will be resumed on the main game logic thread.
        /// </summary>
        /// <param name="milliseconds">The length of time, in milliseconds, to wait before firing the action.</param>
        /// <param name="onFired">The action to fire.</param>
        /// <param name="cancellationToken"></param>
        public static void Spawn(int milliseconds, Action onFired, CancellationToken cancellationToken = default)
        {
            var timer = new Timer(milliseconds, false, onFired);
            IoCManager.Resolve<ITimerManager>().AddTimer(timer, cancellationToken);
        }

        /// <summary>
        ///     Schedule an action to be fired after a certain delay.
        ///     The action will be resumed on the main game logic thread.
        /// </summary>
        /// <param name="duration">The length of time, to wait before firing the action.</param>
        /// <param name="onFired">The action to fire.</param>
        /// <param name="cancellationToken"></param>
        public static void Spawn(TimeSpan duration, Action onFired, CancellationToken cancellationToken = default)
        {
            Spawn((int)duration.TotalMilliseconds, onFired, cancellationToken);
        }

        /// <summary>
        ///     Schedule an action that repeatedly fires after a delay specified in milliseconds.
        /// </summary>
        /// <param name="milliseconds">The length of time, in milliseconds, to delay before firing the repeated action.</param>
        /// <param name="onFired">The action to fire.</param>
        /// <param name="cancellationToken">The CancellationToken for stopping the Timer.</param>
        public static void SpawnRepeating(int milliseconds, Action onFired, CancellationToken cancellationToken)
        {
            var timer = new Timer(milliseconds, true, onFired);
            IoCManager.Resolve<ITimerManager>().AddTimer(timer, cancellationToken);
        }

        /// <summary>
        ///     Schedule an action that repeatedly fires after a delay.
        /// </summary>
        /// <param name="duration">The length of time to delay before firing the repeated action.</param>
        /// <param name="onFired">The action to fire.</param>
        /// <param name="cancellationToken">The CancellationToken for stopping the Timer.</param>
        public static void SpawnRepeating(TimeSpan duration, Action onFired, CancellationToken cancellationToken)
        {
            SpawnRepeating((int)duration.TotalMilliseconds, onFired, cancellationToken);
        }
    }
}
