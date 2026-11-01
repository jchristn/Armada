namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Runs account usage captures: the single-flight gate and persisted lease per group (so multiple
    /// ticks, manual captures and server restarts cannot launch duplicate probes), the per-host and
    /// overall concurrency gates (separate from mission launch slots), the dedicated UsageProbe job each
    /// attempt owns, the dispatch through the host-command seam (Local executor on the Admiral's host,
    /// the remote executor over the link on a Harbor), and the commit of one sanitized snapshot with its
    /// member links, memberships and latest state. A result is never applied after its lease expired (a
    /// lease whose clock starts at dispatch, so waiting for a concurrency slot consumes it), after its
    /// settings generation changed, after capture was disabled, or once its group has no member left to
    /// attribute: the attempt reports the discard instead, and an invalidation cancels the probes it
    /// orphaned. Failures back off at
    /// 2x the effective interval per consecutive failure up to the configured bound, with jitter applied,
    /// and honor a longer provider Retry-After; success resets the backoff; AuthMissing retries slowly;
    /// Unsupported retries only on a runtime, version or configuration change; a disconnected host is a
    /// Failed attempt with its last good data retained and no tight retry loop. Capture never borrows a
    /// captain's Working/Idle state and never touches a mission launch slot.
    /// </summary>
    public class UsageCaptureService
    {
        #region Public-Members

        /// <summary>
        /// The current settings generation. A result from an attempt whose captured generation is no
        /// current is discarded rather than applied.
        /// </summary>
        public long Generation
        {
            get => Interlocked.Read(ref _Generation);
        }

        /// <summary>
        /// How long a persisted lease (an active UsageProbe job for the group) holds a group after the
        /// last update: the bounded round-trip deadline (the host execution deadline plus the reply
        /// grace), doubled, so a restart neither replays a probe whose result may still arrive nor waits
        /// for the stale-job reaper.
        /// </summary>
        public TimeSpan LeaseWindow
        {
            get => TimeSpan.FromSeconds(2 * (_Settings.UsageCapture.ProbeTimeoutSeconds + ReplyGraceSeconds));
        }

        #endregion

        #region Private-Members

        private const int ReplyGraceSeconds = 15;
        private const int AuthMissingRetrySeconds = 3600;

        private string _Header = "[UsageCaptureService] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly UsageAccountResolver _Accounts;
        private readonly IHostCommandExecutor _LocalExecutor;
        private readonly HarborConnectionManager? _Harbors;
        private readonly LoggingModule _Logging;
        private readonly JobService _Jobs;
        private readonly TimeProvider _Clock;
        private readonly Func<double> _Jitter;
        private readonly CancellationToken _Lifetime;
        private long _Generation = 1;
        private readonly object _ConfigurationLock = new object();
        private CancellationTokenSource _ConfigurationCancellation = null!;
        private readonly ConcurrentDictionary<string, GroupState> _GroupStates = new ConcurrentDictionary<string, GroupState>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, InFlightAttempt> _InFlight = new ConcurrentDictionary<string, InFlightAttempt>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _StartLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _HostGates = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _OverallGate;
        private readonly Random _JitterRandom = new Random();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database, for jobs, snapshots, memberships and latest state.</param>
        /// <param name="settings">Server settings (capture bounds and the server-wide switch).</param>
        /// <param name="accounts">Account resolver (host identity and membership grouping).</param>
        /// <param name="localExecutor">Host-command executor for the Admiral's own host; its usage probe
        /// helper runs in-process. A LocalHostCommandExecutor with its UsageProbe set is the usual wiring.</param>
        /// <param name="harbors">Harbor connections, or null when Harbor delegation is not available.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="clock">Clock, for fake-clock scheduling tests; null uses the system clock.</param>
        /// <param name="jitter">Jitter factor in [0.9, 1.1] for due times, for deterministic tests; null
        /// draws uniformly at random.</param>
        /// <param name="lifetime">Cancellation token bound to the process lifetime; it stops in-flight probes.</param>
        public UsageCaptureService(
            DatabaseDriver database,
            ArmadaSettings settings,
            UsageAccountResolver accounts,
            IHostCommandExecutor localExecutor,
            HarborConnectionManager? harbors,
            LoggingModule logging,
            TimeProvider? clock = null,
            Func<double>? jitter = null,
            CancellationToken lifetime = default)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _LocalExecutor = localExecutor ?? throw new ArgumentNullException(nameof(localExecutor));
            _Harbors = harbors;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Jobs = new JobService(database, logging);
            _Clock = clock ?? TimeProvider.System;
            _Jitter = jitter ?? DefaultJitter;
            _Lifetime = lifetime;
            _ConfigurationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            _OverallGate = new SemaphoreSlim(Math.Max(1, settings.UsageCapture.MaximumConcurrentProbes));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Invalidate the scheduling generation and clear per-group backoff and eligibility: a settings or
        /// configuration change cancels the work it still has in flight (running probes stop at their host
        /// seam and their results are discarded rather than applied), and groups are recomputed by the next
        /// rebuild, with Unsupported sources eligible again because the configuration changed.
        /// </summary>
        public void Invalidate()
        {
            lock (_ConfigurationLock)
            {
                Interlocked.Increment(ref _Generation);
                _GroupStates.Clear();
                // Cancel the probes a configuration change orphaned, then arm the next generation's
                // cancellation: a cancelled attempt reports its discard instead of a stale write.
                _ConfigurationCancellation.Cancel();
                _ConfigurationCancellation.Dispose();
                _ConfigurationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_Lifetime);
            }

            _Logging.Info(_Header + "settings generation invalidated to " + Generation + "; in-flight captures stop and groups reschedule");
        }

        /// <summary>
        /// The cancellation token an attempt runs on: the process lifetime plus the current configuration
        /// generation, so an invalidation stops the probes it orphaned.
        /// </summary>
        /// <returns>The attempt's cancellation token source, disposed by the attempt when it ends.</returns>
        private CancellationTokenSource AttemptCancellation()
        {
            lock (_ConfigurationLock)
            {
                return CancellationTokenSource.CreateLinkedTokenSource(_Lifetime, _ConfigurationCancellation.Token);
            }
        }

        /// <summary>
        /// The service's notion of now (UTC), so a scheduler sharing its fake clock sees the same time.
        /// </summary>
        /// <returns>The current time.</returns>
        public DateTime NowUtc()
        {
            return Now();
        }

        /// <summary>
        /// When the group is next eligible for a capture, or null when it is not scheduled again (an
        /// Unsupported source retries only on a runtime, version or configuration change).
        /// </summary>
        /// <param name="groupKey">Group key.</param>
        /// <returns>The next eligible time, or null.</returns>
        public DateTime? NextEligibleUtc(string groupKey)
        {
            return _GroupStates.TryGetValue(groupKey, out GroupState? state) ? state.NextEligibleUtc : null;
        }

        /// <summary>
        /// The group's consecutive failure count (its backoff exponent), or 0 when it has none.
        /// </summary>
        /// <param name="groupKey">Group key.</param>
        /// <returns>The failure count.</returns>
        public int FailureCount(string groupKey)
        {
            return _GroupStates.TryGetValue(groupKey, out GroupState? state) ? state.FailureCount : 0;
        }

        /// <summary>
        /// Whether an attempt for the group is running right now (its in-flight flight).
        /// </summary>
        /// <param name="groupKey">Group key.</param>
        /// <returns>True when in flight.</returns>
        public bool IsInFlight(string groupKey)
        {
            return _InFlight.ContainsKey(groupKey);
        }

        /// <summary>
        /// Schedule a group's first observed due time: the effective interval with jitter, floored by the
        /// server-wide minimum, so rebuilt groups start randomized instead of stampeding.
        /// </summary>
        /// <param name="group">Group.</param>
        public void ScheduleInitial(UsageCaptureGroup group)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));
            GroupState state = _GroupStates.GetOrAdd(group.GroupKey, _ => new GroupState());
            lock (state)
            {
                // A group that already answered (even Unsupported, which is deliberately not scheduled
                // again) keeps its own schedule: only a never-observed group, or one the invalidation
                // cleared, gets a fresh randomized due time.
                if (state.Observed || state.NextEligibleUtc.HasValue) return;
                DateTime now = Now();
                state.NextEligibleUtc = now.AddSeconds(EffectiveDueSeconds(group.EffectiveIntervalSeconds));
            }
        }

        /// <summary>
        /// Start (or join) one capture attempt for a group. A request for a group whose attempt is
        /// already running joins it and returns its job id; a group whose unexpired persisted lease is
        /// held reports it; otherwise a new dedicated UsageProbe job is enqueued and the attempt runs in
        /// the background, bounded by the per-host and overall concurrency gates. The returned receipt
        /// does not wait for the attempt: await its Completion for the committed snapshot.
        /// </summary>
        /// <param name="group">Group to capture.</param>
        /// <param name="triggeringCaptainId">Captain that asked for the capture, when one did; recorded as
        /// the snapshot's triggering captain (never the account owner). Null for scheduler ticks.</param>
        /// <param name="token">Cancellation token for the request itself; the attempt runs on the service's
        /// lifetime token.</param>
        /// <returns>The capture receipt.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the group is null.</exception>
        public async Task<UsageCaptureAttempt> TryStartAsync(UsageCaptureGroup group, string? triggeringCaptainId, CancellationToken token = default)
        {
            if (group == null) throw new ArgumentNullException(nameof(group));

            DateTime now = Now();

            if (!_Settings.UsageCapture.Enabled)
                return Skipped(null, null, "CaptureDisabled");

            if (group.MemberCaptainIds.Count == 0)
                return Skipped(null, NextEligibleUtc(group.GroupKey), "NoMembers");

            // Cooldown is checked before the start lock too, so a manual capture that arrives while a
            // flight is being set up does not pay for the lock when it is simply not eligible.
            DateTime? eligible = NextEligibleUtc(group.GroupKey);
            if (eligible.HasValue && eligible.Value > now && !_InFlight.ContainsKey(group.GroupKey))
                return Skipped(null, eligible, "Cooldown");

            SemaphoreSlim startLock = _StartLocks.GetOrAdd(group.GroupKey, _ => new SemaphoreSlim(1, 1));
            await startLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                now = Now();

                if (!_Settings.UsageCapture.Enabled)
                    return Skipped(null, null, "CaptureDisabled");

                // Manual capture (and a tick) join an existing flight: one attempt, one job id, one snapshot.
                if (_InFlight.TryGetValue(group.GroupKey, out InFlightAttempt? flight))
                    return new UsageCaptureAttempt
                    {
                        JobId = flight.JobId,
                        JoinedExisting = true,
                        Started = false,
                        NextEligibleUtc = NextEligibleUtc(group.GroupKey),
                        Completion = flight.Completion
                    };

                Job? lease = await FindActiveLeaseAsync(group, token).ConfigureAwait(false);
                if (lease != null)
                {
                    DateTime expires = lease.LastUpdateUtc + LeaseWindow;
                    if (expires > now)
                        return new UsageCaptureAttempt
                        {
                            JobId = lease.Id,
                            JoinedExisting = true,
                            Started = false,
                            SkippedReason = "LeaseHeld",
                            NextEligibleUtc = expires,
                            Completion = Task.FromResult(new UsageCaptureAttemptResult { ErrorCode = "LeaseHeld" })
                        };

                    // The prior attempt's lease expired and its result is definitively absent: fail the
                    // stale lease, then a new attempt may start.
                    await _Jobs.TryFinishAsync(lease.Id, JobStatusEnum.Failed, null, "LeaseExpired", token).ConfigureAwait(false);
                    _Logging.Info(_Header + "expired lease " + lease.Id + " for group " + group.GroupKey + " failed; a new attempt may start");
                }

                eligible = NextEligibleUtc(group.GroupKey);
                if (eligible.HasValue && eligible.Value > now)
                    return Skipped(null, eligible, "Cooldown");

                if (group.MemberCaptainIds.Count == 0)
                    return Skipped(null, eligible, "NoMembers");

                Job job = await _Jobs.EnqueueAsync(UsageAccountResolver.JobName(group.GroupKey), JobKindEnum.UsageProbe, group.TenantId, null, token).ConfigureAwait(false);
                long generation = Generation;

                // Register the flight before the attempt starts, so an instantly-finished attempt can
                // never leave a stale in-flight entry behind (and a joiner always finds the live one).
                TaskCompletionSource<UsageCaptureAttemptResult> completionSource = new TaskCompletionSource<UsageCaptureAttemptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                InFlightAttempt attempt = new InFlightAttempt(job.Id, completionSource.Task);
                _InFlight[group.GroupKey] = attempt;

                CancellationTokenSource attemptCancellation = AttemptCancellation();
                _ = RunAttemptAsync(job, group, triggeringCaptainId, generation, attempt, completionSource, attemptCancellation);

                return new UsageCaptureAttempt
                {
                    JobId = job.Id,
                    JoinedExisting = false,
                    Started = true,
                    NextEligibleUtc = eligible,
                    Completion = completionSource.Task
                };
            }
            finally
            {
                startLock.Release();
            }
        }

        /// <summary>
        /// Close every open account membership of a captain (capture was disabled, the captain moved
        /// hosts or its credentials changed elsewhere); idempotent.
        /// </summary>
        /// <param name="tenantId">Tenant.</param>
        /// <param name="captainId">Captain.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task CloseMembershipsAsync(string tenantId, string captainId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(tenantId) || String.IsNullOrWhiteSpace(captainId)) return;
            try
            {
                int closed = await _Database.UsageMemberships.CloseAsync(tenantId, captainId, Now(), token).ConfigureAwait(false);
                if (closed > 0) _Logging.Debug(_Header + "closed " + closed + " usage membership(s) of captain " + captainId);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "could not close usage memberships of captain " + captainId + ": " + e.Message);
            }
        }

        #endregion

        #region Private-Methods

        private async Task RunAttemptAsync(Job job, UsageCaptureGroup group, string? triggeringCaptainId, long generation, InFlightAttempt flight, TaskCompletionSource<UsageCaptureAttemptResult> completion, CancellationTokenSource attemptCancellation)
        {
            UsageCaptureAttemptResult result;
            try
            {
                result = await RunAttemptCoreAsync(job, group, triggeringCaptainId, generation, attemptCancellation.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "capture of group " + group.GroupKey + " crashed: " + e.ToString());
                result = new UsageCaptureAttemptResult { ErrorCode = "CaptureAttemptCrashed", DiscardedReason = "CaptureAttemptCrashed" };
            }
            finally
            {
                attemptCancellation.Dispose();
                RemoveFlight(group.GroupKey, flight);
            }

            completion.TrySetResult(result);
        }

        private async Task<UsageCaptureAttemptResult> RunAttemptCoreAsync(Job job, UsageCaptureGroup group, string? triggeringCaptainId, long generation, CancellationToken attemptToken)
        {
            try
            {
                // The lease clock starts at dispatch, before the concurrency gates: an attempt that waits
                // for a free slot consumes its own lease, exactly the persisted lease another process or a
                // restart would see.
                DateTime dispatched = Now();
                DateTime leaseDeadline = dispatched + LeaseWindow;

                if (!await _Jobs.TryStartAsync(job, 10, _Lifetime).ConfigureAwait(false))
                    return new UsageCaptureAttemptResult { DiscardedReason = "JobNotStartable", ErrorCode = "JobNotStartable" };

                await _OverallGate.WaitAsync(attemptToken).ConfigureAwait(false);
                SemaphoreSlim hostGate = HostGate(group.HostId);
                await hostGate.WaitAsync(attemptToken).ConfigureAwait(false);
                try
                {
                    // Before the credential host is touched at all: an attempt whose lease expired while it
                    // waited for a free slot never runs the probe it can no longer answer for.
                    if (Now() > leaseDeadline)
                        return await DiscardAsync(job, "LeaseExpired",
                            UsageProbeResult.Failed("LeaseExpired", "the capture attempt's lease expired before it reached the credential host"),
                            attemptToken).ConfigureAwait(false);

                    UsageProbeResult probe = await CaptureThroughHostAsync(job, group, attemptToken).ConfigureAwait(false);
                    DateTime finished = Now();

                    if (generation != Generation)
                        return await DiscardAsync(job, "SettingsGenerationChanged", probe, attemptToken).ConfigureAwait(false);

                    // A capture that is disabled while its probe was held is a stale write too: the
                    // operator asked for the account not to be read, and the answer is never applied.
                    if (!_Settings.UsageCapture.Enabled)
                        return await DiscardAsync(job, "CaptureDisabled", probe, attemptToken).ConfigureAwait(false);

                    // No result may be applied after its lease expired: a slow attempt whose window passed
                    // reports the discard instead of overwriting the replacement attempt's state.
                    if (finished > leaseDeadline)
                        return await DiscardAsync(job, "LeaseExpired", probe, attemptToken).ConfigureAwait(false);

                    // A member that verifiably stopped capturing (or moved to another group) while the probe
                    // ran is not attributed: only the members still in this configuration are written.
                    List<string> members = await EffectiveMembersAsync(group, attemptToken).ConfigureAwait(false);
                    if (members.Count == 0)
                        return await DiscardAsync(job, "NoMembers", probe, attemptToken).ConfigureAwait(false);

                    UsageSnapshot snapshot = await CommitAsync(group, members, probe, triggeringCaptainId, finished, attemptToken).ConfigureAwait(false);
                    DateTime? next = ScheduleNext(group, probe, finished);

                    bool healthy = probe.Status == UsageSnapshotStatusEnum.Ok
                        || probe.Status == UsageSnapshotStatusEnum.Estimated
                        || probe.Status == UsageSnapshotStatusEnum.Unsupported
                        || probe.Status == UsageSnapshotStatusEnum.AuthMissing;

                    _ = await _Jobs.TryFinishAsync(job.Id,
                        healthy ? JobStatusEnum.Succeeded : JobStatusEnum.Failed,
                        healthy ? SnapshotResultJson(snapshot) : null,
                        healthy ? null : (probe.ErrorCode ?? "CaptureFailed"),
                        attemptToken).ConfigureAwait(false);

                    _Logging.Info(_Header + "capture of group " + group.GroupKey + " answered " + probe.Status
                        + (probe.ErrorCode == null ? String.Empty : " (" + probe.ErrorCode + ")")
                        + " in " + (long)(finished - dispatched).TotalMilliseconds + " ms; snapshot " + snapshot.Id);

                    return new UsageCaptureAttemptResult
                    {
                        Snapshot = snapshot,
                        Persisted = true,
                        Status = probe.Status,
                        ErrorCode = probe.ErrorCode,
                        NextEligibleUtc = next
                    };
                }
                finally
                {
                    hostGate.Release();
                    _OverallGate.Release();
                }
            }
            catch (Exception e)
            {
                // The attempt itself failed before it produced an answer: record one failure result for
                // this attempt with a stable error code, so the job and the group state stay truthful.
                UsageProbeResult failure = e is OperationCanceledException
                    ? UsageProbeResult.Failed("ProbeCancelled", "the capture attempt was cancelled")
                    : UsageProbeResult.Failed("CaptureAttemptFailed", SecretRedactor.Redact(e.Message));
                try
                {
                    // The stale-write guards apply to a failure result too: a configuration change or a
                    // disabled switch means this attempt's answer belongs to a configuration that no longer
                    // exists, and it is never persisted.
                    if (generation != Generation || !_Settings.UsageCapture.Enabled)
                    {
                        _ = await _Jobs.TryFinishAsync(job.Id, JobStatusEnum.Failed, null, "SettingsGenerationChanged", CancellationToken.None).ConfigureAwait(false);
                        return new UsageCaptureAttemptResult { Status = failure.Status, ErrorCode = failure.ErrorCode, DiscardedReason = "SettingsGenerationChanged" };
                    }

                    List<string> members = await EffectiveMembersAsync(group, CancellationToken.None).ConfigureAwait(false);
                    if (members.Count == 0)
                        return await DiscardAsync(job, "NoMembers", failure, CancellationToken.None).ConfigureAwait(false);

                    UsageSnapshot snapshot = await CommitAsync(group, members, failure, triggeringCaptainId, Now(), CancellationToken.None).ConfigureAwait(false);
                    ScheduleNext(group, failure, Now());
                    _ = await _Jobs.TryFinishAsync(job.Id, JobStatusEnum.Failed, null, failure.ErrorCode, CancellationToken.None).ConfigureAwait(false);
                    return new UsageCaptureAttemptResult { Snapshot = snapshot, Persisted = true, Status = failure.Status, ErrorCode = failure.ErrorCode };
                }
                catch (Exception commitError)
                {
                    _Logging.Warn(_Header + "capture of group " + group.GroupKey + " failed and its failure result could not be recorded: " + commitError.Message);
                    _ = await _Jobs.TryFinishAsync(job.Id, JobStatusEnum.Failed, null, failure.ErrorCode, CancellationToken.None).ConfigureAwait(false);
                    return new UsageCaptureAttemptResult { Status = failure.Status, ErrorCode = failure.ErrorCode };
                }
            }
        }

        private void RemoveFlight(string groupKey, InFlightAttempt flight)
        {
            // Remove only this flight: a superseding attempt that already registered must never be
            // unregistered by the old one's cleanup.
            if (_InFlight.TryGetValue(groupKey, out InFlightAttempt? current) && ReferenceEquals(current, flight))
                _ = ((ICollection<KeyValuePair<string, InFlightAttempt>>)_InFlight).Remove(new KeyValuePair<string, InFlightAttempt>(groupKey, flight));
        }

        private async Task<UsageProbeResult> CaptureThroughHostAsync(Job job, UsageCaptureGroup group, CancellationToken token)
        {
            IHostCommandExecutor? executor = ExecutorFor(group);
            if (executor == null)
                return UsageProbeResult.Failed("HostUnavailable", "the credential host cannot be reached; the last good observation is kept");

            UsageProbeHost host = new UsageProbeHost
            {
                TenantId = group.TenantId,
                HostId = group.HostId,
                HarborId = group.HarborId,
                Runtime = group.Runtime,
                Mode = group.Mode,
                CommandJson = group.CommandJson,
                AllowPty = group.AllowPty,
                TimeoutSeconds = _Settings.UsageCapture.ProbeTimeoutSeconds,
                ConfigurationRevision = group.ConfigurationRevision,
                DispatchedUtc = Now()
            };

            HostCommandRequest request = new HostCommandRequest
            {
                Kind = HostCommandKindEnum.UsageProbe,
                UsageProbeJson = host.Serialize(),
                TimeoutMs = _Settings.UsageCapture.ProbeTimeoutSeconds * 1000,
                Label = "usage-capture " + group.Runtime
            };

            // Cancelling the dedicated job from Jobs must really stop the probe's process tree: a small
            // watchdog links the attempt to the job row's status while the probe runs.
            using CancellationTokenSource cancellable = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task watchdog = Task.Run(() => WatchJobCancellationAsync(job.Id, cancellable));
            try
            {
                HostCommandResult result = await executor.RunAsync(request, cancellable.Token).ConfigureAwait(false);
                return Interpret(result);
            }
            finally
            {
                cancellable.Cancel();
                try { await watchdog.ConfigureAwait(false); } catch (Exception) { }
            }
        }

        private async Task WatchJobCancellationAsync(string jobId, CancellationTokenSource cancellable)
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellable.Token).ConfigureAwait(false);
                    Job? current = await _Database.Jobs.ReadAsync(jobId, CancellationToken.None).ConfigureAwait(false);
                    if (current != null && current.Status != JobStatusEnum.Queued && current.Status != JobStatusEnum.Running)
                    {
                        // The operator cancelled the capture job; the probe (and any process tree it
                        // spawned on the credential host) stops now rather than running to its deadline.
                        cancellable.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // The watchdog is best effort: the probe's own deadline still bounds the attempt.
            }
        }

        private IHostCommandExecutor? ExecutorFor(UsageCaptureGroup group)
        {
            if (String.IsNullOrWhiteSpace(group.HarborId)) return _LocalExecutor;
            if (_Harbors == null) return null;
            return new RemoteHostCommandExecutor(_Harbors, group.HarborId);
        }

        private static UsageProbeResult Interpret(HostCommandResult result)
        {
            if (result == null) return UsageProbeResult.Failed("CaptureAttemptFailed", "the host returned nothing");
            if (result.TimedOut) return UsageProbeResult.Failed("ProbeTimeout", "the probe was stopped at its execution deadline");
            if (String.IsNullOrWhiteSpace(result.StandardOutput))
                return UsageProbeResult.Failed("MalformedOutput", "the host answered without a probe result payload");
            return UsageProbeResult.Deserialize(result.StandardOutput, result.ExitCode == 0 ? "MalformedOutput" : "ProbeFailed");
        }

        private async Task<UsageSnapshot> CommitAsync(UsageCaptureGroup group, List<string> memberCaptainIds, UsageProbeResult probe, string? triggeringCaptainId, DateTime receivedUtc, CancellationToken token)
        {
            UsageSnapshot snapshot = new UsageSnapshot
            {
                TenantId = group.TenantId,
                CaptainId = triggeringCaptainId,
                HarborId = group.HarborId,
                HostId = group.HostId,
                Runtime = group.Runtime,
                Provider = probe.Provider,
                AccountKey = probe.AccountKey,
                CapturedUtc = probe.CapturedUtc,
                ReceivedUtc = receivedUtc,
                Status = probe.Status,
                Source = probe.Source,
                WindowsJson = UsageWindowsJson.Serialize(probe.Windows),
                RawExcerpt = CapExcerpt(probe.RawExcerpt),
                ErrorCode = probe.ErrorCode
            };

            snapshot = await _Database.UsageSnapshots.CreateAsync(snapshot, memberCaptainIds, token).ConfigureAwait(false);

            await ReconcileMembershipsAsync(group, memberCaptainIds, probe, receivedUtc, token).ConfigureAwait(false);
            await UpsertLatestStateAsync(group, snapshot, token).ConfigureAwait(false);
            return snapshot;
        }

        /// <summary>
        /// The members a commit may still attribute: those whose captain row is still capturing and still
        /// resolves into this group's configuration. A member that verifiably stopped capturing (or moved)
        /// while the probe ran is dropped, so its result is never a stale write against the captain; a
        /// member whose row cannot be read is kept, because there is nothing to reconcile against.
        /// </summary>
        /// <param name="group">Group the attempt ran for.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The members still eligible for attribution.</returns>
        private async Task<List<string>> EffectiveMembersAsync(UsageCaptureGroup group, CancellationToken token)
        {
            List<string> effective = new List<string>();
            foreach (string captainId in group.MemberCaptainIds)
            {
                if (String.IsNullOrWhiteSpace(captainId)) continue;

                Captain? captain;
                try
                {
                    captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "could not re-read member captain " + captainId + "; it is kept as a member: " + e.Message);
                    effective.Add(captainId);
                    continue;
                }

                if (captain == null)
                {
                    effective.Add(captainId);
                    continue;
                }

                if (!captain.UsageCaptureActive)
                {
                    _Logging.Debug(_Header + "captain " + captainId + " stopped capturing while its group's probe ran; its result is not attributed");
                    continue;
                }

                if (!await StillInGroupAsync(captain, group, token).ConfigureAwait(false))
                {
                    _Logging.Debug(_Header + "captain " + captainId + " moved out of group " + group.GroupKey + " while its probe ran; its result is not attributed");
                    continue;
                }

                effective.Add(captainId);
            }

            return effective;
        }

        /// <summary>
        /// Whether a captain still resolves into the group an attempt ran for. A verification that cannot
        /// be made answers true, so an unrelated failure never silently drops a member's attribution.
        /// </summary>
        /// <param name="captain">Captain to verify.</param>
        /// <param name="group">Group the attempt ran for.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True while the captain belongs to the group.</returns>
        private async Task<bool> StillInGroupAsync(Captain captain, UsageCaptureGroup group, CancellationToken token)
        {
            try
            {
                UsageProbeHost? host = await _Accounts.TryResolveHostAsync(_Database, _Harbors, captain, _Settings, token).ConfigureAwait(false);
                if (host == null) return false;

                string? fingerprint = captain.UsageCaptureMode == UsageCaptureModeEnum.Custom
                    ? UsageAccountResolver.CommandFingerprint(captain.UsageCaptureCommand)
                    : null;
                string key = UsageAccountResolver.GroupKey(captain.TenantId ?? String.Empty, host.HostId, captain.Runtime, fingerprint);
                return String.Equals(key, group.GroupKey, StringComparison.Ordinal);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "could not verify the group of captain " + captain.Id + "; it is kept as a member: " + e.Message);
                return true;
            }
        }

        private async Task ReconcileMembershipsAsync(UsageCaptureGroup group, List<string> memberCaptainIds, UsageProbeResult probe, DateTime observedUtc, CancellationToken token)
        {
            // A missing identity never merges unrelated captains into an unknown group.
            if (String.IsNullOrWhiteSpace(probe.AccountKey)) return;

            foreach (string captainId in memberCaptainIds)
            {
                if (String.IsNullOrWhiteSpace(captainId)) continue;
                try
                {
                    List<UsageAccountMembership> active = await _Database.UsageMemberships.ReadActiveAsync(group.TenantId, captainId, token).ConfigureAwait(false);
                    bool matching = active.Exists(membership =>
                        String.Equals(membership.HostId, group.HostId, StringComparison.Ordinal)
                        && membership.Runtime == group.Runtime
                        && String.Equals(membership.AccountKey, probe.AccountKey, StringComparison.Ordinal));
                    if (matching) continue;

                    // The captain's account identity changed (a credential change or a host move): close
                    // the old attribution, then open the new one. History is never rewritten.
                    await _Database.UsageMemberships.CloseAsync(group.TenantId, captainId, observedUtc, token).ConfigureAwait(false);
                    await _Database.UsageMemberships.CreateAsync(new UsageAccountMembership
                    {
                        TenantId = group.TenantId,
                        CaptainId = captainId,
                        HostId = group.HostId,
                        Runtime = group.Runtime,
                        Provider = probe.Provider,
                        AccountKey = probe.AccountKey!,
                        ConfigurationRevision = group.ConfigurationRevision,
                        EffectiveFromUtc = observedUtc
                    }, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "could not reconcile the usage membership of captain " + captainId + ": " + e.Message);
                }
            }
        }

        private async Task UpsertLatestStateAsync(UsageCaptureGroup group, UsageSnapshot snapshot, CancellationToken token)
        {
            string? accountKey = snapshot.AccountKey;
            bool observedIdentity = !String.IsNullOrWhiteSpace(accountKey);
            if (!observedIdentity)
            {
                // A failure that could not observe the account identity (the host was unreachable) is
                // still about the account this group last observed: the error is stored against it. An
                // identity nobody ever observed never merges captains into an unknown group.
                accountKey = _GroupStates.TryGetValue(group.GroupKey, out GroupState? state) ? state.LastAccountKey : null;
                if (String.IsNullOrWhiteSpace(accountKey)) return;
            }

            try
            {
                bool good = snapshot.Status == UsageSnapshotStatusEnum.Ok || snapshot.Status == UsageSnapshotStatusEnum.Estimated;
                UsageLatestState state = new UsageLatestState
                {
                    TenantId = group.TenantId,
                    HostId = group.HostId,
                    Runtime = group.Runtime,
                    Provider = snapshot.Provider,
                    AccountKey = accountKey!,
                    LastUpdateUtc = snapshot.ReceivedUtc
                };

                if (good && observedIdentity)
                {
                    state.LastGoodSnapshotId = snapshot.Id;
                    state.LastGoodCapturedUtc = snapshot.CapturedUtc;
                    state.LastGoodReceivedUtc = snapshot.ReceivedUtc;
                    state.LastGoodStatus = snapshot.Status;
                    state.LastGoodWindowsJson = snapshot.WindowsJson;
                }
                else
                {
                    state.LastErrorSnapshotId = snapshot.Id;
                    state.LastErrorCapturedUtc = snapshot.CapturedUtc;
                    state.LastErrorStatus = snapshot.Status;
                    state.LastErrorCode = snapshot.ErrorCode;
                }

                await _Database.UsageSnapshots.UpsertLatestStateAsync(state, token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "could not upsert the latest usage state for account " + accountKey + ": " + e.Message);
            }
        }

        private DateTime? ScheduleNext(UsageCaptureGroup group, UsageProbeResult probe, DateTime now)
        {
            GroupState state = _GroupStates.GetOrAdd(group.GroupKey, _ => new GroupState());
            lock (state)
            {
                state.Observed = true;
                if (!String.IsNullOrWhiteSpace(probe.AccountKey)) state.LastAccountKey = probe.AccountKey;
                switch (probe.Status)
                {
                    case UsageSnapshotStatusEnum.Ok:
                    case UsageSnapshotStatusEnum.Estimated:
                        state.FailureCount = 0;
                        state.NextEligibleUtc = now.AddSeconds(EffectiveDueSeconds(group.EffectiveIntervalSeconds));
                        break;

                    case UsageSnapshotStatusEnum.AuthMissing:
                        // Credential problems retry slowly, hourly or on a configuration change, never at
                        // the interval, and never merge into an unknown account.
                        state.FailureCount++;
                        state.NextEligibleUtc = now.AddSeconds(Math.Min(
                            Math.Max(AuthMissingRetrySeconds, group.EffectiveIntervalSeconds),
                            _Settings.UsageCapture.MaximumBackoffSeconds));
                        break;

                    case UsageSnapshotStatusEnum.Unsupported:
                        // No usable source: not scheduled again until the runtime, version or
                        // configuration changes (which invalidates the generation and clears this state).
                        state.FailureCount = 0;
                        state.NextEligibleUtc = null;
                        break;

                    default:
                        // Failure: back off at 2x the effective interval per consecutive failure, capped
                        // at the configured bound (and never shorter than the effective interval), with
                        // jitter applied; a disconnected host keeps its last good data and never loops.
                        state.FailureCount++;
                        double backoff = group.EffectiveIntervalSeconds;
                        for (int i = 0; i < state.FailureCount; i++)
                        {
                            backoff *= 2;
                            if (backoff >= _Settings.UsageCapture.MaximumBackoffSeconds) break;
                        }
                        int seconds = (int)Math.Min(Math.Max(backoff, group.EffectiveIntervalSeconds), _Settings.UsageCapture.MaximumBackoffSeconds);
                        state.NextEligibleUtc = now.AddSeconds(BackoffDueSecondsWithJitter(seconds));
                        break;
                }

                // A longer provider Retry-After always wins over the computed delay.
                if (probe.RetryAfterUtc.HasValue && (!state.NextEligibleUtc.HasValue || probe.RetryAfterUtc.Value > state.NextEligibleUtc.Value))
                    state.NextEligibleUtc = probe.RetryAfterUtc.Value;

                return state.NextEligibleUtc;
            }
        }

        private int EffectiveDueSeconds(int effectiveIntervalSeconds)
        {
            // A due time is the effective interval times uniform(0.9, 1.1), with the server-wide minimum
            // enforced as a floor after the jitter.
            int seconds = (int)Math.Round(effectiveIntervalSeconds * _Jitter());
            int floor = _Settings.UsageCapture.MinimumIntervalSeconds;
            return seconds < floor ? floor : seconds;
        }

        private int BackoffDueSecondsWithJitter(int seconds)
        {
            int jittered = (int)Math.Round(seconds * _Jitter());
            return jittered < _Settings.UsageCapture.MinimumIntervalSeconds ? _Settings.UsageCapture.MinimumIntervalSeconds : jittered;
        }

        private async Task<Job?> FindActiveLeaseAsync(UsageCaptureGroup group, CancellationToken token)
        {
            // The lease is this group's own active UsageProbe job, wherever the page ordering put it: newer
            // unrelated jobs must never hide an unexpired lease behind the first page, so every page of the
            // active probe jobs is traversed before an attempt is allowed to launch.
            string jobName = UsageAccountResolver.JobName(group.GroupKey);
            Job? latest = null;
            int pageNumber = 1;
            while (true)
            {
                JobQuery query = new JobQuery
                {
                    Kind = JobKindEnum.UsageProbe,
                    Statuses = new List<JobStatusEnum> { JobStatusEnum.Queued, JobStatusEnum.Running },
                    PageNumber = pageNumber,
                    PageSize = 200
                };

                EnumerationResult<Job> page = await _Database.Jobs.EnumeratePageAsync(query, token).ConfigureAwait(false);
                foreach (Job job in page.Objects)
                {
                    if (!String.Equals(job.Name, jobName, StringComparison.Ordinal)) continue;
                    if (latest == null || job.LastUpdateUtc > latest.LastUpdateUtc) latest = job;
                }

                if (page.Objects.Count == 0 || pageNumber >= page.TotalPages) break;
                pageNumber++;
            }

            return latest;
        }

        private async Task<UsageCaptureAttemptResult> DiscardAsync(Job job, string reason, UsageProbeResult probe, CancellationToken token)
        {
            _ = await _Jobs.TryFinishAsync(job.Id, JobStatusEnum.Failed, null, reason, token).ConfigureAwait(false);
            _Logging.Info(_Header + "discarded a capture result for a stale generation: " + reason);
            return new UsageCaptureAttemptResult
            {
                Status = probe.Status,
                ErrorCode = probe.ErrorCode,
                DiscardedReason = reason
            };
        }

        private static string SnapshotResultJson(UsageSnapshot snapshot)
        {
            return "{\"snapshotId\":\"" + snapshot.Id + "\",\"status\":\"" + snapshot.Status + "\"}";
        }

        private string CapExcerpt(string? excerpt)
        {
            if (String.IsNullOrWhiteSpace(excerpt)) return String.Empty;
            string redacted = SecretRedactor.Redact(excerpt);
            int maximum = Math.Max(256, _Settings.UsageCapture.MaximumExcerptBytes);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(redacted);
            if (bytes.Length <= maximum) return redacted;
            return System.Text.Encoding.UTF8.GetString(bytes, 0, maximum);
        }

        private UsageCaptureAttempt Skipped(string? jobId, DateTime? nextEligibleUtc, string reason)
        {
            return new UsageCaptureAttempt
            {
                JobId = jobId,
                JoinedExisting = false,
                Started = false,
                NextEligibleUtc = nextEligibleUtc,
                SkippedReason = reason,
                Completion = Task.FromResult(new UsageCaptureAttemptResult { ErrorCode = reason })
            };
        }

        private SemaphoreSlim HostGate(string hostId)
        {
            return _HostGates.GetOrAdd(hostId, _ => new SemaphoreSlim(Math.Max(1, _Settings.UsageCapture.MaximumConcurrentProbesPerHost)));
        }

        private double DefaultJitter()
        {
            lock (_JitterRandom)
            {
                return _JitterRandom.NextDouble() * 0.2 + 0.9;
            }
        }

        private DateTime Now()
        {
            return _Clock.GetUtcNow().UtcDateTime;
        }

        #endregion

        #region Private-Types

        private sealed class GroupState
        {
            public DateTime? NextEligibleUtc { get; set; } = null;
            public int FailureCount { get; set; } = 0;
            public bool Observed { get; set; } = false;
            public string? LastAccountKey { get; set; } = null;
        }

        private sealed class InFlightAttempt
        {
            public string JobId { get; }
            public Task<UsageCaptureAttemptResult> Completion { get; }

            public InFlightAttempt(string jobId, Task<UsageCaptureAttemptResult> completion)
            {
                JobId = jobId;
                Completion = completion;
            }
        }

        #endregion
    }
}