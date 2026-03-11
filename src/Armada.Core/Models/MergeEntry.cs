namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// An entry in the merge queue representing a branch to be tested and merged.
    /// </summary>
    public class MergeEntry
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Mission identifier this merge entry belongs to.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Branch name to merge.
        /// </summary>
        public string BranchName
        {
            get => _BranchName;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(BranchName));
                _BranchName = value;
            }
        }

        /// <summary>
        /// Target branch to merge into (e.g., "main").
        /// </summary>
        public string TargetBranch { get; set; } = "main";

        /// <summary>
        /// Current status of this merge entry.
        /// </summary>
        public MergeStatusEnum Status { get; set; } = MergeStatusEnum.Queued;

        /// <summary>
        /// Priority in the queue (lower = higher priority).
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// Batch identifier when this entry is being tested as part of a batch.
        /// </summary>
        public string? BatchId { get; set; } = null;

        /// <summary>
        /// Test command to run for verification.
        /// </summary>
        public string? TestCommand { get; set; } = null;

        /// <summary>
        /// Test output or error message.
        /// </summary>
        public string? TestOutput { get; set; } = null;

        /// <summary>
        /// Test exit code.
        /// </summary>
        public int? TestExitCode { get; set; } = null;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when tests started.
        /// </summary>
        public DateTime? TestStartedUtc { get; set; } = null;

        /// <summary>
        /// Timestamp when the entry was landed or failed.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable("mrg_", 24);
        private string _BranchName = "unknown";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MergeEntry()
        {
        }

        /// <summary>
        /// Instantiate with branch name.
        /// </summary>
        /// <param name="branchName">Branch to merge.</param>
        /// <param name="targetBranch">Target branch.</param>
        public MergeEntry(string branchName, string targetBranch = "main")
        {
            BranchName = branchName;
            TargetBranch = targetBranch;
        }

        #endregion
    }
}
