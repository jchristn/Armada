namespace Armada.Core.Services
{
    using System.Collections.Concurrent;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Envelope state for one dispatch decision: the effective envelope, why each visible captain is excluded, the
    /// idle pool routing drew from, and the escalation baseline.
    /// </summary>
    internal sealed class WorkerEnvelopeGate
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
            if (!captain.Active || Pinned) return false;
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
            return captain.Active && (String.IsNullOrEmpty(captain.AllowedPersonas) || MissionCaptainRouter.CaptainAllowsPersona(captain, PersonaCatalog.Worker));
        }
    }
}
