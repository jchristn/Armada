namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Captain routing: eligibility, persona defaults, tiers, captain overrides and per-vessel active missions.
    /// </summary>
    public partial class MissionService
    {
        #region Private-Members

        private PersonaOutcomeStatsService? _OutcomeStats = null;

        #endregion

        #region Private-Methods

        private async Task<bool> HasStrictCaptainOverrideAsync(Mission mission, CancellationToken token)
        {
            List<CaptainAssignmentOverride> overrides = await ReadVoyageCaptainOverridesAsync(mission.VoyageId, token).ConfigureAwait(false);
            CaptainAssignmentOverride? chosen = overrides.FirstOrDefault(o => PersonaCatalog.Matches(o.Persona, RoutingPersona(mission)))
                ?? overrides.FirstOrDefault(o => o.Persona == "*" || String.IsNullOrEmpty(o.Persona));
            return chosen?.StrictCaptain == true && !String.IsNullOrEmpty(chosen.CaptainId);
        }

        private async Task<HashSet<string>> ReviewAuthorCaptainIdsAsync(Mission mission, CancellationToken token)
        {
            HashSet<string> authors = new HashSet<string>(StringComparer.Ordinal);
            if (!PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge)) return authors;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { mission.Id };
            string? parentId = mission.DependsOnMissionId;
            if (String.IsNullOrEmpty(parentId)) return authors;

            // Parents of a voyage mission come from one summary read of the voyage instead of one read per hop (RS-10.3).
            Dictionary<string, MissionSummary>? voyage = null;
            if (!String.IsNullOrEmpty(mission.VoyageId))
            {
                voyage = new Dictionary<string, MissionSummary>(StringComparer.Ordinal);
                foreach (MissionSummary summary in await _Database.Missions.EnumerateSummariesByVoyageAsync(mission.VoyageId, token).ConfigureAwait(false))
                    voyage[summary.Id] = summary;
            }

            while (!String.IsNullOrEmpty(parentId) && seen.Add(parentId))
            {
                string? parentVoyageId;
                string? parentPersona;
                string? parentCaptainId;
                string? nextId;
                if (voyage != null)
                {
                    if (!voyage.TryGetValue(parentId, out MissionSummary? summary)) break;
                    parentVoyageId = summary.VoyageId;
                    parentPersona = summary.Persona;
                    parentCaptainId = summary.CaptainId;
                    nextId = summary.DependsOnMissionId;
                }
                else
                {
                    Mission? parent = await _Database.Missions.ReadAsync(parentId, token).ConfigureAwait(false);
                    if (parent == null) break;
                    parentVoyageId = parent.VoyageId;
                    parentPersona = parent.Persona;
                    parentCaptainId = parent.CaptainId;
                    nextId = parent.DependsOnMissionId;
                }
                if (parentVoyageId != mission.VoyageId || PersonaCatalog.Matches(parentPersona, PersonaCatalog.Judge)) break;
                if (!String.IsNullOrEmpty(parentCaptainId)) authors.Add(parentCaptainId);
                if (PersonaCatalog.Matches(parentPersona, PersonaCatalog.Worker)) break;
                parentId = nextId;
            }
            return authors;
        }

        /// <summary>
        /// Why an idle captain may not take a mission, or null when it may. Uses the same rules as the assignment
        /// blocker's eligibility check, so the two cannot disagree.
        /// </summary>
        /// <param name="captain">Captain, assumed idle.</param>
        /// <param name="mission">Mission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A short English reason, or null when the captain is eligible.</returns>
        /// <exception cref="ArgumentNullException">The captain or mission is null.</exception>
        internal async Task<string?> CaptainIneligibilityReasonAsync(Captain captain, Mission mission, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            mission = mission.AdmissionView();
            await ResolvePreferredCaptainAsync(mission, token, persist: false).ConfigureAwait(false);
            CaptainEligibilityContext context = await BuildCaptainEligibilityContextAsync(mission, token).ConfigureAwait(false);
            return CaptainIneligibilityReason(captain, mission, context);
        }

        private async Task<CaptainEligibilityContext> BuildCaptainEligibilityContextAsync(Mission mission, CancellationToken token)
        {
            CaptainEligibilityContext context = new CaptainEligibilityContext();
            context.RoutingPersona = RoutingPersona(mission);
            if (!String.IsNullOrEmpty(mission.RequestedCaptainId))
            {
                Captain? requested = await PassCaptainAsync(mission.RequestedCaptainId, token).ConfigureAwait(false);
                context.InactiveRequestedCaptain = requested?.Active == false;
            }
            List<Captain> visible = (await PassCaptainsAsync(token).ConfigureAwait(false)).Where(c => CaptainVisibleToMission(c, mission)).ToList();
            // Review authors only change eligibility when they are excluded; otherwise they are merely less preferred. Both the
            // exclusion and the Judge strength rule compare models, so another captain on an author's model counts as an author.
            await AddJudgeRulesAsync(context, mission, visible, token).ConfigureAwait(false);
            await AddContextFitAsync(context, mission, visible, token).ConfigureAwait(false);
            await AddOutcomeScoresAsync(context, mission, visible, token).ConfigureAwait(false);
            context.EscalatedToRequestedCaptain = !String.IsNullOrEmpty(mission.RequestedCaptainId) && (IntegrationBarrier.IsEscalatedRework(mission)
                || mission.EscalationCount > 0);
            context.UsageFallbackAuthorized = _Settings.EffectiveUsageSelection.Mode == UsageSelectionModeEnum.Enforce && mission.UsageRecovery?.AllowFallback == true;
            context.UsageFallbackRequired = _Settings.EffectiveUsageSelection.Mode == UsageSelectionModeEnum.Enforce
                && !String.IsNullOrEmpty(mission.RequestedCaptainId) && mission.UsageRecovery?.AllowFallback != true;
            context.StrictCaptainOverride = await HasStrictCaptainOverrideAsync(mission, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(mission.RequestedCaptainId))
            {
                context.DefaultCaptainId = await PersonaDefaultCaptainIdAsync(mission, context.RoutingPersona, token).ConfigureAwait(false);
                context.Persona = await PassPersonaAsync(mission.TenantId, context.RoutingPersona, mission.UserId, token).ConfigureAwait(false);
            }
            context.EnvelopeGate = await BuildWorkerEnvelopeGateAsync(mission, token).ConfigureAwait(false);
            return context;
        }

        private static string? CaptainIneligibilityReason(Captain captain, Mission mission, CaptainEligibilityContext context)
            => EvaluateCaptainIneligibility(captain, mission, context)?.Summary;

        private static CaptainEligibilityFailure? EvaluateCaptainIneligibility(Captain captain, Mission mission, CaptainEligibilityContext context)
        {
            CaptainEligibilityFailure Failure(string code, string summary) => new CaptainEligibilityFailure { Code = code, Summary = summary };
            // The selector, diagnostics and repair admission all apply this predicate. Requested and persona-default
            // captains bypass the ordinary persona/tier fence; safety, scope, authorship and envelopes still apply.
            if (context.InactiveRequestedCaptain && (!context.UsageFallbackAuthorized || captain.Id == mission.RequestedCaptainId)) return Failure("RequestedCaptainInactive", "requested captain is inactive; enable Active before assigning new work");
            if (!captain.Active) return Failure("CaptainInactive", "inactive; enable Active before assigning new work");
            if (context.ExcludedAuthors.Contains(captain.Id)) return Failure("ReviewAuthorExcluded", "authored an earlier stage");
            if (!CaptainVisibleToMission(captain, mission)) return Failure("CaptainScopeMismatch", "belongs to another tenant or user");
            if (captain.QuarantineUntilUtc.HasValue && captain.QuarantineUntilUtc > DateTime.UtcNow) return Failure(UsageQuotaHold.IsQuotaOwned(captain.QuarantineReason) ? "AccountQuotaExhausted" : "CaptainQuarantined", "quarantined");
            if (context.EnvelopeGate != null && context.EnvelopeGate.Exclusions.TryGetValue(captain.Id, out string? envelopeReason)) return Failure("WorkerEnvelopeIncompatible", envelopeReason);
            if (context.ContextExclusions.TryGetValue(captain.Id, out string? contextReason)) return Failure("ContextTooSmall", contextReason);
            if (!String.IsNullOrEmpty(mission.RequestedCaptainId) && captain.Id == mission.RequestedCaptainId) return null;
            if (context.EscalatedToRequestedCaptain) return Failure("RequestedCaptainEscalation", "escalated to another captain");
            if (context.StrictCaptainOverride) return Failure("StrictCaptainOverride", "the voyage requires another captain");
            if (context.UsageFallbackRequired) return Failure("FallbackPermissionRequired", "preferred captain fallback requires administrator authorization");
            if (context.JudgePolicy == JudgeStrengthPolicyEnum.Wait && !JudgeStrengthRule.Meets(captain, context.JudgeRequiredTier))
                return Failure("JudgeStrength", "tier " + CaptainTierSelector.EffectiveTier(captain) + " is below the reviewed work's tier " + context.JudgeRequiredTier!.Value + " (Judge strength rule)");
            if (context.DefaultCaptainId != null && captain.Id == context.DefaultCaptainId) return null;
            if (!String.IsNullOrEmpty(captain.AllowedPersonas) && !CaptainAllowsPersona(captain, context.RoutingPersona)) return Failure("PersonaNotAllowed", "persona not allowed");
            if (mission.Tier.HasValue && !CaptainTierSelector.CanHandle(captain, mission.Tier.Value)) return Failure("TierBelowRequirement", "tier below " + mission.Tier.Value);
            if (context.Persona != null && context.Persona.CaptainPriorities.Count > 0
                && !PersonaCaptainPrioritySelector.AllowsGeneralFallback(context.Persona)
                && PersonaCaptainPrioritySelector.Select(context.Persona, new List<Captain> { captain }) == null
                && !(context.EnvelopeGate != null && context.EnvelopeGate.CanEscalateTo(captain, mission)))
                return Failure("PersonaPriorityMismatch", "not in persona " + context.Persona.Name + "'s captain priorities");
            return null;
        }

        private static bool CaptainVisibleToMission(Captain captain, Mission mission)
        {
            return captain.TenantId == mission.TenantId && (captain.UserId == null || captain.UserId == mission.UserId);
        }

        /// <summary>
        /// Add what each of the mission's tenant's captains is doing to a blocker (the first
        /// <see cref="MissionAssignmentBlocker.MaxListed"/> by name; the full count goes in CaptainCount). Idle captains
        /// also say whether they may take the mission, for example "idle (not assigned: persona not allowed)".
        /// </summary>
        /// <param name="blocker">Blocker to add captain statuses to.</param>
        /// <param name="captains">Captains to describe.</param>
        /// <param name="mission">Mission the blocker explains.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">The blocker, captains or mission is null.</exception>
        internal async Task DescribeCaptainsAsync(MissionAssignmentBlocker blocker, List<Captain> captains, Mission mission, CancellationToken token = default)
        {
            if (blocker == null) throw new ArgumentNullException(nameof(blocker));
            if (captains == null) throw new ArgumentNullException(nameof(captains));
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            mission = mission.AdmissionView();
            await ResolvePreferredCaptainAsync(mission, token, persist: false).ConfigureAwait(false);
            // Only captains in the mission's tenant are described, so the explanation never names another tenant's work.
            string? tenantId = mission.TenantId;
            IEnumerable<Captain> visible = String.IsNullOrEmpty(tenantId)
                ? captains
                : captains.Where(c => String.Equals(c.TenantId, tenantId, StringComparison.Ordinal));
            CaptainEligibilityContext? eligibility = null;
            List<Captain> ordered = visible.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
            blocker.CaptainCount = ordered.Count;
            foreach (Captain captain in ordered.Take(MissionAssignmentBlocker.MaxListed))
            {
                MissionAssignmentCaptainStatus status = new MissionAssignmentCaptainStatus
                {
                    CaptainId = captain.Id,
                    CaptainName = captain.Name,
                    State = captain.State
                };

                switch (captain.State)
                {
                    case CaptainStateEnum.Idle:
                        if (eligibility == null) eligibility = await BuildCaptainEligibilityContextAsync(mission, token).ConfigureAwait(false);
                        string? ineligibleReason = CaptainIneligibilityReason(captain, mission, eligibility);
                        status.EligibleForMission = ineligibleReason == null;
                        status.Detail = ineligibleReason == null ? "idle (assigned)" : "idle (not assigned: " + ineligibleReason + ")";
                        break;
                    case CaptainStateEnum.Working:
                        status.MissionId = captain.ActiveMissionIds.Count == 1 ? captain.ActiveMissionIds[0] : null;
                        Mission? current = status.MissionId == null ? null : await _Database.Missions.ReadAsync(status.MissionId, token).ConfigureAwait(false);
                        status.Detail = current != null ? "working on mission '" + current.Title + "'" : "working on another mission";
                        break;
                    case CaptainStateEnum.Planning:
                        PlanningSession? planning = !String.IsNullOrEmpty(captain.CurrentSessionId)
                            ? await _Database.PlanningSessions.ReadAsync(captain.CurrentSessionId, token).ConfigureAwait(false)
                            : (await _Database.PlanningSessions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false))
                            .Where(p => p.Status == PlanningSessionStatusEnum.Created || p.Status == PlanningSessionStatusEnum.Active || p.Status == PlanningSessionStatusEnum.Responding || p.Status == PlanningSessionStatusEnum.Stopping)
                            .OrderByDescending(p => p.LastUpdateUtc)
                            .FirstOrDefault();
                        if (planning != null && (planning.CaptainId != captain.Id || planning.TenantId != mission.TenantId || (planning.UserId != null && planning.UserId != mission.UserId))) planning = null;
                        status.PlanningSessionId = planning?.Id;
                        status.Detail = planning != null ? "in planning session '" + planning.Title + "'" : "reserved by a planning session";
                        break;
                    case CaptainStateEnum.Refining:
                        ObjectiveRefinementSession? refinement = !String.IsNullOrEmpty(captain.CurrentSessionId)
                            ? await _Database.ObjectiveRefinementSessions.ReadAsync(captain.CurrentSessionId, token).ConfigureAwait(false)
                            : (await _Database.ObjectiveRefinementSessions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false))
                            .Where(r => r.Status == ObjectiveRefinementSessionStatusEnum.Created || r.Status == ObjectiveRefinementSessionStatusEnum.Active || r.Status == ObjectiveRefinementSessionStatusEnum.Responding || r.Status == ObjectiveRefinementSessionStatusEnum.Stopping)
                            .OrderByDescending(r => r.LastUpdateUtc)
                            .FirstOrDefault();
                        if (refinement != null && (refinement.CaptainId != captain.Id || refinement.TenantId != mission.TenantId || (refinement.UserId != null && refinement.UserId != mission.UserId))) refinement = null;
                        status.RefinementSessionId = refinement?.Id;
                        status.ObjectiveId = refinement?.ObjectiveId;
                        Objective? objective = refinement == null || String.IsNullOrEmpty(refinement.ObjectiveId) ? null : await _Database.Objectives.ReadAsync(refinement.ObjectiveId, token).ConfigureAwait(false);
                        status.Detail = objective != null
                            ? "refining backlog item '" + objective.Title + "' (stop the refinement session to free it)"
                            : "reserved by a backlog refinement session (stop the session to free it)";
                        break;
                    case CaptainStateEnum.Quarantined:
                        status.QuarantineUntilUtc = captain.QuarantineUntilUtc;
                        status.QuarantineReason = captain.QuarantineReason;
                        status.Detail = "quarantined"
                            + (!String.IsNullOrEmpty(captain.QuarantineReason) ? " (" + captain.QuarantineReason + ")" : "")
                            + (captain.QuarantineUntilUtc.HasValue ? " until " + captain.QuarantineUntilUtc.Value.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC" : "")
                            + "; release it from the captain page to use it sooner";
                        break;
                    case CaptainStateEnum.Stalled:
                        status.Detail = "stalled and awaiting recovery";
                        break;
                    case CaptainStateEnum.Stopping:
                        status.Detail = "stopping";
                        break;
                    case CaptainStateEnum.Analyzing:
                        status.Detail = "analyzing a vessel";
                        break;
                    default:
                        status.Detail = captain.State.ToString();
                        break;
                }

                if (captain.State == CaptainStateEnum.Working && captain.ActiveMissionCount > 0)
                {
                    status.Detail = captain.ActiveMissionCount + " / " + captain.MaxConcurrency + " mission slots in use";
                    if (captain.HasMissionCapacity)
                    {
                        eligibility ??= await BuildCaptainEligibilityContextAsync(mission, token).ConfigureAwait(false);
                        string? reason = CaptainIneligibilityReason(captain, mission, eligibility);
                        status.EligibleForMission = reason == null;
                        status.Detail += reason == null ? " (available for this mission)" : " (not assigned: " + reason + ")";
                    }
                }
                blocker.Captains.Add(status);
            }
        }

        private async Task<Captain?> FindRoutedCaptainAsync(Mission mission, WorkerEnvelopeGate? envelopeGate, CaptainAdmissionSelection selection, CancellationToken token)
        {
            string? persona = RoutingPersona(mission);
            CaptainTierEnum? requiredTier = mission.Tier;
            string? requestedCaptainId = mission.RequestedCaptainId;
            CaptainEligibilityContext context = selection.Eligibility;
            List<Captain> idleCaptains = selection.Available.Where(c => CaptainIneligibilityReason(c, mission, context) == null).ToList();
            selection.RoutingEligible = idleCaptains.ToList();
            HashSet<string> reviewAuthors = await ReviewAuthorCaptainIdsAsync(mission, token).ConfigureAwait(false);
            idleCaptains = await ApplyCapacityLimitsAsync(idleCaptains, mission, token, selection.CapacityExclusions).ConfigureAwait(false);
            if (envelopeGate != null) envelopeGate.IdlePool = idleCaptains;
            if (idleCaptains.Count == 0)
                return null;

            // A captain that already failed to start this mission is used again only when no other idle captain could
            // take it (a single-captain setup keeps retrying with backoff). A strict assignment names its only captain.
            if (_LaunchFailures.TryGetValue(mission.Id, out MissionLaunchFailure? priorFailures)
                && !(!String.IsNullOrEmpty(requestedCaptainId) && await HasStrictCaptainOverrideAsync(mission, token).ConfigureAwait(false)))
            {
                List<Captain> untried;
                lock (priorFailures.FailedCaptainIds) untried = idleCaptains.Where(c => !priorFailures.FailedCaptainIds.Contains(c.Id)).ToList();
                if (untried.Count > 0 && untried.Count < idleCaptains.Count)
                {
                    _Logging.Info(_Header + "mission " + mission.Id + " skips " + (idleCaptains.Count - untried.Count) + " captain(s) that already failed to start it");
                    idleCaptains = untried;
                    if (envelopeGate != null) envelopeGate.IdlePool = idleCaptains;
                }
            }

            List<CaptainRoutingBucket> groups = selection.RoutingBuckets;
            HashSet<string> grouped = new HashSet<string>(StringComparer.Ordinal);
            void AddGroup(string reason, IEnumerable<Captain> captains)
            {
                List<Captain> members = captains.Where(c => grouped.Add(c.Id)).ToList();
                if (members.Count > 0) groups.Add(new CaptainRoutingBucket { ReasonCode = reason, Captains = members });
            }
            void AddGeneral(IEnumerable<Captain> captains)
            {
                foreach (CaptainRoutingBucket group in BuildGeneralRoutingBuckets(mission, persona, requiredTier, requestedCaptainId,
                    captains.Where(c => !grouped.Contains(c.Id)).ToList(), context)) AddGroup(group.ReasonCode, group.Captains);
            }

            if (!String.IsNullOrEmpty(requestedCaptainId))
            {
                AddGroup("RequestedCaptain", idleCaptains.Where(c => c.Id == requestedCaptainId));
                // Strict and recovery assignments have no fallback buckets, including when their captain is absent.
                if (IntegrationBarrier.IsEscalatedRework(mission) || mission.EscalationCount > 0
                    || await HasStrictCaptainOverrideAsync(mission, token).ConfigureAwait(false))
                    return groups.FirstOrDefault()?.Captains.FirstOrDefault();
            }
            else if (context.DefaultCaptainId != null)
                AddGroup("PersonaDefaultCaptain", idleCaptains.Where(c => c.Id == context.DefaultCaptainId));

            if (context.JudgePolicy != JudgeStrengthPolicyEnum.Off)
            {
                // RS-8.7: a Judge that meets the rule and is of another model family than every author first, then one that
                // meets the rule (ProceedAndFlag also falls through to the rest, and the assignment is flagged).
                AddGeneral(idleCaptains.Where(c => !reviewAuthors.Contains(c.Id) && !context.SameFamilyAsAuthors.Contains(c.Id)
                    && JudgeStrengthRule.Meets(c, context.JudgeRequiredTier)));
                AddGeneral(idleCaptains.Where(c => !reviewAuthors.Contains(c.Id) && JudgeStrengthRule.Meets(c, context.JudgeRequiredTier)));
            }
            if (reviewAuthors.Count > 0 && !_Settings.ExcludeReviewAuthorsFromJudge)
                AddGeneral(idleCaptains.Where(c => !reviewAuthors.Contains(c.Id)));
            AddGeneral(idleCaptains);
            return groups.FirstOrDefault()?.Captains.FirstOrDefault();
        }

        private static List<CaptainRoutingBucket> BuildGeneralRoutingBuckets(Mission mission, string? persona, CaptainTierEnum? requiredTier,
            string? requestedCaptainId, List<Captain> candidates, CaptainEligibilityContext context)
        {
            List<CaptainRoutingBucket> groups = new List<CaptainRoutingBucket>();
            HashSet<string> grouped = new HashSet<string>(StringComparer.Ordinal);
            void Add(string reason, IEnumerable<Captain> source, bool priority)
            {
                List<Captain> available = source.Where(c => !grouped.Contains(c.Id)).ToList();
                // Priority definitions use active count then ID, whereas general routing also uses last update.
                foreach (bool preferred in new[] { true, false })
                {
                    IEnumerable<Captain> pool = available.Where(c => (!String.IsNullOrEmpty(persona) && PersonaCatalog.Matches(c.PreferredPersona, persona)) == preferred);
                    // RS-8.6: with outcome ranking on, captains whose model has enough results for the persona come first, best
                    // score first; the rest keep the load and last-update order. With no scores this is the 1.4.0 order.
                    List<Captain> ordered = priority
                        ? pool.OrderBy(c => c.ActiveMissionCount).ThenBy(c => c.Id, StringComparer.Ordinal).ToList()
                        : pool.OrderBy(c => context.OutcomeScores.ContainsKey(c.Id) ? 0 : 1)
                            .ThenByDescending(c => context.OutcomeScores.TryGetValue(c.Id, out PersonaOutcomeScore? score) ? score.Score : 0)
                            .ThenBy(c => c.ActiveMissionCount).ThenBy(c => c.LastUpdateUtc).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
                    if (ordered.Count == 0) continue;
                    foreach (Captain captain in ordered) grouped.Add(captain.Id);
                    groups.Add(new CaptainRoutingBucket { ReasonCode = reason + (preferred ? "PreferredPersona" : ""), Captains = ordered });
                }
            }
            if (String.IsNullOrEmpty(requestedCaptainId) && !String.IsNullOrEmpty(persona) && context.Persona != null && context.Persona.CaptainPriorities.Count > 0)
            {
                Persona definition = context.Persona;
                if (PersonaCaptainPrioritySelector.Validate(definition) != null) return groups;
                IEnumerable<PersonaCaptainPriority> priorities = definition.PriorityAvailability == PersonaPriorityAvailabilityEnum.WaitForFirstPriority
                    ? definition.CaptainPriorities.Take(1) : definition.CaptainPriorities;
                int index = 0;
                foreach (PersonaCaptainPriority priority in priorities)
                {
                    Add("PersonaPriority" + (++index), candidates.Where(c => c.Active && PersonaCaptainPrioritySelector.Matches(priority, c)
                        && (!requiredTier.HasValue || CaptainTierSelector.CanHandle(c, requiredTier.Value))), true);
                }
                if (!PersonaCaptainPrioritySelector.AllowsGeneralFallback(definition)) return groups;
            }
            if (requiredTier.HasValue)
            {
                foreach (CaptainTierEnum tier in candidates.Where(c => CaptainTierSelector.CanHandle(c, requiredTier.Value))
                    .Select(CaptainTierSelector.EffectiveTier).Distinct().OrderBy(t => t))
                    Add("Tier" + tier, candidates.Where(c => CaptainTierSelector.EffectiveTier(c) == tier), false);
            }
            else Add("EligibleCaptain", candidates, false);
            return groups;
        }

        /// <summary>
        /// Resolve and persist the preferred captain and fallback tier for a mission from an explicit value already
        /// set or the voyage-level override for the mission's persona. A persona's default captain is not persisted:
        /// it is a preference consulted on each dispatch tick (see PersonaDefaultCaptainIdAsync), so it never becomes
        /// a pin or a tier floor that keeps the mission waiting while other captains are idle. A dangling captain
        /// reference is left as-is and degrades to normal routing at assignment time. Best-effort: never throws for a
        /// missing persona/voyage.
        /// </summary>
        private async Task ResolvePreferredCaptainAsync(Mission mission, CancellationToken token, bool persist = true)
        {
            if (mission == null) return;
            if (!String.IsNullOrEmpty(mission.RequestedCaptainId)) return;
            string routingPersona = RoutingPersona(mission);

            string? resolvedCaptainId = null;
            CaptainTierEnum? resolvedTier = null;

            List<CaptainAssignmentOverride> overrides = await ReadVoyageCaptainOverridesAsync(mission.VoyageId, token).ConfigureAwait(false);

            // An exact per-persona override wins; a wildcard override ("*" or empty persona) applies to every
            // step and is the fallback. The wildcard lets a dispatch pin one captain regardless of pipeline
            // (including "Inherit", where the resolved stages are not known at dispatch time).
            CaptainAssignmentOverride? exactMatch = null;
            CaptainAssignmentOverride? wildcardMatch = null;
            foreach (CaptainAssignmentOverride ov in overrides)
            {
                if (String.IsNullOrEmpty(ov.Persona) || ov.Persona == "*")
                {
                    if (wildcardMatch == null) wildcardMatch = ov;
                    continue;
                }

                if (PersonaCatalog.Matches(ov.Persona, routingPersona))
                {
                    exactMatch = ov;
                    break;
                }
            }

            CaptainAssignmentOverride? chosen = exactMatch ?? wildcardMatch;
            if (chosen != null)
            {
                resolvedCaptainId = String.IsNullOrEmpty(chosen.CaptainId) ? null : chosen.CaptainId;
                resolvedTier = chosen.FallbackTier;
            }

            if (String.IsNullOrEmpty(resolvedCaptainId) && resolvedTier == null) return;

            bool changed = false;
            if (!String.IsNullOrEmpty(resolvedCaptainId))
            {
                mission.RequestedCaptainId = resolvedCaptainId;
                changed = true;
            }

            if (mission.Tier == null)
            {
                if (resolvedTier != null)
                {
                    mission.Tier = resolvedTier;
                    changed = true;
                }
                else if (!String.IsNullOrEmpty(mission.RequestedCaptainId))
                {
                    CaptainTierEnum? preferredTier = await CaptainEffectiveTierAsync(mission.RequestedCaptainId, token).ConfigureAwait(false);
                    if (preferredTier != null)
                    {
                        mission.Tier = preferredTier;
                        changed = true;
                    }
                }
            }

            if (changed && persist)
            {
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The vessel's execution and verification missions, which the broad-scope and branch safety checks look
        /// at. One indexed projection (vessel and status, no outputs or diffs), read once per vessel per dispatch pass
        /// (D17, RS-4.7).
        /// </summary>
        private Task<List<Mission>> ActiveMissionsOnVesselAsync(string vesselId, CancellationToken token)
            => PassRunningOnVesselAsync(vesselId, token);

        private async Task<MissionAssignmentBlocker?> EvaluateGlobalAdmissionAsync(CancellationToken token)
            => (await PassCapacityAsync(token).ConfigureAwait(false)).GlobalBlocker();

        private async Task<MissionAssignmentBlocker?> EvaluateVesselAdmissionAsync(Mission mission, Vessel vessel, CancellationToken token)
        {
            List<MissionAdmissionSnapshot> missions = await PassAdmissionSnapshotsAsync(vessel.Id, token).ConfigureAwait(false);
            return VesselAdmission.EvaluateSnapshots(mission, vessel, missions);
        }

        private static List<Mission> BranchHolders(Mission mission, IEnumerable<Mission> vesselMissions)
        {
            if (String.IsNullOrEmpty(mission.BranchName)) return new List<Mission>();
            return vesselMissions.Where(m => m.Id != mission.Id
                && (m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress)
                && String.Equals(m.BranchName, mission.BranchName, StringComparison.Ordinal)).ToList();
        }

        private async Task<string?> PersonaDefaultCaptainIdAsync(Mission mission, string? routingPersona, CancellationToken token)
        {
            if (String.IsNullOrEmpty(routingPersona)) return null;
            Persona? persona = await PassPersonaAsync(mission.TenantId, routingPersona, mission.UserId, token).ConfigureAwait(false);
            // A nonempty hierarchy supersedes the legacy default captain.
            if (persona == null || persona.CaptainPriorities.Count > 0 || String.IsNullOrEmpty(persona.DefaultCaptainId)) return null;
            return persona.DefaultCaptainId;
        }

        private async Task<Persona?> ReadPersonaByNameAsync(string? tenantId, string personaName, CancellationToken token, string? userId = null)
        {
            if (!String.IsNullOrEmpty(tenantId))
            {
                Persona? scoped = await _Database.Personas.ReadByNameAsync(tenantId, personaName, token).ConfigureAwait(false);
                if (scoped != null && (scoped.UserId == null || scoped.UserId == userId || scoped.Scope == Armada.Core.Enums.ScopeEnum.TenantWide)) return scoped;
            }

            Persona? shared = await _Database.Personas.ReadByNameAsync(personaName, token).ConfigureAwait(false);
            return shared != null && shared.TenantId == tenantId &&
                (shared.UserId == null || shared.UserId == userId || shared.Scope == Armada.Core.Enums.ScopeEnum.TenantWide) ? shared : null;
        }

        private async Task<CaptainTierEnum?> CaptainEffectiveTierAsync(string captainId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(captainId)) return null;
            Captain? captain = await PassCaptainAsync(captainId, token).ConfigureAwait(false);
            if (captain == null) return null;
            return CaptainTierSelector.EffectiveTier(captain);
        }

        private async Task<List<CaptainAssignmentOverride>> ReadVoyageCaptainOverridesAsync(string? voyageId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(voyageId)) return new List<CaptainAssignmentOverride>();
            Voyage? voyage = await PassVoyageAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null) return new List<CaptainAssignmentOverride>();
            return DeserializeCaptainOverrides(voyage.CaptainOverridesJson);
        }

        /// <summary>
        /// Deserialize a voyage's captain-override JSON into a list. Returns an empty list for null, empty, or
        /// malformed input rather than throwing.
        /// </summary>
        /// <param name="json">Serialized override array, or null.</param>
        /// <returns>The parsed overrides, or an empty list.</returns>
        public static List<CaptainAssignmentOverride> DeserializeCaptainOverrides(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return new List<CaptainAssignmentOverride>();
            try
            {
                List<CaptainAssignmentOverride>? parsed = JsonSerializer.Deserialize<List<CaptainAssignmentOverride>>(json);
                return parsed ?? new List<CaptainAssignmentOverride>();
            }
            catch (JsonException)
            {
                return new List<CaptainAssignmentOverride>();
            }
        }

        /// <summary>
        /// Serialize a list of captain overrides for voyage persistence. Returns null when the list is null or
        /// empty so the column stays null rather than storing "[]".
        /// </summary>
        /// <param name="overrides">The overrides to serialize, or null.</param>
        /// <returns>The serialized JSON array, or null.</returns>
        public static string? SerializeCaptainOverrides(List<CaptainAssignmentOverride>? overrides)
        {
            if (overrides == null || overrides.Count == 0) return null;
            return JsonSerializer.Serialize(overrides);
        }

        private static bool CaptainAllowsPersona(Captain captain, string persona)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (String.IsNullOrEmpty(persona)) return true;
            if (String.IsNullOrEmpty(captain.AllowedPersonas)) return true;

            try
            {
                List<string>? allowedPersonas = JsonSerializer.Deserialize<List<string>>(captain.AllowedPersonas);
                if (allowedPersonas != null)
                {
                    foreach (string allowedPersona in allowedPersonas)
                    {
                        if (PersonaCatalog.Matches(allowedPersona, persona))
                            return true;
                    }

                    return false;
                }
            }
            catch (JsonException)
            {
            }

            // AllowedPersonas is a JSON array of persona names. A value that does not deserialize is not searched as
            // text: the captain is treated as allowing no restricted persona until the setting is corrected.
            return false;
        }

        private PersonaOutcomeStatsService OutcomeStats => _OutcomeStats ??= new PersonaOutcomeStatsService(_Database);

        private async Task AddJudgeRulesAsync(CaptainEligibilityContext context, Mission mission, List<Captain> visible, CancellationToken token)
        {
            if (!PersonaCatalog.Matches(mission.Persona, PersonaCatalog.Judge)) return;
            Voyage? voyage = String.IsNullOrEmpty(mission.VoyageId) ? null : await PassVoyageAsync(mission.VoyageId, token).ConfigureAwait(false);
            context.JudgePolicy = JudgeStrengthRule.Effective(voyage);
            if (!_Settings.ExcludeReviewAuthorsFromJudge && context.JudgePolicy == JudgeStrengthPolicyEnum.Off) return;

            List<Captain> authors = new List<Captain>();
            foreach (string authorId in await ReviewAuthorCaptainIdsAsync(mission, token).ConfigureAwait(false))
            {
                Captain? author = visible.FirstOrDefault(c => c.Id == authorId) ?? await PassCaptainAsync(authorId, token).ConfigureAwait(false);
                if (author != null) authors.Add(author);
            }
            HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Captain author in authors)
            {
                ModelEndpoint? endpoint = await CaptainEndpointAsync(author, token).ConfigureAwait(false);
                identities.Add(JudgeStrengthRule.ModelIdentity(author, endpoint));
                families.Add(ModelFamily.Of(author, endpoint));
            }
            if (context.JudgePolicy != JudgeStrengthPolicyEnum.Off) context.JudgeRequiredTier = JudgeStrengthRule.RequiredTier(authors);
            foreach (Captain captain in visible)
            {
                ModelEndpoint? endpoint = await CaptainEndpointAsync(captain, token).ConfigureAwait(false);
                if (families.Contains(ModelFamily.Of(captain, endpoint))) context.SameFamilyAsAuthors.Add(captain.Id);
                // ExcludeReviewAuthorsFromJudge compares model identity (RS-8.7): an author's captain and any other captain on
                // the same model are excluded.
                if (_Settings.ExcludeReviewAuthorsFromJudge && (authors.Any(a => a.Id == captain.Id) || identities.Contains(JudgeStrengthRule.ModelIdentity(captain, endpoint))))
                    context.ExcludedAuthors.Add(captain.Id);
            }
            if (_Settings.ExcludeReviewAuthorsFromJudge)
                foreach (Captain author in authors) context.ExcludedAuthors.Add(author.Id);
        }

        private async Task AddContextFitAsync(CaptainEligibilityContext context, Mission mission, List<Captain> visible, CancellationToken token)
        {
            foreach (Captain captain in visible)
            {
                ModelEndpoint? endpoint = await CaptainEndpointAsync(captain, token).ConfigureAwait(false);
                ModelProfile? profile = await CaptainProfileAsync(captain, token).ConfigureAwait(false);
                string? reason = MissionPromptFit.IneligibilityReason(mission, MissionPromptFit.ServedContext(endpoint, profile));
                if (reason != null) context.ContextExclusions[captain.Id] = reason;
            }
        }

        private async Task AddOutcomeScoresAsync(CaptainEligibilityContext context, Mission mission, List<Captain> visible, CancellationToken token)
        {
            RoutingDataSettings settings = _Settings.RoutingData;
            if (!settings.RankByOutcomes || String.IsNullOrEmpty(context.RoutingPersona)) return;
            Dictionary<string, PersonaOutcomeScore> scores;
            try
            {
                scores = await OutcomeStats.ReadAsync(mission.TenantId, settings.HalfLifeDays, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "outcome ranking skipped for mission " + mission.Id + ": " + ex.Message);
                return;
            }
            foreach (Captain captain in visible)
            {
                ModelProfile? profile = await CaptainProfileAsync(captain, token).ConfigureAwait(false);
                if (profile == null) continue;
                string key = PersonaOutcomeStats.Key(ModelProfileRules.IdentityKey(profile.TenantId, profile.Identity), context.RoutingPersona);
                if (scores.TryGetValue(key, out PersonaOutcomeScore? score) && score.Samples >= settings.MinimumSamples)
                    context.OutcomeScores[captain.Id] = score;
            }
        }

        private async Task<ModelEndpoint?> CaptainEndpointAsync(Captain captain, CancellationToken token)
        {
            if (String.IsNullOrEmpty(captain.ModelEndpointId)) return null;
            return await PassEndpointAsync(captain.ModelEndpointId, token).ConfigureAwait(false);
        }

        private async Task<ModelProfile?> CaptainProfileAsync(Captain captain, CancellationToken token)
        {
            if (String.IsNullOrEmpty(captain.ModelProfileId)) return null;
            ModelProfile? profile = await PassProfileAsync(captain.ModelProfileId, token).ConfigureAwait(false);
            return profile != null && String.Equals(profile.TenantId, captain.TenantId, StringComparison.Ordinal) ? profile : null;
        }

        private async Task RecordRoutingNotesAsync(Mission mission, Captain captain, CaptainAdmissionSelection selection, CancellationToken token)
        {
            CaptainEligibilityContext context = selection.Eligibility;
            List<ArmadaEvent> events = new List<ArmadaEvent>();
            if (context.JudgePolicy == JudgeStrengthPolicyEnum.ProceedAndFlag && !JudgeStrengthRule.Meets(captain, context.JudgeRequiredTier))
            {
                string message = "Judge strength exception: " + captain.Name + " (tier " + CaptainTierSelector.EffectiveTier(captain)
                    + ") judges work of tier " + context.JudgeRequiredTier!.Value + " because no captain that meets the rule was available.";
                _Logging.Warn(_Header + "mission " + mission.Id + ": " + message);
                events.Add(RoutingEvent(mission, captain, JudgeStrengthRule.ExceptionEventType, message, null));
            }
            CaptainRoutingBucket? bucket = selection.RoutingBuckets.FirstOrDefault(b => b.Captains.Any(c => c.Id == captain.Id));
            if (bucket != null && bucket.Captains.Any(c => context.OutcomeScores.ContainsKey(c.Id)))
            {
                List<CaptainOutcomeRank> ranks = bucket.Captains.Take(CaptainOutcomeRank.MaxListed).Select(c => CaptainOutcomeRank.From(c,
                    context.OutcomeScores.TryGetValue(c.Id, out PersonaOutcomeScore? score) ? score : null)).ToList();
                string message = "Outcome ranking (" + context.RoutingPersona + ", " + bucket.ReasonCode + ") chose " + captain.Name + ": "
                    + String.Join("; ", ranks.Select(r => r.Describe()));
                _Logging.Info(_Header + "mission " + mission.Id + ": " + message);
                events.Add(RoutingEvent(mission, captain, CaptainOutcomeRank.EventType, message, JsonSerializer.Serialize(ranks)));
            }
            foreach (ArmadaEvent evt in events)
            {
                try
                {
                    await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    _Logging.Warn(_Header + "could not record " + evt.EventType + " for mission " + mission.Id + ": " + ex.Message);
                }
            }
        }

        private static ArmadaEvent RoutingEvent(Mission mission, Captain captain, string type, string message, string? payload)
        {
            ArmadaEvent evt = new ArmadaEvent(type, message);
            evt.TenantId = mission.TenantId;
            evt.UserId = mission.UserId;
            evt.EntityType = "mission";
            evt.EntityId = mission.Id;
            evt.CaptainId = captain.Id;
            evt.MissionId = mission.Id;
            evt.VesselId = mission.VesselId;
            evt.VoyageId = mission.VoyageId;
            evt.Payload = payload;
            return evt;
        }

        private static bool UsesSharedLocalAndWorkingDirectory(Vessel vessel)
        {
            if (vessel == null) return false;
            if (String.IsNullOrWhiteSpace(vessel.LocalPath) || String.IsNullOrWhiteSpace(vessel.WorkingDirectory))
                return false;

            try
            {
                string localPath = Path.GetFullPath(vessel.LocalPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string workingDirectory = Path.GetFullPath(vessel.WorkingDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return String.Equals(localPath, workingDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return String.Equals(vessel.LocalPath.Trim(), vessel.WorkingDirectory.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        #endregion

        #region Private-Types

        private sealed class CaptainEligibilityContext
        {
            public string RoutingPersona { get; set; } = "";
            public HashSet<string> ExcludedAuthors { get; set; } = new HashSet<string>(StringComparer.Ordinal);
            public bool UsageFallbackAuthorized { get; set; }
            public bool UsageFallbackRequired { get; set; }
            public bool EscalatedToRequestedCaptain { get; set; } = false;
            public bool StrictCaptainOverride { get; set; } = false;
            public bool InactiveRequestedCaptain { get; set; } = false;
            public string? DefaultCaptainId { get; set; } = null;
            public Persona? Persona { get; set; } = null;
            public WorkerEnvelopeGate? EnvelopeGate { get; set; } = null;
            public Dictionary<string, string> ContextExclusions { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
            public JudgeStrengthPolicyEnum JudgePolicy { get; set; } = JudgeStrengthPolicyEnum.Off;
            public CaptainTierEnum? JudgeRequiredTier { get; set; } = null;
            public HashSet<string> SameFamilyAsAuthors { get; } = new HashSet<string>(StringComparer.Ordinal);
            public Dictionary<string, PersonaOutcomeScore> OutcomeScores { get; } = new Dictionary<string, PersonaOutcomeScore>(StringComparer.Ordinal);
        }

        #endregion
    }
}
