namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Body of POST /api/v1/vessels/{id}/overlap-preview and the arguments of preview_mission_overlap: the mission a
    /// caller is about to dispatch.
    /// </summary>
    public class MissionOverlapPreviewRequest
    {
        #region Public-Members

        /// <summary>
        /// Mission title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Mission brief (scoped-files directives and paths in it are read).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// The dependency the caller already set, or null; the preview suggests none when it is set.
        /// </summary>
        public string? DependsOnMissionId { get; set; } = null;

        /// <summary>
        /// An active mission to leave out of the comparison (the mission itself when re-checking it), or null.
        /// </summary>
        public string? ExcludeMissionId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionOverlapPreviewRequest()
        {
        }

        #endregion
    }
}
