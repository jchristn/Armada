namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Result of MCP delete_vessel: the vessel is deleted, and any cleanup step that could not complete (a mission that
    /// could not be stopped or deleted, a dock that could not be purged, a directory left in place) is reported. Warnings
    /// lists at most <see cref="MaxWarnings"/> of them; WarningCount is the full count.
    /// </summary>
    public class VesselDeleteResult
    {
        #region Public-Members

        /// <summary>
        /// Most warnings listed in <see cref="Warnings"/>.
        /// </summary>
        public const int MaxWarnings = 50;

        /// <summary>
        /// Always "deleted".
        /// </summary>
        public string Status { get; set; } = "deleted";

        /// <summary>
        /// Identifier of the deleted vessel.
        /// </summary>
        public string VesselId { get; set; } = String.Empty;

        /// <summary>
        /// Number of cleanup steps that could not complete. Zero when cleanup was clean.
        /// </summary>
        public int WarningCount { get; set; } = 0;

        /// <summary>
        /// The first cleanup warnings, in the order they occurred; null when there are none.
        /// </summary>
        [CollectionBound(MaxWarnings, Reason = "First warnings only; WarningCount is the full count")]
        public List<string>? Warnings { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselDeleteResult()
        {
        }

        /// <summary>
        /// Build from the vessel id and every cleanup warning.
        /// </summary>
        /// <param name="vesselId">Deleted vessel identifier.</param>
        /// <param name="warnings">Every cleanup warning, or null.</param>
        /// <returns>The result with at most <see cref="MaxWarnings"/> warnings listed.</returns>
        public static VesselDeleteResult Create(string vesselId, List<string>? warnings)
        {
            VesselDeleteResult result = new VesselDeleteResult { VesselId = vesselId ?? String.Empty };
            if (warnings != null && warnings.Count > 0)
            {
                result.WarningCount = warnings.Count;
                result.Warnings = warnings.Count > MaxWarnings ? warnings.GetRange(0, MaxWarnings) : new List<string>(warnings);
            }

            return result;
        }

        #endregion
    }
}
