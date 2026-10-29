namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A voyage's cascade graph: its missions (status and black box entry) and the edges between them (DependsOn,
    /// CancelledBecause, BlockedBy, Sibling), at most 200 missions.
    /// </summary>
    public class BlackBoxCascadeGraph
    {
        #region Public-Members

        /// <summary>
        /// Voyage (vyg_ prefix).
        /// </summary>
        public string VoyageId { get; set; } = "";

        /// <summary>
        /// Voyage title.
        /// </summary>
        public string? VoyageTitle { get; set; } = null;

        /// <summary>
        /// Voyage status.
        /// </summary>
        public string VoyageStatus { get; set; } = "";

        /// <summary>
        /// The voyage's current black box entry, or null.
        /// </summary>
        public string? EntryId { get; set; } = null;

        /// <summary>
        /// The cascade root: the earliest failed mission that others depend on (or the earliest failure), or null.
        /// </summary>
        public string? RootMissionId { get; set; } = null;

        /// <summary>
        /// The root mission's black box entry, or null.
        /// </summary>
        public string? RootEntryId { get; set; } = null;

        /// <summary>
        /// Missions, oldest first, at most 200.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxCascadeNodes, Reason = "At most 200 missions; MissionCount has the total")]
        public List<BlackBoxCascadeNode> Nodes
        {
            get => _Nodes;
            set => _Nodes = value ?? new List<BlackBoxCascadeNode>();
        }

        /// <summary>
        /// Edges between the listed missions, at most 1000.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxCascadeEdges, Reason = "Edges between the listed nodes, at most 1000")]
        public List<BlackBoxCascadeEdge> Edges
        {
            get => _Edges;
            set => _Edges = value ?? new List<BlackBoxCascadeEdge>();
        }

        /// <summary>
        /// Missions in the voyage.
        /// </summary>
        public long MissionCount { get; set; } = 0;

        /// <summary>
        /// True when the voyage has more than 200 missions or the edges were cut at 1000.
        /// </summary>
        public bool Truncated { get; set; } = false;

        #endregion

        #region Private-Members

        private List<BlackBoxCascadeNode> _Nodes = new List<BlackBoxCascadeNode>();
        private List<BlackBoxCascadeEdge> _Edges = new List<BlackBoxCascadeEdge>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxCascadeGraph()
        {
        }

        #endregion
    }
}
