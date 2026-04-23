namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// A batch of related missions tracked together.
    /// </summary>
    public class Voyage
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
        /// Ordered list of playbooks selected for this voyage.
        /// </summary>
        public List<SelectedPlaybook> SelectedPlaybooks { get; set; } = new List<SelectedPlaybook>();

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
