namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Reconciles autonomous mission recovery from Armada's own evidence. Each maintenance pass:
    /// classifies recently failed missions and opens an incident for the recoverable ones; dispatches a
    /// bounded rescue mission per incident and re-dispatches when a rescue itself fails (up to a cap); and
    /// advances incidents Open -&gt; Mitigated -&gt; Closed as their rescue missions land. Nothing here requires a
    /// human -- the state transitions are driven entirely by mission outcomes.
    /// </summary>
    public class MissionRecoveryCoordinator
    {
        #region Public-Members

        /// <summary>
        /// Black box capture for an original mission whose rescue budget is used up, or null.
        /// </summary>
        public IBlackBoxCapture? BlackBox { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[MissionRecoveryCoordinator] ";
        private readonly LoggingModule _Logging;
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly IncidentService _Incidents;
        private readonly SemaphoreSlim _MaintenanceGate = new SemaphoreSlim(1, 1);

        private static readonly TimeSpan _FailureLookback = TimeSpan.FromHours(24);
        private const int MaxDiffExcerptChars = 4000;
        private const string ExhaustedNote = "Autonomous rescue exhausted the attempt budget; needs a human.";

        private static readonly MissionStatusEnum[] _ActiveStatuses = new[]
        {
            MissionStatusEnum.Pending,
            MissionStatusEnum.Assigned,
            MissionStatusEnum.InProgress,
            MissionStatusEnum.Testing,
            MissionStatusEnum.Verifying,
            MissionStatusEnum.Review,
            MissionStatusEnum.WorkProduced,
            MissionStatusEnum.PullRequestOpen
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Armada settings.</param>
        public MissionRecoveryCoordinator(LoggingModule logging, DatabaseDriver database, ArmadaSettings settings)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Incidents = new IncidentService(_Database);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one reconciliation pass. Safe to call repeatedly; every action is idempotent because incidents
        /// link the missions they cover, so a mission is never processed twice.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public async Task MaintainAsync(CancellationToken token = default)
        {
            await _MaintenanceGate.WaitAsync(token).ConfigureAwait(false);
            try { await MaintainCoreAsync(token).ConfigureAwait(false); }
            finally { _MaintenanceGate.Release(); }
        }

        private async Task MaintainCoreAsync(CancellationToken token)
        {
            int maxAttempts = _Settings.MaxMissionRecoveryAttempts;
            if (maxAttempts <= 0) return; // Autonomous recovery disabled.

            // A system, admin-scoped context with no tenant/user so incident snapshots carry no owner FK.
            AuthContext auth = new AuthContext { IsAuthenticated = true, IsAdmin = true, AuthMethod = "system" };

            List<Incident> recoveryIncidents = await ReadRecoveryIncidentsAsync(auth, token).ConfigureAwait(false);
            HashSet<string> linkedMissionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Incident incident in recoveryIncidents)
            {
                if (!String.IsNullOrEmpty(incident.MissionId)) linkedMissionIds.Add(incident.MissionId!);
                foreach (string rescueId in incident.RescueMissionIds) linkedMissionIds.Add(rescueId);
            }

            // A) Advance existing recovery incidents.
            foreach (Incident incident in recoveryIncidents)
            {
                if (incident.Status == IncidentStatusEnum.Closed || incident.Status == IncidentStatusEnum.RolledBack)
                    continue;

                await ReconcileIncidentAsync(auth, incident, maxAttempts, token).ConfigureAwait(false);
            }

            // B) Open incidents (and dispatch a first rescue) for newly failed, recoverable missions.
            await OpenIncidentsForNewFailuresAsync(auth, linkedMissionIds, maxAttempts, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task ReconcileIncidentAsync(AuthContext auth, Incident incident, int maxAttempts, CancellationToken token)
        {
            // Second step of the lifecycle: a mitigated incident closes once the fix has held for a cycle.
            if (incident.Status == IncidentStatusEnum.Mitigated)
            {
                await TransitionAsync(auth, incident, IncidentStatusEnum.Closed, "Fix landed and held; incident closed.", token).ConfigureAwait(false);
                return;
            }

            // Cancellation and deletion express operator intent to stop this recovery chain.
            // Do not interpret a cancelled or removed rescue as another retryable failure.
            Mission? original = String.IsNullOrEmpty(incident.MissionId)
                ? null
                : await _Database.Missions.ReadAsync(incident.MissionId!, token).ConfigureAwait(false);
            if (original == null || original.Status == MissionStatusEnum.Cancelled)
            {
                await TransitionAsync(auth, incident, IncidentStatusEnum.Closed,
                    "Recovery stopped: the original mission was cancelled or deleted.", token).ConfigureAwait(false);
                return;
            }
            if (incident.RescueMissionIds.Count > 0)
            {
                Mission? latestRescue = await _Database.Missions.ReadAsync(incident.RescueMissionIds[incident.RescueMissionIds.Count - 1], token).ConfigureAwait(false);
                if (latestRescue == null || latestRescue.Status == MissionStatusEnum.Cancelled)
                {
                    await TransitionAsync(auth, incident, IncidentStatusEnum.Closed,
                        "Recovery stopped: the latest rescue mission was cancelled or deleted.", token).ConfigureAwait(false);
                    return;
                }
            }

            List<Mission> rescues = new List<Mission>();
            foreach (string rescueId in incident.RescueMissionIds)
            {
                Mission? rescue = await _Database.Missions.ReadAsync(rescueId, token).ConfigureAwait(false);
                if (rescue != null) rescues.Add(rescue);
            }

            bool anyComplete = rescues.Any(m => m.Status == MissionStatusEnum.Complete);
            if (anyComplete)
            {
                Mission landed = rescues.First(m => m.Status == MissionStatusEnum.Complete);
                Mission? unresolved = await _Database.Missions.ReadAsync(original.Id, token).ConfigureAwait(false);
                if (unresolved != null && (unresolved.Status == MissionStatusEnum.Failed || unresolved.Status == MissionStatusEnum.LandingFailed))
                {
                    unresolved.Status = MissionStatusEnum.Complete;
                    unresolved.FailureReason = "Resolved by landed rescue " + landed.Id + ". Prior failure: " + unresolved.FailureReason;
                    unresolved.CompletedUtc = DateTime.UtcNow;
                    unresolved.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.Missions.TryUpdateIfStatusAsync(unresolved, new[] { MissionStatusEnum.Failed, MissionStatusEnum.LandingFailed }, token).ConfigureAwait(false);
                }
                await TransitionAsync(auth, incident, IncidentStatusEnum.Mitigated, "A rescue mission landed successfully.", token).ConfigureAwait(false);
                return;
            }

            bool anyActive = rescues.Any(m => _ActiveStatuses.Contains(m.Status));
            if (anyActive) return; // A rescue is in flight; wait for it.

            // No rescue in flight and none succeeded: dispatch another if the budget allows.
            // The budget (MaxMissionRecoveryAttempts, at most 5) bounds RescueMissionIds; the link-list limit is checked too
            // so an incident edited by hand to hold many ids is never pushed past it.
            if (incident.RecoveryAttempts < maxAttempts && incident.RescueMissionIds.Count < CollectionLimits.MaxLinkedIds)
            {
                if (String.IsNullOrEmpty(incident.MissionId)) return;
                Mission? rescueMission = await DispatchRescueAsync(original, incident, token).ConfigureAwait(false);
                if (rescueMission == null) return;
                incident.RescueMissionIds.Add(rescueMission.Id);
                incident.RecoveryAttempts += 1;
                incident.RecoveryNotes = "Dispatched rescue mission " + rescueMission.Id + " (attempt " + incident.RecoveryAttempts + "/" + maxAttempts + ").";
                await UpdateIncidentAsync(auth, incident, token).ConfigureAwait(false);
                _Logging.Info(_Header + "dispatched rescue " + rescueMission.Id + " for incident " + incident.Id +
                    " (attempt " + incident.RecoveryAttempts + "/" + maxAttempts + ")");
            }
            else if (String.IsNullOrEmpty(incident.RecoveryNotes) || !incident.RecoveryNotes!.Contains(ExhaustedNote, StringComparison.Ordinal))
            {
                incident.RecoveryNotes = ExhaustedNote;
                await UpdateIncidentAsync(auth, incident, token).ConfigureAwait(false);
                _Logging.Warn(_Header + "incident " + incident.Id + " exhausted its rescue budget; left open for a human");
                Mission? lastRescue = rescues.LastOrDefault();
                await BlackBox.RecordMissionFailureAsync(original, original.Status, BlackBoxStepEnum.RescueFailed, context =>
                {
                    context.IncidentId = incident.Id;
                    context.Note = "Incident " + incident.Id + " used " + incident.RecoveryAttempts + " of " + maxAttempts + " rescue attempt(s); rescues: "
                        + String.Join(", ", rescues.Select(r => r.Id + " (" + r.Status + ")"))
                        + (lastRescue?.LastBlackBoxEntryId != null ? "; last rescue entry " + lastRescue.LastBlackBoxEntryId : "") + ".";
                }, token).ConfigureAwait(false);
            }
        }

        private async Task OpenIncidentsForNewFailuresAsync(AuthContext auth, HashSet<string> linkedMissionIds, int maxAttempts, CancellationToken token)
        {
            DateTime cutoff = DateTime.UtcNow - _FailureLookback;

            List<Mission> failed = new List<Mission>();
            failed.AddRange(await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.Failed, token).ConfigureAwait(false));
            failed.AddRange(await _Database.Missions.EnumerateByStatusAsync(MissionStatusEnum.LandingFailed, token).ConfigureAwait(false));

            foreach (Mission mission in failed)
            {
                if (linkedMissionIds.Contains(mission.Id)) continue;
                if (mission.LastUpdateUtc < cutoff) continue;

                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(mission);
                if (!MissionFailureClassifier.IsRecoverable(kind)) continue;
                if (!await IsRescueEligibleAsync(mission, token).ConfigureAwait(false)) continue;

                Incident incident = await _Incidents.CreateAsync(auth, new IncidentUpsertRequest
                {
                    Title = "Mission recovery: " + mission.Title,
                    Summary = "Autonomous recovery opened for a " + kind + " failure.",
                    Status = IncidentStatusEnum.Open,
                    Severity = IncidentSeverityEnum.Medium,
                    VesselId = mission.VesselId,
                    MissionId = mission.Id,
                    VoyageId = mission.VoyageId,
                    Impact = "Mission " + mission.Id + " failed: " + Trim(mission.FailureReason, 500),
                    RootCause = "Classified failure kind: " + kind
                }, token).ConfigureAwait(false);

                incident.FailureKind = kind.ToString();
                await UpdateIncidentAsync(auth, incident, token).ConfigureAwait(false);
                linkedMissionIds.Add(mission.Id);
                _Logging.Info(_Header + "opened incident " + incident.Id + " for failed mission " + mission.Id + " (" + kind + ")");

                // Dispatch the first rescue immediately when the budget allows.
                if (maxAttempts > 0 && incident.RescueMissionIds.Count < CollectionLimits.MaxLinkedIds)
                {
                    Mission? rescueMission = await DispatchRescueAsync(mission, incident, token).ConfigureAwait(false);
                    if (rescueMission == null) continue;
                    incident.RescueMissionIds.Add(rescueMission.Id);
                    incident.RecoveryAttempts += 1;
                    incident.RecoveryNotes = "Dispatched rescue mission " + rescueMission.Id + " (attempt 1/" + maxAttempts + ").";
                    await UpdateIncidentAsync(auth, incident, token).ConfigureAwait(false);
                    linkedMissionIds.Add(rescueMission.Id);
                    _Logging.Info(_Header + "dispatched first rescue " + rescueMission.Id + " for incident " + incident.Id);
                }
            }
        }

        private async Task<bool> IsRescueEligibleAsync(Mission mission, CancellationToken token)
        {
            // A rescue is a standalone Worker that lands on its own. That suits standalone implementation work only: a
            // review, planning or recording stage has no code for a Worker to fix, and a pipeline stage has its own rework
            // and repair inside its voyage, which a rescue landed beside it would bypass (including its Judge).
            if (!String.IsNullOrEmpty(mission.Persona) && !PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Worker)) return false;

            // A rescue's failure belongs to the incident that dispatched it, which retries within its budget. If that
            // incident is gone (purging the original mission removes it and clears ParentMissionId), rescuing the rescue
            // would restart the chain without a cap.
            if (mission.IsRescue || !String.IsNullOrEmpty(mission.ParentMissionId)) return false;
            if (!String.IsNullOrEmpty(mission.DependsOnMissionId)) return false;
            if (String.IsNullOrEmpty(mission.VoyageId)) return true;

            List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(mission.VoyageId!, token).ConfigureAwait(false);
            return !voyageMissions.Any(m => String.Equals(m.DependsOnMissionId, mission.Id, StringComparison.Ordinal));
        }

        private async Task<Mission?> DispatchRescueAsync(Mission original, Incident incident, CancellationToken token)
        {
            // The rescue is a fresh, standalone Worker mission (no voyage attachment, so it never re-opens a
            // terminal voyage) linked back through ParentMissionId and the incident. The normal pending-dispatch
            // sweep assigns it on the next health cycle.
            List<Mission> peers = await _Database.Missions.EnumerateByVesselAsync(original.VesselId!, token).ConfigureAwait(false);
            Mission? existing = peers.FirstOrDefault(m => m.Id != original.Id && m.TenantId == original.TenantId
                && _ActiveStatuses.Contains(m.Status)
                && (m.ParentMissionId == original.Id || String.Equals(m.Title, original.Title, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(m.Title, "[Rescue] " + original.Title, StringComparison.OrdinalIgnoreCase)
                    || ReferencesOriginal(m, original)));
            if (existing != null)
            {
                _Logging.Info(_Header + "not dispatching a rescue for " + original.Id + ": mission " + existing.Id + " (" + existing.Status
                    + ") already recovers the same work");
                ArmadaEvent evt = new ArmadaEvent("mission.rescue_deduplicated", "Rescue for " + original.Id + " suppressed because " + existing.Id + " is open")
                {
                    TenantId = original.TenantId, UserId = original.UserId, EntityType = "mission", EntityId = original.Id,
                    MissionId = original.Id, VesselId = original.VesselId, VoyageId = original.VoyageId
                };
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
                return null;
            }
            Mission rescue = new Mission();
            rescue.TenantId = original.TenantId;
            rescue.UserId = original.UserId;
            rescue.VesselId = original.VesselId;
            rescue.IsEvaluation = original.IsEvaluation;
            rescue.WorkerEnvelope = original.WorkerEnvelope;
            rescue.ParentMissionId = original.Id;
            rescue.IsRescue = true;
            rescue.Persona = PersonaCatalog.Worker;
            rescue.Mode = MissionModeEnum.Implementation;
            rescue.Tier = original.Tier;
            rescue.Priority = Math.Max(0, original.Priority - 50);
            rescue.Title = "[Rescue] " + original.Title;
            rescue.Description = HasRecordedWork(original) ? BuildIntegrationRescueBrief(original, incident) : BuildRescueBrief(original, incident);
            rescue.Status = MissionStatusEnum.Pending;

            rescue = await _Database.Missions.CreateAsync(rescue, token).ConfigureAwait(false);
            return rescue;
        }

        /// <summary>
        /// Whether a mission names the original (by id, or by its produced commit) in its brief, as an operator-dispatched
        /// recovery of the same work does.
        /// </summary>
        private static bool ReferencesOriginal(Mission candidate, Mission original)
        {
            string text = (candidate.Title ?? "") + "\n" + (candidate.Description ?? "");
            if (text.Contains(original.Id, StringComparison.Ordinal)) return true;
            string? commit = original.CommitHash;
            if (String.IsNullOrEmpty(commit) || commit.Length < 7) return false;
            return text.Contains(commit.Substring(0, Math.Min(commit.Length, 9)), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether the failed mission left a commit on a named branch: its work exists and needs integrating, not redoing.
        /// </summary>
        private static bool HasRecordedWork(Mission original)
        {
            return !String.IsNullOrWhiteSpace(original.CommitHash) && !String.IsNullOrWhiteSpace(original.BranchName);
        }

        private static string BuildIntegrationRescueBrief(Mission original, Incident incident)
        {
            string commit = original.CommitHash!.Trim();
            string branch = original.BranchName!.Trim();
            string brief =
                "This is an autonomous RESCUE mission. A prior attempt at the work below already produced commit " + commit +
                " on branch " + branch + ", but landing it failed. Do NOT re-implement the change: integrate that existing commit onto the current target.\n\n" +
                "## Steps\n" +
                "1. Start from the prior attempt's work: `git merge --no-edit " + commit + "` (if the commit is not in this checkout, first `git fetch origin " + branch + "`, then merge `FETCH_HEAD`). Stop and report BLOCKED if the commit cannot be found.\n" +
                "2. Integrate the latest target head (fetch it, then merge it), resolving every conflict so both the target's changes and the prior commit's intent survive.\n" +
                "3. Fix only what the failure below requires, stay strictly in scope, and rerun the relevant build and tests.\n\n" +
                "## Original Mission\n" +
                original.Title + "\n\n" +
                (String.IsNullOrWhiteSpace(original.Description) ? "" : Trim(original.Description, 3000) + "\n\n") +
                "## Failure To Fix\n" +
                "Classified kind: " + (incident.FailureKind ?? "Unknown") + "\n" +
                "Reason: " + Trim(original.FailureReason, 1500) + "\n\n";

            if (!String.IsNullOrWhiteSpace(original.DiffSnapshot))
            {
                brief += "## Prior Attempt Diff (already in commit " + commit + ")\n```diff\n" + Trim(original.DiffSnapshot, MaxDiffExcerptChars) + "\n```\n\n";
            }

            brief += "End with a standalone `[ARMADA:RESULT] COMPLETE` line and a brief summary of how the commit was integrated.";
            return brief;
        }

        private static string BuildRescueBrief(Mission original, Incident incident)
        {
            string brief =
                "This is an autonomous RESCUE mission. A prior attempt at the work below failed and needs a " +
                "fix-forward. Fetch the current target branch head before starting. Start from that head and re-apply only the required change; do not reuse the failed dock's stale base. Before handoff, integrate the latest target head, check for conflicts, and rerun the relevant checks.\n\n" +
                "Re-implement the change correctly, addressing the failure, and stay strictly in scope.\n\n" +
                "## Original Mission\n" +
                original.Title + "\n\n" +
                (String.IsNullOrWhiteSpace(original.Description) ? "" : Trim(original.Description, 3000) + "\n\n") +
                "## Failure To Fix\n" +
                "Classified kind: " + (incident.FailureKind ?? "Unknown") + "\n" +
                "Reason: " + Trim(original.FailureReason, 1500) + "\n\n";

            if (!String.IsNullOrWhiteSpace(original.DiffSnapshot))
            {
                brief += "## Prior Attempt Diff (for reference)\n```diff\n" + Trim(original.DiffSnapshot, MaxDiffExcerptChars) + "\n```\n\n";
            }

            brief += "End with a standalone `[ARMADA:RESULT] COMPLETE` line and a brief summary of the fix.";
            return brief;
        }

        private async Task<List<Incident>> ReadRecoveryIncidentsAsync(AuthContext auth, CancellationToken token)
        {
            List<Incident> results = new List<Incident>();
            // Only recovery incidents (those with a FailureKind), filtered and paged in the database.
            IncidentQuery query = new IncidentQuery { PageNumber = 1, PageSize = 200, HasFailureKind = true };

            while (true)
            {
                EnumerationResult<Incident> page = await _Incidents.EnumerateAsync(auth, query, token).ConfigureAwait(false);
                foreach (Incident incident in page.Objects)
                {
                    if (!String.IsNullOrEmpty(incident.FailureKind)) results.Add(incident);
                }
                if (page.Objects.Count < query.PageSize) break;
                query.PageNumber += 1;
            }

            return results;
        }

        private async Task TransitionAsync(AuthContext auth, Incident incident, IncidentStatusEnum status, string note, CancellationToken token)
        {
            incident.Status = status;
            incident.RecoveryNotes = note;
            await UpdateIncidentAsync(auth, incident, token).ConfigureAwait(false);
            _Logging.Info(_Header + "incident " + incident.Id + " -> " + status + " (" + note + ")");
        }

        private async Task UpdateIncidentAsync(AuthContext auth, Incident incident, CancellationToken token)
        {
            await _Incidents.UpdateAsync(auth, incident.Id, new IncidentUpsertRequest
            {
                Title = incident.Title,
                Summary = incident.Summary,
                Status = incident.Status,
                Severity = incident.Severity,
                VesselId = incident.VesselId,
                MissionId = incident.MissionId,
                VoyageId = incident.VoyageId,
                Impact = incident.Impact,
                RootCause = incident.RootCause,
                RecoveryNotes = incident.RecoveryNotes,
                FailureKind = incident.FailureKind,
                RecoveryAttempts = incident.RecoveryAttempts,
                RescueMissionIds = incident.RescueMissionIds
            }, token).ConfigureAwait(false);
        }

        private static string Trim(string? value, int maxChars)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            if (value!.Length <= maxChars) return value;
            return value.Substring(0, maxChars) + "...(truncated)";
        }

        #endregion
    }
}
