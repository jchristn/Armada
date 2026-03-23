namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// A worker agent instance executing missions.
    /// </summary>
    public class Captain
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
        /// Captain name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Agent runtime type.
        /// </summary>
        public AgentRuntimeEnum Runtime { get; set; } = AgentRuntimeEnum.ClaudeCode;

        /// <summary>
        /// User-supplied system instructions for this captain. Injected into every mission's
        /// instructions before vessel context and mission details. Use this to specialize
        /// captain behavior, add guardrails, or provide persistent context.
        /// </summary>
        public string? SystemInstructions { get; set; } = null;

        /// <summary>
        /// Current state of the captain.
        /// </summary>
        public CaptainStateEnum State { get; set; } = CaptainStateEnum.Idle;

        /// <summary>
        /// Currently assigned mission identifier.
        /// </summary>
        public string? CurrentMissionId { get; set; } = null;

        /// <summary>
        /// Currently assigned dock identifier.
        /// </summary>
        public string? CurrentDockId { get; set; } = null;

        /// <summary>
        /// Operating system process identifier.
        /// </summary>
        public int? ProcessId { get; set; } = null;

        /// <summary>
        /// Number of auto-recovery attempts for the current mission.
        /// </summary>
        public int RecoveryAttempts { get; set; } = 0;

        /// <summary>
        /// Last heartbeat timestamp in UTC.
        /// </summary>
        public DateTime? LastHeartbeatUtc { get; set; } = null;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.CaptainIdPrefix, 24);
        private string _Name = "Captain";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Captain()
        {
        }

        /// <summary>
        /// Instantiate with name and runtime.
        /// </summary>
        /// <param name="name">Captain name.</param>
        /// <param name="runtime">Agent runtime type.</param>
        public Captain(string name, AgentRuntimeEnum runtime = AgentRuntimeEnum.ClaudeCode)
        {
            Name = name;
            Runtime = runtime;
        }

        #endregion
    }
}
