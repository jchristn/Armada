namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// A <see cref="TimeProvider"/> whose monotonic clock and timers move only when a test calls <see cref="Advance"/>:
    /// timers created with <see cref="CreateTimer"/> fire, on the calling thread, when the clock passes their due time.
    /// Its wall clock is the real one. Thread safe.
    /// </summary>
    public sealed class ManualTimerTimeProvider : TimeProvider
    {
        #region Public-Members

        /// <summary>
        /// Timestamps count 100-nanosecond ticks.
        /// </summary>
        public override long TimestampFrequency
        {
            get { return TimeSpan.TicksPerSecond; }
        }

        /// <summary>
        /// How many timers are waiting to fire.
        /// </summary>
        public int PendingTimers
        {
            get { lock (_Lock) return _Timers.FindAll(t => t.IsArmed).Count; }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly List<ManualTimer> _Timers = new List<ManualTimer>();
        private long _Ticks = 0;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override long GetTimestamp()
        {
            return Interlocked.Read(ref _Ticks);
        }

        /// <inheritdoc />
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            ManualTimer timer = new ManualTimer(this, callback, state);
            lock (_Lock) _Timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        /// <summary>
        /// Move the clock forward and fire every timer whose due time it passes.
        /// </summary>
        /// <param name="by">How far.</param>
        public void Advance(TimeSpan by)
        {
            long now = Interlocked.Add(ref _Ticks, by.Ticks);
            List<ManualTimer> due = new List<ManualTimer>();
            lock (_Lock)
            {
                foreach (ManualTimer timer in _Timers)
                {
                    if (timer.IsArmed && timer.DueTicks <= now) due.Add(timer);
                }
            }

            foreach (ManualTimer timer in due) timer.Fire();
        }

        /// <summary>
        /// Forget a disposed timer.
        /// </summary>
        /// <param name="timer">Timer.</param>
        internal void Remove(ManualTimer timer)
        {
            lock (_Lock) _Timers.Remove(timer);
        }

        #endregion
    }
}
