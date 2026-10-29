namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A mission in a voyage cascade graph.
    /// </summary>
    public class BlackBoxCascadeNode
    {
        #region Public-Members

        /// <summary>
        /// Mission (msn_ prefix).
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public string Status { get; set; } = "";

        /// <summary>
        /// Failure kind, or null.
        /// </summary>
        public string? FailureKind { get; set; } = null;

        /// <summary>
        /// The mission's current black box entry, or null.
        /// </summary>
        public string? EntryId { get; set; } = null;

        /// <summary>
        /// Its dependency, or null.
        /// </summary>
        public string? DependsOnMissionId { get; set; } = null;

        /// <summary>
        /// True for the cascade root (the first failure the others follow from).
        /// </summary>
        public bool IsRoot { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxCascadeNode()
        {
        }

        #endregion
    }
}
