namespace Armada.Core.Services
{
    using System;
    using System.Threading;

    /// <summary>
    /// Passes on at most one value per interval, newest wins: the first value goes out at once, values that arrive within
    /// the interval after a send replace each other, and the newest of them goes out when the interval ends. Measured on
    /// the monotonic clock of a <see cref="TimeProvider"/>. Thread-safe.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public sealed class LatestValueThrottle<T> : IDisposable where T : class
    {
        #region Private-Members

        private readonly TimeSpan _Interval;
        private readonly TimeProvider _Time;
        private readonly Action<T> _Send;
        private readonly object _Lock = new object();
        private long _LastSent = 0;
        private bool _HasSent = false;
        private T? _Pending = null;
        private ITimer? _Timer = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="interval">Shortest time between two sends; zero or negative sends every value.</param>
        /// <param name="time">Time source.</param>
        /// <param name="send">Receives each value passed on. Called outside the throttle's lock; exceptions are swallowed.</param>
        public LatestValueThrottle(TimeSpan interval, TimeProvider time, Action<T> send)
        {
            _Interval = interval < TimeSpan.Zero ? TimeSpan.Zero : interval;
            _Time = time ?? throw new ArgumentNullException(nameof(time));
            _Send = send ?? throw new ArgumentNullException(nameof(send));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Offer a value.
        /// </summary>
        /// <param name="value">Value.</param>
        public void Submit(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            T? sendNow = null;
            lock (_Lock)
            {
                if (_Disposed) return;
                TimeSpan sinceLast = _HasSent ? _Time.GetElapsedTime(_LastSent) : _Interval;
                if (_Timer == null && sinceLast >= _Interval)
                {
                    sendNow = value;
                    _LastSent = _Time.GetTimestamp();
                    _HasSent = true;
                }
                else
                {
                    _Pending = value;
                    if (_Timer == null)
                        _Timer = _Time.CreateTimer(OnTimer, null, _Interval - sinceLast, Timeout.InfiniteTimeSpan);
                }
            }

            if (sendNow != null) Deliver(sendNow);
        }

        /// <summary>
        /// Send a held value now, if any.
        /// </summary>
        public void Flush()
        {
            T? pending;
            lock (_Lock)
            {
                if (_Disposed) return;
                _Timer?.Dispose();
                _Timer = null;
                pending = _Pending;
                _Pending = null;
                if (pending != null)
                {
                    _LastSent = _Time.GetTimestamp();
                    _HasSent = true;
                }
            }

            if (pending != null) Deliver(pending);
        }

        /// <summary>
        /// Stop: a held value is dropped and nothing more is sent.
        /// </summary>
        public void Dispose()
        {
            lock (_Lock)
            {
                _Disposed = true;
                _Pending = null;
                _Timer?.Dispose();
                _Timer = null;
            }
        }

        #endregion

        #region Private-Methods

        private void OnTimer(object? state)
        {
            Flush();
        }

        private void Deliver(T value)
        {
            try { _Send(value); }
            catch { }
        }

        #endregion
    }
}
