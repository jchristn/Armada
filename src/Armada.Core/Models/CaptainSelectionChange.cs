namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Invalidation notice that a mission's captain-selection state changed. Carries identifiers and the stable
    /// outcome only; clients re-fetch authorized detail and never receive candidates or account identity here.
    /// </summary>
    public sealed class CaptainSelectionChange
    {
        /// <summary>Mission tenant; scopes delivery.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>Mission identifier.</summary>
        public string MissionId { get; set; } = String.Empty;

        /// <summary>Mission voyage, or null.</summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>Recorded decision, or null when the change did not create one (for example a recovery authorization).</summary>
        public string? DecisionId { get; set; } = null;

        /// <summary>Stable outcome or change kind, for example Admitted, Waiting or RecoveryAuthorized.</summary>
        public string Outcome { get; set; } = String.Empty;
    }
}
