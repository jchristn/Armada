namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Request to evaluate vessel health. When <see cref="VesselIds"/> is non-empty only those vessels are evaluated
    /// (inactive ones included); otherwise when <see cref="FleetId"/> is set, the active vessels of that fleet; otherwise
    /// every active vessel in the tenant.
    /// </summary>
    public class VesselHealthEvaluateRequest
    {
        #region Public-Members

        /// <summary>
        /// Vessel identifiers to evaluate, or null/empty for all. Every identifier must belong to the caller's tenant.
        /// </summary>
        public List<string>? VesselIds { get; set; } = null;

        /// <summary>
        /// Fleet whose active vessels to evaluate, or null. Ignored when VesselIds is non-empty.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Whether to run the dependency and vulnerability checks even when the manifest hash is unchanged and the
        /// previous results are fresh. Null means true for manual evaluations (scheduled evaluations never force).
        /// </summary>
        public bool? Force { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselHealthEvaluateRequest()
        {
        }

        #endregion
    }
}
