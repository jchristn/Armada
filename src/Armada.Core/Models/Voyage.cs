namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A batch of related missions tracked together.
    /// </summary>
    public class Voyage
    {
        #region Public-Members

        /// <summary>Per-run tool permission and mission review approval choices.</summary>
        public Armada.Core.Models.RunApprovalOptions? ApprovalOptions { get; set; } = null;

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
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Voyage title.
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
        /// Voyage description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Current voyage status.
        /// </summary>
        public VoyageStatusEnum Status { get; set; } = VoyageStatusEnum.Open;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when voyage completed in UTC.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Per-voyage override for AutoPush. Null = use global setting.
        /// </summary>
        public bool? AutoPush { get; set; } = null;

        /// <summary>
        /// Per-voyage override for AutoCreatePullRequests. Null = use global setting.
        /// </summary>
        public bool? AutoCreatePullRequests { get; set; } = null;

        /// <summary>
        /// Per-voyage override for AutoMergePullRequests. Null = use global setting.
        /// </summary>
        public bool? AutoMergePullRequests { get; set; } = null;

        /// <summary>
        /// Per-voyage override for landing mode. Null = use vessel or global setting.
        /// When set, this takes precedence over per-voyage boolean overrides.
        /// </summary>
        public LandingModeEnum? LandingMode { get; set; } = null;

        /// <summary>
        /// Source planning session identifier, if this voyage originated from planning.
        /// </summary>
        public string? SourcePlanningSessionId { get; set; } = null;

        /// <summary>
        /// Source planning message identifier, if dispatch was created from a specific transcript entry.
        /// </summary>
        public string? SourcePlanningMessageId { get; set; } = null;

        /// <summary>
        /// Ordered list of playbooks selected for this voyage. At most <see cref="CollectionLimits.MaxConfigItems"/>;
        /// dispatch rejects more.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<SelectedPlaybook> SelectedPlaybooks { get; set; } = new List<SelectedPlaybook>();

        /// <summary>
        /// Serialized per-persona captain overrides selected at dispatch, as a JSON array of
        /// <see cref="CaptainAssignmentOverride"/>. Resolves the preferred captain and fallback tier for every
        /// mission of a given persona in this voyage, including fan-out missions created later. Null or empty
        /// means no per-voyage overrides (fall back to persona defaults / normal routing).
        /// </summary>
        public string? CaptainOverridesJson { get; set; } = null;

        /// <summary>
        /// True for evaluation work (fixture battery runs and other model evaluations). Set at dispatch; every mission of
        /// the voyage inherits it, so their stage outcomes are tagged IsEvaluation.
        /// </summary>
        public bool IsEvaluation { get; set; } = false;

        /// <summary>
        /// The voyage's current black box entry (bbx_ prefix), or null when it has none.
        /// </summary>
        public string? LastBlackBoxEntryId { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.VoyageIdPrefix, 24);
        private string _Title = "New Voyage";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Voyage()
        {
        }

        /// <summary>
        /// Instantiate with title.
        /// </summary>
        /// <param name="title">Voyage title.</param>
        /// <param name="description">Voyage description.</param>
        public Voyage(string title, string? description = null)
        {
            Title = title;
            Description = description;
        }

        #endregion
    }
}
