namespace Armada.Core.Models
{
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Repeated reports of the same friction, collapsed into one row. One captain reporting a problem
    /// is an anecdote. Nine captains reporting it is a defect with evidence, and the count is the part
    /// an operator triages on.
    /// </summary>
    public class PapercutGroup
    {
        #region Public-Members

        /// <summary>
        /// Grouping key: vessel, category, and normalized title.
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// Vessel the friction was reported against, or null when the report carried no vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Friction category.
        /// </summary>
        public PapercutCategoryEnum Category { get; set; } = PapercutCategoryEnum.Other;

        /// <summary>
        /// Highest severity reported in the group.
        /// </summary>
        public PapercutSeverityEnum HighestSeverity { get; set; } = PapercutSeverityEnum.Low;

        /// <summary>
        /// Title of the most recent report in the group.
        /// </summary>
        public string SampleTitle { get; set; } = "";

        /// <summary>
        /// Detail of the most recent report in the group.
        /// </summary>
        public string? SampleDetail { get; set; } = null;

        /// <summary>
        /// Path of the most recent report in the group that carried one.
        /// </summary>
        public string? SamplePath { get; set; } = null;

        /// <summary>
        /// Number of reports in the group.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Number of distinct captains that reported it. A high count from one captain is a habit;
        /// a high count across captains is a real defect.
        /// </summary>
        public int DistinctCaptainCount { get; set; } = 0;

        /// <summary>
        /// Earliest report timestamp in UTC.
        /// </summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Latest report timestamp in UTC.
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Up to five mission identifiers that reported it, newest first, for evidence links.
        /// </summary>
        [CollectionBound(PapercutService.MaxSampleMissions, Reason = "PapercutService.Group keeps at most MaxSampleMissions ids per group")]
        public List<string> SampleMissionIds
        {
            get => _SampleMissionIds;
            set => _SampleMissionIds = value ?? new List<string>();
        }

        #endregion

        #region Private-Members

        private List<string> _SampleMissionIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PapercutGroup()
        {
        }

        #endregion
    }
}
