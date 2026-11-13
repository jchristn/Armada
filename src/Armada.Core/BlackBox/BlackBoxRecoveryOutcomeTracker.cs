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
    using SyslogLogging;

    /// <summary>
    /// Recovery outcome tracking: records every confirmed recovery run (blackbox_recovery_outcomes) and settles it when
    /// the mission (or voyage) next reaches a terminal status: Complete is Succeeded; Failed, LandingFailed, Cancelled or a
    /// newer black box entry is Failed; a run whose own step failed is RunFailed at once; nothing within seven days is
    /// Expired. Each settled outcome is noted on the entry (author System) and refreshes the advisor's path success rates.
    /// Thread-safe.
    /// </summary>
    public class BlackBoxRecoveryOutcomeTracker : IBlackBoxRecoveryObserver
    {
        #region Public-Members

        /// <summary>
        /// The path success-rate snapshot this tracker refreshes.
        /// </summary>
        public BlackBoxPathStatsCache Stats => _Stats;

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxRecoveryOutcomeTracker] ";
        private readonly DatabaseDriver _Database;
        private readonly BlackBoxPathStatsCache _Stats;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="stats">Path statistics snapshot.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxRecoveryOutcomeTracker(DatabaseDriver database, BlackBoxPathStatsCache stats, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task OnRecoveryRunAsync(AuthContext caller, BlackBoxEntry entry, BlackBoxRecoveryPath path, BlackBoxRecoveryRunResult result, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.DryRun) return;
            BlackBoxRecoveryOutcome outcome = new BlackBoxRecoveryOutcome
            {
                TenantId = entry.TenantId,
                UserId = caller.UserId,
                EntryId = entry.Id,
                MissionId = entry.MissionId,
                VoyageId = entry.VoyageId,
                VesselId = entry.VesselId,
                RuleId = path.RuleId,
                PathId = path.Id,
                RunbookId = path.RunbookId,
                RunSucceeded = result.Succeeded,
                Outcome = result.Succeeded ? BlackBoxRecoveryOutcomeEnum.Pending : BlackBoxRecoveryOutcomeEnum.RunFailed,
                CreatedUtc = DateTime.UtcNow
            };
            if (!result.Succeeded) outcome.ResolvedUtc = outcome.CreatedUtc;
            await _Database.BlackBoxRecoveryOutcomes.CreateAsync(outcome, token).ConfigureAwait(false);
            if (!result.Succeeded) await RefreshQuietlyAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Settle Pending outcomes whose mission (or voyage) reached a terminal status since the run (at most 200 per pass,
        /// oldest first).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcomes settled.</returns>
        public async Task<int> SettleAsync(CancellationToken token = default)
        {
            EnumerationResult<BlackBoxRecoveryOutcome> pending = await _Database.BlackBoxRecoveryOutcomes.EnumeratePendingAsync(new EnumerationQuery { PageNumber = 1, PageSize = BlackBoxInsightLimits.MaxOutcomeScan }, token).ConfigureAwait(false);
            int settled = 0;
            DateTime now = DateTime.UtcNow;
            foreach (BlackBoxRecoveryOutcome o in pending.Objects)
            {
                BlackBoxRecoveryOutcomeEnum? verdict = null;
                string? status = null;
                if (!String.IsNullOrEmpty(o.MissionId))
                {
                    Mission? mission = await _Database.Missions.ReadAsync(o.MissionId!, token).ConfigureAwait(false);
                    if (mission == null) { verdict = BlackBoxRecoveryOutcomeEnum.Expired; status = "Deleted"; }
                    else
                    {
                        status = mission.Status.ToString();
                        BlackBoxEntry? current = String.IsNullOrEmpty(mission.LastBlackBoxEntryId) || mission.LastBlackBoxEntryId == o.EntryId
                            ? null : await _Database.BlackBoxEntries.ReadAsync(mission.LastBlackBoxEntryId!, null, true, token).ConfigureAwait(false);
                        DateTime changed = mission.CompletedUtc.HasValue && mission.CompletedUtc.Value > mission.LastUpdateUtc ? mission.CompletedUtc.Value : mission.LastUpdateUtc;
                        if (current != null && current.CreatedUtc > o.CreatedUtc) verdict = BlackBoxRecoveryOutcomeEnum.Failed;
                        else if (mission.Status == MissionStatusEnum.Complete && changed >= o.CreatedUtc) verdict = BlackBoxRecoveryOutcomeEnum.Succeeded;
                        else if ((mission.Status == MissionStatusEnum.Failed || mission.Status == MissionStatusEnum.LandingFailed || mission.Status == MissionStatusEnum.Cancelled) && changed > o.CreatedUtc) verdict = BlackBoxRecoveryOutcomeEnum.Failed;
                    }
                }
                else if (!String.IsNullOrEmpty(o.VoyageId))
                {
                    Voyage? voyage = await _Database.Voyages.ReadAsync(o.VoyageId!, token).ConfigureAwait(false);
                    if (voyage == null) { verdict = BlackBoxRecoveryOutcomeEnum.Expired; status = "Deleted"; }
                    else
                    {
                        status = voyage.Status.ToString();
                        if (voyage.LastUpdateUtc > o.CreatedUtc && voyage.Status == VoyageStatusEnum.Complete) verdict = BlackBoxRecoveryOutcomeEnum.Succeeded;
                        else if (voyage.LastUpdateUtc > o.CreatedUtc && (voyage.Status == VoyageStatusEnum.Failed || voyage.Status == VoyageStatusEnum.Cancelled)) verdict = BlackBoxRecoveryOutcomeEnum.Failed;
                    }
                }
                if (verdict == null && now - o.CreatedUtc > TimeSpan.FromDays(BlackBoxInsightLimits.OutcomeWatchDays)) verdict = BlackBoxRecoveryOutcomeEnum.Expired;
                if (verdict == null) continue;
                if (!await _Database.BlackBoxRecoveryOutcomes.SettleAsync(o.Id, verdict.Value, status, now, token).ConfigureAwait(false)) continue;
                settled++;
                await NoteAsync(o, verdict.Value, status, token).ConfigureAwait(false);
            }
            if (settled > 0) await RefreshQuietlyAsync(token).ConfigureAwait(false);
            return settled;
        }

        #endregion

        #region Private-Methods

        private async Task NoteAsync(BlackBoxRecoveryOutcome o, BlackBoxRecoveryOutcomeEnum verdict, string? status, CancellationToken token)
        {
            string what = verdict == BlackBoxRecoveryOutcomeEnum.Succeeded ? "worked: the " + (o.MissionId != null ? "mission" : "voyage") + " reached " + status
                : verdict == BlackBoxRecoveryOutcomeEnum.Failed ? "did not work: the " + (o.MissionId != null ? "mission" : "voyage") + " ended " + status
                : "has no outcome: nothing terminal happened within " + BlackBoxInsightLimits.OutcomeWatchDays + " days" + (status != null ? " (now " + status + ")" : String.Empty);
            try
            {
                BlackBoxEntry? entry = await _Database.BlackBoxEntries.ReadAsync(o.EntryId, null, false, token).ConfigureAwait(false);
                if (entry == null) return;
                await _Database.BlackBoxNotes.CreateAsync(new BlackBoxNote
                {
                    EntryId = entry.Id,
                    TenantId = entry.TenantId,
                    UserId = o.UserId,
                    AuthorKind = BlackBoxNoteAuthorKindEnum.System,
                    Body = "Recovery outcome (" + o.Id + "): path " + o.PathId + " run at " + o.CreatedUtc.ToString("u") + " " + what + ".",
                    CreatedUtc = DateTime.UtcNow
                }, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not note the outcome of " + o.Id + " on " + o.EntryId + ": " + e.Message);
            }
        }

        private async Task RefreshQuietlyAsync(CancellationToken token)
        {
            try
            {
                await _Stats.RefreshAsync(token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not refresh recovery path statistics: " + e.Message);
            }
        }

        #endregion
    }
}
