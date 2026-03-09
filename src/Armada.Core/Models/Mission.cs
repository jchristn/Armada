namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// An atomic unit of work assigned to a captain.
    /// </summary>
    public class Mission
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
        /// Parent voyage identifier.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Target vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Assigned captain identifier.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Mission title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Title));
                _Title = value;
            }
        }

        /// <summary>
        /// Detailed mission description and instructions.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Current mission status.
        /// </summary>
        public MissionStatusEnum Status { get; set; } = MissionStatusEnum.Pending;

        /// <summary>
        /// Mission priority (lower is higher priority).
        /// </summary>
        public int Priority { get; set; } = 100;

        /// <summary>
        /// Parent mission identifier for sub-tasks.
        /// </summary>
        public string? ParentMissionId { get; set; } = null;

        /// <summary>
        /// Git branch name for this mission's work.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Assigned dock identifier for this mission's worktree.
        /// </summary>
        public string? DockId { get; set; } = null;

        /// <summary>
        /// Operating system process identifier for the agent working this mission.
        /// </summary>
        public int? ProcessId { get; set; } = null;

        /// <summary>
        /// Pull request URL if created.
        /// </summary>
        public string? PrUrl { get; set; } = null;

        /// <summary>
        /// Git commit hash (HEAD) captured when the mission completed.
        /// </summary>
        public string? CommitHash { get; set; } = null;

        /// <summary>
        /// Saved git diff snapshot captured at mission completion, before worktree reclamation.
        /// </summary>
        public string? DiffSnapshot { get; set; } = null;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when work started in UTC.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Timestamp when work completed in UTC.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.MissionIdPrefix, 24);
        private string _Title = "New Mission";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Mission()
        {
        }

        /// <summary>
        /// Instantiate with title.
        /// </summary>
        /// <param name="title">Mission title.</param>
        /// <param name="description">Mission description.</param>
        public Mission(string title, string? description = null)
        {
            Title = title;
            Description = description;
        }

        #endregion
    }
}
