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
            while (!String.IsNullOrEmpty(parentId) && seen.Add(parentId))
            {
                Mission? parent = await _Database.Missions.ReadAsync(parentId, token).ConfigureAwait(false);
                if (parent == null || parent.VoyageId != mission.VoyageId || PersonaCatalog.Matches(parent.Persona, PersonaCatalog.Judge)) break;
                if (!String.IsNullOrEmpty(parent.CaptainId)) authors.Add(parent.CaptainId);
                if (PersonaCatalog.Matches(parent.Persona, PersonaCatalog.Worker)) break;
                parentId = parent.DependsOnMissionId;
            }
            return authors;
        }

        private async Task<bool> IsAnyCaptainEligibleAsync(List<Captain> idleCaptains, Mission mission, CancellationToken token)
        {
            CaptainEligibilityContext context = await BuildCaptainEligibilityContextAsync(mission, token).ConfigureAwait(false);
            return idleCaptains.Any(c => CaptainIneligibilityReason(c, mission, context) == null);
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
            CaptainEligibilityContext context = await BuildCaptainEligibilityContextAsync(mission, token).ConfigureAwait(false);
            return CaptainIneligibilityReason(captain, mission, context);
        }

        private async Task<CaptainEligibilityContext> BuildCaptainEligibilityContextAsync(Mission mission, CancellationToken token)
        {
            CaptainEligibilityContext context = new CaptainEligibilityContext();
            context.RoutingPersona = RoutingPersona(mission);
            // Review authors only change eligibility when they are excluded; otherwise they are merely less preferred.
            if (_Settings.ExcludeReviewAuthorsFromJudge)
                context.ExcludedAuthors = await ReviewAuthorCaptainIdsAsync(mission, token).ConfigureAwait(false);
            context.EscalatedToRequestedCaptain = !String.IsNullOrEmpty(mission.RequestedCaptainId) && (IntegrationBarrier.IsEscalatedRework(mission)
                || (mission.Description ?? "").Contains("## Escalated to ", StringComparison.Ordinal));
            context.StrictCaptainOverride = await HasStrictCaptainOverrideAsync(mission, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(mission.RequestedCaptainId))
            {
                context.DefaultCaptainId = await PersonaDefaultCaptainIdAsync(mission, context.RoutingPersona, token).ConfigureAwait(false);
                context.Persona = await ReadPersonaByNameAsync(mission.TenantId, context.RoutingPersona, token, mission.UserId).ConfigureAwait(false);
            }
            context.EnvelopeGate = await BuildWorkerEnvelopeGateAsync(mission, token).ConfigureAwait(false);
            return context;
        }

        private static string? CaptainIneligibilityReason(Captain captain, Mission mission, CaptainEligibilityContext context)
        {
            // Mirrors FindAvailableCaptainAsync: a requested (dictated) captain that is idle is always eligible, as is the
            // persona's default captain; otherwise the persona fence, the tier floor and the persona's priorities apply.
            if (context.ExcludedAuthors.Contains(captain.Id)) return "authored an earlier stage";
            if (!CaptainVisibleToMission(captain, mission)) return "belongs to another tenant or user";
            if (captain.QuarantineUntilUtc.HasValue && captain.QuarantineUntilUtc > DateTime.UtcNow) return "quarantined";
            if (context.EnvelopeGate != null && context.EnvelopeGate.Exclusions.TryGetValue(captain.Id, out string? envelopeReason)) return envelopeReason;
            if (!String.IsNullOrEmpty(mission.RequestedCaptainId) && captain.Id == mission.RequestedCaptainId) return null;
            if (context.EscalatedToRequestedCaptain) return "escalated to another captain";
            if (context.StrictCaptainOverride) return "the voyage requires another captain";
            if (context.DefaultCaptainId != null && captain.Id == context.DefaultCaptainId) return null;
            if (!String.IsNullOrEmpty(captain.AllowedPersonas) && !CaptainAllowsPersona(captain, context.RoutingPersona)) return "persona not allowed";
            if (mission.Tier.HasValue && !CaptainTierSelector.CanHandle(captain, mission.Tier.Value)) return "tier below " + mission.Tier.Value;
            if (context.Persona != null && context.Persona.CaptainPriorities.Count > 0
                && !PersonaCaptainPrioritySelector.AllowsGeneralFallback(context.Persona)
                && PersonaCaptainPrioritySelector.Select(context.Persona, new List<Captain> { captain }) == null
                && !(context.EnvelopeGate != null && context.EnvelopeGate.CanEscalateTo(captain, mission)))
                return "not in persona " + context.Persona.Name + "'s captain priorities";
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
                        status.MissionId = captain.CurrentMissionId;
                        Mission? current = String.IsNullOrEmpty(captain.CurrentMissionId) ? null : await _Database.Missions.ReadAsync(captain.CurrentMissionId, token).ConfigureAwait(false);
                        status.Detail = current != null ? "working on mission '" + current.Title + "'" : "working on another mission";
                        break;
                    case CaptainStateEnum.Planning:
                        PlanningSession? planning = (await _Database.PlanningSessions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false))
                            .Where(p => p.Status == PlanningSessionStatusEnum.Created || p.Status == PlanningSessionStatusEnum.Active || p.Status == PlanningSessionStatusEnum.Responding || p.Status == PlanningSessionStatusEnum.Stopping)
                            .OrderByDescending(p => p.LastUpdateUtc)
                            .FirstOrDefault();
                        status.PlanningSessionId = planning?.Id;
                        status.Detail = planning != null ? "in planning session '" + planning.Title + "'" : "reserved by a planning session";
                        break;
                    case CaptainStateEnum.Refining:
                        ObjectiveRefinementSession? refinement = (await _Database.ObjectiveRefinementSessions.EnumerateByCaptainAsync(captain.Id, token).ConfigureAwait(false))
                            .Where(r => r.Status == ObjectiveRefinementSessionStatusEnum.Created || r.Status == ObjectiveRefinementSessionStatusEnum.Active || r.Status == ObjectiveRefinementSessionStatusEnum.Responding || r.Status == ObjectiveRefinementSessionStatusEnum.Stopping)
                            .OrderByDescending(r => r.LastUpdateUtc)
                            .FirstOrDefault();
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

                blocker.Captains.Add(status);
            }
        }

        private async Task<Captain?> FindRoutedCaptainAsync(Mission mission, WorkerEnvelopeGate? envelopeGate, CancellationToken token)
        {
            string? persona = RoutingPersona(mission);
            CaptainTierEnum? requiredTier = mission.Tier;
            string? requestedCaptainId = mission.RequestedCaptainId;
            // Only idle captains are eligible for assignment
            List<Captain> idleCaptains = await _Database.Captains.EnumerateByStateAsync(CaptainStateEnum.Idle, token).ConfigureAwait(false);
            idleCaptains = idleCaptains.Where(c => CaptainVisibleToMission(c, mission) &&
                (!c.QuarantineUntilUtc.HasValue || c.QuarantineUntilUtc <= DateTime.UtcNow)).ToList();
            HashSet<string> reviewAuthors = await ReviewAuthorCaptainIdsAsync(mission, token).ConfigureAwait(false);
            if (_Settings.ExcludeReviewAuthorsFromJudge)
                idleCaptains.RemoveAll(c => reviewAuthors.Contains(c.Id));
            idleCaptains = await ApplyCapacityLimitsAsync(idleCaptains, mission, token).ConfigureAwait(false);
            // The Worker eligibility envelope applies to every path below, a requested or default captain included.
            if (envelopeGate != null)
            {
                idleCaptains = idleCaptains.Where(c => !envelopeGate.Exclusions.ContainsKey(c.Id)).ToList();
                envelopeGate.IdlePool = idleCaptains;
            }
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

            // Preferred (dictated) captain: honor it when it is idle, bypassing the AllowedPersonas fence and
            // tier routing since the operator explicitly chose it. When it is busy (or has been deleted), fall
            // through to persona/tier routing using requiredTier as the fallback tier.
            if (!String.IsNullOrEmpty(requestedCaptainId))
            {
                Captain? preferred = idleCaptains.FirstOrDefault(c => c.Id == requestedCaptainId);
                if (preferred != null)
                    return preferred;

                // Recovery must wait for its chosen alternative, never silently rerun the failed captain.
                if (IntegrationBarrier.IsEscalatedRework(mission) || (mission.Description ?? "").Contains("## Escalated to ", StringComparison.Ordinal)) return null;
                if (await HasStrictCaptainOverrideAsync(mission, token).ConfigureAwait(false)) return null;
                Captain? requested = await _Database.Captains.ReadAsync(requestedCaptainId, token).ConfigureAwait(false);
                if (requested == null)
                    _Logging.Warn(_Header + "requested captain " + requestedCaptainId + " not found; falling back to normal persona/tier routing");
                else
                    _Logging.Info(_Header + "requested captain " + requestedCaptainId + " is busy; falling back by tier for this dispatch tick");
            }

            // A persona's default captain is preferred when idle, like a requested captain, but is never persisted, so
            // when it is busy the mission falls through to normal routing with no tier floor taken from it.
            if (String.IsNullOrEmpty(requestedCaptainId))
            {
                string? defaultCaptainId = await PersonaDefaultCaptainIdAsync(mission, persona, token).ConfigureAwait(false);
                Captain? personaDefault = defaultCaptainId == null ? null : idleCaptains.FirstOrDefault(c => c.Id == defaultCaptainId);
                if (personaDefault != null) return personaDefault;
            }

            // With review authors not excluded, a Judge still prefers a captain that did not author the work it reviews
            // and falls back to an author only when routing finds no other captain.
            if (reviewAuthors.Count > 0 && !_Settings.ExcludeReviewAuthorsFromJudge)
            {
                List<Captain> independent = idleCaptains.Where(c => !reviewAuthors.Contains(c.Id)).ToList();
                if (independent.Count > 0 && independent.Count < idleCaptains.Count)
                {
                    Captain? reviewer = await SelectRoutedCaptainAsync(mission, persona, requiredTier, requestedCaptainId, independent, token).ConfigureAwait(false);
                    if (reviewer != null) return reviewer;
                    _Logging.Info(_Header + "judge mission " + mission.Id + " has no eligible captain besides the authors of the reviewed work; an author reviews it");
                }
            }

            return await SelectRoutedCaptainAsync(mission, persona, requiredTier, requestedCaptainId, idleCaptains, token).ConfigureAwait(false);
        }

        private async Task<Captain?> SelectRoutedCaptainAsync(Mission mission, string? persona, CaptainTierEnum? requiredTier, string? requestedCaptainId, List<Captain> idleCaptains, CancellationToken token)
        {
            // Filter by AllowedPersonas (null persona = any; null AllowedPersonas = fills any persona).
            List<Captain> eligible;
            if (String.IsNullOrEmpty(persona))
            {
                eligible = new List<Captain>(idleCaptains);
            }
            else
            {
                eligible = new List<Captain>();
                foreach (Captain captain in idleCaptains)
                {
                    if (String.IsNullOrEmpty(captain.AllowedPersonas) || CaptainAllowsPersona(captain, persona))
                        eligible.Add(captain);
                }
            }

            if (eligible.Count == 0)
            {
                return null;
            }

            // Hierarchies apply only when dispatch did not explicitly request a captain.
            if (String.IsNullOrEmpty(requestedCaptainId) && !String.IsNullOrEmpty(persona))
            {
                Persona? definition = await ReadPersonaByNameAsync(mission.TenantId, persona, token, mission.UserId).ConfigureAwait(false);
                if (definition != null && definition.CaptainPriorities.Count > 0)
                {
                    List<Captain> priorityEligible = eligible.Where(c => !requiredTier.HasValue ||
                        CaptainTierSelector.CanHandle(c, requiredTier.Value)).ToList();
                    Captain? selected = PersonaCaptainPrioritySelector.Select(definition, priorityEligible);
                    if (selected != null)
                    {
                        int matchedIndex = definition.CaptainPriorities.FindIndex(p => PersonaCaptainPrioritySelector.Matches(p, selected));
                        _Logging.Info(_Header + "persona " + persona + " selected captain " + selected.Id +
                            " at priority " + (matchedIndex + 1));
                        return selected;
                    }
                    if (!PersonaCaptainPrioritySelector.AllowsGeneralFallback(definition)) return null;
                }
            }

            // Tier routing -- only when the mission explicitly requires a tier (so default dispatch is
            // unchanged). Narrow to captains at/above the required tier, preferring the lowest eligible
            // tier so strong captains are not consumed by cheaper work (upward-only fallback).
            if (requiredTier.HasValue)
            {
                List<Captain> tierEligible = eligible
                    .Where(c => CaptainTierSelector.CanHandle(c, requiredTier.Value))
                    .ToList();
                if (tierEligible.Count == 0)
                {
                    // No idle captain is strong enough for this mission's tier; leave it pending.
                    return null;
                }
                CaptainTierEnum bestTier = tierEligible.Min(CaptainTierSelector.EffectiveTier);
                eligible = tierEligible.Where(c => CaptainTierSelector.EffectiveTier(c) == bestTier).ToList();
            }

            // Prefer captains whose PreferredPersona matches
            if (!String.IsNullOrEmpty(persona))
            {
                foreach (Captain captain in eligible)
                {
                    if (!String.IsNullOrEmpty(captain.PreferredPersona) &&
                        PersonaCatalog.Matches(captain.PreferredPersona, persona))
                    {
                        return captain;
                    }
                }
            }

            // No preferred match -- return first eligible
            return eligible[0];
        }

        /// <summary>
        /// Resolve and persist the preferred captain and fallback tier for a mission from an explicit value already
        /// set or the voyage-level override for the mission's persona. A persona's default captain is not persisted:
        /// it is a preference consulted on each dispatch tick (see PersonaDefaultCaptainIdAsync), so it never becomes
        /// a pin or a tier floor that keeps the mission waiting while other captains are idle. A dangling captain
        /// reference is left as-is and degrades to normal routing at assignment time. Best-effort: never throws for a
        /// missing persona/voyage.
        /// </summary>
        private async Task ResolvePreferredCaptainAsync(Mission mission, CancellationToken token)
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

            if (changed)
            {
                mission.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The vessel's Assigned and InProgress missions, the only ones the vessel lock, broad-scope and branch checks look
        /// at. Read by status rather than as the vessel's whole history (with its stored outputs and diffs), which every
        /// pending mission used to load on every dispatch tick (D17).
        /// </summary>
        private async Task<List<Mission>> ActiveMissionsOnVesselAsync(string vesselId, CancellationToken token)
        {
            List<Mission> active = new List<Mission>();
            foreach (MissionStatusEnum status in new[] { MissionStatusEnum.Assigned, MissionStatusEnum.InProgress })
                active.AddRange((await _Database.Missions.EnumerateByStatusAsync(status, token).ConfigureAwait(false)).Where(m => m.VesselId == vesselId));
            return active;
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
            Persona? persona = await ReadPersonaByNameAsync(mission.TenantId, routingPersona, token, mission.UserId).ConfigureAwait(false);
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
            Captain? captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
            if (captain == null) return null;
            return CaptainTierSelector.EffectiveTier(captain);
        }

        private async Task<List<CaptainAssignmentOverride>> ReadVoyageCaptainOverridesAsync(string? voyageId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(voyageId)) return new List<CaptainAssignmentOverride>();
            Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
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
            public bool EscalatedToRequestedCaptain { get; set; } = false;
            public bool StrictCaptainOverride { get; set; } = false;
            public string? DefaultCaptainId { get; set; } = null;
            public Persona? Persona { get; set; } = null;
            public WorkerEnvelopeGate? EnvelopeGate { get; set; } = null;
        }

        #endregion
    }
}
