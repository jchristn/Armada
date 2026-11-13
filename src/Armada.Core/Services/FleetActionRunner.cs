namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Executes fleet action runs in the background. The server owns one instance: it is started from
    /// ArmadaServer.StartAsync (which performs restart recovery) and stopped on shutdown.
    /// <para>
    /// Command runs execute their targets concurrently, gated by the run's Concurrency and by the global
    /// FleetActions.MaxConcurrency cap shared across every Command run in the process. Each target runs the rendered
    /// command in the vessel's working directory through the platform shell (see
    /// <see cref="FleetActionShellCommandBuilder"/>), on the vessel's preferred Harbor when it has one.
    /// </para>
    /// <para>
    /// Mission runs are paced rather than executed: <see cref="SyncMissionRunsAsync"/> (called every health-check
    /// cycle and right after a run starts) updates running targets from their voyages and dispatches pending targets
    /// while fewer than Concurrency voyages from the run are active.
    /// </para>
    /// Thread-safe. Target transitions and run rollups for a run are serialized by a per-run lock.
    /// </summary>
    public class FleetActionRunner : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// How often a target waiting for a global concurrency slot re-checks, in milliseconds.
        /// Default 250, minimum 10, maximum 5000.
        /// </summary>
        public int GlobalSlotPollMs
        {
            get => _GlobalSlotPollMs;
            set => _GlobalSlotPollMs = value < 10 ? 10 : (value > 5000 ? 5000 : value);
        }

        /// <summary>
        /// Wall clock for the runner's timestamps (StartedUtc, CompletedUtc, LastUpdateUtc) and the retention cutoff.
        /// Defaults to <see cref="DateTime.UtcNow"/>. Waits never use it: <see cref="WaitForRunAsync"/> measures its
        /// timeout on a monotonic clock, so a wall-clock jump (the host sleeping and waking, an NTP step) cannot end it
        /// early.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public Func<DateTime> UtcNow
        {
            get => _UtcNow;
            set => _UtcNow = value ?? throw new ArgumentNullException(nameof(UtcNow));
        }

        /// <summary>
        /// Timeout for the dirty-tree pre-check (git status --porcelain), in milliseconds.
        /// Default 60000, minimum 1000, maximum 600000.
        /// </summary>
        public int GitStatusTimeoutMs
        {
            get => _GitStatusTimeoutMs;
            set => _GitStatusTimeoutMs = value < 1000 ? 1000 : (value > 600000 ? 600000 : value);
        }

        /// <summary>
        /// Number of Command targets currently holding a global concurrency slot.
        /// </summary>
        public int GlobalInFlight
        {
            get
            {
                lock (_GlobalLock) return _GlobalInFlight;
            }
        }

        #endregion

        #region Private-Members

        private const string _WorkerOnlyPipelineName = "WorkerOnly";
        private const int _ApplicabilityTimeoutMs = 120000;
        private const int _FailureSummaryBytes = 8192;
        private readonly string _Header = "[FleetActionRunner] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly IFleetActionMissionDispatcher? _MissionDispatcher;
        private readonly IHostCommandExecutor _LocalExecutor;
        private readonly Func<string?, FleetActionOutputParsers.IFleetActionOutputParser?> _OutputParserResolver;
        private readonly HarborConnectionManager? _Harbors;
        private readonly FleetActionHealthSummaryBuilder _HealthSummary;
        private readonly ConcurrentDictionary<string, FleetActionRunContext> _Active = new ConcurrentDictionary<string, FleetActionRunContext>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _RunLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _MissionSyncGate = new SemaphoreSlim(1, 1);
        private readonly object _GlobalLock = new object();
        private CancellationTokenSource _Lifetime = new CancellationTokenSource();
        private int _GlobalInFlight = 0;
        private int _GlobalSlotPollMs = 250;
        private int _GitStatusTimeoutMs = 60000;
        private Func<DateTime> _UtcNow = DefaultUtcNow;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings; FleetActions values are read live on every use.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="missionDispatcher">Mission dispatcher, or null to fail Mission targets with DispatchUnavailable.</param>
        /// <param name="localExecutor">Executor for the Admiral host; defaults to <see cref="LocalHostCommandExecutor"/>.</param>
        /// <param name="harbors">Harbor connection manager for vessels with a preferred Harbor, or null.</param>
        /// <param name="outputParserResolver">Optional stateless parser resolver; defaults to the built-in registry.</param>
        /// <exception cref="ArgumentNullException">Thrown when database, settings, or logging is null.</exception>
        public FleetActionRunner(
            DatabaseDriver database,
            ArmadaSettings settings,
            LoggingModule logging,
            IFleetActionMissionDispatcher? missionDispatcher,
            IHostCommandExecutor? localExecutor = null,
            HarborConnectionManager? harbors = null,
            Func<string?, FleetActionOutputParsers.IFleetActionOutputParser?>? outputParserResolver = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _MissionDispatcher = missionDispatcher;
            _LocalExecutor = localExecutor ?? new LocalHostCommandExecutor();
            _Harbors = harbors;
            _OutputParserResolver = outputParserResolver ?? FleetActionOutputParsers.FleetActionParserRegistry.Get;
            _HealthSummary = new FleetActionHealthSummaryBuilder(database);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the runner and recover runs left unfinished by a previous process: Command targets still marked
        /// Running are failed with reason Interrupted and pending Command targets resume. Mission runs are left to the
        /// next <see cref="SyncMissionRunsAsync"/> call (the server's health-check loop), so no voyage is dispatched
        /// before the Admiral finishes wiring its agent lifecycle.
        /// </summary>
        /// <param name="token">Server lifetime token.</param>
        /// <returns>Task.</returns>
        public async Task StartAsync(CancellationToken token = default)
        {
            _Lifetime.Dispose();
            _Lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);

            List<FleetActionRun> runs = await _Database.FleetActionRuns.EnumerateUnfinishedAsync(token).ConfigureAwait(false);
            foreach (FleetActionRun run in runs)
            {
                try
                {
                    // Mission runs keep their state in voyages; the next SyncMissionRunsAsync picks them up.
                    if (run.Kind == FleetActionKindEnum.Mission) continue;

                    List<FleetActionRunTarget> targets = await _Database.FleetActionRunTargets.ReadAllByRunAsync(run.Id, token).ConfigureAwait(false);
                    foreach (FleetActionRunTarget target in targets.Where(t => t.Status == FleetActionTargetStatusEnum.Running))
                    {
                        target.Status = FleetActionTargetStatusEnum.Failed;
                        target.FailureReason = FleetActionReasonCodes.Interrupted;
                        target.ErrorText = AppendLine(target.ErrorText, "The Admiral restarted while this command was running.");
                        StampCompletion(target);
                        await _Database.FleetActionRunTargets.UpdateAsync(target, token).ConfigureAwait(false);
                        RecordMetrics(run.Kind, target);
                    }

                    await RefreshRunAsync(run.Id, token).ConfigureAwait(false);
                    if (targets.Any(t => t.Status == FleetActionTargetStatusEnum.Pending))
                    {
                        _Logging.Info(_Header + "resuming command run " + run.Id);
                        StartCommandRun(run.Id);
                    }
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "recovery of run " + run.Id + " failed: " + e.Message);
                }
            }
        }

        /// <summary>
        /// Stop the runner. In-flight commands are killed; their targets stay Running in the database and are
        /// reported as Interrupted by the next <see cref="StartAsync"/>.
        /// </summary>
        public void Stop()
        {
            try
            {
                _Lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            List<Task> tasks = _Active.Values.Select(c => c.Task).Where(t => t != null).Select(t => t!).ToList();
            try
            {
                Task.WaitAll(tasks.ToArray(), 5000);
            }
            catch (AggregateException)
            {
            }
        }

        /// <summary>
        /// Begin processing a newly created run.
        /// </summary>
        /// <param name="run">Run.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="run"/> is null.</exception>
        public void Enqueue(FleetActionRun run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            if (run.Kind == FleetActionKindEnum.Command)
            {
                StartCommandRun(run.Id);
                return;
            }

            CancellationToken lifetime = _Lifetime.Token;
            string runId = run.Id;
            _ = Task.Run(async () =>
            {
                try
                {
                    await SyncMissionRunAsync(runId, lifetime).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "initial sync of mission run " + runId + " failed: " + e.Message);
                }
            });
        }

        /// <summary>
        /// Whether a Command run is executing in this process.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <returns>True when executing.</returns>
        public bool IsRunActive(string runId)
        {
            if (String.IsNullOrEmpty(runId)) return false;
            return _Active.ContainsKey(runId);
        }

        /// <summary>
        /// Cancel a run. Pending targets become Cancelled; running Command targets are killed and become Cancelled;
        /// running Mission targets have their voyage cancelled unless it already finished. Cancelling an already
        /// cancelled run returns it unchanged.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runId"/> is null or empty.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the run does not exist.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the run already finished.</exception>
        public async Task<FleetActionRun> CancelRunAsync(string runId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));

            FleetActionRunContext? context = null;
            SemaphoreSlim runLock = GetRunLock(runId);
            await runLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                FleetActionRun? run = await _Database.FleetActionRuns.ReadAsync(runId, token).ConfigureAwait(false);
                if (run == null) throw new KeyNotFoundException("Fleet action run not found: " + runId);
                if (run.Status == FleetActionRunStatusEnum.Cancelled) return run;
                if (IsTerminal(run.Status)) throw new InvalidOperationException("Fleet action run " + runId + " already finished with status " + run.Status + ".");

                bool active = _Active.TryGetValue(runId, out context);
                if (context != null) context.CancelRequested = true;

                run.Status = FleetActionRunStatusEnum.Cancelled;
                run.LastUpdateUtc = _UtcNow();
                await _Database.FleetActionRuns.UpdateAsync(run, token).ConfigureAwait(false);

                List<FleetActionRunTarget> targets = await _Database.FleetActionRunTargets.ReadAllByRunAsync(runId, token).ConfigureAwait(false);
                foreach (FleetActionRunTarget target in targets)
                {
                    if (target.Status == FleetActionTargetStatusEnum.Pending)
                    {
                        await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Cancelled, null, null, null, token).ConfigureAwait(false);
                        continue;
                    }

                    if (target.Status != FleetActionTargetStatusEnum.Running) continue;

                    if (run.Kind == FleetActionKindEnum.Mission)
                    {
                        await CancelMissionTargetAsync(run, target, token).ConfigureAwait(false);
                    }
                    else if (!active)
                    {
                        // No process is running it here (for example between a restart and recovery).
                        await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Cancelled, null, null, null, token).ConfigureAwait(false);
                    }
                }

                await RefreshRunCoreAsync(runId, token).ConfigureAwait(false);
            }
            finally
            {
                runLock.Release();
            }

            // Outside the lock: in-flight targets take the lock to record their cancellation.
            context?.Cancel();

            FleetActionRun? updated = await _Database.FleetActionRuns.ReadAsync(runId, token).ConfigureAwait(false);
            return updated ?? throw new KeyNotFoundException("Fleet action run not found: " + runId);
        }

        /// <summary>
        /// Sync every unfinished Mission run: update running targets from their voyages and dispatch pending targets
        /// up to each run's Concurrency. Overlapping calls return immediately.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task SyncMissionRunsAsync(CancellationToken token = default)
        {
            if (!await _MissionSyncGate.WaitAsync(0, token).ConfigureAwait(false)) return;
            try
            {
                List<FleetActionRun> runs = await _Database.FleetActionRuns.EnumerateUnfinishedAsync(token).ConfigureAwait(false);
                foreach (FleetActionRun run in runs.Where(r => r.Kind == FleetActionKindEnum.Mission))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        await SyncMissionRunAsync(run.Id, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        _Logging.Warn(_Header + "sync of mission run " + run.Id + " failed: " + e.Message);
                    }
                }
            }
            finally
            {
                _MissionSyncGate.Release();
            }
        }

        /// <summary>
        /// Delete finished runs (and their targets) created more than FleetActions.RunRetentionDays ago, across
        /// all tenants. Unfinished runs are never pruned.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of runs deleted.</returns>
        public async Task<int> PruneExpiredRunsAsync(CancellationToken token = default)
        {
            DateTime cutoff = _UtcNow().AddDays(-_Settings.FleetActions.RunRetentionDays);
            List<TenantMetadata> tenants = await _Database.Tenants.EnumerateAsync(token).ConfigureAwait(false);
            int deleted = 0;

            foreach (TenantMetadata tenant in tenants)
            {
                List<string> expired = new List<string>();
                int pageNumber = 1;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    EnumerationQuery query = new EnumerationQuery { PageNumber = pageNumber, PageSize = 100, CreatedBefore = cutoff };
                    EnumerationResult<FleetActionRun> page = await _Database.FleetActionRuns.EnumerateAsync(tenant.Id, query, token).ConfigureAwait(false);
                    foreach (FleetActionRun run in page.Objects)
                    {
                        if (IsTerminal(run.Status) && !_Active.ContainsKey(run.Id)) expired.Add(run.Id);
                    }

                    if (page.Objects.Count < query.PageSize || pageNumber >= page.TotalPages) break;
                    pageNumber++;
                }

                foreach (string runId in expired)
                {
                    await _Database.FleetActionRuns.DeleteAsync(tenant.Id, runId, token).ConfigureAwait(false);
                    _RunLocks.TryRemove(runId, out SemaphoreSlim? _);
                    deleted++;
                }
            }

            if (deleted > 0) _Logging.Info(_Header + "pruned " + deleted + " expired fleet action run(s)");
            return deleted;
        }

        /// <summary>
        /// Wait until a run reaches a terminal status, polling the database.
        /// </summary>
        /// <param name="runId">Run identifier.</param>
        /// <param name="timeoutMs">Maximum wait in milliseconds, minimum 1, measured on a monotonic clock (a wall-clock
        /// jump such as the host sleeping and waking does not shorten it).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The finished run, or null when it did not finish in time or does not exist.</returns>
        public async Task<FleetActionRun?> WaitForRunAsync(string runId, int timeoutMs, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(runId)) throw new ArgumentNullException(nameof(runId));
            if (timeoutMs < 1) timeoutMs = 1;

            // Measured on a monotonic clock: a wall-clock deadline ended the wait early (returning null for a run that
            // was still finishing) whenever the clock jumped forward, as it does when the host sleeps and wakes.
            Stopwatch waited = Stopwatch.StartNew();
            while (true)
            {
                FleetActionRun? run = await _Database.FleetActionRuns.ReadAsync(runId, token).ConfigureAwait(false);
                if (run == null) return null;
                if (IsTerminal(run.Status) && run.CompletedUtc.HasValue && !_Active.ContainsKey(runId)) return run;
                if (waited.ElapsedMilliseconds >= timeoutMs) return null;
                await Task.Delay(50, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Whether a run status is terminal (Completed, CompletedWithFailures, Cancelled, or Failed).
        /// </summary>
        /// <param name="status">Run status.</param>
        /// <returns>True when terminal.</returns>
        public static bool IsTerminal(FleetActionRunStatusEnum status)
        {
            return status == FleetActionRunStatusEnum.Completed
                || status == FleetActionRunStatusEnum.CompletedWithFailures
                || status == FleetActionRunStatusEnum.Cancelled
                || status == FleetActionRunStatusEnum.Failed;
        }

        /// <summary>
        /// Keep the first <paramref name="maxBytes"/> UTF-8 bytes of a string (a captain's report reads from the top),
        /// never splitting a character.
        /// </summary>
        /// <param name="value">Text; null returns null.</param>
        /// <param name="maxBytes">Maximum bytes, minimum 1.</param>
        /// <param name="truncated">Set true when text was dropped.</param>
        /// <returns>The possibly shortened text.</returns>
        public static string? TruncateHead(string? value, int maxBytes, out bool truncated)
        {
            truncated = false;
            if (value == null) return null;
            if (maxBytes < 1) maxBytes = 1;
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;

            truncated = true;
            int bytes = 0;
            int end = 0;
            while (end < value.Length)
            {
                int charLength = (end + 1 < value.Length && Char.IsHighSurrogate(value[end]) && Char.IsLowSurrogate(value[end + 1])) ? 2 : 1;
                int charBytes = Encoding.UTF8.GetByteCount(value.Substring(end, charLength));
                if (bytes + charBytes > maxBytes) break;
                bytes += charBytes;
                end += charLength;
            }

            return value.Substring(0, end);
        }

        /// <summary>
        /// Keep the last <paramref name="maxBytes"/> UTF-8 bytes of a string (the end of process output is usually
        /// where errors are), never splitting a character.
        /// </summary>
        /// <param name="value">Text; null returns null.</param>
        /// <param name="maxBytes">Maximum bytes, minimum 1.</param>
        /// <param name="truncated">Set true when text was dropped.</param>
        /// <returns>The possibly shortened text.</returns>
        public static string? TruncateTail(string? value, int maxBytes, out bool truncated)
        {
            truncated = false;
            if (value == null) return null;
            if (maxBytes < 1) maxBytes = 1;
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;

            truncated = true;
            int bytes = 0;
            int start = value.Length;
            while (start > 0)
            {
                int charLength = (start >= 2 && Char.IsLowSurrogate(value[start - 1]) && Char.IsHighSurrogate(value[start - 2])) ? 2 : 1;
                int charBytes = Encoding.UTF8.GetByteCount(value.Substring(start - charLength, charLength));
                if (bytes + charBytes > maxBytes) break;
                bytes += charBytes;
                start -= charLength;
            }

            return value.Substring(start);
        }

        /// <summary>
        /// Dispose: stops the runner and releases its resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                Stop();
                _Lifetime.Dispose();
                _MissionSyncGate.Dispose();
            }

            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private static DateTime DefaultUtcNow()
        {
            return DateTime.UtcNow;
        }

        private SemaphoreSlim GetRunLock(string runId)
        {
            return _RunLocks.GetOrAdd(runId, _ => new SemaphoreSlim(1, 1));
        }

        private void StartCommandRun(string runId)
        {
            FleetActionRunContext context = new FleetActionRunContext(_Lifetime.Token);
            if (!_Active.TryAdd(runId, context))
            {
                context.Dispose();
                return;
            }

            context.Task = Task.Run(async () =>
            {
                try
                {
                    await ProcessCommandRunAsync(runId, context).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_Lifetime.IsCancellationRequested)
                {
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "command run " + runId + " failed: " + e.ToString());
                    await FailRunSafeAsync(runId, e.Message).ConfigureAwait(false);
                }
                finally
                {
                    _Active.TryRemove(runId, out FleetActionRunContext? _);
                    context.Dispose();
                }
            });
        }

        private async Task ProcessCommandRunAsync(string runId, FleetActionRunContext context)
        {
            FleetActionRun? run = await _Database.FleetActionRuns.ReadAsync(runId).ConfigureAwait(false);
            if (run == null || IsTerminal(run.Status)) return;

            await MarkRunStartedAsync(runId).ConfigureAwait(false);

            List<FleetActionRunTarget> pending = (await _Database.FleetActionRunTargets.ReadAllByRunAsync(runId).ConfigureAwait(false))
                .Where(t => t.Status == FleetActionTargetStatusEnum.Pending)
                .OrderBy(t => t.CreatedUtc)
                .ThenBy(t => t.Id, StringComparer.Ordinal)
                .ToList();

            using (SemaphoreSlim perRun = new SemaphoreSlim(run.Concurrency, run.Concurrency))
            {
                List<Task> tasks = pending.Select(t => ProcessCommandTargetAsync(run, t.Id, perRun, context)).ToList();
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            if (!_Lifetime.IsCancellationRequested) await RefreshRunAsync(runId).ConfigureAwait(false);
        }

        private async Task ProcessCommandTargetAsync(FleetActionRun run, string targetId, SemaphoreSlim perRun, FleetActionRunContext context)
        {
            bool perRunHeld = false;
            bool globalHeld = false;
            try
            {
                await perRun.WaitAsync(context.Token).ConfigureAwait(false);
                perRunHeld = true;
                await AcquireGlobalSlotAsync(context.Token).ConfigureAwait(false);
                globalHeld = true;

                FleetActionRunTarget? target = await ClaimTargetAsync(run.Id, targetId, context).ConfigureAwait(false);
                if (target == null) return;

                await ExecuteCommandTargetAsync(run, target, context.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.Token.IsCancellationRequested)
            {
                if (context.CancelRequested) await CancelTargetIfUnfinishedAsync(run, targetId).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "target " + targetId + " of run " + run.Id + " failed: " + e.Message);
                await FailTargetSafeAsync(run, targetId, FleetActionReasonCodes.ExecutionError, e.Message).ConfigureAwait(false);
            }
            finally
            {
                if (globalHeld) ReleaseGlobalSlot();
                if (perRunHeld) perRun.Release();
                if (!_Lifetime.IsCancellationRequested) await RefreshRunSafeAsync(run.Id).ConfigureAwait(false);
            }
        }

        private async Task<FleetActionRunTarget?> ClaimTargetAsync(string runId, string targetId, FleetActionRunContext context)
        {
            SemaphoreSlim runLock = GetRunLock(runId);
            await runLock.WaitAsync(context.Token).ConfigureAwait(false);
            try
            {
                if (context.CancelRequested) return null;
                FleetActionRunTarget? target = await _Database.FleetActionRunTargets.ReadAsync(targetId).ConfigureAwait(false);
                if (target == null || target.Status != FleetActionTargetStatusEnum.Pending) return null;

                target.Status = FleetActionTargetStatusEnum.Running;
                target.StartedUtc = _UtcNow();
                target.LastUpdateUtc = _UtcNow();
                return await _Database.FleetActionRunTargets.UpdateAsync(target).ConfigureAwait(false);
            }
            finally
            {
                runLock.Release();
            }
        }

        private async Task ExecuteCommandTargetAsync(FleetActionRun run, FleetActionRunTarget target, CancellationToken token)
        {
            string tenantId = String.IsNullOrEmpty(run.TenantId) ? Constants.DefaultTenantId : run.TenantId!;
            int maxBytes = _Settings.FleetActions.MaxOutputBytes;

            Vessel? vessel = await _Database.Vessels.ReadAsync(tenantId, target.VesselId, token).ConfigureAwait(false);
            if (vessel == null)
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.VesselNotFound, null, null, token).ConfigureAwait(false);
                return;
            }

            if (run.RequiresNetwork && (_Settings.FleetActions.Offline || _Settings.FleetActions.OfflineHarborIds.Contains(vessel.PreferredHarborId ?? String.Empty)))
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.NetworkUnavailable, null, null, token).ConfigureAwait(false);
                return;
            }

            if (String.IsNullOrWhiteSpace(vessel.WorkingDirectory))
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.NoWorkingDirectory, null,
                    "Vessel " + vessel.Name + " has no working directory.", token).ConfigureAwait(false);
                return;
            }

            string workingDirectory = vessel.WorkingDirectory!;
            FleetActionExecutorSelection? selection = await ResolveExecutorAsync(vessel, token).ConfigureAwait(false);
            if (selection == null)
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.HarborUnavailable, null,
                    "Preferred Harbor " + vessel.PreferredHarborId + " is not connected.", token).ConfigureAwait(false);
                return;
            }

            if (!selection.IsRemote && !Directory.Exists(workingDirectory))
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.NoWorkingDirectory, null,
                    "Working directory does not exist on the Admiral host: " + workingDirectory, token).ConfigureAwait(false);
                return;
            }

            bool windows = selection.IsRemote ? FleetActionShellCommandBuilder.IsWindowsPlatform(selection.HarborOsPlatform) : OperatingSystem.IsWindows();
            string? commandTemplate = FleetActionShellCommandBuilder.SelectCommand(run.CommandText, run.CommandWindows, windows);
            foreach (string tool in run.RequiredTools)
            {
                // Definitions permit executable names only. Quote again to keep preflight separate from execution.
                string probe = windows
                    ? "if (Get-Command -Name '" + tool.Replace("'", "''") + "' -CommandType Application -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"
                    : "command -v '" + tool.Replace("'", "'\"'\"'") + "' >/dev/null 2>&1";
                HostCommandRequest preflight = selection.IsRemote
                    ? FleetActionShellCommandBuilder.BuildRemote(probe, workingDirectory, GitStatusTimeoutMs, selection.HarborOsPlatform)
                    : FleetActionShellCommandBuilder.BuildLocal(probe, workingDirectory, GitStatusTimeoutMs);
                HostCommandResult available = await selection.Executor.RunAsync(preflight, token).ConfigureAwait(false);
                if (available.TimedOut || available.ExitCode != 0)
                {
                    await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.ToolMissing + tool, null, null, token).ConfigureAwait(false);
                    return;
                }
            }

            string? applicabilityTemplate = FleetActionShellCommandBuilder.SelectCommand(run.ApplicabilityCommand, run.ApplicabilityCommandWindows, windows);
            FleetActionTemplateContext context = await BuildContextAsync(new string?[] { commandTemplate, applicabilityTemplate }, vessel, tenantId, run, token).ConfigureAwait(false);
            if (await SkipUnresolvedCommandAsync(run, target, vessel, commandTemplate, context, token).ConfigureAwait(false)) return;

            string rendered;
            try
            {
                rendered = FleetActionTemplateRenderer.Render(commandTemplate, context);
            }
            catch (FleetActionTemplateException e)
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, FleetActionReasonCodes.TemplateError, e.Message, token).ConfigureAwait(false);
                return;
            }

            if (!await IsApplicableAsync(run, target, vessel, selection, workingDirectory, applicabilityTemplate, context, token).ConfigureAwait(false)) return;

            target.RenderedText = rendered;
            if (String.IsNullOrWhiteSpace(rendered))
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, FleetActionReasonCodes.TemplateError, "The rendered command is empty.", token).ConfigureAwait(false);
                return;
            }

            if (run.RequiresCleanWorkingTree)
            {
                HostCommandResult status = await selection.Executor.RunAsync(new HostCommandRequest
                {
                    Executable = "git",
                    WorkingDirectory = workingDirectory,
                    Arguments = new List<string> { "status", "--porcelain" },
                    TimeoutMs = GitStatusTimeoutMs
                }, token).ConfigureAwait(false);

                if (status.TimedOut || status.ExitCode != 0)
                {
                    string detail = status.TimedOut ? "git status timed out." : ("git status exited with code " + status.ExitCode + ". " + status.StandardError).Trim();
                    await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, FleetActionReasonCodes.GitStatusFailed,
                        TruncateTail(detail, maxBytes, out bool _), token).ConfigureAwait(false);
                    return;
                }

                if (!String.IsNullOrWhiteSpace(status.StandardOutput))
                {
                    target.OutputText = TruncateTail(status.StandardOutput, maxBytes, out bool dirtyTruncated);
                    target.OutputTruncated = dirtyTruncated;
                    await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.DirtyTree, null,
                        "The working tree has uncommitted changes.", token).ConfigureAwait(false);
                    return;
                }
            }

            int timeoutMs = run.TimeoutSeconds * 1000;
            HostCommandRequest request = selection.IsRemote
                ? FleetActionShellCommandBuilder.BuildRemote(rendered, workingDirectory, timeoutMs, selection.HarborOsPlatform)
                : FleetActionShellCommandBuilder.BuildLocal(rendered, workingDirectory, timeoutMs);
            request.Kind = HostCommandKindEnum.Command;
            request.Label = "Fleet action";

            await CommandAudit.RecordAsync(_Database, new CommandAuditRecord
            {
                Source = "FleetAction",
                Command = rendered,
                WorkingDirectory = workingDirectory,
                Host = selection.IsRemote ? ("Harbor " + vessel.PreferredHarborId) : "Admiral",
                TenantId = run.TenantId,
                UserId = run.UserId,
                VesselId = vessel.Id,
                EntityType = "FleetActionRun",
                EntityId = run.Id
            }, _Logging, token).ConfigureAwait(false);

            // The target budget includes parsing, using a monotonic clock so sleep/clock changes cannot extend it.
            long executionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            HostCommandResult result = await selection.Executor.RunAsync(request, token).ConfigureAwait(false);

            string? parserId = run.OutputParser == FleetActionOutputParsers.FleetActionParserRegistry.AutoParserId ? context.AuditParserId : run.OutputParser;
            bool secretReport = parserId == "gitleaks-json";
            string safeOutput = secretReport ? "Secret scan raw output redacted." : result.StandardOutput;
            string safeError = secretReport ? "Secret scan diagnostics redacted." : result.StandardError;
            target.OutputText = TruncateTail(safeOutput, maxBytes, out bool outputTruncated);
            string? errorText = TruncateTail(safeError, maxBytes, out bool errorTruncated);
            target.OutputTruncated = outputTruncated || errorTruncated;

            if (result.TimedOut)
            {
                target.ExitCode = null;
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.TimedOut, null, FleetActionReasonCodes.Timeout, errorText, token).ConfigureAwait(false);
                return;
            }

            token.ThrowIfCancellationRequested();
            FleetActionOutputParsers.FleetActionParseResult? parsed = null;
            FleetActionOutputParsers.IFleetActionOutputParser? parser = _OutputParserResolver(parserId);
            if (parser != null)
            {
                double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - executionStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                int remainingMs = Math.Max(0, timeoutMs - (int)Math.Min(Int32.MaxValue, elapsedMs));
                using CancellationTokenSource parseDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                if (remainingMs == 0) parseDeadline.Cancel(); else parseDeadline.CancelAfter(remainingMs);
                try
                {
                    parsed = parser.Parse(result.StandardOutput, parseDeadline.Token);
                    parseDeadline.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && parseDeadline.IsCancellationRequested)
                {
                    target.ExitCode = null;
                    await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.TimedOut, null, FleetActionReasonCodes.Timeout, errorText, token).ConfigureAwait(false);
                    return;
                }
            }
            token.ThrowIfCancellationRequested();
            target.ScanSummary = parsed?.Summary;
            // Without a parser, the command's output (a failing test run, an outdated report) is what a follow-up mission
            // needs through {{scan.summary}}.
            if (parser == null && !secretReport)
                target.ScanSummary = BuildOutputSummary(result.ExitCode, result.StandardOutput, result.StandardError);
            target.ExitCode = result.ExitCode;
            if (parsed?.Parsed == true && parsed.Total > 0)
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Findings, null, null, errorText, token).ConfigureAwait(false);
            }
            else if (result.ExitCode == 0)
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Succeeded, null, null, errorText, token).ConfigureAwait(false);
            }
            else
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, FleetActionReasonCodes.NonZeroExit, errorText, token).ConfigureAwait(false);
            }
        }

        private async Task<FleetActionExecutorSelection?> ResolveExecutorAsync(Vessel vessel, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(vessel.PreferredHarborId))
                return new FleetActionExecutorSelection(_LocalExecutor, false, null);

            string harborId = vessel.PreferredHarborId!;
            if (_Harbors == null || !_Harbors.IsConnected(harborId)) return null;

            Harbor? harbor = await _Database.Harbors.ReadAsync(harborId, token).ConfigureAwait(false);
            return new FleetActionExecutorSelection(new RemoteHostCommandExecutor(_Harbors, harborId), true, harbor?.OsPlatform);
        }

        private async Task<FleetActionTemplateContext> BuildContextAsync(IEnumerable<string?> templates, Vessel vessel, string tenantId, FleetActionRun run, CancellationToken token)
        {
            List<string?> list = templates.ToList();
            FleetActionTemplateContext context = FleetActionTemplateContext.FromVessel(vessel);
            if (list.Any(t => FleetActionTemplateRenderer.References(t, FleetActionTemplateRenderer.HealthSummaryVariable)))
                context.HealthSummary = await _HealthSummary.BuildAsync(tenantId, vessel.Id, token).ConfigureAwait(false);
            context.InputValues = run.InputValues;
            if (run.Kind == FleetActionKindEnum.Mission && list.Any(t => FleetActionTemplateRenderer.References(t, "scan.summary")) && !String.IsNullOrWhiteSpace(run.ScanSourceActionKey))
                context.ScanSummary = await FindScanSummaryAsync(tenantId, vessel.Id, run.ScanSourceActionKey!, token).ConfigureAwait(false);
            return context;
        }

        // A Command template that names an ecosystem command which resolved to nothing has nothing to run.
        private async Task<bool> SkipUnresolvedCommandAsync(FleetActionRun run, FleetActionRunTarget target, Vessel vessel, string? template, FleetActionTemplateContext context, CancellationToken token)
        {
            if (FleetActionTemplateRenderer.References(template, FleetActionTemplateRenderer.BuildCommandVariable) && String.IsNullOrWhiteSpace(context.BuildCommand))
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.NoBuildCommand, null,
                    "Vessel " + vessel.Name + " has no build command: none is configured and no detected ecosystem supplies one.", token).ConfigureAwait(false);
                return true;
            }

            if (run.Kind != FleetActionKindEnum.Command) return false;
            foreach (string variable in FleetActionTemplateRenderer.ResolvedCommandVariables)
            {
                if (variable == FleetActionTemplateRenderer.BuildCommandVariable || !FleetActionTemplateRenderer.References(template, variable)) continue;
                string value = FleetActionTemplateRenderer.Render("{{" + variable + "}}", context);
                if (!String.IsNullOrWhiteSpace(value)) continue;
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.NoMatchingEcosystem, null,
                    "No command resolves for {{" + variable + "}} on vessel " + vessel.Name + " (detected ecosystems: "
                    + (String.IsNullOrEmpty(context.Ecosystems) ? "none" : context.Ecosystems) + ").", token).ConfigureAwait(false);
                return true;
            }

            return false;
        }

        // Runs the action's applicability check; false means the target was completed as Skipped NotApplicable (or failed).
        private async Task<bool> IsApplicableAsync(FleetActionRun run, FleetActionRunTarget target, Vessel vessel, FleetActionExecutorSelection selection, string workingDirectory,
            string? applicabilityTemplate, FleetActionTemplateContext context, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(applicabilityTemplate)) return true;

            string check;
            try
            {
                check = FleetActionTemplateRenderer.Render(applicabilityTemplate, context);
            }
            catch (FleetActionTemplateException e)
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, FleetActionReasonCodes.TemplateError, e.Message, token).ConfigureAwait(false);
                return false;
            }

            HostCommandRequest request = selection.IsRemote
                ? FleetActionShellCommandBuilder.BuildRemote(check, workingDirectory, _ApplicabilityTimeoutMs, selection.HarborOsPlatform)
                : FleetActionShellCommandBuilder.BuildLocal(check, workingDirectory, _ApplicabilityTimeoutMs);
            request.Kind = HostCommandKindEnum.Command;
            request.Label = "Fleet action applicability";
            await CommandAudit.RecordAsync(_Database, new CommandAuditRecord
            {
                Source = "FleetAction",
                Command = check,
                WorkingDirectory = workingDirectory,
                Host = selection.IsRemote ? ("Harbor " + vessel.PreferredHarborId) : "Admiral",
                TenantId = run.TenantId,
                UserId = run.UserId,
                VesselId = vessel.Id,
                EntityType = "FleetActionRun",
                EntityId = run.Id
            }, _Logging, token).ConfigureAwait(false);

            HostCommandResult result = await selection.Executor.RunAsync(request, token).ConfigureAwait(false);
            if (!result.TimedOut && result.ExitCode == 0) return true;

            string detail = result.TimedOut
                ? "The applicability check timed out."
                : ("The applicability check exited with code " + result.ExitCode + "; the action does not apply to this vessel.");
            await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.NotApplicable, null,
                TruncateTail((detail + "\n" + result.StandardError).Trim(), _Settings.FleetActions.MaxOutputBytes, out bool _), token).ConfigureAwait(false);
            return false;
        }

        private async Task<string?> ResolveWorkerOnlyPipelineIdAsync(string tenantId, CancellationToken token)
        {
            Pipeline? pipeline = await _Database.Pipelines.ReadByNameAsync(tenantId, _WorkerOnlyPipelineName, token).ConfigureAwait(false)
                ?? await _Database.Pipelines.ReadByNameAsync(_WorkerOnlyPipelineName, token).ConfigureAwait(false);
            return pipeline?.Id;
        }

        private static string BuildOutputSummary(int exitCode, string? standardOutput, string? standardError)
        {
            string combined = ((standardOutput ?? String.Empty).TrimEnd() + "\n" + (standardError ?? String.Empty).TrimEnd()).Trim();
            string tail = TruncateTail(combined, _FailureSummaryBytes, out bool truncated) ?? String.Empty;
            return "Exit code " + exitCode + "." + (tail.Length == 0 ? " No output." : ("\n" + (truncated ? "Last output (truncated):\n" : "Output:\n") + tail));
        }

        private async Task<string> FindScanSummaryAsync(string tenantId, string vesselId, string sourceKey, CancellationToken token)
        {
            FleetAction? source = await _Database.FleetActions.ReadAsync(tenantId, sourceKey, token).ConfigureAwait(false)
                ?? await _Database.FleetActions.ReadByBuiltInKeyAsync(tenantId, sourceKey, token).ConfigureAwait(false);
            if (source == null) return String.Empty;
            FleetActionRunTarget? latest = null;
            int pageNumber = 1;
            while (true)
            {
                EnumerationResult<FleetActionRun> page = await _Database.FleetActionRuns.EnumerateAsync(tenantId, new EnumerationQuery { PageNumber = pageNumber, PageSize = 100 }, token).ConfigureAwait(false);
                foreach (FleetActionRun previous in page.Objects)
                {
                    if (previous.ActionId != source.Id || previous.Kind != FleetActionKindEnum.Command || previous.CompletedUtc == null
                        || (previous.Status != FleetActionRunStatusEnum.Completed && previous.Status != FleetActionRunStatusEnum.CompletedWithFailures)) continue;
                    foreach (FleetActionRunTarget candidate in await _Database.FleetActionRunTargets.ReadAllByRunAsync(previous.Id, token).ConfigureAwait(false))
                    {
                        if (candidate.VesselId == vesselId && candidate.CompletedUtc.HasValue && candidate.ScanSummary != null
                            && (latest == null || candidate.CompletedUtc > latest.CompletedUtc)) latest = candidate;
                    }
                }
                if (pageNumber >= page.TotalPages) break;
                pageNumber++;
            }
            return latest?.ScanSummary ?? String.Empty;
        }

        private async Task AcquireGlobalSlotAsync(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                lock (_GlobalLock)
                {
                    if (_GlobalInFlight < _Settings.FleetActions.MaxConcurrency)
                    {
                        _GlobalInFlight++;
                        return;
                    }
                }

                await Task.Delay(GlobalSlotPollMs, token).ConfigureAwait(false);
            }
        }

        private void ReleaseGlobalSlot()
        {
            lock (_GlobalLock)
            {
                if (_GlobalInFlight > 0) _GlobalInFlight--;
            }
        }

        private async Task SyncMissionRunAsync(string runId, CancellationToken token)
        {
            SemaphoreSlim runLock = GetRunLock(runId);
            await runLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                FleetActionRun? run = await _Database.FleetActionRuns.ReadAsync(runId, token).ConfigureAwait(false);
                if (run == null || IsTerminal(run.Status) || run.Kind != FleetActionKindEnum.Mission) return;

                if (!run.StartedUtc.HasValue || run.Status == FleetActionRunStatusEnum.Pending)
                {
                    run.StartedUtc ??= _UtcNow();
                    run.Status = FleetActionRunStatusEnum.Running;
                    run.LastUpdateUtc = _UtcNow();
                    run = await _Database.FleetActionRuns.UpdateAsync(run, token).ConfigureAwait(false);
                }

                List<FleetActionRunTarget> targets = await _Database.FleetActionRunTargets.ReadAllByRunAsync(runId, token).ConfigureAwait(false);

                foreach (FleetActionRunTarget target in targets.Where(t => t.Status == FleetActionTargetStatusEnum.Running && !String.IsNullOrEmpty(t.VoyageId)))
                {
                    token.ThrowIfCancellationRequested();
                    await ApplyVoyageOutcomeAsync(run, target, token).ConfigureAwait(false);
                }

                int active = targets.Count(t => t.Status == FleetActionTargetStatusEnum.Running);
                foreach (FleetActionRunTarget target in targets
                    .Where(t => t.Status == FleetActionTargetStatusEnum.Pending)
                    .OrderBy(t => t.CreatedUtc)
                    .ThenBy(t => t.Id, StringComparer.Ordinal)
                    .ToList())
                {
                    if (active >= run.Concurrency) break;
                    token.ThrowIfCancellationRequested();
                    bool running = await DispatchMissionTargetAsync(run, target, token).ConfigureAwait(false);
                    if (running) active++;
                }

                await RefreshRunCoreAsync(runId, token).ConfigureAwait(false);
            }
            finally
            {
                runLock.Release();
            }
        }

        private async Task<bool> ApplyVoyageOutcomeAsync(FleetActionRun run, FleetActionRunTarget target, CancellationToken token)
        {
            if (_MissionDispatcher == null) return false;

            FleetActionVoyageOutcomeEnum outcome = await _MissionDispatcher.GetOutcomeAsync(target.VoyageId!, token).ConfigureAwait(false);
            if (outcome == FleetActionVoyageOutcomeEnum.Succeeded || outcome == FleetActionVoyageOutcomeEnum.Failed)
            {
                // The captain's final message is the target's output: for a report-only action it is the report.
                string? report = await _MissionDispatcher.GetReportAsync(target.VoyageId!, token).ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(report))
                {
                    target.OutputText = TruncateHead(report, _Settings.FleetActions.MaxOutputBytes, out bool reportTruncated);
                    target.OutputTruncated = reportTruncated;
                }
            }

            switch (outcome)
            {
                case FleetActionVoyageOutcomeEnum.Succeeded:
                    await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Succeeded, null, null, null, token).ConfigureAwait(false);
                    return true;
                case FleetActionVoyageOutcomeEnum.Failed:
                    await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, FleetActionReasonCodes.VoyageFailed, null, token).ConfigureAwait(false);
                    return true;
                case FleetActionVoyageOutcomeEnum.Cancelled:
                    await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Cancelled, null, null, null, token).ConfigureAwait(false);
                    return true;
                case FleetActionVoyageOutcomeEnum.Missing:
                    await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, FleetActionReasonCodes.VoyageMissing,
                        "Voyage " + target.VoyageId + " no longer exists.", token).ConfigureAwait(false);
                    return true;
                default:
                    return false;
            }
        }

        private async Task<bool> DispatchMissionTargetAsync(FleetActionRun run, FleetActionRunTarget target, CancellationToken token)
        {

            if (_MissionDispatcher == null)
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, FleetActionReasonCodes.DispatchUnavailable,
                    "No mission dispatcher is configured on this server.", token).ConfigureAwait(false);
                return false;
            }

            string tenantId = String.IsNullOrEmpty(run.TenantId) ? Constants.DefaultTenantId : run.TenantId!;
            Vessel? vessel = await _Database.Vessels.ReadAsync(tenantId, target.VesselId, token).ConfigureAwait(false);
            if (vessel == null)
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.VesselNotFound, null, null, token).ConfigureAwait(false);
                return false;
            }

            if (run.RequiresNetwork && (_Settings.FleetActions.Offline || _Settings.FleetActions.OfflineHarborIds.Contains(vessel.PreferredHarborId ?? String.Empty)))
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.NetworkUnavailable, null, null, token).ConfigureAwait(false);
                return false;
            }

            FleetActionExecutorSelection? checkSelection = null;
            bool checkWindows = OperatingSystem.IsWindows();
            bool canCheck = !String.IsNullOrWhiteSpace(run.ApplicabilityCommand) || !String.IsNullOrWhiteSpace(run.ApplicabilityCommandWindows);
            if (canCheck && !String.IsNullOrWhiteSpace(vessel.WorkingDirectory))
            {
                // The check is best effort for Mission targets: a vessel whose checkout cannot be reached is dispatched,
                // and the prompt still tells the captain what to do when the change does not apply.
                checkSelection = await ResolveExecutorAsync(vessel, token).ConfigureAwait(false);
                if (checkSelection != null && !checkSelection.IsRemote && !Directory.Exists(vessel.WorkingDirectory)) checkSelection = null;
                if (checkSelection != null && checkSelection.IsRemote) checkWindows = FleetActionShellCommandBuilder.IsWindowsPlatform(checkSelection.HarborOsPlatform);
            }

            string? applicabilityTemplate = checkSelection == null ? null : FleetActionShellCommandBuilder.SelectCommand(run.ApplicabilityCommand, run.ApplicabilityCommandWindows, checkWindows);
            FleetActionTemplateContext context = await BuildContextAsync(new string?[] { run.PromptTemplate, applicabilityTemplate }, vessel, tenantId, run, token).ConfigureAwait(false);
            if (await SkipUnresolvedCommandAsync(run, target, vessel, run.PromptTemplate, context, token).ConfigureAwait(false)) return false;

            string prompt;
            try
            {
                prompt = FleetActionTemplateRenderer.Render(run.PromptTemplate, context);
            }
            catch (FleetActionTemplateException e)
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, FleetActionReasonCodes.TemplateError, e.Message, token).ConfigureAwait(false);
                return false;
            }

            target.RenderedText = prompt;
            if (checkSelection != null && !await IsApplicableAsync(run, target, vessel, checkSelection, vessel.WorkingDirectory!, applicabilityTemplate, context, token).ConfigureAwait(false))
                return false;

            bool readOnly = MissionModeContract.IsReadOnly(run.MissionMode);
            string? pipelineId = run.PipelineId;
            if (readOnly && String.IsNullOrWhiteSpace(pipelineId)) pipelineId = await ResolveWorkerOnlyPipelineIdAsync(tenantId, token).ConfigureAwait(false);

            DispatchValidationResult validation = await _MissionDispatcher.ValidateAsync(vessel, pipelineId, token).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Skipped, FleetActionReasonCodes.DispatchRejected, null,
                    validation.Message ?? validation.Error.ToString(), token).ConfigureAwait(false);
                return false;
            }

            string title = "Fleet action: " + run.ActionName + " (" + vessel.Name + ")";
            target.StartedUtc = _UtcNow();

            string voyageId;
            try
            {
                FleetActionMissionDispatch dispatch = new FleetActionMissionDispatch(vessel)
                {
                    Title = title,
                    Prompt = prompt,
                    PipelineId = validation.ResolvedPipelineId ?? pipelineId,
                    Persona = run.Persona,
                    ApprovalOptions = run.ApprovalOptions,
                    MissionMode = run.MissionMode,
                    // A report-only mission has nothing to land.
                    LandingMode = readOnly ? LandingModeEnum.None : null
                };
                voyageId = await _MissionDispatcher.DispatchAsync(dispatch, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, FleetActionReasonCodes.DispatchFailed, e.Message, token).ConfigureAwait(false);
                return false;
            }

            target.VoyageId = voyageId;
            target.Status = FleetActionTargetStatusEnum.Running;
            target.LastUpdateUtc = _UtcNow();
            await _Database.FleetActionRunTargets.UpdateAsync(target, token).ConfigureAwait(false);
            return true;
        }

        private async Task CancelMissionTargetAsync(FleetActionRun run, FleetActionRunTarget target, CancellationToken token)
        {
            string? errorText = null;
            if (_MissionDispatcher != null && !String.IsNullOrEmpty(target.VoyageId))
            {
                bool finished = await ApplyVoyageOutcomeAsync(run, target, token).ConfigureAwait(false);
                if (finished) return;
                try
                {
                    await _MissionDispatcher.CancelVoyageAsync(target.VoyageId!, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // The run is already Cancelled: record why this voyage may still be running instead of aborting the
                    // cancel part way, which left the remaining targets Running under a Cancelled run.
                    _Logging.Warn(_Header + "could not cancel voyage " + target.VoyageId + " of run " + run.Id + ": " + ex.Message);
                    errorText = "Cancelling voyage " + target.VoyageId + " failed: " + ex.Message + ". Cancel the voyage directly.";
                }
            }

            await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Cancelled, null, null, errorText, token).ConfigureAwait(false);
        }

        private async Task CompleteTargetAsync(
            FleetActionKindEnum kind,
            FleetActionRunTarget target,
            FleetActionTargetStatusEnum status,
            string? skipReason,
            string? failureReason,
            string? errorText,
            CancellationToken token)
        {
            target.Status = status;
            target.SkipReason = skipReason;
            target.FailureReason = failureReason;
            if (errorText != null) target.ErrorText = errorText;
            StampCompletion(target);
            await _Database.FleetActionRunTargets.UpdateAsync(target, CancellationToken.None).ConfigureAwait(false);
            RecordMetrics(kind, target);
        }

        private async Task CancelTargetIfUnfinishedAsync(FleetActionRun run, string targetId)
        {
            try
            {
                SemaphoreSlim runLock = GetRunLock(run.Id);
                await runLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    FleetActionRunTarget? target = await _Database.FleetActionRunTargets.ReadAsync(targetId).ConfigureAwait(false);
                    if (target == null) return;
                    if (target.Status != FleetActionTargetStatusEnum.Pending && target.Status != FleetActionTargetStatusEnum.Running) return;
                    await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Cancelled, null, null, null, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    runLock.Release();
                }
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "could not record cancellation of target " + targetId + ": " + e.Message);
            }
        }

        private async Task FailTargetSafeAsync(FleetActionRun run, string targetId, string reason, string message)
        {
            try
            {
                FleetActionRunTarget? target = await _Database.FleetActionRunTargets.ReadAsync(targetId).ConfigureAwait(false);
                if (target == null) return;
                if (target.Status != FleetActionTargetStatusEnum.Pending && target.Status != FleetActionTargetStatusEnum.Running) return;
                await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, reason, message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "could not record failure of target " + targetId + ": " + e.Message);
            }
        }

        private async Task FailRunSafeAsync(string runId, string message)
        {
            try
            {
                SemaphoreSlim runLock = GetRunLock(runId);
                await runLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    FleetActionRun? run = await _Database.FleetActionRuns.ReadAsync(runId).ConfigureAwait(false);
                    if (run == null || IsTerminal(run.Status)) return;

                    List<FleetActionRunTarget> targets = await _Database.FleetActionRunTargets.ReadAllByRunAsync(runId).ConfigureAwait(false);
                    foreach (FleetActionRunTarget target in targets.Where(t => t.Status == FleetActionTargetStatusEnum.Pending || t.Status == FleetActionTargetStatusEnum.Running))
                    {
                        await CompleteTargetAsync(run.Kind, target, FleetActionTargetStatusEnum.Failed, null, FleetActionReasonCodes.ExecutionError, message, CancellationToken.None).ConfigureAwait(false);
                    }

                    run.Status = FleetActionRunStatusEnum.Failed;
                    run.LastUpdateUtc = _UtcNow();
                    await _Database.FleetActionRuns.UpdateAsync(run).ConfigureAwait(false);
                    await RefreshRunCoreAsync(runId, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    runLock.Release();
                }
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "could not mark run " + runId + " failed: " + e.Message);
            }
        }

        private async Task MarkRunStartedAsync(string runId)
        {
            SemaphoreSlim runLock = GetRunLock(runId);
            await runLock.WaitAsync().ConfigureAwait(false);
            try
            {
                FleetActionRun? run = await _Database.FleetActionRuns.ReadAsync(runId).ConfigureAwait(false);
                if (run == null || IsTerminal(run.Status)) return;
                run.StartedUtc ??= _UtcNow();
                run.Status = FleetActionRunStatusEnum.Running;
                run.LastUpdateUtc = _UtcNow();
                await _Database.FleetActionRuns.UpdateAsync(run).ConfigureAwait(false);
            }
            finally
            {
                runLock.Release();
            }
        }

        private async Task RefreshRunSafeAsync(string runId)
        {
            try
            {
                await RefreshRunAsync(runId).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "could not refresh run " + runId + ": " + e.Message);
            }
        }

        private async Task RefreshRunAsync(string runId, CancellationToken token = default)
        {
            SemaphoreSlim runLock = GetRunLock(runId);
            await runLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await RefreshRunCoreAsync(runId, token).ConfigureAwait(false);
            }
            finally
            {
                runLock.Release();
            }
        }

        private async Task RefreshRunCoreAsync(string runId, CancellationToken token)
        {
            FleetActionRun? run = await _Database.FleetActionRuns.ReadAsync(runId, token).ConfigureAwait(false);
            if (run == null) return;

            List<FleetActionRunTarget> targets = await _Database.FleetActionRunTargets.ReadAllByRunAsync(runId, token).ConfigureAwait(false);
            run.TargetCount = targets.Count;
            run.FindingsCount = targets.Count(t => t.Status == FleetActionTargetStatusEnum.Findings);
            run.SucceededCount = targets.Count(t => t.Status == FleetActionTargetStatusEnum.Succeeded);
            run.FailedCount = targets.Count(t => t.Status == FleetActionTargetStatusEnum.Failed || t.Status == FleetActionTargetStatusEnum.TimedOut);
            run.SkippedCount = targets.Count(t => t.Status == FleetActionTargetStatusEnum.Skipped);
            run.CancelledCount = targets.Count(t => t.Status == FleetActionTargetStatusEnum.Cancelled);

            bool unresolved = targets.Any(t => t.Status == FleetActionTargetStatusEnum.Pending || t.Status == FleetActionTargetStatusEnum.Running);
            bool sticky = run.Status == FleetActionRunStatusEnum.Cancelled || run.Status == FleetActionRunStatusEnum.Failed;

            if (sticky)
            {
                if (!unresolved && !run.CompletedUtc.HasValue) run.CompletedUtc = _UtcNow();
            }
            else if (unresolved)
            {
                bool started = run.StartedUtc.HasValue || targets.Any(t => t.Status != FleetActionTargetStatusEnum.Pending);
                run.Status = started ? FleetActionRunStatusEnum.Running : FleetActionRunStatusEnum.Pending;
                if (started && !run.StartedUtc.HasValue) run.StartedUtc = _UtcNow();
            }
            else
            {
                run.Status = run.FailedCount > 0 ? FleetActionRunStatusEnum.CompletedWithFailures : FleetActionRunStatusEnum.Completed;
                run.StartedUtc ??= _UtcNow();
                if (!run.CompletedUtc.HasValue) run.CompletedUtc = _UtcNow();
            }

            run.LastUpdateUtc = _UtcNow();
            await _Database.FleetActionRuns.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
        }

        private void StampCompletion(FleetActionRunTarget target)
        {
            DateTime now = _UtcNow();
            target.CompletedUtc = now;
            target.LastUpdateUtc = now;
            if (target.StartedUtc.HasValue) target.DurationMs = (long)(now - target.StartedUtc.Value).TotalMilliseconds;
        }

        private static string AppendLine(string? existing, string line)
        {
            if (String.IsNullOrEmpty(existing)) return line;
            return existing + Environment.NewLine + line;
        }

        private void RecordMetrics(FleetActionKindEnum kind, FleetActionRunTarget target)
        {
            try
            {
                KeyValuePair<string, object?> kindTag = new KeyValuePair<string, object?>("kind", kind.ToString());
                ArmadaMetrics.FleetActionTargets.Add(1, kindTag, new KeyValuePair<string, object?>("outcome", target.Status.ToString()));

                bool executed = target.Status == FleetActionTargetStatusEnum.Findings
                    || target.Status == FleetActionTargetStatusEnum.Succeeded
                    || target.Status == FleetActionTargetStatusEnum.Failed
                    || target.Status == FleetActionTargetStatusEnum.TimedOut;
                if (executed && target.DurationMs.HasValue)
                    ArmadaMetrics.FleetActionTargetDuration.Record(target.DurationMs.Value / 1000.0, kindTag);
            }
            catch (Exception e)
            {
                _Logging.Debug(_Header + "metric emission failed: " + e.Message);
            }
        }

        #endregion
    }
}
