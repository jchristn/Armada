namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// One evaluated (profile, revision group, persona, vessel scope) and the result of every rule.
    /// </summary>
    public class ModelAlertTarget
    {
        /// <summary>
        /// Tenant, or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Model profile identifier.
        /// </summary>
        public string ProfileId { get; set; } = "";

        /// <summary>
        /// The profile's model, for display.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Revision group (revision identifiers comma separated, or "unmatched").
        /// </summary>
        public string RevisionGroup { get; set; } = "";

        /// <summary>
        /// Persona.
        /// </summary>
        public string Persona { get; set; } = "";

        /// <summary>
        /// Vessel, or null for every vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Vessel name, or null.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Missions counted in the group.
        /// </summary>
        public int Missions { get; set; } = 0;

        /// <summary>
        /// Attempts counted in the group.
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// Result of every rule, in rule order.
        /// </summary>
        public List<ModelAlertRuleResult> Rules { get; set; } = new List<ModelAlertRuleResult>();
    }
}
