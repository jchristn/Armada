namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One mission row of a work card.
    /// </summary>
    public class AskWorkMissionSnapshot
    {
        #region Public-Members

        /// <summary>
        /// Mission identifier (msn_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? String.Empty;
        }

        /// <summary>
        /// Mission title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set => _Title = value ?? String.Empty;
        }

        /// <summary>
        /// Mission status.
        /// </summary>
        public string Status
        {
            get => _Status;
            set => _Status = value ?? String.Empty;
        }

        /// <summary>
        /// Voyage (vyg_ prefix), or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Vessel (vsl_ prefix), or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Persona / pipeline stage, or null.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Assigned captain, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Assigned captain's name, or null.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Pipeline stage (the mission's persona), or null.
        /// </summary>
        public string? PipelineStage { get; set; } = null;

        /// <summary>
        /// Working branch, or null.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Latest check run for the mission (chk_ prefix), or null.
        /// </summary>
        public string? CheckRunId { get; set; } = null;

        /// <summary>
        /// Status of the latest check run for the mission, or null.
        /// </summary>
        public string? CheckRunStatus { get; set; } = null;

        /// <summary>
        /// Merge-queue entry of the mission, or null.
        /// </summary>
        public string? MergeEntryId { get; set; } = null;

        /// <summary>
        /// Status of the mission's merge-queue entry, or null.
        /// </summary>
        public string? MergeQueueStatus { get; set; } = null;

        /// <summary>
        /// Alias of MergeQueueStatus, or null.
        /// </summary>
        public string? MergeStatus { get; set; } = null;

        /// <summary>
        /// Pull request URL, or null.
        /// </summary>
        public string? PrUrl { get; set; } = null;

        /// <summary>
        /// Landed, PullRequestOpen, LandingFailed, or null while not landed.
        /// </summary>
        public string? LandingOutcome { get; set; } = null;

        /// <summary>
        /// Failure reason, or null.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// UTC start time, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// UTC completion time, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = String.Empty;
        private string _Title = String.Empty;
        private string _Status = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskWorkMissionSnapshot()
        {
        }

        #endregion
    }
}
