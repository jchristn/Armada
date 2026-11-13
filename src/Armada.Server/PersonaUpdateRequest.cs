namespace Armada.Server
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>Partial persona update. Omitted routing fields preserve stored settings.</summary>
    public class PersonaUpdateRequest
    {
        /// <summary>Optional description.</summary>
        public string? Description { get; set; }
        /// <summary>Optional prompt template name.</summary>
        public string? PromptTemplateName { get; set; }
        /// <summary>Default captain; omitted keeps it, an explicit null clears the legacy default.</summary>
        public string? DefaultCaptainId { get; set; }
        /// <summary>Optional ownership scope, editable by administrators.</summary>
        public ScopeEnum? Scope { get; set; }
        /// <summary>Ordered matching classes; empty clears, null preserves. At most 256 entries.</summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<PersonaCaptainPriority>? CaptainPriorities { get; set; }
        /// <summary>Optional unavailable-class policy.</summary>
        public PersonaPriorityAvailabilityEnum? PriorityAvailability { get; set; }
        /// <summary>Optional exhausted-list policy.</summary>
        public PersonaPriorityExhaustionEnum? PriorityExhaustion { get; set; }
    }
}
