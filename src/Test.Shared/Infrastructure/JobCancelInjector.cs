namespace Test.Shared.Infrastructure
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// Cancels a job exactly once, through <see cref="JobService.CancelAsync"/> on the real driver, immediately before a
    /// chosen worker write of that job reaches the database. This reproduces "the cancel lands between the worker's read
    /// and its write" deterministically: the worker has already decided what to write, and the cancel commits first.
    /// </summary>
    public sealed class JobCancelInjector
    {
        #region Public-Members

        /// <summary>
        /// Completes with the cancelled job once the cancel has been injected.
        /// </summary>
        public Task<Job> Injected
        {
            get { return _Injected.Task; }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Real;
        private readonly JobWriteMomentEnum _Moment;
        private readonly Func<Job, bool> _Match;
        private readonly JobService _Jobs;
        private readonly TaskCompletionSource<Job> _Injected = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _Fired = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Install on a hooked driver.
        /// </summary>
        /// <param name="hooked">Driver the worker under test uses.</param>
        /// <param name="real">The real driver underneath (the cancel bypasses the hook).</param>
        /// <param name="moment">Which worker write to land the cancel in front of.</param>
        /// <param name="match">Selects the job to cancel from its stored row.</param>
        public JobCancelInjector(JobHookDatabaseDriver hooked, DatabaseDriver real, JobWriteMomentEnum moment, Func<Job, bool> match)
        {
            if (hooked == null) throw new ArgumentNullException(nameof(hooked));
            _Real = real ?? throw new ArgumentNullException(nameof(real));
            _Moment = moment;
            _Match = match ?? throw new ArgumentNullException(nameof(match));
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            _Jobs = new JobService(real, logging);
            hooked.HookedJobs.BeforeWriteAsync = BeforeWriteAsync;
        }

        #endregion

        #region Private-Methods

        private async Task BeforeWriteAsync(string jobId, JobStatusEnum? status)
        {
            if (Volatile.Read(ref _Fired) != 0) return;
            Job? stored = await _Real.Jobs.ReadAsync(jobId).ConfigureAwait(false);
            if (stored == null || !_Match(stored)) return;

            bool isStart = status == JobStatusEnum.Running && stored.Status == JobStatusEnum.Queued;
            bool isHeartbeat = stored.Status == JobStatusEnum.Running && (status == null || status == JobStatusEnum.Running);
            if (_Moment == JobWriteMomentEnum.Start && !isStart) return;
            if (_Moment == JobWriteMomentEnum.Heartbeat && !isHeartbeat) return;
            if (Interlocked.Exchange(ref _Fired, 1) != 0) return;

            Job cancelled = await _Jobs.CancelAsync(stored).ConfigureAwait(false);
            _Injected.TrySetResult(cancelled);
        }

        #endregion
    }
}
