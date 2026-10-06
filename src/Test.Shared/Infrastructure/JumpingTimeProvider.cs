namespace Test.Shared.Infrastructure
{
    using System;
    using System.Diagnostics;
    using System.Threading;

    /// <summary>
    /// A <see cref="TimeProvider"/> for clock-jump regression tests. Its wall clock (<see cref="GetUtcNow"/>) jumps
    /// forward by <see cref="WallJump"/> on every reading, the way the wall clock leaps when a laptop sleeps and wakes or
    /// NTP steps the clock, so any deadline or interval computed from it is always already past. Its monotonic clock
    /// (<see cref="GetTimestamp"/>) follows the real monotonic clock plus whatever <see cref="Advance"/> added, so code
    /// that measures durations on the monotonic clock behaves normally and tests can move it forward without waiting.
    /// Thread safe.
    /// </summary>
    public sealed class JumpingTimeProvider : TimeProvider
    {
        #region Public-Members

        /// <summary>
        /// How far the wall clock jumps forward on each reading. Default 10 minutes.
        /// </summary>
        public TimeSpan WallJump { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Number of wall-clock readings so far.
        /// </summary>
        public long WallReadings
        {
            get { return Interlocked.Read(ref _WallReadings); }
        }

        /// <summary>
        /// Number of monotonic-clock readings so far (a wait loop reads it once per iteration, so tests can wait for a
        /// loop to have gone around without sleeping).
        /// </summary>
        public long TimestampReadings
        {
            get { return Interlocked.Read(ref _TimestampReadings); }
        }

        #endregion

        #region Private-Members

        private readonly DateTimeOffset _Origin = DateTimeOffset.UtcNow;
        private long _WallReadings = 0;
        private long _AdvancedTicks = 0;
        private long _TimestampReadings = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public JumpingTimeProvider()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Current wall-clock time; every reading is <see cref="WallJump"/> later than the previous one.
        /// </summary>
        /// <returns>The jumped wall-clock time.</returns>
        public override DateTimeOffset GetUtcNow()
        {
            long reading = Interlocked.Increment(ref _WallReadings);
            return _Origin + TimeSpan.FromTicks(WallJump.Ticks * reading);
        }

        /// <summary>
        /// Current monotonic timestamp: the real monotonic clock plus everything added with <see cref="Advance"/>.
        /// </summary>
        /// <returns>The timestamp, in <see cref="Stopwatch.Frequency"/> units.</returns>
        public override long GetTimestamp()
        {
            Interlocked.Increment(ref _TimestampReadings);
            return Stopwatch.GetTimestamp() + Interlocked.Read(ref _AdvancedTicks);
        }

        /// <summary>
        /// Move the monotonic clock forward without waiting.
        /// </summary>
        /// <param name="by">Amount to advance; negative values are ignored.</param>
        public void Advance(TimeSpan by)
        {
            if (by <= TimeSpan.Zero) return;
            long ticks = (long)(by.TotalSeconds * Stopwatch.Frequency);
            Interlocked.Add(ref _AdvancedTicks, ticks);
        }

        #endregion
    }
}
