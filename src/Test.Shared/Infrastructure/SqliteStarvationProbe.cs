namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Measures whether a single write can be starved by a continuous burst of other writes. Background writers insert
    /// request-history rows back to back (as the REST layer does for every request); meanwhile the probe issues one CLI
    /// permission insert per trial, each once every background writer has completed another write since the last
    /// trial. Every write is stamped from one shared counter before it starts and after it finishes, so a target's
    /// overtakes (background writes that started after it and finished before it) are counted without timing.
    /// </summary>
    public sealed class SqliteStarvationProbe
    {
        #region Public-Members

        /// <summary>
        /// Number of concurrent background writers.
        /// </summary>
        public int Writers { get; }

        /// <summary>
        /// Number of target writes.
        /// </summary>
        public int Trials { get; }

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Driver;
        private long _Sequence = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Database driver.</param>
        /// <param name="writers">Concurrent background writers.</param>
        /// <param name="trials">Target writes.</param>
        public SqliteStarvationProbe(DatabaseDriver driver, int writers, int trials)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            if (writers < 1) throw new ArgumentOutOfRangeException(nameof(writers));
            if (trials < 1) throw new ArgumentOutOfRangeException(nameof(trials));
            Writers = writers;
            Trials = trials;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the probe.
        /// </summary>
        /// <returns>Overtakes and latencies.</returns>
        public async Task<SqliteStarvationResult> RunAsync()
        {
            ConcurrentQueue<SqliteStarvationSpan> background = new ConcurrentQueue<SqliteStarvationSpan>();
            List<SqliteStarvationSpan> targets = new List<SqliteStarvationSpan>();
            SemaphoreSlim progress = new SemaphoreSlim(0);

            using (CancellationTokenSource stop = new CancellationTokenSource())
            {
                List<Task> writers = new List<Task>();
                for (int i = 0; i < Writers; i++)
                {
                    int writer = i;
                    writers.Add(Task.Run(() => WriteUntilStoppedAsync(writer, background, progress, stop.Token)));
                }

                try
                {
                    for (int trial = 0; trial < Trials; trial++)
                    {
                        // Contention is live: every writer has finished another write since the last trial.
                        while (progress.Wait(0)) { }
                        for (int i = 0; i < Writers; i++) await progress.WaitAsync().ConfigureAwait(false);

                        CliPermissionRequest request = new CliPermissionRequest();
                        request.TenantId = "ten_probe";
                        request.ThreadId = "ath_probe_" + trial;
                        request.Runtime = AgentRuntimeEnum.ClaudeCode;
                        request.ToolName = "Bash";
                        request.InputText = "{\"command\":\"git status\"}";
                        request.SummaryText = "probe " + trial;

                        long start = Interlocked.Increment(ref _Sequence);
                        Stopwatch watch = Stopwatch.StartNew();
                        await _Driver.CliPermissionRequests.CreateAsync(request).ConfigureAwait(false);
                        watch.Stop();
                        long end = Interlocked.Increment(ref _Sequence);
                        targets.Add(new SqliteStarvationSpan(start, end, watch.Elapsed.TotalMilliseconds));
                    }
                }
                finally
                {
                    stop.Cancel();
                    await Task.WhenAll(writers).ConfigureAwait(false);
                }
            }

            List<SqliteStarvationSpan> writes = background.ToList();
            SqliteStarvationResult result = new SqliteStarvationResult();
            result.WriterCount = writes.Count;
            result.MaxWriterLatencyMs = writes.Count == 0 ? 0 : writes.Max(w => w.LatencyMs);
            foreach (SqliteStarvationSpan target in targets)
            {
                result.Overtakes.Add(writes.Count(w => w.Start > target.Start && w.End < target.End));
                result.TargetLatenciesMs.Add(target.LatencyMs);
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private async Task WriteUntilStoppedAsync(int writer, ConcurrentQueue<SqliteStarvationSpan> spans, SemaphoreSlim progress, CancellationToken token)
        {
            int index = 0;
            while (!token.IsCancellationRequested)
            {
                RequestHistoryEntry entry = new RequestHistoryEntry
                {
                    TenantId = "ten_probe",
                    Method = "GET",
                    Route = "/api/v1/ask/threads/probe-" + writer + "-" + index++,
                    RouteTemplate = "/api/v1/ask/threads/{id}",
                    StatusCode = 200,
                    DurationMs = 1,
                    IsSuccess = true,
                    CreatedUtc = DateTime.UtcNow
                };

                long start = Interlocked.Increment(ref _Sequence);
                Stopwatch watch = Stopwatch.StartNew();
                await _Driver.RequestHistory.CreateAsync(entry, null).ConfigureAwait(false);
                watch.Stop();
                long end = Interlocked.Increment(ref _Sequence);
                spans.Enqueue(new SqliteStarvationSpan(start, end, watch.Elapsed.TotalMilliseconds));
                progress.Release();
            }
        }

        #endregion
    }
}
