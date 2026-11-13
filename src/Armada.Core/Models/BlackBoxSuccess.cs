namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A minimal record of a successful landing kept by the success flight recorder for blackBox.successRetentionDays,
    /// so a failure can be compared with nearby successes. Stored in blackbox_successes.
    /// </summary>
    public class BlackBoxSuccess
    {
        #region Public-Members

        /// <summary>
        /// Id (bbs_ prefix).
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owner of the mission.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Mission that landed.
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Voyage, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Captain, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Mission title (clipped to 500 characters).
        /// </summary>
        public string? MissionTitle { get; set; } = null;

        /// <summary>
        /// Commit the mission started from (Mission.StartCommit), or null.
        /// </summary>
        public string? BaseCommit { get; set; } = null;

        /// <summary>
        /// Commit that landed on the target (Mission.CommitHash), or null.
        /// </summary>
        public string? TargetCommit { get; set; } = null;

        /// <summary>
        /// Target branch, or null.
        /// </summary>
        public string? TargetBranch { get; set; } = null;

        /// <summary>
        /// Files the mission changed (from its diff), first 200.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxSuccessFiles, Reason = "First 200 changed files; FileCount has the total")]
        public List<string> FilesChanged
        {
            get => _FilesChanged;
            set => _FilesChanged = value ?? new List<string>();
        }

        /// <summary>
        /// How many files the mission changed.
        /// </summary>
        public int FileCount { get; set; } = 0;

        /// <summary>
        /// When the mission was created.
        /// </summary>
        public DateTime? MissionCreatedUtc { get; set; } = null;

        /// <summary>
        /// When the mission started.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// When it landed (completed).
        /// </summary>
        public DateTime LandedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Milliseconds from creation to start, or null.
        /// </summary>
        public long? QueueMs { get; set; } = null;

        /// <summary>
        /// Milliseconds from start to landing, or null.
        /// </summary>
        public long? RunMs { get; set; } = null;

        /// <summary>
        /// Milliseconds from creation to landing, or null.
        /// </summary>
        public long? TotalMs { get; set; } = null;

        /// <summary>
        /// When the record was written.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the record expires.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.BlackBoxSuccessIdPrefix, 24);
        private List<string> _FilesChanged = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxSuccess()
        {
        }

        #endregion
    }
}
