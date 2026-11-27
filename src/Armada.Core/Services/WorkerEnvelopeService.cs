namespace Armada.Core.Services
{
    using System.Collections.Concurrent;
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// The Worker eligibility envelope: effective envelope, captain gate, and recorded decisions.
    /// </summary>
    internal sealed class WorkerEnvelopeService
    {
        #region Private-Members

        private readonly MissionServiceContext _Context;
        private readonly DatabaseDriver _Database;
        private readonly string _Header;
        private readonly LoggingModule _Logging;

        internal const int _MaxEnvelopeDecisionsTracked = 10000;

        private readonly ConcurrentDictionary<string, string> _EnvelopeDecisionSignatures =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, byte> _EnvelopePlaceholderWarnings =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal WorkerEnvelopeService(MissionService service, MissionServiceContext context)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Database = context.Database;
            _Header = context.Header;
            _Logging = context.Logging;
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// The Worker eligibility envelope that applies to a mission: its own envelope tightened onto the vessel default,
        /// or null when neither sets anything. Stored values are used as they are; an invalid stored value is ignored.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="vessel">The mission's vessel, or null when it could not be read.</param>
        /// <returns>The effective envelope, or null.</returns>
        /// <exception cref="ArgumentNullException">The mission is null.</exception>
        internal static WorkerEnvelope? EffectiveWorkerEnvelope(Mission mission, Vessel? vessel)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            return WorkerEnvelopeRules.Effective(vessel?.WorkerEnvelope, mission.WorkerEnvelope);
        }

        /// <summary>
        /// Append the Worker envelope section to an Architect or Judge persona prompt whose template has no
        /// <c>{WorkerEnvelope}</c> placeholder (an operator-customized template from before Phase 6.1) while an envelope
        /// applies, and warn once per template. The stored template is not changed.
        /// </summary>
        /// <param name="personaPrompt">Rendered persona prompt.</param>
        /// <param name="mission">Mission.</param>
        /// <param name="templateName">Name of the persona template that was rendered, for the warning.</param>
        /// <param name="templateParams">Template parameters (the rendered <c>WorkerEnvelope</c> value).</param>
        /// <returns>The persona prompt, with the envelope section when it was missing.</returns>
        /// <exception cref="ArgumentNullException">The mission or parameters are null.</exception>
        internal string EnsureWorkerEnvelopeSection(string personaPrompt, Mission mission, string templateName, Dictionary<string, string> templateParams)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (templateParams == null) throw new ArgumentNullException(nameof(templateParams));
            templateParams.TryGetValue("WorkerEnvelope", out string? section);
            string result = WorkerEnvelopeRules.EnsurePromptSection(personaPrompt, mission.Persona, section, out bool appended);
            if (appended && _EnvelopePlaceholderWarnings.TryAdd(templateName ?? "", 0))
            {
                _Logging.Warn(_Header + "prompt template '" + templateName + "' has no {WorkerEnvelope} placeholder (customized before Phase 6.1); "
                    + "appending the Worker envelope section after it. Add {WorkerEnvelope} to the template to place it yourself.");
            }
            return result;
        }

        /// <summary>
        /// The envelope gate for a mission, or null when the mission is not a Worker stage or no envelope can exclude a
        /// captain (no envelope, or one with only file and step limits): routing is then unchanged.
        /// </summary>
        internal async Task<WorkerEnvelopeGate?> BuildWorkerEnvelopeGateAsync(Mission mission, CancellationToken token)
        {
            if (!PersonaCatalog.Matches(MissionCaptainRouter.RoutingPersona(mission), PersonaCatalog.Worker)) return null;
            Vessel? vessel = String.IsNullOrEmpty(mission.VesselId) ? null : await _Context.MissionDispatchPass.PassVesselAsync(mission.VesselId, token).ConfigureAwait(false);
            WorkerEnvelope? envelope = EffectiveWorkerEnvelope(mission, vessel);
            if (!WorkerEnvelopeRules.ConstrainsCaptains(envelope)) return null;

            WorkerEnvelopeGate gate = new WorkerEnvelopeGate();
            gate.Envelope = envelope!;
            gate.Visible = (await _Context.MissionDispatchPass.PassCaptainsAsync(token).ConfigureAwait(false))
                .Where(c => MissionCaptainRouter.CaptainVisibleToMission(c, mission)).ToList();
            Dictionary<string, ModelProfile?> profiles = new Dictionary<string, ModelProfile?>(StringComparer.Ordinal);
            foreach (Captain captain in gate.Visible)
            {
                ModelProfile? profile = null;
                if (!String.IsNullOrEmpty(captain.ModelProfileId) && !profiles.TryGetValue(captain.ModelProfileId, out profile))
                {
                    profile = await _Context.MissionDispatchPass.PassProfileAsync(captain.ModelProfileId, token).ConfigureAwait(false);
                    if (profile != null && !String.Equals(profile.TenantId, captain.TenantId, StringComparison.Ordinal)) profile = null;
                    profiles[captain.ModelProfileId] = profile;
                }
                string? reason = WorkerEnvelopeRules.IneligibilityReason(gate.Envelope, captain, profile);
                if (reason != null) gate.Exclusions[captain.Id] = reason;
            }

            bool escalatedRework = !String.IsNullOrEmpty(mission.RequestedCaptainId) && (IntegrationBarrier.IsEscalatedRework(mission)
                || mission.EscalationCount > 0);
            gate.Pinned = escalatedRework || (!String.IsNullOrEmpty(mission.RequestedCaptainId) && await _Context.MissionCaptainRouter.HasStrictCaptainOverrideAsync(mission, token).ConfigureAwait(false));
            Persona? persona = String.IsNullOrEmpty(mission.RequestedCaptainId)
                ? await _Context.MissionDispatchPass.PassPersonaAsync(mission.TenantId, MissionCaptainRouter.RoutingPersona(mission), mission.UserId, token).ConfigureAwait(false)
                : null;
            gate.BaselineTier = gate.ComputeBaselineTier(mission, persona);
            return gate;
        }

        internal async Task RecordWorkerEnvelopeDecisionAsync(Mission mission, WorkerEnvelopeGate gate, WorkerEnvelopeOutcomeEnum outcome, Captain? chosen, CancellationToken token)
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
            // The stored list is bounded; the summary and the signature above count every excluded captain.
            if (decision.Excluded.Count > WorkerEnvelopeDecision.MaxExcluded)
                decision.Excluded.RemoveRange(WorkerEnvelopeDecision.MaxExcluded, decision.Excluded.Count - WorkerEnvelopeDecision.MaxExcluded);
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
        internal async Task ApplyArchitectWorkerEnvelopeAsync(Mission architect, Mission worker, ParsedArchitectMission parsed, ArchitectFanOutWork work, CancellationToken token)
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
            HashSet<string> members = new HashSet<string>(StringComparer.Ordinal) { worker.Id };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (Mission candidate in work.Missions)
                {
                    if (members.Contains(candidate.Id) || String.IsNullOrEmpty(candidate.DependsOnMissionId) || !members.Contains(candidate.DependsOnMissionId)) continue;
                    if (PersonaCatalog.Matches(candidate.Persona, PersonaCatalog.Worker)) continue;
                    members.Add(candidate.Id);
                    chain.Add(candidate);
                    grew = true;
                }
            }
            foreach (Mission stage in chain)
            {
                stage.WorkerEnvelope = envelope;
                work.Changed(stage);
            }

            if (problems.Count == 0) return;
            string message = "Worker envelope for '" + worker.Title + "' was clamped to the vessel default: " + String.Join("; ", problems) + ".";
            _Logging.Warn(_Header + "mission " + worker.Id + ": " + message);
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
            work.Events.Add(evt);
        }

        #endregion

        #region Private-Methods

        private async Task<Captain?> FindAvailableCaptainAsync(Mission mission, CancellationToken token)
        {
            return (await _Context.MissionAdmissionPipeline.SelectCaptainAdmissionAsync(mission, token, recordEnvelope: true).ConfigureAwait(false)).Selected;
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
    }
}
