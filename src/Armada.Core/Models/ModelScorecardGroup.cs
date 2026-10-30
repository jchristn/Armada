namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The scorecard of one revision of a model profile, or of revisions an administrator marked compatible (pooled).
    /// Revisions that are not marked compatible are always separate groups. Outcomes that match no recorded revision form
    /// a group with no revision IDs.
    /// </summary>
    public class ModelScorecardGroup
    {
        #region Public-Members

        /// <summary>
        /// Revisions in the group (mdv_ IDs), oldest first. Empty for outcomes that match no recorded revision.
        /// </summary>
        public List<string> RevisionIds
        {
            get => _RevisionIds;
            set => _RevisionIds = value ?? new List<string>();
        }

        /// <summary>
        /// True when the group pools more than one revision because they are marked compatible.
        /// </summary>
        public bool Pooled { get; set; } = false;

        /// <summary>
        /// Note about the group, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Missions in the window with at least one counted attempt in this group.
        /// </summary>
        public int Missions { get; set; } = 0;

        /// <summary>
        /// Counted attempts (stage outcomes) in this group.
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// The metrics.
        /// </summary>
        public List<ModelScorecardMetric> Metrics
        {
            get => _Metrics;
            set => _Metrics = value ?? new List<ModelScorecardMetric>();
        }

        #endregion

        #region Private-Members

        private List<string> _RevisionIds = new List<string>();
        private List<ModelScorecardMetric> _Metrics = new List<ModelScorecardMetric>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelScorecardGroup()
        {
        }

        #endregion
    }
}
