namespace Armada.Core.Models
{
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A persistent planning conversation between a user and a captain.
    /// </summary>
    public class PlanningSession
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
        /// Reserved captain identifier.
        /// </summary>
        public string CaptainId { get; set; } = String.Empty;

        /// <summary>
        /// Target vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = String.Empty;

        /// <summary>
        /// Optional fleet identifier used for UX filtering.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Reserved dock identifier.
        /// </summary>
        public string? DockId { get; set; } = null;

        /// <summary>
        /// Branch name used by the planning dock.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Session title.
        /// </summary>
        public string Title { get; set; } = "Planning Session";

        /// <summary>
        /// Current session status.
        /// </summary>
        public PlanningSessionStatusEnum Status { get; set; } = PlanningSessionStatusEnum.Created;

        /// <summary>
        /// Optional pipeline identifier inherited by dispatches created from this session.
        /// </summary>
        public string? PipelineId { get; set; } = null;

        /// <summary>
        /// Ordered playbook selections attached to this planning session.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<SelectedPlaybook> SelectedPlaybooks { get; set; } = new List<SelectedPlaybook>();

        /// <summary>
        /// Current process identifier while a response is in flight.
        /// </summary>
        public int? ProcessId { get; set; } = null;

        /// <summary>
        /// Optional failure reason.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Timestamp when the session became active in UTC.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Timestamp when the session stopped in UTC.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.PlanningSessionIdPrefix, 24);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize selected playbooks for persistence.
        /// </summary>
        /// <returns>JSON payload.</returns>
        public string SerializeSelectedPlaybooks()
        {
            return JsonSerializer.Serialize(SelectedPlaybooks ?? new List<SelectedPlaybook>());
        }

        /// <summary>
        /// Load selected playbooks from persisted JSON.
        /// </summary>
        /// <param name="json">Serialized playbooks.</param>
        public void DeserializeSelectedPlaybooks(string? json)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                SelectedPlaybooks = new List<SelectedPlaybook>();
                return;
            }

            try
            {
                SelectedPlaybooks = JsonSerializer.Deserialize<List<SelectedPlaybook>>(json) ?? new List<SelectedPlaybook>();
            }
            catch
            {
                SelectedPlaybooks = new List<SelectedPlaybook>();
            }
        }

        #endregion
    }
}
