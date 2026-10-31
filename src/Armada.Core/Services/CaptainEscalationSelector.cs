namespace Armada.Core.Services
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

    /// <summary>Chooses a different, visible captain for a bounded recovery attempt without requiring a planning stage.</summary>
    public static class CaptainEscalationSelector
    {
        /// <summary>
        /// Prefer the voyage's planner, then an available alternative from Worker priorities, then its reviewer,
        /// then another eligible worker. Ownership, persona restrictions, quarantine and minimum tier apply.
        /// A reviewer chosen as worker must be excluded from the following review.
        /// </summary>
        /// <exception cref="ArgumentNullException">Database, mission or voyage missions is null.</exception>
        public static async Task<Captain?> SelectAsync(DatabaseDriver database, Mission mission, string? failedCaptainId,
            IReadOnlyList<Mission> voyageMissions, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(mission);
            ArgumentNullException.ThrowIfNull(voyageMissions);
            List<Captain> captains = await database.Captains.EnumerateAsync(token).ConfigureAwait(false);
            List<Captain> eligible = captains.Where(c => c.Id != failedCaptainId && c.TenantId == mission.TenantId
                && (c.UserId == null || c.UserId == mission.UserId)
                && (c.State == CaptainStateEnum.Idle || c.State == CaptainStateEnum.Working)
                && (!c.QuarantineUntilUtc.HasValue || c.QuarantineUntilUtc <= DateTime.UtcNow)
                && (!mission.Tier.HasValue || CaptainTierSelector.CanHandle(c, mission.Tier.Value))
                && AllowsWorker(c)).ToList();
            eligible = await MeetingWorkerEnvelopeAsync(database, mission, eligible, token).ConfigureAwait(false);
            foreach (string role in new[] { PersonaCatalog.Architect, PersonaCatalog.ProductManager })
            {
                foreach (Mission planner in voyageMissions.Where(m => PersonaCatalog.Matches(m.Persona, role)))
                {
                    Captain? candidate = eligible.FirstOrDefault(c => c.Id == planner.CaptainId);
                    if (candidate != null) return candidate;
                }
            }
            List<Persona> personas = (await database.Personas.EnumerateAsync(token).ConfigureAwait(false))
                .Where(p => p.Active && p.TenantId == mission.TenantId && (p.UserId == null || p.UserId == mission.UserId || p.Scope == ScopeEnum.TenantWide)).ToList();
            Persona? worker = personas.FirstOrDefault(p => PersonaCatalog.Matches(p.Name, PersonaCatalog.Worker));
            List<Captain> idle = eligible.Where(c => c.State == CaptainStateEnum.Idle).ToList();
            if (worker != null)
            {
                Captain? priority = PersonaCaptainPrioritySelector.Select(worker, idle);
                if (priority != null) return priority;
                if (worker.CaptainPriorities.Count == 0)
                {
                    Captain? preferred = idle.FirstOrDefault(c => c.Id == worker.DefaultCaptainId);
                    if (preferred != null) return preferred;
                }
            }
            // A Worker > Judge voyage has no planner. Its reviewer is a known alternative, not a model-name guess.
            foreach (Mission review in voyageMissions.Where(m => PersonaCatalog.Matches(m.Persona, PersonaCatalog.Judge)).OrderByDescending(m => m.CreatedUtc))
            {
                Captain? candidate = eligible.FirstOrDefault(c => c.Id == (review.CaptainId ?? review.RequestedCaptainId));
                if (candidate != null) return candidate;
                Persona? reviewer = personas.FirstOrDefault(p => PersonaCatalog.Matches(p.Name, PersonaCatalog.Judge));
                if (reviewer != null)
                {
                    candidate = PersonaCaptainPrioritySelector.Select(reviewer, idle);
                    if (candidate == null && reviewer.CaptainPriorities.Count == 0) candidate = idle.FirstOrDefault(c => c.Id == reviewer.DefaultCaptainId);
                    if (candidate != null) return candidate;
                }
            }
            if (worker != null && !PersonaCaptainPrioritySelector.AllowsGeneralFallback(worker)) return null;
            return idle.OrderByDescending(c => PersonaCatalog.Matches(c.PreferredPersona, PersonaCatalog.Worker))
                .ThenByDescending(CaptainTierSelector.EffectiveTier).ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
        }

        private static bool AllowsWorker(Captain captain)
        {
            if (String.IsNullOrWhiteSpace(captain.AllowedPersonas)) return true;
            try
            {
                List<string>? roles = JsonSerializer.Deserialize<List<string>>(captain.AllowedPersonas);
                return roles != null && roles.Any(role => PersonaCatalog.Matches(role, PersonaCatalog.Worker));
            }
            catch (JsonException) { return false; }
        }

        // A recovery captain must meet the Worker eligibility envelope too, or the pinned rework could never start.
        private static async Task<List<Captain>> MeetingWorkerEnvelopeAsync(DatabaseDriver database, Mission mission, List<Captain> captains, CancellationToken token)
        {
            Vessel? vessel = String.IsNullOrEmpty(mission.VesselId) ? null : await database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            WorkerEnvelope? envelope = WorkerEnvelopeRules.Effective(vessel?.WorkerEnvelope, mission.WorkerEnvelope);
            if (!WorkerEnvelopeRules.ConstrainsCaptains(envelope)) return captains;
            List<Captain> meeting = new List<Captain>();
            foreach (Captain captain in captains)
            {
                ModelProfile? profile = String.IsNullOrEmpty(captain.ModelProfileId) ? null : await database.ModelProfiles.ReadAsync(captain.ModelProfileId, token).ConfigureAwait(false);
                if (WorkerEnvelopeRules.IneligibilityReason(envelope, captain, profile) == null) meeting.Add(captain);
            }
            return meeting;
        }
    }
}
