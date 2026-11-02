namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Stateless, thread-safe hierarchy matching for already scope-, persona-, tier-, and availability-filtered captains.
    /// </summary>
    public static class PersonaCaptainPrioritySelector
    {
        // Matches the bounded model and endpoint columns on the server database providers.
        private const int _MatchValueMaximumLength = 1024;

        /// <summary>Validate routing settings. Returns a field-specific error, or null for valid settings.</summary>
        /// <exception cref="ArgumentNullException">The persona is null.</exception>
        public static string? Validate(Persona persona)
        {
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            if (!Enum.IsDefined(persona.PriorityAvailability)) return "PriorityAvailability is invalid";
            if (!Enum.IsDefined(persona.PriorityExhaustion)) return "PriorityExhaustion is invalid";
            for (int index = 0; index < persona.CaptainPriorities.Count; index++)
            {
                PersonaCaptainPriority? priority = persona.CaptainPriorities[index];
                string field = "CaptainPriorities[" + index + "]";
                if (priority == null) return field + " must not be null";
                if (priority.Runtime.HasValue && !Enum.IsDefined(priority.Runtime.Value)) return field + ".Runtime is invalid";
                if (priority.MinimumTier.HasValue && !Enum.IsDefined(priority.MinimumTier.Value)) return field + ".MinimumTier is invalid";
                if (priority.Model?.Length > _MatchValueMaximumLength) return field + ".Model must not exceed 1024 characters";
                if (priority.Endpoint?.Length > _MatchValueMaximumLength) return field + ".Endpoint must not exceed 1024 characters";
                if (priority.Model != null && String.IsNullOrWhiteSpace(priority.Model)) return field + ".Model must not be blank";
                if (priority.Endpoint != null && String.IsNullOrWhiteSpace(priority.Endpoint)) return field + ".Endpoint must not be blank";
                if (priority.Endpoint != null && priority.Runtime != AgentRuntimeEnum.Mux && priority.Runtime != AgentRuntimeEnum.ApiEndpoint) return field + ".Endpoint requires Runtime Mux or ApiEndpoint";
                if (priority.Runtime == null && priority.Model == null && priority.Endpoint == null && priority.MinimumTier == null)
                    return field + " requires at least one matching field";
            }
            return null;
        }

        /// <summary>Whether all supplied matching fields match a captain. Does not check availability or ownership.</summary>
        /// <exception cref="ArgumentNullException">The priority or captain is null.</exception>
        public static bool Matches(PersonaCaptainPriority priority, Captain captain)
        {
            if (priority == null) throw new ArgumentNullException(nameof(priority));
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (priority.Runtime.HasValue && priority.Runtime != captain.Runtime) return false;
            if (priority.MinimumTier.HasValue && !CaptainTierSelector.CanHandle(captain, priority.MinimumTier.Value)) return false;
            if (priority.Model != null && !String.Equals(priority.Model, captain.Model, StringComparison.OrdinalIgnoreCase)) return false;
            if (priority.Endpoint != null)
            {
                if (priority.Runtime == AgentRuntimeEnum.ApiEndpoint)
                {
                    if (captain.Runtime != AgentRuntimeEnum.ApiEndpoint || !String.Equals(priority.Endpoint, captain.ModelEndpointId, StringComparison.Ordinal)) return false;
                }
                else
                {
                    if (captain.Runtime != AgentRuntimeEnum.Mux) return false;
                    MuxCaptainOptions? options;
                    try { options = CaptainRuntimeOptions.Deserialize<MuxCaptainOptions>(captain.RuntimeOptionsJson); }
                    catch (JsonException) { return false; }
                    if (!String.Equals(priority.Endpoint, options?.Endpoint, StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Select from eligible idle captains in hierarchy order. Within a class, prefer the persona's
        /// preferred captains, then the fewest occupied slots and stable captain id order. Null means no available match.
        /// </summary>
        /// <exception cref="ArgumentNullException">The persona or eligible captain list is null.</exception>
        public static Captain? Select(Persona persona, IReadOnlyList<Captain> eligibleCaptains)
        {
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            if (eligibleCaptains == null) throw new ArgumentNullException(nameof(eligibleCaptains));
            if (Validate(persona) != null) return null;
            IEnumerable<PersonaCaptainPriority> priorities = persona.PriorityAvailability == PersonaPriorityAvailabilityEnum.WaitForFirstPriority
                ? persona.CaptainPriorities.Take(1) : persona.CaptainPriorities;
            foreach (PersonaCaptainPriority priority in priorities)
            {
                Captain? selected = eligibleCaptains.Where(c => c.Active && Matches(priority, c))
                    .OrderByDescending(c => PersonaCatalog.Matches(c.PreferredPersona, persona.Name))
                    .ThenBy(c => c.ActiveMissionCount).ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();
                if (selected != null) return selected;
            }
            return null;
        }

        /// <summary>Whether legacy routing is permitted after priorities produce no available match.</summary>
        /// <exception cref="ArgumentNullException">The persona is null.</exception>
        public static bool AllowsGeneralFallback(Persona persona)
        {
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            return persona.CaptainPriorities.Count == 0 ||
                (Validate(persona) == null && persona.PriorityAvailability == PersonaPriorityAvailabilityEnum.FallThrough &&
                 persona.PriorityExhaustion == PersonaPriorityExhaustionEnum.UseEligibleCaptain);
        }
    }
}
