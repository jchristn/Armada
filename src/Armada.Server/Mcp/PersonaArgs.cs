namespace Armada.Server.Mcp
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    /// <summary>
    /// MCP tool arguments for persona operations.
    /// </summary>
    public class PersonaArgs
    {
        /// <summary>
        /// Persona name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Persona description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Prompt template name for this persona.
        /// </summary>
        public string? PromptTemplateName { get; set; }

        /// <summary>
        /// Optional default (preferred) captain id (cpt_ prefix) for this persona. Pre-fills the per-step
        /// captain at dispatch and seeds the preferred captain when CaptainPriorities is empty.
        /// </summary>
        public string? DefaultCaptainId { get; set; }
        /// <summary>Ordered captain matching classes. Empty clears; null preserves on update.</summary>
        public List<PersonaCaptainPriority>? CaptainPriorities { get; set; }

        /// <summary>Escalation ladder, lowest rung first (RS-5.6). Empty clears; null preserves on update.</summary>
        public List<PersonaCaptainPriority>? EscalationLadder { get; set; }

        /// <summary>Optional unavailable-class policy. Defaults to FallThrough on create.</summary>
        public PersonaPriorityAvailabilityEnum? PriorityAvailability { get; set; }

        /// <summary>Optional exhausted-list policy. Defaults to Wait on create.</summary>
        public PersonaPriorityExhaustionEnum? PriorityExhaustion { get; set; }

        /// <summary>
        /// Optional ownership scope (UserSpecific or TenantWide).
        /// </summary>
        public ScopeEnum? Scope { get; set; }
    }
}
