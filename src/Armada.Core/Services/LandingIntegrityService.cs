namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Health-loop sweep that keeps unlanded work landable. It recreates a missing dock worktree from its branch for
    /// missions that still need it (WorkProduced, LandingFailed, Review, PullRequestOpen), and retries a LocalMerge
    /// landing that was refused because the target checkout had tracked changes once that checkout is clean again,
    /// until <see cref="ArmadaSettings.LandingDeferralDeadlineHours"/> after the first refusal. Work per sweep is bounded.
    /// </summary>
    public class LandingIntegrityService
    {
        #region Public-Members

        /// <summary>
        /// Event recorded when a LocalMerge landing waits on a dirty checkout; its payload is a <see cref="LandingDeferral"/>.
        /// </summary>
        public const string DeferredEventType = "mission.landing_deferred";

        /// <summary>
        /// Event recorded once when automatic retries of a deferred landing stop at the deadline.
        /// </summary>
        public const string DeferralExpiredEventType = "mission.landing_deferral_expired";

        /// <summary>
        /// Event recorded each time the sweep retries a deferred landing automatically; counted against
        /// <see cref="ArmadaSettings.MaxLandingRetries"/> for the deferral it belongs to.
        /// </summary>
        public const string AutoRetryEventType = "mission.landing_auto_retry";

        /// <summary>
        /// Event recorded once when a deferred landing has used all <see cref="ArmadaSettings.MaxLandingRetries"/>
        /// automatic retries.
        /// </summary>
        public const string RetriesExhaustedEventType = "mission.landing_retries_exhausted";

        /// <summary>
        /// Most dock directories checked for existence in one sweep.
        /// </summary>
        public int MaxDockChecksPerSweep { get; set; } = 25;

        /// <summary>
        /// Most dock worktrees recreated in one sweep.
        /// </summary>
        public int MaxDockRepairsPerSweep { get; set; } = 5;

        /// <summary>
        /// Most deferred landings retried in one sweep.
        /// </summary>
        public int MaxLandingRetriesPerSweep { get; set; } = 3;

        /// <summary>
        /// How long a dock that was found present is left alone before it is checked again.
        /// </summary>
        public TimeSpan DockRecheckInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// How long after a deferred landing was last retried it may be retried again.
        /// </summary>
        public TimeSpan LandingRetryInterval { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// A WorkProduced mission updated more recently than this is still being completed and is skipped.
        /// </summary>
        public TimeSpan SettleTime { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Whether a mission's completion handler is still running; such a mission is skipped. Optional.
        /// </summary>
        public Func<string, bool>? IsCompletionInFlight { get; set; } = null;

        /// <summary>
        /// Hosts of Harbor-held checkouts, used to tell whether a deferred landing's checkout is clean. Without it only
        /// checkouts on the Admiral's host are checked.
        /// </summary>
        public DockHostResolver? Hosts { get; set; } = null;

        /// <summary>
        /// Black box capture for deferred landings that expire or use up their automatic retries, or null.
        /// </summary>
        public IBlackBoxCapture? BlackBox { get; set; } = null;

        #endregion

        #region Private-Members

        private static readonly MissionStatusEnum[] _UnlandedStatuses = new[]
        {
            MissionStatusEnum.WorkProduced,
            MissionStatusEnum.LandingFailed,
            MissionStatusEnum.Review,
            MissionStatusEnum.PullRequestOpen
        };

        private string _Header = "[LandingIntegrity] ";
        private LoggingModule _Logging;
        private DatabaseDriver _Database;
        private ArmadaSettings _Settings;
        private IDockService _Docks;
        private ILandingService _Landing;
        private IGitService _Git;
        private ConcurrentDictionary<string, DateTime> _DockCheckedUtc = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private ConcurrentDictionary<string, string> _ReportedRepairFailures = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private ConcurrentDictionary<string, DateTime> _LandingRetriedUtc = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="docks">Dock service.</param>
        /// <param name="landing">Landing service (retry_landing).</param>
        /// <param name="git">Git service for the Admiral's host.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public LandingIntegrityService(LoggingModule logging, DatabaseDriver database, ArmadaSettings settings, IDockService docks, ILandingService landing, IGitService git)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Docks = docks ?? throw new ArgumentNullException(nameof(docks));
            _Landing = landing ?? throw new ArgumentNullException(nameof(landing));
            _Git = git ?? throw new ArgumentNullException(nameof(git));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read a <see cref="LandingDeferral"/> from a mission.landing_deferred event.
        /// </summary>
        /// <param name="evt">Event, or null.</param>
        /// <returns>The deferral, or null when the event is missing or unreadable.</returns>
        public static LandingDeferral? ReadDeferral(ArmadaEvent? evt)
        {
            if (evt == null || String.IsNullOrWhiteSpace(evt.Payload) || evt.EventType != DeferredEventType) return null;
            try
            {
                return JsonSerializer.Deserialize<LandingDeferral>(evt.Payload);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Run one sweep: repair missing docks of unlanded missions, then retry deferred landings whose checkout is clean.
        /// A sweep already running makes this call return 0.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Docks repaired plus landings retried.</returns>
        public async Task<int> SweepAsync(CancellationToken token = default)
        {
            if (!await _Gate.WaitAsync(0, token).ConfigureAwait(false)) return 0;
            try
            {
                int repaired = await RepairMissingDocksAsync(token).ConfigureAwait(false);
                int retried = await RetryDeferredLandingsAsync(token).ConfigureAwait(false);
                return repaired + retried;
            }
            finally
            {
                _Gate.Release();
            }
        }

        /// <summary>
        /// Recreate the missing worktree of each unlanded mission's dock whose branch still exists. Bounded by
        /// <see cref="MaxDockChecksPerSweep"/> and <see cref="MaxDockRepairsPerSweep"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Docks repaired.</returns>
        public async Task<int> RepairMissingDocksAsync(CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            int checks = 0;
            int repaired = 0;
            foreach (MissionStatusEnum status in _UnlandedStatuses)
            {
                foreach (MissionSummary mission in await ReadMissionsAsync(status, token).ConfigureAwait(false))
                {
                    if (checks >= MaxDockChecksPerSweep || repaired >= MaxDockRepairsPerSweep) return repaired;
                    if (String.IsNullOrEmpty(mission.DockId)) continue;
                    if (IsCompletionInFlight != null && IsCompletionInFlight(mission.Id)) continue;
                    if (mission.Status == MissionStatusEnum.WorkProduced && mission.LastUpdateUtc > now - SettleTime) continue;
                    if (_DockCheckedUtc.TryGetValue(mission.DockId, out DateTime lastChecked) && lastChecked > now - DockRecheckInterval) continue;

                    Dock? dock = await _Database.Docks.ReadAsync(mission.DockId, token).ConfigureAwait(false);
                    // A reclaimed dock was released on purpose (a pipeline handoff or a published pull request).
                    if (dock == null || !dock.Active || String.IsNullOrEmpty(dock.WorktreePath) || String.IsNullOrEmpty(dock.BranchName)) continue;

                    checks++;
                    bool? exists = await _Docks.WorktreeExistsAsync(dock, token).ConfigureAwait(false);
                    if (exists != false)
                    {
                        if (exists == true) _DockCheckedUtc[dock.Id] = now;
                        continue;
                    }

                    DockRestoreResult result = await _Docks.RestoreWorktreeAsync(dock.Id, "integrity sweep for mission " + mission.Id + " (" + mission.Status + ")", false, token).ConfigureAwait(false);
                    if (result.Restored)
                    {
                        repaired++;
                        _DockCheckedUtc[dock.Id] = now;
                        _ReportedRepairFailures.TryRemove(dock.Id, out string? _);
                        continue;
                    }

                    string reason = result.Reason ?? "unknown";
                    if (_ReportedRepairFailures.TryGetValue(dock.Id, out string? reported) && reported == reason) continue;
                    _ReportedRepairFailures[dock.Id] = reason;
                    _Logging.Warn(_Header + "dock " + dock.Id + " of mission " + mission.Id + " (" + mission.Status + ") is missing at " + dock.WorktreePath + " and could not be recreated: " + reason);
                    await EmitAsync("dock.repair_failed", "Dock " + dock.Id + " is missing at " + dock.WorktreePath + " and could not be recreated from branch "
                        + dock.BranchName + ": " + reason, mission, "dock", dock.Id, null, token).ConfigureAwait(false);
                }
            }
            return repaired;
        }

        /// <summary>
        /// Retry each LandingFailed (Infra) mission whose landing was deferred on a dirty checkout, once the checkout is
        /// clean and before the deadline, at most <see cref="ArmadaSettings.MaxLandingRetries"/> times per deferral
        /// (0 disables automatic retries). Records <see cref="DeferralExpiredEventType"/> once when the deadline passes and
        /// <see cref="RetriesExhaustedEventType"/> once when the retries are used up. Bounded by
        /// <see cref="MaxLandingRetriesPerSweep"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Landings retried.</returns>
        public async Task<int> RetryDeferredLandingsAsync(CancellationToken token = default)
        {
            if (_Settings.LandingDeferralDeadlineHours <= 0) return 0;
            DateTime now = DateTime.UtcNow;
            int retried = 0;
            foreach (MissionSummary mission in await ReadMissionsAsync(MissionStatusEnum.LandingFailed, token).ConfigureAwait(false))
            {
                if (retried >= MaxLandingRetriesPerSweep) break;
                if (mission.FailureKind != MissionFailureKindEnum.Infra) continue;
                if (_LandingRetriedUtc.TryGetValue(mission.Id, out DateTime lastRetry) && lastRetry > now - LandingRetryInterval) continue;

                ArmadaEvent? deferredEvent = await LatestEventAsync(mission.Id, DeferredEventType, token).ConfigureAwait(false);
                LandingDeferral? deferral = ReadDeferral(deferredEvent);
                // The deferral must describe the current failure: it is recorded right after the mission's update.
                if (deferral == null || deferredEvent!.CreatedUtc < mission.LastUpdateUtc.AddSeconds(-2)) continue;

                if (now > deferral.DeadlineUtc)
                {
                    ArmadaEvent? expired = await LatestEventAsync(mission.Id, DeferralExpiredEventType, token).ConfigureAwait(false);
                    if (expired == null || expired.CreatedUtc < deferredEvent.CreatedUtc)
                    {
                        _Logging.Warn(_Header + "mission " + mission.Id + " has waited on " + deferral.CheckoutPath + " since " + deferral.FirstDeferredUtc.ToString("u")
                            + "; no more automatic landing retries");
                        await EmitAsync(DeferralExpiredEventType, "Stopped retrying the landing automatically: " + deferral.CheckoutPath
                            + " was not clean by " + deferral.DeadlineUtc.ToString("u") + ". Clean it and retry landing.", mission, "mission", mission.Id, null, token).ConfigureAwait(false);
                        await RecordDeferralEndAsync(mission.Id, deferral, BlackBoxStepEnum.DeferralExpired,
                            "Automatic landing retries stopped: " + deferral.CheckoutPath + " was not clean by " + deferral.DeadlineUtc.ToString("u") + ".", token).ConfigureAwait(false);
                    }
                    continue;
                }

                if (!await IsCheckoutCleanAsync(deferral, token).ConfigureAwait(false)) continue;

                // Each deferral episode (since its first refusal) gets at most MaxLandingRetries automatic retries; the
                // count is read from the event log so it survives an Admiral restart.
                long autoRetries = await CountEventsSinceAsync(mission.Id, AutoRetryEventType, deferral.FirstDeferredUtc, token).ConfigureAwait(false);
                if (autoRetries >= _Settings.MaxLandingRetries)
                {
                    ArmadaEvent? exhausted = await LatestEventAsync(mission.Id, RetriesExhaustedEventType, token).ConfigureAwait(false);
                    if (exhausted == null || exhausted.CreatedUtc < deferral.FirstDeferredUtc)
                    {
                        _Logging.Warn(_Header + "mission " + mission.Id + " used " + autoRetries + " of " + _Settings.MaxLandingRetries
                            + " automatic landing retries; no more automatic retries");
                        await EmitAsync(RetriesExhaustedEventType, "Stopped retrying the landing automatically after " + autoRetries + " of "
                            + _Settings.MaxLandingRetries + " retries (maxLandingRetries). Retry landing manually.", mission, "mission", mission.Id, null, token).ConfigureAwait(false);
                        await RecordDeferralEndAsync(mission.Id, deferral, BlackBoxStepEnum.RetryLanding,
                            "Automatic landing retries used up: " + autoRetries + " of " + _Settings.MaxLandingRetries + " (maxLandingRetries).", token).ConfigureAwait(false);
                    }
                    continue;
                }

                _LandingRetriedUtc[mission.Id] = now;
                retried++;
                await EmitAsync(AutoRetryEventType, "Retrying the deferred landing automatically (" + (autoRetries + 1) + " of " + _Settings.MaxLandingRetries + ")",
                    mission, "mission", mission.Id, null, token).ConfigureAwait(false);
                _Logging.Info(_Header + "checkout " + deferral.CheckoutPath + " is clean; retrying the deferred landing of mission " + mission.Id);
                try
                {
                    await _Landing.RetryLandingAsync(mission.Id, null, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _Logging.Warn(_Header + "retrying the deferred landing of mission " + mission.Id + " failed: " + ex.Message);
                }
            }
            return retried;
        }

        #endregion

        #region Private-Methods

        private async Task RecordDeferralEndAsync(string missionId, LandingDeferral deferral, BlackBoxStepEnum step, string note, CancellationToken token)
        {
            if (BlackBox == null) return;
            Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null) return;
            // The mission stays LandingFailed; the entry records that waiting on the dirty checkout ended.
            await BlackBox.RecordMissionFailureAsync(mission, mission.Status, step, context =>
            {
                context.GitFacts = new BlackBoxGitFacts { CheckoutPath = deferral.CheckoutPath, DirtyPaths = new List<string>(deferral.ModifiedPaths), GitOperation = "status --porcelain" };
                context.Note = note + " Deferred since " + deferral.FirstDeferredUtc.ToString("u") + ".";
            }, token).ConfigureAwait(false);
        }

        private async Task<List<MissionSummary>> ReadMissionsAsync(MissionStatusEnum status, CancellationToken token)
        {
            EnumerationResult<MissionSummary> page = await _Database.Missions.EnumerateSummariesAsync(new EnumerationQuery
            {
                Status = status.ToString(),
                PageSize = 200,
                PageNumber = 1
            }, token).ConfigureAwait(false);
            return page.Objects.OrderByDescending(m => m.LastUpdateUtc).ToList();
        }

        private async Task<ArmadaEvent?> LatestEventAsync(string missionId, string eventType, CancellationToken token)
        {
            EnumerationResult<ArmadaEvent> events = await _Database.Events.EnumerateAsync(new EnumerationQuery
            {
                MissionId = missionId,
                EventType = eventType,
                PageSize = 1,
                Order = EnumerationOrderEnum.CreatedDescending
            }, token).ConfigureAwait(false);
            return events.Objects.FirstOrDefault();
        }

        private async Task<long> CountEventsSinceAsync(string missionId, string eventType, DateTime sinceUtc, CancellationToken token)
        {
            EnumerationResult<ArmadaEvent> events = await _Database.Events.EnumerateAsync(new EnumerationQuery
            {
                MissionId = missionId,
                EventType = eventType,
                CreatedAfter = sinceUtc.AddSeconds(-1),
                PageSize = 1
            }, token).ConfigureAwait(false);
            return events.TotalRecords;
        }

        private async Task<bool> IsCheckoutCleanAsync(LandingDeferral deferral, CancellationToken token)
        {
            if (String.IsNullOrEmpty(deferral.CheckoutPath)) return false;
            try
            {
                IGitService git = _Git;
                if (!String.IsNullOrEmpty(deferral.HarborId))
                {
                    if (Hosts == null) return false;
                    git = Hosts.ForHarbor(deferral.HarborId).Git;
                }
                GitWorkingTreeStatus status = await git.GetWorkingTreeStatusAsync(deferral.CheckoutPath, token).ConfigureAwait(false);
                return status.IsClean;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _Logging.Debug(_Header + "could not read the status of " + deferral.CheckoutPath + ": " + ex.Message);
                return false;
            }
        }

        private async Task EmitAsync(string eventType, string message, MissionSummary mission, string entityType, string entityId, string? payload, CancellationToken token)
        {
            try
            {
                ArmadaEvent evt = new ArmadaEvent(eventType, message);
                evt.TenantId = mission.TenantId;
                evt.UserId = mission.UserId;
                evt.EntityType = entityType;
                evt.EntityId = entityId;
                evt.MissionId = mission.Id;
                evt.VesselId = mission.VesselId;
                evt.VoyageId = mission.VoyageId;
                evt.CaptainId = mission.CaptainId;
                evt.Payload = payload;
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _Logging.Warn(_Header + "could not record " + eventType + " for mission " + mission.Id + ": " + ex.Message);
            }
        }

        #endregion
    }
}
