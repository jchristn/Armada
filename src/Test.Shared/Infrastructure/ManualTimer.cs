namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A one-shot timer of a <see cref="ManualTimerTimeProvider"/>: it fires when the provider's clock is advanced past
    /// its due time. Periods are not supported (a period other than infinite is ignored).
    /// </summary>
    public sealed class ManualTimer : ITimer
    {
        #region Public-Members

        /// <summary>
        /// Whether the timer is waiting to fire.
        /// </summary>
        public bool IsArmed { get; private set; } = false;

        /// <summary>
        /// The provider timestamp at which it fires.
        /// </summary>
        public long DueTicks { get; private set; } = 0;

        #endregion

        #region Private-Members

        private readonly ManualTimerTimeProvider _Provider;
        private readonly TimerCallback _Callback;
        private readonly object? _State;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider whose clock drives it.</param>
        /// <param name="callback">Callback.</param>
        /// <param name="state">Callback state.</param>
        public ManualTimer(ManualTimerTimeProvider provider, TimerCallback callback, object? state)
        {
            _Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _Callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _State = state;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                IsArmed = false;
                return true;
            }

            DueTicks = _Provider.GetTimestamp() + (dueTime < TimeSpan.Zero ? 0 : dueTime.Ticks);
            IsArmed = true;
            return true;
        }

        /// <summary>
        /// Fire now if armed.
        /// </summary>
        public void Fire()
        {
            if (!IsArmed) return;
            IsArmed = false;
            _Callback(_State);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            IsArmed = false;
            _Provider.Remove(this);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        #endregion
    }
}
