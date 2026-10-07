namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request to create (dispatch) a voyage of missions.
    /// </summary>
    public class VoyageCreateRequest
    {
        #region Public-Members

        /// <summary>
        /// Voyage title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Voyage description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Target vessel id, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Pipeline id, or null to inherit.
        /// </summary>
        public string? PipelineId { get; set; } = null;

        /// <summary>
        /// Pipeline name, or null.
        /// </summary>
        public string? Pipeline { get; set; } = null;

        /// <summary>
        /// Missions to dispatch. Never null.
        /// </summary>
        public List<DispatchRequest> Missions { get; set; } = new List<DispatchRequest>();

        /// <summary>
        /// Selected playbooks, or null.
        /// </summary>
        public List<Armada.Core.Models.SelectedPlaybook>? SelectedPlaybooks { get; set; } = null;

        /// <summary>
        /// Linked backlog item id, or null.
        /// </summary>
        public string? ObjectiveId { get; set; } = null;

        /// <summary>
        /// Captain assignments per pipeline step, or null.
        /// </summary>
        public List<Armada.Core.Models.CaptainAssignmentOverride>? CaptainAssignments { get; set; } = null;

        /// <summary>
        /// Optional landing mode for this voyage's missions; null inherits the vessel's, then the global default.
        /// </summary>
        public Armada.Core.Enums.LandingModeEnum? LandingMode { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VoyageCreateRequest()
        {
        }

        #endregion
    }
}
