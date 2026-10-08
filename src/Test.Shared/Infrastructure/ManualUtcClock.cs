namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// A UTC clock a test moves by hand, for services that take a <c>Func&lt;DateTime&gt;</c> clock.
    /// </summary>
    public sealed class ManualUtcClock
    {
        #region Private-Members

        private DateTime _Now;
        private readonly object _Lock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate at a start time.
        /// </summary>
        /// <param name="startUtc">The start time (UTC).</param>
        public ManualUtcClock(DateTime startUtc)
        {
            _Now = startUtc;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The current time; pass this method as the clock.
        /// </summary>
        /// <returns>The current time (UTC).</returns>
        public DateTime Now()
        {
            lock (_Lock) return _Now;
        }

        /// <summary>
        /// Move the clock forward.
        /// </summary>
        /// <param name="by">How far.</param>
        public void Advance(TimeSpan by)
        {
            lock (_Lock) _Now = _Now.Add(by);
        }

        #endregion
    }
}
