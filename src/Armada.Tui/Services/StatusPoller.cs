namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Polls what the header shows, on the dashboard's schedule: server health every 30 s, background jobs every 5 s
    /// while any run (30 s when idle), and the Needs You inbox every 20 s and on socket activity (at most every 4 s).
    /// Results are posted to the UI loop. Thread-safe.
    /// </summary>
    public class StatusPoller : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Last health result, or null when the server was unreachable.
        /// </summary>
        public HealthResult? Health { get; private set; } = null;

        /// <summary>
        /// True after the first health check completed.
        /// </summary>
        public bool HealthChecked { get; private set; } = false;

        /// <summary>
        /// Queued and running jobs.
        /// </summary>
        public List<Job> ActiveJobs { get; private set; } = new List<Job>();

        /// <summary>
        /// Needs You items.
        /// </summary>
        public List<InboxItem> Inbox { get; private set; } = new List<InboxItem>();

        /// <summary>
        /// True once the inbox has been fetched since <see cref="Start"/> (or a manual poll).
        /// </summary>
        public bool InboxChecked { get; private set; } = false;

        /// <summary>
        /// Critical inbox items.
        /// </summary>
        public int InboxCritical
        {
            get { return Inbox.Count(i => i.Severity == InboxSeverityEnum.Critical); }
        }

        /// <summary>
        /// Raised on the UI loop after any poll updates.
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>
        /// Time source. The poll schedule is kept on its monotonic clock, never its wall clock: on the wall clock a
        /// backward step (NTP) postponed every poll until the clock caught up, and a forward jump fired them all at
        /// once. Defaults to <see cref="TimeProvider.System"/>; tests substitute a provider whose wall clock jumps.
        /// </summary>
        internal TimeProvider Time
        {
            get => _Time;
            set => _Time = value ?? throw new ArgumentNullException(nameof(Time));
        }

        #endregion

        #region Private-Members

        private readonly Func<ArmadaClient> _Client;
        private readonly IUiDispatcher _Dispatcher;
        private CancellationTokenSource? _Cts = null;
        private TimeProvider _Time = TimeProvider.System;

        // Schedule, as monotonic timestamps from _Time; null means due now (never polled, or Start reset it).
        private readonly object _ScheduleLock = new object();
        private long? _NextHealth = null;
        private long? _NextJobs = null;
        private long? _NextInbox = null;
        private long? _LastInbox = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="client">Returns the current client.</param>
        /// <param name="dispatcher">Dispatcher.</param>
        public StatusPoller(Func<ArmadaClient> client, IUiDispatcher dispatcher)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
            _Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start polling.
        /// </summary>
        public void Start()
        {
            Stop();
            CancellationTokenSource cts = new CancellationTokenSource();
            _Cts = cts;
            lock (_ScheduleLock)
            {
                _NextHealth = _NextJobs = _NextInbox = null;
            }
            InboxChecked = false;
            _ = Task.Run(() => LoopAsync(cts.Token));
        }

        /// <summary>
        /// Stop polling.
        /// </summary>
        public void Stop()
        {
            try { _Cts?.Cancel(); } catch (ObjectDisposedException) { }
            _Cts = null;
        }

        /// <summary>
        /// Request an inbox refetch soon (socket activity), throttled to once every 4 s.
        /// </summary>
        public void NudgeInbox()
        {
            long now = _Time.GetTimestamp();
            lock (_ScheduleLock)
            {
                long earliest = _LastInbox.HasValue ? After(_LastInbox.Value, TimeSpan.FromSeconds(4)) : now;
                _NextInbox = earliest > now ? earliest : now;
            }
        }

        /// <summary>
        /// Poll everything once now (tests and startup).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task PollAllAsync(CancellationToken token = default)
        {
            await PollHealthAsync(token).ConfigureAwait(false);
            await PollJobsAsync(token).ConfigureAwait(false);
            await PollInboxAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Stop polling.
        /// </summary>
        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await PollDueAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try { await Task.Delay(500, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>
        /// Run whichever polls are due now and schedule their next run (one iteration of the polling loop).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        internal async Task PollDueAsync(CancellationToken token = default)
        {
            long now = _Time.GetTimestamp();

            if (TakeDue(ref _NextHealth, now, TimeSpan.FromSeconds(30)))
                await PollHealthAsync(token).ConfigureAwait(false);

            bool jobsDue;
            lock (_ScheduleLock)
            {
                jobsDue = !_NextJobs.HasValue || now >= _NextJobs.Value;
            }

            if (jobsDue)
            {
                await PollJobsAsync(token).ConfigureAwait(false);
                lock (_ScheduleLock)
                {
                    _NextJobs = After(now, TimeSpan.FromSeconds(ActiveJobs.Count > 0 ? 5 : 30));
                }
            }

            if (TakeDue(ref _NextInbox, now, TimeSpan.FromSeconds(20)))
                await PollInboxAsync(token).ConfigureAwait(false);
        }

        private bool TakeDue(ref long? next, long now, TimeSpan interval)
        {
            lock (_ScheduleLock)
            {
                if (next.HasValue && now < next.Value) return false;
                next = After(now, interval);
                return true;
            }
        }

        private long After(long timestamp, TimeSpan interval)
        {
            return timestamp + (long)(interval.TotalSeconds * _Time.TimestampFrequency);
        }

        private async Task PollHealthAsync(CancellationToken token)
        {
            HealthResult? health;
            try { health = await _Client().GetHealthAsync(token).ConfigureAwait(false); }
            catch (ArmadaApiException) { health = null; }
            _Dispatcher.Post(() =>
            {
                Health = health;
                HealthChecked = true;
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }

        private async Task PollJobsAsync(CancellationToken token)
        {
            try
            {
                EnumerationResult<Job>? jobs = await _Client().ListJobsAsync(token).ConfigureAwait(false);
                List<Job> active = (jobs?.Objects ?? new List<Job>()).Where(j => j.Status == JobStatusEnum.Queued || j.Status == JobStatusEnum.Running).ToList();
                _Dispatcher.Post(() =>
                {
                    ActiveJobs = active;
                    Changed?.Invoke(this, EventArgs.Empty);
                });
            }
            catch (ArmadaApiException)
            {
                // Keep the last value.
            }
        }

        private async Task PollInboxAsync(CancellationToken token)
        {
            long polled = _Time.GetTimestamp();
            lock (_ScheduleLock)
            {
                _LastInbox = polled;
            }
            try
            {
                List<InboxItem>? inbox = await _Client().GetInboxAsync(token).ConfigureAwait(false);
                _Dispatcher.Post(() =>
                {
                    Inbox = inbox ?? new List<InboxItem>();
                    InboxChecked = true;
                    Changed?.Invoke(this, EventArgs.Empty);
                });
            }
            catch (ArmadaApiException)
            {
                // Keep the last value.
            }
        }

        #endregion
    }
}
