namespace Test.Shared.Infrastructure
{
    using System;
    using Armada.Tui.Services;

    /// <summary>
    /// A clock tests advance by hand.
    /// </summary>
    public sealed class ManualClock : IClock
    {
        /// <inheritdoc />
        public DateTime UtcNow { get; private set; } = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Advance time.
        /// </summary>
        /// <param name="by">Amount.</param>
        public void Advance(TimeSpan by)
        {
            UtcNow = UtcNow.Add(by);
        }
    }
}
