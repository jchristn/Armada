namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Body for starting a fleet action run, either from a saved action or ad hoc with an inline definition.
    /// </summary>
    public class FleetActionRunRequest
    {
        #region Public-Members

        /// <summary>Runtime values keyed by the declared input name.</summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public Dictionary<string, string> InputValues { get => _InputValues; set => _InputValues = value ?? new(); }
        private Dictionary<string, string> _InputValues = new();

        /// <summary>
        /// Target vessel identifiers in the caller's tenant. Required unless <see cref="FromRunId"/> is set; 1 to the per-run maximum after
        /// de-duplication. A vessel from another tenant (or unknown) rejects the whole request.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems)]
        public List<string> VesselIds
        {
            get => _VesselIds;
            set => _VesselIds = value ?? new List<string>();
        }

        /// <summary>
        /// Follow-up run: the id of an earlier run in the caller's tenant (for example a scan). When set, the targets are
        /// that run's vessels whose target status is in <see cref="FromTargetStatuses"/>, and <see cref="VesselIds"/>
        /// must be empty. Pair a scan action with its fix action to dispatch captains only where the scan found something.
        /// </summary>
        public string? FromRunId { get; set; } = null;

        /// <summary>
        /// Target statuses selected from <see cref="FromRunId"/>. Defaults to Findings and Failed (a failed test run has
        /// no parsed findings but still needs a fix).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<FleetActionTargetStatusEnum>? FromTargetStatuses { get; set; } = null;

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
