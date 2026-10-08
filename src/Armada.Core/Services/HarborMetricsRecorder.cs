namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Metrics;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Records what the Harbor metrics are computed from, as the Harbor link reports it: one durable row per captain
    /// launch delegated to a Harbor (launch, start, first output, exit, and outcome), the link's transitions (connected,
    /// reconnecting, disconnected), and the heartbeats' round-trip times and reconnect counters, accumulated in memory and
    /// written as one row per Harbor per minute. Every method is best-effort: a persistence failure is logged and never
    /// breaks the link or a launch.
    /// </summary>
    public class HarborMetricsRecorder
    {
        #region Private-Members

        private readonly string _Header = "[HarborMetricsRecorder] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly HarborServerSettings _Settings;
        private readonly Func<DateTime> _Clock;
        private readonly ConcurrentDictionary<string, DateTime> _FirstOutput = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> _StopRequested = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _Reconnecting = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _PendingReconcile = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly Dictionary<string, HarborLinkSample> _OpenSamples = new Dictionary<string, HarborLinkSample>(StringComparer.Ordinal);
        private readonly object _SampleLock = new object();
        private static readonly TimeSpan _SampleBucket = TimeSpan.FromMinutes(1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="settings">Harbor server settings, read live: <see cref="HarborServerSettings.HeartbeatTimeoutSeconds"/> is
        /// how long a closed link counts as reconnecting before it counts as down.</param>
        /// <param name="clock">UTC clock; defaults to <see cref="DateTime.UtcNow"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when the database, logging, or settings is null.</exception>
        public HarborMetricsRecorder(DatabaseDriver database, LoggingModule logging, HarborServerSettings settings, Func<DateTime>? clock = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Clock = clock ?? (() => DateTime.UtcNow);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record that the Admiral accepted a Harbor's handshake. The Harbor's first heartbeat on this link then settles
        /// jobs from earlier links that it no longer runs.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task OnLinkConnectedAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) return;
            DateTime now = _Clock();
            _Reconnecting.TryRemove(harborId, out DateTime _);
            _PendingReconcile[harborId] = now;
            await AddEventAsync(harborId, HarborLinkEventTypeEnum.Connected, now, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Record that a Harbor's link closed. It counts as reconnecting until it dials back in, or as disconnected once
        /// <see cref="HarborServerSettings.HeartbeatTimeoutSeconds"/> passes without it (see <see cref="SweepAsync"/>).
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task OnLinkClosedAsync(string harborId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId)) return;
            DateTime now = _Clock();
            _PendingReconcile.TryRemove(harborId, out DateTime _);
            _Reconnecting[harborId] = now;
            await FlushSampleAsync(harborId, token).ConfigureAwait(false);
            await AddEventAsync(harborId, HarborLinkEventTypeEnum.Reconnecting, now, "Link closed", token).ConfigureAwait(false);
        }

        /// <summary>
        /// Record a heartbeat: count it in its minute with the round trip and reconnect counters it reports, and on the
        /// first heartbeat of a link mark jobs launched on earlier links that the Harbor no longer runs as lost.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="heartbeat">The heartbeat.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task OnHeartbeatAsync(string harborId, HarborHeartbeat heartbeat, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId) || heartbeat == null) return;
            DateTime now = _Clock();
            DateTime minute = TimeBucketMath.Floor(now, _SampleBucket);

            HarborLinkSample? completed = null;
            lock (_SampleLock)
            {
                if (_OpenSamples.TryGetValue(harborId, out HarborLinkSample? open) && open.BucketStartUtc != minute)
                {
                    completed = open;
                    _OpenSamples.Remove(harborId);
                }

                if (!_OpenSamples.TryGetValue(harborId, out HarborLinkSample? sample))
                {
                    sample = new HarborLinkSample { HarborId = harborId, BucketStartUtc = minute, CreatedUtc = now };
                    _OpenSamples[harborId] = sample;
                }

                sample.HeartbeatCount++;
                if (heartbeat.LastRoundTripMs.HasValue && heartbeat.LastRoundTripMs.Value >= 0)
                {
                    long rtt = heartbeat.LastRoundTripMs.Value;
                    sample.RoundTripCount++;
                    sample.RoundTripTotalMs += rtt;
                    if (!sample.RoundTripMaxMs.HasValue || rtt > sample.RoundTripMaxMs.Value) sample.RoundTripMaxMs = rtt;
                }

                if (heartbeat.ReconnectCount.HasValue) sample.ReconnectCount = heartbeat.ReconnectCount;
                if (heartbeat.LastReconnectUtc.HasValue) sample.LastReconnectUtc = heartbeat.LastReconnectUtc.Value.ToUniversalTime();
            }

            if (completed != null) await WriteSampleAsync(completed, token).ConfigureAwait(false);

            if (_PendingReconcile.TryRemove(harborId, out DateTime connectedUtc))
                await MarkLostJobsAsync(harborId, connectedUtc, heartbeat.LiveJobIds, now, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Record a launch sent to a Harbor.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="tenantId">Tenant of the Harbor's link, when known.</param>
        /// <param name="request">The launch request.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task OnLaunchAsync(string harborId, string? tenantId, HarborLaunchRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(harborId) || request == null || String.IsNullOrWhiteSpace(request.JobId)) return;
            DateTime now = _Clock();
            try
            {
                HarborJobRecord record = new HarborJobRecord
                {
                    JobId = request.JobId,
                    HarborId = harborId,
                    TenantId = tenantId,
                    Kind = request.JobKindType,
                    Runtime = request.Runtime ?? String.Empty,
                    Model = String.IsNullOrWhiteSpace(request.Model) ? null : request.Model,
                    MissionId = request.MissionId,
                    CaptainId = request.CaptainId,
                    LaunchedUtc = now,
                    Outcome = HarborJobOutcomeEnum.Running,
                    CreatedUtc = now,
                    LastUpdateUtc = now
                };
                await _Database.HarborJobs.CreateAsync(record, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not record launch of job " + request.JobId + " on harbor " + harborId + ": " + e.Message);
            }
        }

        /// <summary>
        /// Record that a Harbor started a job's process.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task OnStartedAsync(string jobId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return;
            DateTime now = _Clock();
            try
            {
                HarborJobRecord? record = await _Database.HarborJobs.ReadByJobIdAsync(jobId, token).ConfigureAwait(false);
                if (record == null || record.StartedUtc.HasValue) return;
                record.StartedUtc = now;
                await _Database.HarborJobs.UpdateAsync(record, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not record start of job " + jobId + ": " + e.Message);
            }
        }

        /// <summary>
        /// Note a job's output. Only the first is kept, in memory, and written when the job ends.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        public void OnOutput(string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return;
            _FirstOutput.TryAdd(jobId, _Clock());
        }

        /// <summary>
        /// Note that the Admiral asked a Harbor to stop a job, so its exit counts as stopped rather than failed.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        public void OnStopRequested(string jobId)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return;
            _StopRequested[jobId] = true;
        }

        /// <summary>
        /// Record that a job's process exited. An exit always wins over an earlier "lost" verdict.
        /// </summary>
        /// <param name="exited">The exit message.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task OnExitedAsync(HarborExited exited, CancellationToken token = default)
        {
            if (exited == null || String.IsNullOrWhiteSpace(exited.JobId)) return;
            DateTime now = _Clock();
            bool stopped = _StopRequested.TryRemove(exited.JobId, out bool _);
            bool hasFirstOutput = _FirstOutput.TryRemove(exited.JobId, out DateTime firstOutput);
            try
            {
                HarborJobRecord? record = await _Database.HarborJobs.ReadByJobIdAsync(exited.JobId, token).ConfigureAwait(false);
                if (record == null) return;

                record.EndedUtc = now;
                record.ExitCode = exited.ExitCode;
                record.StopRequested = record.StopRequested || stopped;
                if (hasFirstOutput && !record.FirstOutputUtc.HasValue) record.FirstOutputUtc = firstOutput;
                record.DurationMs = exited.DurationMs ?? (long)(now - record.LaunchedUtc).TotalMilliseconds;
                record.TimeToFirstOutputMs = exited.TimeToFirstTokenMs
                    ?? (record.FirstOutputUtc.HasValue ? (long?)(long)(record.FirstOutputUtc.Value - record.LaunchedUtc).TotalMilliseconds : null);
                if (exited.ExitCode == 0) record.Outcome = HarborJobOutcomeEnum.Succeeded;
                else if (record.StopRequested) record.Outcome = HarborJobOutcomeEnum.Stopped;
                else record.Outcome = HarborJobOutcomeEnum.Failed;

                await _Database.HarborJobs.UpdateAsync(record, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not record exit of job " + exited.JobId + ": " + e.Message);
            }
        }

        /// <summary>
        /// Record that a Harbor could not launch a job (it reported an error for it).
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task OnLaunchFailedAsync(string jobId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(jobId)) return;
            DateTime now = _Clock();
            _StopRequested.TryRemove(jobId, out bool _);
            _FirstOutput.TryRemove(jobId, out DateTime _);
            try
            {
                HarborJobRecord? record = await _Database.HarborJobs.ReadByJobIdAsync(jobId, token).ConfigureAwait(false);
                if (record == null || record.EndedUtc.HasValue) return;
                record.EndedUtc = now;
                record.DurationMs = (long)(now - record.LaunchedUtc).TotalMilliseconds;
                record.Outcome = HarborJobOutcomeEnum.Failed;
                await _Database.HarborJobs.UpdateAsync(record, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not record failed launch of job " + jobId + ": " + e.Message);
            }
        }

        /// <summary>
        /// Periodic upkeep, run on the Admiral's health-check loop: a Harbor whose link has been closed for longer than
        /// <see cref="HarborServerSettings.HeartbeatTimeoutSeconds"/> gets a disconnected event (at the end of that grace),
        /// and minute samples that no heartbeat has closed are written.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public async Task SweepAsync(CancellationToken token = default)
        {
            DateTime now = _Clock();
            TimeSpan grace = TimeSpan.FromSeconds(_Settings.HeartbeatTimeoutSeconds);

            foreach (KeyValuePair<string, DateTime> pending in _Reconnecting)
            {
                if (now - pending.Value < grace) continue;
                if (!_Reconnecting.TryRemove(new KeyValuePair<string, DateTime>(pending.Key, pending.Value))) continue;
                await AddEventAsync(pending.Key, HarborLinkEventTypeEnum.Disconnected, pending.Value.Add(grace),
                    "No reconnect within " + _Settings.HeartbeatTimeoutSeconds + " seconds", token).ConfigureAwait(false);
            }

            DateTime currentMinute = TimeBucketMath.Floor(now, _SampleBucket);
            List<HarborLinkSample> stale = new List<HarborLinkSample>();
            lock (_SampleLock)
            {
                foreach (KeyValuePair<string, HarborLinkSample> open in _OpenSamples)
                    if (open.Value.BucketStartUtc < currentMinute) stale.Add(open.Value);
                foreach (HarborLinkSample sample in stale) _OpenSamples.Remove(sample.HarborId);
            }

            foreach (HarborLinkSample sample in stale) await WriteSampleAsync(sample, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Settle link state left open by an Admiral that stopped: every Harbor whose latest link event is not a disconnect
        /// gets one, at the end of its reconnect grace (after a close) or at its last heartbeat (after a connect), since the
        /// link cannot have outlived the Admiral. Call once at startup, before Harbors can link.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of disconnect events written.</returns>
        public async Task<int> ReconcileOnStartupAsync(CancellationToken token = default)
        {
            int written = 0;
            DateTime now = _Clock();
            TimeSpan grace = TimeSpan.FromSeconds(_Settings.HeartbeatTimeoutSeconds);
            try
            {
                List<string> harborIds = await _Database.HarborLinkEvents.EnumerateHarborIdsAsync(token).ConfigureAwait(false);
                foreach (string harborId in harborIds)
                {
                    HarborLinkEvent? latest = await _Database.HarborLinkEvents.ReadLatestBeforeAsync(harborId, DateTime.MaxValue, token).ConfigureAwait(false);
                    if (latest == null || latest.EventType == HarborLinkEventTypeEnum.Disconnected) continue;

                    DateTime at;
                    if (latest.EventType == HarborLinkEventTypeEnum.Reconnecting)
                    {
                        at = latest.OccurredUtc.Add(grace);
                    }
                    else
                    {
                        Harbor? harbor = await _Database.Harbors.ReadAsync(harborId, token).ConfigureAwait(false);
                        DateTime lastSeen = harbor?.LastSeenUtc ?? latest.OccurredUtc;
                        at = lastSeen > latest.OccurredUtc ? lastSeen : latest.OccurredUtc;
                    }

                    if (at > now) at = now;
                    await AddEventAsync(harborId, HarborLinkEventTypeEnum.Disconnected, at, "Admiral stopped", token).ConfigureAwait(false);
                    written++;
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not reconcile Harbor link state at startup: " + e.Message);
            }

            return written;
        }

        /// <summary>
        /// Write every open minute sample now (for example when the Admiral stops).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public async Task FlushAsync(CancellationToken token = default)
        {
            List<HarborLinkSample> open = new List<HarborLinkSample>();
            lock (_SampleLock)
            {
                open.AddRange(_OpenSamples.Values);
                _OpenSamples.Clear();
            }

            foreach (HarborLinkSample sample in open) await WriteSampleAsync(sample, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task FlushSampleAsync(string harborId, CancellationToken token)
        {
            HarborLinkSample? sample = null;
            lock (_SampleLock)
            {
                if (_OpenSamples.TryGetValue(harborId, out sample)) _OpenSamples.Remove(harborId);
            }

            if (sample != null) await WriteSampleAsync(sample, token).ConfigureAwait(false);
        }

        private async Task WriteSampleAsync(HarborLinkSample sample, CancellationToken token)
        {
            try
            {
                await _Database.HarborLinkSamples.CreateAsync(sample, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not record link sample for harbor " + sample.HarborId + ": " + e.Message);
            }
        }

        private async Task AddEventAsync(string harborId, HarborLinkEventTypeEnum type, DateTime occurredUtc, string? detail, CancellationToken token)
        {
            try
            {
                await _Database.HarborLinkEvents.CreateAsync(new HarborLinkEvent
                {
                    HarborId = harborId,
                    EventType = type,
                    OccurredUtc = occurredUtc,
                    Detail = detail
                }, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not record link event " + type + " for harbor " + harborId + ": " + e.Message);
            }
        }

        private async Task MarkLostJobsAsync(string harborId, DateTime connectedUtc, List<string>? liveJobIds, DateTime now, CancellationToken token)
        {
            try
            {
                HashSet<string> live = new HashSet<string>(liveJobIds ?? new List<string>(), StringComparer.Ordinal);
                List<HarborJobRecord> open = await _Database.HarborJobs.EnumerateOpenAsync(harborId, token).ConfigureAwait(false);
                foreach (HarborJobRecord record in open)
                {
                    // Only jobs launched over an earlier link: a job launched on this link may not be in the Harbor's
                    // live set yet.
                    if (record.LaunchedUtc >= connectedUtc || live.Contains(record.JobId)) continue;
                    record.EndedUtc = now;
                    record.Outcome = HarborJobOutcomeEnum.Lost;
                    await _Database.HarborJobs.UpdateAsync(record, token).ConfigureAwait(false);
                    _Logging.Info(_Header + "harbor " + harborId + " reconnected without job " + record.JobId + "; recorded it as lost");
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "could not settle jobs of harbor " + harborId + " after it reconnected: " + e.Message);
            }
        }

        #endregion
    }
}
