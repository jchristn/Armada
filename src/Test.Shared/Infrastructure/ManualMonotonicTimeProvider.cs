namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading;

    /// <summary>
    /// A <see cref="TimeProvider"/> whose monotonic clock (<see cref="GetTimestamp"/>) moves only when a test calls
    /// <see cref="Advance"/>, so durations measured on it are exact and no test has to wait. Its wall clock is the real
    /// one. Thread safe.
    /// </summary>
    public sealed class ManualMonotonicTimeProvider : TimeProvider
    {
        #region Public-Members

        /// <summary>
        /// Timestamps count 100-nanosecond ticks.
        /// </summary>
        public override long TimestampFrequency
        {
            get { return TimeSpan.TicksPerSecond; }
        }

        #endregion

        #region Private-Members

        private long _Ticks = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate at timestamp zero.
        /// </summary>
        public ManualMonotonicTimeProvider()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Current monotonic timestamp.
        /// </summary>
        /// <returns>Ticks advanced so far.</returns>
        public override long GetTimestamp()
        {
            return Interlocked.Read(ref _Ticks);
        }

        /// <summary>
        /// Move the monotonic clock forward.
        /// </summary>
        /// <param name="by">How far.</param>
        public void Advance(TimeSpan by)
        {
            Interlocked.Add(ref _Ticks, by.Ticks);
        }

        /// <summary>
        /// Move the monotonic clock forward by whole milliseconds.
        /// </summary>
        /// <param name="ms">Milliseconds.</param>
        public void AdvanceMs(int ms)
        {
            Advance(TimeSpan.FromMilliseconds(ms));
        }

        #endregion
    }
}
