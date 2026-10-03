using System.Collections.Generic;
using System.Threading;
using Robust.Shared.Exceptions;
using Robust.Shared.IoC;

namespace Robust.Shared.Timing
{
    internal sealed class TimerManager : ITimerManager
    {
        [Dependency] private readonly IRuntimeLog _runtimeLog = default!;

        private readonly List<(Timer, CancellationToken)> _timers
            = new();

        /// <summary>
        /// Queues a timer to be driven by <see cref="UpdateTimers"/>. No validation or de-duplication is done,
        /// the timer is not updated immediately, and this method is not thread-safe.
        /// </summary>
        /// <param name="timer">The timer to run.</param>
        /// <param name="cancellationToken">Cancelling this token stops and discards the timer.</param>
        public void AddTimer(Timer timer, CancellationToken cancellationToken = default)
        {
            _timers.Add((timer, cancellationToken));
        }

        /// <summary>
        /// Advances every queued timer by the frame time and then discards finished or cancelled ones.
        /// </summary>
        /// <param name="frameEventArgs">Frame information supplying the elapsed time.</param>
        public void UpdateTimers(FrameEventArgs frameEventArgs)
        {
            // Index-based loop with the count re-read each pass, so timers added by a
            // callback during this update are also processed in the same pass.
            for (var i = 0; i < _timers.Count; i++)
            {
                var (timer, token) = _timers[i];

                if (token.IsCancellationRequested)
                {
                    continue;
                }

                timer.Update(frameEventArgs.DeltaSeconds, _runtimeLog);
            }

            // Cleanup also catches timers that were cancelled while the pass was running.
            _timers.RemoveAll(entry => !entry.Item1.IsActive || entry.Item2.IsCancellationRequested);
        }
    }
}
