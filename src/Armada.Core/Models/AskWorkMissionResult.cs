namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The outcome of one mission of finished tracked work (see <see cref="AskWorkResult"/>).
    /// </summary>
    public class AskWorkMissionResult
    {
        #region Public-Members

        /// <summary>
        /// Mission identifier.
        /// </summary>
        public string MissionId
        {
            get => _MissionId;
            set => _MissionId = value ?? String.Empty;
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
        /// Mission status (MissionStatusEnum name).
        /// </summary>
        public string Status
        {
            get => _Status;
            set => _Status = value ?? String.Empty;
        }

        /// <summary>
        /// Name of the vessel the mission ran on, or null.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// Name of the captain that ran the mission, or null.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Failure reason, or null.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// The captain's final message for the mission (its last response, or the tail of its output when the runtime
        /// reports no final message), whitespace-trimmed and capped by the builder; null when none was recorded.
        /// </summary>
        public string? FinalMessage { get; set; } = null;

        /// <summary>
        /// Landing outcome (Landed, PullRequestMerged, PullRequestOpen, LandingFailed), or null.
        /// </summary>
        public string? LandingOutcome { get; set; } = null;

        /// <summary>
        /// Pull request URL, or null.
        /// </summary>
        public string? PrUrl { get; set; } = null;

        /// <summary>
        /// Branch name, or null.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Mission runtime in milliseconds (started to completed), or null.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Files changed in the captured diff, or null when no diff was captured.
        /// </summary>
        public int? FilesChanged { get; set; } = null;

        /// <summary>
        /// Lines added in the captured diff, or null when no diff was captured.
        /// </summary>
        public int? LinesAdded { get; set; } = null;

        /// <summary>
        /// Lines removed in the captured diff, or null when no diff was captured.
        /// </summary>
        public int? LinesRemoved { get; set; } = null;

        /// <summary>
        /// The mission's most recent check runs, newest first.
        /// </summary>
        public List<AskWorkCheckResult> Checks
        {
            get => _Checks;
            set => _Checks = value ?? new List<AskWorkCheckResult>();
        }

        #endregion

        #region Private-Members

        private string _MissionId = String.Empty;
        private string _Title = String.Empty;
        private string _Status = String.Empty;
        private List<AskWorkCheckResult> _Checks = new List<AskWorkCheckResult>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskWorkMissionResult()
        {
        }

        #endregion
    }
}
