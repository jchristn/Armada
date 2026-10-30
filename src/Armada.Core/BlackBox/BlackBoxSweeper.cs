namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Belt and braces for capture (docs/BLACKBOX.md, "Sweeper and backfill"): captures missions in a terminal failure state
    /// (Failed, LandingFailed, Blocked, non-operator Cancelled) whose last entry is missing or older than their last update,
    /// and failed or cancelled voyages without an entry (CaptureMode Sweeper, or Backfill for a backfill run); requeues
    /// EnrichmentFailed entries that are due and Captured entries the queue dropped. Pages through the database; never
    /// captures a subject twice in one pass. Thread-safe.
    /// </summary>
    public class BlackBoxSweeper
    {
        #region Public-Members

        /// <summary>
        /// Age after which a Captured entry is considered dropped by the queue and requeued. Default 10 minutes.
        /// </summary>
        public TimeSpan StaleCapturedAfter { get; set; } = TimeSpan.FromMinutes(10);

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxSweeper] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly IBlackBoxCapture _Capture;
        private readonly IBlackBoxEnrichmentQueue? _Queue;
        private readonly LoggingModule _Logging;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Armada settings.</param>
        /// <param name="capture">Capture service.</param>
        /// <param name="queue">Enrichment queue, or null.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public BlackBoxSweeper(DatabaseDriver database, ArmadaSettings settings, IBlackBoxCapture capture, IBlackBoxEnrichmentQueue? queue, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _Queue = queue;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one sweep (CaptureMode Sweeper), bounded by BlackBox.Sweeper.PageSize subjects per kind.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What the sweep did.</returns>
        public Task<BlackBoxBackfillResult> SweepAsync(CancellationToken token = default)
        {
            return RunAsync(BlackBoxCaptureModeEnum.Sweeper, null, _Settings.BlackBox.Sweeper.PageSize, token);
        }

        /// <summary>
        /// Backfill historical failures (CaptureMode Backfill). Idempotent: subjects with a current entry are skipped.
        /// </summary>
        /// <param name="since">Only subjects updated at or after this time, or null.</param>
        /// <param name="maxSubjects">Most subjects to capture (1 to 100000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What the backfill did.</returns>
        public Task<BlackBoxBackfillResult> BackfillAsync(DateTime? since, int maxSubjects, CancellationToken token = default)
        {
            int max = Math.Clamp(maxSubjects, 1, 100000);
            return RunAsync(BlackBoxCaptureModeEnum.Backfill, since, max, token);
        }

        /// <summary>
        /// Best guess of the failure step from a mission's failure kind and status (Unknown when ambiguous).
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <returns>The step.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public static BlackBoxStepEnum InferStep(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            switch (mission.FailureKind)
            {
                case MissionFailureKindEnum.ScopeViolation: return BlackBoxStepEnum.ScopeViolation;
                case MissionFailureKindEnum.Boundary: return BlackBoxStepEnum.BoundaryViolation;
                case MissionFailureKindEnum.NoOp: return BlackBoxStepEnum.NoOp;
                case MissionFailureKindEnum.JudgeRejected: return BlackBoxStepEnum.JudgeRejected;
                case MissionFailureKindEnum.ReviewDenied: return BlackBoxStepEnum.ReviewDenied;
                case MissionFailureKindEnum.MaxRuntimeExceeded: return BlackBoxStepEnum.MaxRuntime;
                case MissionFailureKindEnum.StallRecoveryExhausted: return BlackBoxStepEnum.Stall;
                case MissionFailureKindEnum.OperatorAction: return BlackBoxStepEnum.OperatorStop;
                case MissionFailureKindEnum.InvalidOutput: return BlackBoxStepEnum.InvalidOutput;
                case MissionFailureKindEnum.Blocked: return BlackBoxStepEnum.CaptainBlocked;
                case MissionFailureKindEnum.DependencyFailed: return BlackBoxStepEnum.DependencyCancelled;
                case MissionFailureKindEnum.Crash: return BlackBoxStepEnum.ProcessCrash;
                case MissionFailureKindEnum.LandingConflict: return BlackBoxStepEnum.TargetIntegration;
            }
            // A landing failure captured after the fact keeps its phase (Landing), so the handoff and landing bias and
            // the resilience gaps see it: the step comes from the reason, else the generic integration step.
            if (mission.Status == MissionStatusEnum.LandingFailed) return InferLandingStep(mission.FailureReason);
            return BlackBoxStepEnum.Unknown;
        }

        /// <summary>
        /// Best guess of the landing step from a landing failure's reason (TargetIntegration when nothing more specific matches).
        /// </summary>
        /// <param name="reason">Failure reason, or null.</param>
        /// <returns>A Landing phase step.</returns>
        public static BlackBoxStepEnum InferLandingStep(string? reason)
        {
            string r = (reason ?? String.Empty).ToLowerInvariant();
            if (r.Contains("deferral") && r.Contains("expired")) return BlackBoxStepEnum.DeferralExpired;
            if (r.Contains("uncommitted") || r.Contains("dirty")) return BlackBoxStepEnum.DirtyCheckout;
            if (r.Contains("working directory") || r.Contains("no such file") || r.Contains("dock") && (r.Contains("missing") || r.Contains("gone"))) return BlackBoxStepEnum.DockMissing;
            if (r.Contains("unpublished")) return BlackBoxStepEnum.UnpublishedAncestry;
            if (r.Contains("pull request") || r.Contains("gh pr")) return BlackBoxStepEnum.PullRequest;
            if (r.Contains("verification") || r.Contains("verify")) return BlackBoxStepEnum.Verification;
            if (r.Contains("push") || r.Contains("non-fast-forward")) return BlackBoxStepEnum.Push;
            if (r.Contains("fast-forward") || r.Contains("fast forward")) return BlackBoxStepEnum.FastForward;
            return BlackBoxStepEnum.TargetIntegration;
        }

        #endregion

        #region Private-Methods

        private async Task<BlackBoxBackfillResult> RunAsync(BlackBoxCaptureModeEnum mode, DateTime? since, int max, CancellationToken token)
        {
            BlackBoxBackfillResult result = new BlackBoxBackfillResult { Mode = mode, StartedUtc = DateTime.UtcNow };
            if (!_Settings.BlackBox.Enabled)
            {
                result.FinishedUtc = DateTime.UtcNow;
                return result;
            }
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                int pageSize = Math.Clamp(_Settings.BlackBox.Sweeper.PageSize, 1, 1000);
                await SweepMissionsAsync(mode, since, max, pageSize, result, token).ConfigureAwait(false);
                await SweepVoyagesAsync(mode, since, max, pageSize, result, token).ConfigureAwait(false);
                if (mode == BlackBoxCaptureModeEnum.Sweeper && _Queue != null) await RequeueAsync(pageSize, result, token).ConfigureAwait(false);
            }
            finally
            {
                _Gate.Release();
            }
            result.FinishedUtc = DateTime.UtcNow;
            if (result.Captured > 0 || result.Failed > 0)
                _Logging.Info(_Header + mode + ": captured " + result.Captured + ", failed " + result.Failed + ", scanned " + result.Scanned);
            return result;
        }

        private async Task SweepMissionsAsync(BlackBoxCaptureModeEnum mode, DateTime? since, int max, int pageSize, BlackBoxBackfillResult result, CancellationToken token)
        {
            HashSet<string> attempted = new HashSet<string>(StringComparer.Ordinal);
            int page = 1;
            while (attempted.Count < max)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<string> ids = await _Database.BlackBoxEntries.EnumerateUncapturedMissionIdsAsync(since, new EnumerationQuery { PageNumber = page, PageSize = pageSize }, token).ConfigureAwait(false);
                if (ids.Objects.Count == 0) break;
                List<string> fresh = ids.Objects.Where(id => !attempted.Contains(id)).ToList();
                if (fresh.Count == 0) { page++; continue; }
                foreach (string id in fresh)
                {
                    if (attempted.Count >= max) break;
                    attempted.Add(id);
                    result.Scanned++;
                    Mission? mission = await _Database.Missions.ReadAsync(id, token).ConfigureAwait(false);
                    if (mission == null) { result.Skipped++; continue; }
                    string? entryId = await _Capture.CaptureMissionFailureAsync(new BlackBoxCaptureContext
                    {
                        Mission = mission,
                        Step = InferStep(mission),
                        CaptureMode = mode,
                        Component = nameof(BlackBoxSweeper)
                    }, token).ConfigureAwait(false);
                    if (entryId != null) result.Captured++;
                    else result.Failed++;
                }
            }
        }

        private async Task SweepVoyagesAsync(BlackBoxCaptureModeEnum mode, DateTime? since, int max, int pageSize, BlackBoxBackfillResult result, CancellationToken token)
        {
            HashSet<string> attempted = new HashSet<string>(StringComparer.Ordinal);
            int page = 1;
            while (attempted.Count < max)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<string> ids = await _Database.BlackBoxEntries.EnumerateUncapturedVoyageIdsAsync(since, new EnumerationQuery { PageNumber = page, PageSize = pageSize }, token).ConfigureAwait(false);
                if (ids.Objects.Count == 0) break;
                List<string> fresh = ids.Objects.Where(id => !attempted.Contains(id)).ToList();
                if (fresh.Count == 0) { page++; continue; }
                foreach (string id in fresh)
                {
                    if (attempted.Count >= max) break;
                    attempted.Add(id);
                    result.Scanned++;
                    Voyage? voyage = await _Database.Voyages.ReadAsync(id, token).ConfigureAwait(false);
                    if (voyage == null) { result.Skipped++; continue; }
                    string? entryId = await _Capture.CaptureVoyageFailureAsync(new BlackBoxVoyageCaptureContext
                    {
                        Voyage = voyage,
                        Step = voyage.Status == VoyageStatusEnum.Cancelled ? BlackBoxStepEnum.Cancelled : BlackBoxStepEnum.SettlementFailed,
                        CaptureMode = mode,
                        Component = nameof(BlackBoxSweeper)
                    }, token).ConfigureAwait(false);
                    if (entryId != null) result.Captured++;
                    else result.Failed++;
                }
            }
        }

        private async Task RequeueAsync(int pageSize, BlackBoxBackfillResult result, CancellationToken token)
        {
            EnumerationResult<BlackBoxEntry> retry = await _Database.BlackBoxEntries.EnumerateRetryableAsync(DateTime.UtcNow, _Settings.BlackBox.Sweeper.MaxEnrichmentAttempts, new EnumerationQuery { PageSize = pageSize }, token).ConfigureAwait(false);
            foreach (BlackBoxEntry entry in retry.Objects)
            {
                if (_Queue!.Enqueue(entry.Id)) result.EnrichmentRequeued++;
            }
            BlackBoxEnumerateRequest stale = new BlackBoxEnumerateRequest
            {
                State = BlackBoxStateEnum.Captured,
                CreatedBefore = DateTime.UtcNow - StaleCapturedAfter,
                Order = EnumerationOrderEnum.CreatedAscending,
                PageSize = pageSize
            };
            EnumerationResult<BlackBoxEntry> captured = await _Database.BlackBoxEntries.EnumerateAsync(stale, token).ConfigureAwait(false);
            foreach (BlackBoxEntry entry in captured.Objects)
            {
                if (_Queue!.Enqueue(entry.Id)) result.EnrichmentRequeued++;
            }
        }

        #endregion
    }
}
