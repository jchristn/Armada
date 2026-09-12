namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
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
        #region Private-Members

        private readonly string _Header = "[MissionRecoveryCoordinator] ";
        private readonly LoggingModule _Logging;
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly IncidentService _Incidents;

        private static readonly TimeSpan _FailureLookback = TimeSpan.FromHours(24);
        private const int MaxDiffExcerptChars = 4000;
        private const string ExhaustedNote = "Autonomous rescue exhausted the attempt budget; needs a human.";

        private static readonly MissionStatusEnum[] _ActiveStatuses = new[]
        {
            MissionStatusEnum.Pending,
            MissionStatusEnum.Assigned,
            MissionStatusEnum.InProgress,
            MissionStatusEnum.Testing,
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

            List<Mission> rescues = new List<Mission>();
            foreach (string rescueId in incident.RescueMissionIds)
            {
                Mission? rescue = await _Database.Missions.ReadAsync(rescueId, token).ConfigureAwait(false);
                if (rescue != null) rescues.Add(rescue);
            }

            bool anyComplete = rescues.Any(m => m.Status == MissionStatusEnum.Complete);
            if (anyComplete)
            {
                await TransitionAsync(auth, incident, IncidentStatusEnum.Mitigated, "A rescue mission landed successfully.", token).ConfigureAwait(false);
                return;
            }

            bool anyActive = rescues.Any(m => _ActiveStatuses.Contains(m.Status));
            if (anyActive) return; // A rescue is in flight; wait for it.

            // No rescue in flight and none succeeded: dispatch another if the budget allows.
            if (incident.RecoveryAttempts < maxAttempts)
            {
                if (String.IsNullOrEmpty(incident.MissionId)) return;
                Mission? original = await _Database.Missions.ReadAsync(incident.MissionId!, token).ConfigureAwait(false);
                if (original == null) return;

                Mission rescueMission = await DispatchRescueAsync(original, incident, token).ConfigureAwait(false);
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

                MissionFailureKindEnum kind = MissionFailureClassifier.Classify(mission.Status, mission.FailureReason);
                if (!MissionFailureClassifier.IsRecoverable(kind)) continue;

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
                if (maxAttempts > 0)
                {
                    Mission rescueMission = await DispatchRescueAsync(mission, incident, token).ConfigureAwait(false);
                    incident.RescueMissionIds.Add(rescueMission.Id);
                    incident.RecoveryAttempts += 1;
                    incident.RecoveryNotes = "Dispatched rescue mission " + rescueMission.Id + " (attempt 1/" + maxAttempts + ").";
                    await UpdateIncidentAsync(auth, incident, token).ConfigureAwait(false);
                    linkedMissionIds.Add(rescueMission.Id);
                    _Logging.Info(_Header + "dispatched first rescue " + rescueMission.Id + " for incident " + incident.Id);
                }
            }
        }

        private async Task<Mission> DispatchRescueAsync(Mission original, Incident incident, CancellationToken token)
        {
            // The rescue is a fresh, standalone Worker mission (no voyage attachment, so it never re-opens a
            // terminal voyage) linked back through ParentMissionId and the incident. The normal pending-dispatch
            // sweep assigns it on the next health cycle.
            Mission rescue = new Mission();
            rescue.TenantId = original.TenantId;
            rescue.UserId = original.UserId;
            rescue.VesselId = original.VesselId;
            rescue.ParentMissionId = original.Id;
            rescue.Persona = PersonaCatalog.Worker;
            rescue.Mode = MissionModeEnum.Implementation;
            rescue.Tier = original.Tier;
            rescue.Priority = Math.Max(0, original.Priority - 50);
            rescue.Title = "[Rescue] " + original.Title;
            rescue.Description = BuildRescueBrief(original, incident);
            rescue.Status = MissionStatusEnum.Pending;

            rescue = await _Database.Missions.CreateAsync(rescue, token).ConfigureAwait(false);
            return rescue;
        }

        private static string BuildRescueBrief(Mission original, Incident incident)
        {
            string brief =
                "This is an autonomous RESCUE mission. A prior attempt at the work below failed and needs a " +
                "fix-forward. Re-implement the change correctly, addressing the failure, and stay strictly in scope.\n\n" +
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
            IncidentQuery query = new IncidentQuery { PageNumber = 1, PageSize = 200 };

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
