namespace Armada.Client.Metrics
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Keeps a Harbor's metrics fresh while a view shows them: loads at once when started (or when the range changes, or
    /// on request), then again after each interval, until stopped. Stopping cancels the request in flight and the wait, and
    /// a result for a range or run that has since been replaced is dropped, so <see cref="Loaded"/> only ever reports the
    /// current range. <see cref="Loaded"/> is raised on a thread-pool thread. The wait is injectable so tests drive the
    /// timer without sleeping. Thread-safe.
    /// </summary>
    public class HarborMetricsRefresher : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Raised with each result for the current range and run.
        /// </summary>
        public event EventHandler<HarborMetricsLoadResult>? Loaded;

        /// <summary>
        /// Range being refreshed (1h, 24h, or 7d).
        /// </summary>
        public string Range
        {
            get { lock (_Lock) return _Range; }
        }

        /// <summary>
        /// True between <see cref="Start"/> and <see cref="Stop"/>.
        /// </summary>
        public bool IsRunning
        {
            get { lock (_Lock) return _Cts != null; }
        }

        /// <summary>
        /// Time between refreshes.
        /// </summary>
        public TimeSpan Interval { get; }

        /// <summary>
        /// Results raised so far.
        /// </summary>
        public int LoadCount
        {
            get { return Volatile.Read(ref _LoadCount); }
        }

        /// <summary>
        /// Refresh runs that have ended (stopped, restarted, replaced by a range switch, or disposed). A run ends after its
        /// last answer is raised or dropped, so a test can wait for it instead of sleeping.
        /// </summary>
        public int CompletedRuns
        {
            get { return Volatile.Read(ref _CompletedRuns); }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly HarborMetricsFeed _Feed;
        private readonly Func<TimeSpan, CancellationToken, Task> _Delay;
        private string _Range = "24h";
        private CancellationTokenSource? _Cts = null;
        private int _Generation = 0;
        private int _LoadCount = 0;
        private int _CompletedRuns = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="feed">Feed.</param>
        /// <param name="interval">Time between refreshes (at least one second).</param>
        /// <param name="delay">Wait between refreshes (tests pass a gate); null uses <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
        public HarborMetricsRefresher(HarborMetricsFeed feed, TimeSpan interval, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _Feed = feed ?? throw new ArgumentNullException(nameof(feed));
            if (interval < TimeSpan.FromSeconds(1)) throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be at least one second.");
            Interval = interval;
            _Delay = delay ?? ((span, token) => Task.Delay(span, token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start (or restart) refreshing a range: load now, then after each interval.
        /// </summary>
        /// <param name="range">1h, 24h, or 7d.</param>
        public void Start(string range)
        {
            CancellationTokenSource cts;
            int generation;
            string current;
            lock (_Lock)
            {
                if (_Disposed) throw new ObjectDisposedException(nameof(HarborMetricsRefresher));
                _Range = String.IsNullOrWhiteSpace(range) ? "24h" : range.Trim();
                CancelLocked();
                cts = new CancellationTokenSource();
                _Cts = cts;
                generation = ++_Generation;
                current = _Range;
            }

            _ = Task.Run(() => LoopAsync(current, generation, cts.Token));
        }

        /// <summary>
        /// Switch to another range and load it now (starts refreshing when stopped).
        /// </summary>
        /// <param name="range">1h, 24h, or 7d.</param>
        public void SetRange(string range)
        {
            Start(range);
        }

        /// <summary>
        /// Load now and restart the interval (starts refreshing when stopped).
        /// </summary>
        public void RefreshNow()
        {
            Start(Range);
        }

        /// <summary>
        /// Stop refreshing: cancel the request in flight and the wait. A result already on its way is dropped.
        /// </summary>
        public void Stop()
        {
            lock (_Lock)
            {
                CancelLocked();
                _Generation++;
            }
        }

        /// <summary>
        /// Stop and release resources.
        /// </summary>
        public void Dispose()
        {
            lock (_Lock)
            {
                if (_Disposed) return;
                _Disposed = true;
                CancelLocked();
                _Generation++;
            }
        }

        #endregion

        #region Private-Methods

        private void CancelLocked()
        {
            CancellationTokenSource? previous = _Cts;
            _Cts = null;
            if (previous == null) return;
            try
            {
                previous.Cancel();
            }
            finally
            {
                previous.Dispose();
            }
        }

        private bool IsCurrent(int generation)
        {
            lock (_Lock) return generation == _Generation && !_Disposed;
        }

        private async Task LoopAsync(string range, int generation, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    HarborMetricsLoadResult result = await _Feed.LoadAsync(range, token).ConfigureAwait(false);
                    if (!IsCurrent(generation)) return;
                    Interlocked.Increment(ref _LoadCount);
                    Loaded?.Invoke(this, result);
                    await _Delay(Interval, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopped, restarted, or disposed.
            }
            catch (ObjectDisposedException)
            {
                // The token source was disposed while a wait was starting.
            }
            finally
            {
                Interlocked.Increment(ref _CompletedRuns);
            }
        }

        #endregion
    }
}
