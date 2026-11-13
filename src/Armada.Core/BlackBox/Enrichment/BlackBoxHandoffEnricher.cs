namespace Armada.Core.BlackBox.Enrichment
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Handoff: for mission entries in Armada's handoff and landing scope (<see cref="BlackBoxResilienceScope"/>), the
    /// ordered handoff and landing trail of the attempt (record.handoff and evidence/handoff-timeline.json): the
    /// completion, stage check, handoff, landing attempt, retry, deferral and reconcile events of the mission, the dock's
    /// and the merge entry's events, the automatic recoveries Armada already attempted (redispatches, output repairs,
    /// landing and deferral retries, rescues, dock repairs, reconciles, orphan recoveries) with their outcome, the dock
    /// and lease transitions, and how far the target moved. Runs after Attempts and TargetMovement (it reads their facts).
    /// </summary>
    public class BlackBoxHandoffEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "Handoff";

        #endregion

        #region Private-Members

        private const int _EventsPerSource = 200;
        private static readonly string[] _MissionKinds = new string[]
        {
            "mission.work_produced", "mission.completed", "mission.stage_check", "mission.contract_repaired", "mission.redispatched",
            "mission.orphan_recovered", "mission.branch_", "mission.dock_preserved", "mission.checkout_sync_skipped", "mission.pull_request_open",
            "mission.landing", "mission.review_", "mission.restarted", "mission.status_changed", "mission.failed", "mission.exit_handler_stale",
            "mission.manual_complete_no_dock", "mission.rescue", "mission.escalated", "mission.cancelled", "mission.resumed", "mission.blocked_by_captain",
            "landing.", "merge", "dock.", "captain.process_exited"
        };

        private readonly DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxHandoffEnricher(DatabaseDriver database, LoggingModule logging) : base(logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a handoff trail from events and the record's facts. Pure.
        /// </summary>
        /// <param name="record">Record (its workspace, conflict and subject counters are read).</param>
        /// <param name="missionEvents">Mission events.</param>
        /// <param name="dockEvents">Dock events.</param>
        /// <param name="mergeEvents">Merge entry events.</param>
        /// <returns>The trail.</returns>
        /// <exception cref="ArgumentNullException">Thrown when record is null.</exception>
        public static BlackBoxHandoffTrail Build(BlackBoxRecord record, IEnumerable<ArmadaEvent>? missionEvents, IEnumerable<ArmadaEvent>? dockEvents, IEnumerable<ArmadaEvent>? mergeEvents)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            List<BlackBoxHandoffStep> steps = new List<BlackBoxHandoffStep>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(BlackBoxTimelineSourceEnum source, IEnumerable<ArmadaEvent>? events, bool filter)
            {
                if (events == null) return;
                foreach (ArmadaEvent e in events)
                {
                    if (e == null || String.IsNullOrEmpty(e.EventType) || e.EventType.StartsWith("blackbox.", StringComparison.Ordinal)) continue;
                    if (filter && !_MissionKinds.Any(k => e.EventType.StartsWith(k, StringComparison.Ordinal))) continue;
                    if (!String.IsNullOrEmpty(e.Id) && !seen.Add(e.Id)) continue;
                    steps.Add(new BlackBoxHandoffStep
                    {
                        TimestampUtc = e.CreatedUtc,
                        Source = source,
                        Kind = e.EventType,
                        Message = BlackBoxLimits.Clip(OneLine(e.Message), BlackBoxLimits.MaxReasonExcerptChars),
                        EntityId = e.Id
                    });
                }
            }
            Add(BlackBoxTimelineSourceEnum.Mission, missionEvents, true);
            Add(BlackBoxTimelineSourceEnum.Dock, dockEvents, false);
            Add(BlackBoxTimelineSourceEnum.MergeEntry, mergeEvents, false);
            List<BlackBoxHandoffStep> ordered = steps.OrderBy(x => x.TimestampUtc).ThenBy(x => (int)x.Source).ThenBy(x => x.Kind, StringComparer.Ordinal).ToList();

            BlackBoxHandoffTrail trail = new BlackBoxHandoffTrail();
            trail.StepTotal = ordered.Count;
            trail.Truncated = ordered.Count > BlackBoxLimits.MaxHandoffSteps;
            trail.Steps = ordered.Skip(Math.Max(0, ordered.Count - BlackBoxLimits.MaxHandoffSteps)).ToList();
            trail.DockTransitions = ordered.Where(x => x.Source == BlackBoxTimelineSourceEnum.Dock || x.Kind.StartsWith("dock.", StringComparison.Ordinal) || x.Kind == "mission.dock_preserved")
                .Reverse().Take(BlackBoxLimits.MaxDockTransitions).Reverse().ToList();
            trail.AutoRecoveries = Recoveries(record, ordered);
            trail.LandingAttempts = ordered.Any(x => x.Kind.StartsWith("mission.landing", StringComparison.Ordinal) || x.Kind == "mission.work_produced")
                ? 1 + ordered.Count(x => x.Kind == "mission.landing_retry" || x.Kind == "mission.landing_auto_retry")
                : 0;

            BlackBoxWorkspace? w = record.Workspace;
            trail.DockStateAtFailure = w?.DockState;
            trail.TargetBranch = w?.TargetBranch ?? record.Subject?.VesselDefaultBranch;
            trail.TargetTipAtStart = w?.TargetTipAtStart;
            trail.TargetTipAtFailure = w?.TargetTipAtFailure;
            int? commits = record.Conflict != null ? Math.Max(record.Conflict.TargetCommitTotal, record.Conflict.TargetCommitsSinceStart.Count) : (int?)null;
            trail.TargetCommitsDuringRun = commits;
            if (!String.IsNullOrEmpty(trail.TargetTipAtStart) && !String.IsNullOrEmpty(trail.TargetTipAtFailure))
                trail.TargetMoved = !String.Equals(trail.TargetTipAtStart, trail.TargetTipAtFailure, StringComparison.OrdinalIgnoreCase);
            else if (commits.HasValue)
                trail.TargetMoved = commits.Value > 0;
            if (ordered.Any(x => x.Kind == "mission.landing_target_advanced")) trail.TargetMoved = true;
            if (ordered.Count == 0) trail.Note = "No handoff or landing events were recorded for this mission.";
            return trail;
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override async Task EnrichCoreAsync(BlackBoxEnrichmentContext context, CancellationToken token)
        {
            BlackBoxEntry entry = context.Entry;
            if (entry.SubjectType != BlackBoxSubjectTypeEnum.Mission || String.IsNullOrEmpty(entry.MissionId)) return;
            if (!BlackBoxResilienceScope.InScope(context.Record)) return;
            string missionId = entry.MissionId!;
            string? tenantId = entry.TenantId;
            List<ArmadaEvent> missionEvents = tenantId != null
                ? await _Database.Events.EnumerateByMissionAsync(tenantId, missionId, _EventsPerSource, token).ConfigureAwait(false)
                : await _Database.Events.EnumerateByMissionAsync(missionId, _EventsPerSource, token).ConfigureAwait(false);
            List<ArmadaEvent> dockEvents = new List<ArmadaEvent>();
            string? dockId = entry.DockId ?? context.Mission?.DockId;
            if (!String.IsNullOrEmpty(dockId))
            {
                dockEvents = tenantId != null
                    ? await _Database.Events.EnumerateByEntityAsync(tenantId, "dock", dockId!, _EventsPerSource, token).ConfigureAwait(false)
                    : await _Database.Events.EnumerateByEntityAsync("dock", dockId!, _EventsPerSource, token).ConfigureAwait(false);
            }
            List<ArmadaEvent> mergeEvents = new List<ArmadaEvent>();
            if (!String.IsNullOrEmpty(entry.MergeEntryId))
            {
                mergeEvents = tenantId != null
                    ? await _Database.Events.EnumerateByEntityAsync(tenantId, "merge_entry", entry.MergeEntryId!, _EventsPerSource, token).ConfigureAwait(false)
                    : await _Database.Events.EnumerateByEntityAsync("merge_entry", entry.MergeEntryId!, _EventsPerSource, token).ConfigureAwait(false);
            }
            BlackBoxHandoffTrail trail = Build(context.Record, missionEvents.Where(e => TenantMatches(entry, e)), dockEvents.Where(e => TenantMatches(entry, e)), mergeEvents.Where(e => TenantMatches(entry, e)));
            if (trail.Truncated) BlackBoxRecordBounder.AddField(context.Record.Truncation, "Handoff.Steps", trail.StepTotal, BlackBoxLimits.MaxHandoffSteps, "items");
            context.Record.Handoff = trail;
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(trail, BlackBoxJson.Options);
            BlackBoxForensics.PutObject(context, new BlackBoxPendingObject { Name = BlackBoxObjectNames.HandoffTimeline, ContentType = BlackBoxObjectNames.JsonContentType, Data = json, IsText = true });
        }

        private static List<BlackBoxAutoRecovery> Recoveries(BlackBoxRecord record, List<BlackBoxHandoffStep> steps)
        {
            Dictionary<BlackBoxAutoRecoveryKindEnum, BlackBoxAutoRecovery> map = new Dictionary<BlackBoxAutoRecoveryKindEnum, BlackBoxAutoRecovery>();
            void Note(BlackBoxAutoRecoveryKindEnum kind, BlackBoxHandoffStep? step, BlackBoxRecoveryOutcomeEnum outcome, bool counts)
            {
                if (!map.TryGetValue(kind, out BlackBoxAutoRecovery? r))
                {
                    r = new BlackBoxAutoRecovery { Kind = kind, Outcome = outcome };
                    map[kind] = r;
                }
                if (counts) r.Attempts++;
                if (step != null)
                {
                    r.LastUtc = step.TimestampUtc;
                    r.Detail = step.Message ?? step.Kind;
                }
                // A terminal marker (exhausted, expired, failed) beats a success; a success beats the default.
                if (Rank(outcome) > Rank(r.Outcome)) r.Outcome = outcome;
            }
            foreach (BlackBoxHandoffStep s in steps)
            {
                switch (s.Kind)
                {
                    case "mission.redispatched": Note(BlackBoxAutoRecoveryKindEnum.Redispatch, s, BlackBoxRecoveryOutcomeEnum.Failed, true); break;
                    case "mission.contract_repaired": Note(BlackBoxAutoRecoveryKindEnum.OutputRepair, s, BlackBoxRecoveryOutcomeEnum.Succeeded, true); break;
                    case "mission.landing_retry":
                    case "mission.landing_auto_retry": Note(BlackBoxAutoRecoveryKindEnum.LandingRetry, s, BlackBoxRecoveryOutcomeEnum.Failed, true); break;
                    case "mission.landing_retries_exhausted": Note(BlackBoxAutoRecoveryKindEnum.LandingRetry, s, BlackBoxRecoveryOutcomeEnum.Expired, false); break;
                    case "mission.landing_deferred": Note(BlackBoxAutoRecoveryKindEnum.DeferralRetry, s, BlackBoxRecoveryOutcomeEnum.Failed, true); break;
                    case "mission.landing_deferral_expired": Note(BlackBoxAutoRecoveryKindEnum.DeferralRetry, s, BlackBoxRecoveryOutcomeEnum.Expired, false); break;
                    case "mission.landing_reconciled": Note(BlackBoxAutoRecoveryKindEnum.Reconcile, s, BlackBoxRecoveryOutcomeEnum.Succeeded, true); break;
                    case "mission.orphan_recovered": Note(BlackBoxAutoRecoveryKindEnum.OrphanRecovery, s, BlackBoxRecoveryOutcomeEnum.Succeeded, true); break;
                    case "mission.rescue_deduplicated": Note(BlackBoxAutoRecoveryKindEnum.Rescue, s, BlackBoxRecoveryOutcomeEnum.Unknown, true); break;
                    case "dock.repaired":
                    case "dock.unstuck": Note(BlackBoxAutoRecoveryKindEnum.DockRepair, s, BlackBoxRecoveryOutcomeEnum.Succeeded, true); break;
                    case "dock.repair_failed": Note(BlackBoxAutoRecoveryKindEnum.DockRepair, s, BlackBoxRecoveryOutcomeEnum.Failed, true); break;
                }
            }
            BlackBoxSubject? subject = record.Subject;
            if (subject != null)
            {
                int redispatches = subject.RedispatchAttempts ?? 0;
                if (redispatches > 0)
                {
                    Note(BlackBoxAutoRecoveryKindEnum.Redispatch, null, BlackBoxRecoveryOutcomeEnum.Failed, false);
                    map[BlackBoxAutoRecoveryKindEnum.Redispatch].Attempts = Math.Max(map[BlackBoxAutoRecoveryKindEnum.Redispatch].Attempts, redispatches);
                }
                int repairs = subject.OutputRepairAttempts ?? 0;
                if (repairs > 0)
                {
                    Note(BlackBoxAutoRecoveryKindEnum.OutputRepair, null, BlackBoxRecoveryOutcomeEnum.Unknown, false);
                    map[BlackBoxAutoRecoveryKindEnum.OutputRepair].Attempts = Math.Max(map[BlackBoxAutoRecoveryKindEnum.OutputRepair].Attempts, repairs);
                }
                int rescues = subject.RescueChain?.Count ?? 0;
                if (rescues > 0)
                {
                    Note(BlackBoxAutoRecoveryKindEnum.Rescue, null, BlackBoxRecoveryOutcomeEnum.Failed, false);
                    map[BlackBoxAutoRecoveryKindEnum.Rescue].Attempts = Math.Max(map[BlackBoxAutoRecoveryKindEnum.Rescue].Attempts, rescues);
                }
            }
            return map.Values.OrderBy(r => (int)r.Kind).Take(BlackBoxLimits.MaxAutoRecoveries).ToList();
        }

        private static int Rank(BlackBoxRecoveryOutcomeEnum outcome)
        {
            switch (outcome)
            {
                case BlackBoxRecoveryOutcomeEnum.Expired: return 4;
                case BlackBoxRecoveryOutcomeEnum.Failed: return 3;
                case BlackBoxRecoveryOutcomeEnum.RunFailed: return 3;
                case BlackBoxRecoveryOutcomeEnum.Succeeded: return 2;
                case BlackBoxRecoveryOutcomeEnum.Pending: return 1;
                default: return 0;
            }
        }

        private static bool TenantMatches(BlackBoxEntry entry, ArmadaEvent e)
        {
            return entry.TenantId == null || e.TenantId == null || e.TenantId == entry.TenantId;
        }

        private static string? OneLine(string? text)
        {
            return text?.Replace('\r', ' ').Replace('\n', ' ');
        }

        #endregion
    }
}
