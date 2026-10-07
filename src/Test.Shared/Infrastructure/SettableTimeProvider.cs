namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// A <see cref="TimeProvider"/> whose wall clock only moves when a test advances it (timers still use the system
    /// clock).
    /// </summary>
    public sealed class SettableTimeProvider : TimeProvider
    {
        #region Private-Members

        private readonly object _Lock = new object();
        private DateTimeOffset _Now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The current fixed time.
        /// </summary>
        /// <returns>The time.</returns>
        public override DateTimeOffset GetUtcNow()
        {
            lock (_Lock) return _Now;
        }

        /// <summary>
        /// Move the clock forward.
        /// </summary>
        /// <param name="by">Amount.</param>
        public void Advance(TimeSpan by)
        {
            lock (_Lock) _Now = _Now.Add(by);
        }

        #endregion
    }
}
