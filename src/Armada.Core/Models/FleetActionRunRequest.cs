namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Body for starting a fleet action run, either from a saved action or ad hoc with an inline definition.
    /// </summary>
    public class FleetActionRunRequest
    {
        #region Public-Members

        /// <summary>
        /// Target vessel identifiers in the caller's tenant. Required, 1 to 500 after de-duplication. A vessel from
        /// another tenant (or unknown) rejects the whole request.
        /// </summary>
        public List<string> VesselIds
        {
            get => _VesselIds;
            set => _VesselIds = value ?? new List<string>();
        }

        /// <summary>
        /// Optional concurrency for this run; defaults to the action's DefaultConcurrency; clamped 1 to 32.
        /// </summary>
        public int? Concurrency { get; set; } = null;

        /// <summary>
        /// Optional per-run overrides.
        /// </summary>
        public FleetActionRunOverrides? Overrides { get; set; } = null;

        /// <summary>
        /// Inline definition for an ad hoc run (POST /api/v1/fleet-actions/run). Ignored when running a saved action.
        /// </summary>
        public FleetActionUpsertRequest? Definition { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _VesselIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionRunRequest()
        {
        }

        #endregion
    }
}
