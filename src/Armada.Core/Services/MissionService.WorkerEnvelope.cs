namespace Armada.Core.Services
{
    using System.Collections.Concurrent;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Worker eligibility envelope (docs/MODEL_HETEROGENEITY.md, 3.3): which captains may run a Worker mission, the
    /// escalation to a stronger compatible captain instead of waiting, and the decision events.
    /// </summary>
    public partial class MissionService
    {
        #region Private-Members

        private const int _MaxEnvelopeDecisionsTracked = 10000;

        private readonly ConcurrentDictionary<string, string> _EnvelopeDecisionSignatures =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The Worker eligibility envelope that applies to a mission: its own envelope tightened onto the vessel default,
        /// or null when neither sets anything. Stored values are used as they are; an invalid stored value is ignored.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="vessel">The mission's vessel, or null when it could not be read.</param>
        /// <returns>The effective envelope, or null.</returns>
        /// <exception cref="ArgumentNullException">The mission is null.</exception>
        public static WorkerEnvelope? EffectiveWorkerEnvelope(Mission mission, Vessel? vessel)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return WorkerEnvelopeRules.Effective(vessel?.WorkerEnvelope, mission.WorkerEnvelope);
        }

        #endregion

        #region Private-Methods

        private async Task<Captain?> FindAvailableCaptainAsync(Mission mission, CancellationToken token)
        {
            WorkerEnvelopeGate? gate = await BuildWorkerEnvelopeGateAsync(mission, token).ConfigureAwait(false);
            Captain? routed = await FindRoutedCaptainAsync(mission, gate, token).ConfigureAwait(false);
            if (gate == null) return routed;

            WorkerEnvelopeOutcomeEnum outcome = WorkerEnvelopeOutcomeEnum.Assigned;
            Captain? chosen = routed;
            if (chosen == null)
            {
                chosen = gate.SelectEscalation(mission);
                if (chosen != null)
                {
                    outcome = WorkerEnvelopeOutcomeEnum.Escalated;
                    _Logging.Info(_Header + "mission " + mission.Id + " escalates to captain " + chosen.Id + " (" + CaptainTierSelector.EffectiveTier(chosen)
                        + "): no idle captain routing would use meets the Worker envelope");
                }
                else
                {
                    outcome = gate.AnyCompatibleCaptain() ? WorkerEnvelopeOutcomeEnum.Waiting : WorkerEnvelopeOutcomeEnum.NoEligibleCaptain;
                }
            }

            await RecordWorkerEnvelopeDecisionAsync(mission, gate, outcome, chosen, token).ConfigureAwait(false);
            return chosen;
        }

        /// <summary>
        /// The envelope gate for a mission, or null when the mission is not a Worker stage or no envelope can exclude a
        /// captain (no envelope, or one with only file and step limits): routing is then unchanged.
        /// </summary>
        private async Task<WorkerEnvelopeGate?> BuildWorkerEnvelopeGateAsync(Mission mission, CancellationToken token)
        {
            if (!PersonaCatalog.Matches(RoutingPersona(mission), PersonaCatalog.Worker)) return null;
            Vessel? vessel = String.IsNullOrEmpty(mission.VesselId) ? null : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            WorkerEnvelope? envelope = EffectiveWorkerEnvelope(mission, vessel);
            if (!WorkerEnvelopeRules.ConstrainsCaptains(envelope)) return null;

            WorkerEnvelopeGate gate = new WorkerEnvelopeGate();
            gate.Envelope = envelope!;
            gate.Visible = (await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false))
                .Where(c => CaptainVisibleToMission(c, mission)).ToList();
            Dictionary<string, ModelProfile?> profiles = new Dictionary<string, ModelProfile?>(StringComparer.Ordinal);
            foreach (Captain captain in gate.Visible)
            {
                ModelProfile? profile = null;
                if (!String.IsNullOrEmpty(captain.ModelProfileId) && !profiles.TryGetValue(captain.ModelProfileId, out profile))
                {
                    profile = await _Database.ModelProfiles.ReadAsync(captain.ModelProfileId, token).ConfigureAwait(false);
                    if (profile != null && !String.Equals(profile.TenantId, captain.TenantId, StringComparison.Ordinal)) profile = null;
                    profiles[captain.ModelProfileId] = profile;
                }
                string? reason = WorkerEnvelopeRules.IneligibilityReason(gate.Envelope, captain, profile);
                if (reason != null) gate.Exclusions[captain.Id] = reason;
            }

            bool escalatedRework = !String.IsNullOrEmpty(mission.RequestedCaptainId) && (IntegrationBarrier.IsEscalatedRework(mission)
                || (mission.Description ?? "").Contains("## Escalated to ", StringComparison.Ordinal));
            gate.Pinned = escalatedRework || (!String.IsNullOrEmpty(mission.RequestedCaptainId) && await HasStrictCaptainOverrideAsync(mission, token).ConfigureAwait(false));
            Persona? persona = String.IsNullOrEmpty(mission.RequestedCaptainId)
                ? await ReadPersonaByNameAsync(mission.TenantId, RoutingPersona(mission), token, mission.UserId).ConfigureAwait(false)
                : null;
            gate.BaselineTier = gate.ComputeBaselineTier(mission, persona);
            return gate;
        }

        private async Task RecordWorkerEnvelopeDecisionAsync(Mission mission, WorkerEnvelopeGate gate, WorkerEnvelopeOutcomeEnum outcome, Captain? chosen, CancellationToken token)
        {
            WorkerEnvelopeDecision decision = new WorkerEnvelopeDecision
            {
                MissionId = mission.Id,
                Outcome = outcome,
                CaptainId = chosen?.Id,
                CaptainName = chosen?.Name,
                CaptainTier = chosen != null && outcome == WorkerEnvelopeOutcomeEnum.Escalated ? CaptainTierSelector.EffectiveTier(chosen) : null,
                BaselineTier = gate.BaselineTier,
                Envelope = gate.Envelope,
                Description = WorkerEnvelopeRules.Describe(gate.Envelope)
            };
            foreach (Captain captain in gate.Visible.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id, StringComparer.Ordinal))
            {
                if (gate.Exclusions.TryGetValue(captain.Id, out string? reason))
                    decision.Excluded.Add(new WorkerEnvelopeExclusion { CaptainId = captain.Id, CaptainName = captain.Name, Reason = reason });
            }
            decision.Summary = SummarizeEnvelopeDecision(mission, decision);

            // Dispatch runs every few seconds; record a decision only when it differs from the last one for the mission.
            string signature = outcome + "|" + decision.CaptainId + "|" + String.Join(";", decision.Excluded.Select(e => e.CaptainId + "=" + e.Reason));
            if (_EnvelopeDecisionSignatures.TryGetValue(mission.Id, out string? previous) && String.Equals(previous, signature, StringComparison.Ordinal)) return;
            if (_EnvelopeDecisionSignatures.Count >= _MaxEnvelopeDecisionsTracked) _EnvelopeDecisionSignatures.Clear();
            _EnvelopeDecisionSignatures[mission.Id] = signature;

            try
            {
                ArmadaEvent evt = new ArmadaEvent(WorkerEnvelopeRules.EventType, decision.Summary);
                evt.TenantId = mission.TenantId;
                evt.UserId = mission.UserId;
                evt.EntityType = "mission";
                evt.EntityId = mission.Id;
                evt.CaptainId = chosen?.Id;
                evt.MissionId = mission.Id;
                evt.VesselId = mission.VesselId;
                evt.VoyageId = mission.VoyageId;
                evt.Payload = JsonSerializer.Serialize(decision);
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not record the Worker envelope decision for mission " + mission.Id + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Store the Architect's envelope for one slice on its Worker mission and the stages derived from it (so the
        /// Judge sees the same envelope). The plan may only tighten the vessel default: a looser field is clamped and an
        /// invalid envelope is dropped (the vessel default applies), and either is recorded as an event with the reason.
        /// </summary>
        private async Task ApplyArchitectWorkerEnvelopeAsync(Mission architect, Mission worker, ParsedArchitectMission parsed, List<Mission> knownMissions, CancellationToken token)
        {
            if (parsed.WorkerEnvelope == null) return;
            Vessel? vessel = String.IsNullOrEmpty(worker.VesselId) ? null : await _Database.Vessels.ReadAsync(worker.VesselId, token).ConfigureAwait(false);
            WorkerEnvelope? envelope;
            List<string> problems;
            try
            {
                envelope = WorkerEnvelopeRules.Tighten(vessel?.WorkerEnvelope, parsed.WorkerEnvelope, out problems);
            }
            catch (ArgumentException ex)
            {
                envelope = null;
                problems = new List<string> { ex.Message + " The Architect's envelope was ignored and the vessel default applies" };
            }

            List<Mission> chain = new List<Mission> { worker };
            if (!String.IsNullOrEmpty(worker.VoyageId))
            {
                List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(worker.VoyageId, token).ConfigureAwait(false);
                HashSet<string> members = new HashSet<string>(StringComparer.Ordinal) { worker.Id };
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (Mission candidate in voyageMissions)
                    {
                        if (members.Contains(candidate.Id) || String.IsNullOrEmpty(candidate.DependsOnMissionId) || !members.Contains(candidate.DependsOnMissionId)) continue;
                        if (PersonaCatalog.Matches(candidate.Persona, PersonaCatalog.Worker)) continue;
                        members.Add(candidate.Id);
                        chain.Add(candidate);
                        grew = true;
                    }
                }
            }
            // Keep the caller's in-memory copies in step so a later update of one of them does not drop the envelope.
            HashSet<string> chainIds = new HashSet<string>(chain.Select(m => m.Id), StringComparer.Ordinal);
            foreach (Mission known in knownMissions)
                if (chainIds.Contains(known.Id)) known.WorkerEnvelope = envelope;
            foreach (Mission stage in chain)
            {
                stage.WorkerEnvelope = envelope;
                stage.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Missions.UpdateAsync(stage, token).ConfigureAwait(false);
            }

            if (problems.Count == 0) return;
            string message = "Worker envelope for '" + worker.Title + "' was clamped to the vessel default: " + String.Join("; ", problems) + ".";
            _Logging.Warn(_Header + "mission " + worker.Id + ": " + message);
            try
            {
                ArmadaEvent evt = new ArmadaEvent(WorkerEnvelopeRules.ClampedEventType, message);
                evt.TenantId = worker.TenantId;
                evt.UserId = worker.UserId;
                evt.EntityType = "mission";
                evt.EntityId = worker.Id;
                evt.MissionId = worker.Id;
                evt.VesselId = worker.VesselId;
                evt.VoyageId = worker.VoyageId;
                evt.Payload = JsonSerializer.Serialize(new WorkerEnvelopeDecision
                {
                    MissionId = worker.Id,
                    Outcome = WorkerEnvelopeOutcomeEnum.Clamped,
                    Envelope = envelope ?? new WorkerEnvelope(),
                    Description = WorkerEnvelopeRules.Describe(envelope),
                    Summary = message
                });
                await _Database.Events.CreateAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not record the Worker envelope clamp for mission " + worker.Id + " (Architect " + architect.Id + "): " + ex.Message);
            }
        }

        private static string SummarizeEnvelopeDecision(Mission mission, WorkerEnvelopeDecision decision)
        {
            string excluded = decision.Excluded.Count == 0
                ? "no captain excluded"
                : decision.Excluded.Count + " captain(s) excluded: " + String.Join("; ", decision.Excluded.Take(5).Select(e => (e.CaptainName ?? e.CaptainId) + " (" + e.Reason + ")"))
                    + (decision.Excluded.Count > 5 ? "; and " + (decision.Excluded.Count - 5) + " more" : "");
            switch (decision.Outcome)
            {
                case WorkerEnvelopeOutcomeEnum.Assigned:
                    return "Worker envelope: assigned " + (decision.CaptainName ?? decision.CaptainId) + "; " + excluded + ".";
                case WorkerEnvelopeOutcomeEnum.Escalated:
                    return "Worker envelope: escalated to " + (decision.CaptainName ?? decision.CaptainId) + " (" + decision.CaptainTier
                        + (decision.BaselineTier.HasValue ? ", above " + decision.BaselineTier.Value : "")
                        + ") because no idle captain routing would use meets the envelope; " + excluded + ".";
                case WorkerEnvelopeOutcomeEnum.Waiting:
                    return "Worker envelope: waiting for a captain that meets the envelope to become idle; " + excluded + ".";
                default:
                    return "Worker envelope: no captain meets the envelope or may run the Worker persona, so '" + mission.Title
                        + "' stays Pending. Give a captain a model profile that meets it, or change the vessel's envelope; " + excluded + ".";
            }
        }

        #endregion

        #region Private-Types

        /// <summary>
        /// Envelope state for one dispatch decision: the effective envelope, why each visible captain is excluded, the
        /// idle pool routing drew from, and the escalation baseline.
        /// </summary>
        private sealed class WorkerEnvelopeGate
        {
            public WorkerEnvelope Envelope { get; set; } = new WorkerEnvelope();
            public Dictionary<string, string> Exclusions { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
            public List<Captain> Visible { get; set; } = new List<Captain>();
            public List<Captain> IdlePool { get; set; } = new List<Captain>();
            public bool Pinned { get; set; } = false;
            public CaptainTierEnum? BaselineTier { get; set; } = null;

            /// <summary>
            /// Highest tier among the envelope-compatible captains normal routing prefers (the requested captain, or the
            /// persona's prioritized captains), whatever their state. Null when routing names no such captain.
            /// </summary>
            public CaptainTierEnum? ComputeBaselineTier(Mission mission, Persona? persona)
            {
                IEnumerable<Captain> preferred = Visible.Where(c => !Exclusions.ContainsKey(c.Id) && MayRunWorker(c));
                if (!String.IsNullOrEmpty(mission.RequestedCaptainId))
                    preferred = preferred.Where(c => c.Id == mission.RequestedCaptainId);
                else if (persona != null && persona.CaptainPriorities.Count > 0)
                    preferred = preferred.Where(c => persona.CaptainPriorities.Any(p => PersonaCaptainPrioritySelector.Matches(p, c)));
                else
                    return null;
                List<Captain> list = preferred.ToList();
                if (list.Count == 0) return null;
                return list.Max(CaptainTierSelector.EffectiveTier);
            }

            /// <summary>
            /// Whether escalation may pick this captain: it meets the envelope, may run the Worker persona, meets the
            /// mission's tier floor and is stronger than the baseline. Never for a pinned (strict or escalated) mission.
            /// </summary>
            public bool CanEscalateTo(Captain captain, Mission mission)
            {
                if (Pinned) return false;
                if (Exclusions.ContainsKey(captain.Id)) return false;
                if (!MayRunWorker(captain)) return false;
                if (mission.Tier.HasValue && !CaptainTierSelector.CanHandle(captain, mission.Tier.Value)) return false;
                return !BaselineTier.HasValue || CaptainTierSelector.EffectiveTier(captain) > BaselineTier.Value;
            }

            /// <summary>
            /// The escalation captain: the lowest-tier idle captain escalation may pick (so the strongest captains are kept
            /// for work that needs them), preferring one whose preferred persona is Worker. Null when there is none.
            /// </summary>
            public Captain? SelectEscalation(Mission mission)
            {
                List<Captain> candidates = IdlePool.Where(c => CanEscalateTo(c, mission)).ToList();
                if (candidates.Count == 0) return null;
                return candidates
                    .OrderBy(CaptainTierSelector.EffectiveTier)
                    .ThenBy(c => !String.IsNullOrEmpty(c.PreferredPersona) && PersonaCatalog.Matches(c.PreferredPersona, PersonaCatalog.Worker) ? 0 : 1)
                    .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.Id, StringComparer.Ordinal)
                    .First();
            }

            /// <summary>
            /// Whether any visible captain, in any state, meets the envelope and may run the Worker persona.
            /// </summary>
            public bool AnyCompatibleCaptain()
            {
                return Visible.Any(c => !Exclusions.ContainsKey(c.Id) && MayRunWorker(c));
            }

            private static bool MayRunWorker(Captain captain)
            {
                return String.IsNullOrEmpty(captain.AllowedPersonas) || CaptainAllowsPersona(captain, PersonaCatalog.Worker);
            }
        }

        #endregion
    }
}
