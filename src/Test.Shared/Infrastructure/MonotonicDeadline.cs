namespace Test.Shared.Infrastructure
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// A test wait deadline measured on the monotonic clock. Wall-clock deadlines (DateTime.UtcNow) expire early when the
    /// clock jumps, for example when a laptop sleeps during a run: the wall clock advances while the process is
    /// suspended, so a wait that has really lasted two seconds reports a timeout. The monotonic clock does not advance
    /// during sleep. Thread safe for reads.
    /// </summary>
    public sealed class MonotonicDeadline
    {
        #region Public-Members

        /// <summary>
        /// True once the timeout has elapsed.
        /// </summary>
        public bool Passed
        {
            get { return _Stopwatch.Elapsed >= _Timeout; }
        }

        /// <summary>
        /// Time left before the deadline; zero once it has passed.
        /// </summary>
        public TimeSpan Remaining
        {
            get
            {
                TimeSpan left = _Timeout - _Stopwatch.Elapsed;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        #endregion

        #region Private-Members

        private readonly Stopwatch _Stopwatch;
        private readonly TimeSpan _Timeout;

        #endregion

        #region Constructors-and-Factories

        private MonotonicDeadline(TimeSpan timeout)
        {
            _Timeout = timeout;
            _Stopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// Start a deadline that passes after <paramref name="timeout"/> of monotonic time.
        /// </summary>
        /// <param name="timeout">Timeout; negative values are treated as zero.</param>
        /// <returns>The deadline.</returns>
        public static MonotonicDeadline After(TimeSpan timeout)
        {
            return new MonotonicDeadline(timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout);
        }

        #endregion
    }
}
