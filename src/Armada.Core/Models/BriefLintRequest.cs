namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Body of POST /api/v1/missions/lint-brief and the arguments of lint_mission_brief.
    /// </summary>
    public class BriefLintRequest
    {
        #region Public-Members

        /// <summary>
        /// Mission title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Mission brief.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Vessel (vsl_ prefix) for the paths-outside-the-vessel check, or null to skip it.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// The dependency the caller set, or null.
        /// </summary>
        public string? DependsOnMissionId { get; set; } = null;

        /// <summary>
        /// The voyage the mission belongs to, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BriefLintRequest()
        {
        }

        #endregion
    }
}
