namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// One target vessel within a <see cref="FleetActionRun"/>. Targets are history: they keep
    /// <see cref="VesselName"/> so they remain meaningful after the vessel is deleted.
    /// <see cref="OutputText"/> and <see cref="ErrorText"/> are deliberately unmanaged text columns (captured process
    /// output, truncated to FleetActions.MaxOutputBytes), per the Structured Persistence rule: the application never
    /// queries inside them, and every status, code, and number is a separate typed column.
    /// </summary>
    public class FleetActionRunTarget
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (fat_ prefix). Defaults to a new identifier; a create backfills an empty value.
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? String.Empty;
        }

        /// <summary>
        /// Owning tenant identifier. Every row carries a tenant; reads and enumerations are scoped by it.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning run identifier (far_ prefix).
        /// </summary>
        public string? RunId { get; set; } = null;

        /// <summary>
        /// Target vessel identifier. Never null.
        /// </summary>
        public string VesselId
        {
            get => _VesselId;
            set => _VesselId = value ?? String.Empty;
        }

        /// <summary>
        /// Snapshot of the target vessel name. Never null.
        /// </summary>
        public string VesselName
        {
            get => _VesselName;
            set => _VesselName = value ?? String.Empty;
        }

        /// <summary>
        /// Target status. Defaults to Pending.
        /// </summary>
        public FleetActionTargetStatusEnum Status { get; set; } = FleetActionTargetStatusEnum.Pending;

        /// <summary>
        /// Stable skip reason code (for example DirtyTree, NoWorkingDirectory, DispatchRejected, NotAuthorized), or null.
        /// </summary>
        public string? SkipReason { get; set; } = null;

        /// <summary>
        /// Stable failure reason code (for example Interrupted), or null.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// The rendered command (Command kind) or prompt (Mission kind) for this target.
        /// </summary>
        public string? RenderedText { get; set; } = null;

        /// <summary>
        /// Process exit code (Command kind), or null.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Captured standard output (Command kind). Unmanaged text: never queried or parsed by the application.
        /// </summary>
        public string? OutputText { get; set; } = null;

        /// <summary>
        /// Captured standard error (Command kind). Unmanaged text: never queried or parsed by the application.
        /// </summary>
        public string? ErrorText { get; set; } = null;

        /// <summary>
        /// Whether output or error text was truncated to the configured maximum.
        /// </summary>
        public bool OutputTruncated { get; set; } = false;

        /// <summary>
        /// Dispatched voyage identifier (Mission kind), or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Timestamp in UTC when the target started, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Timestamp in UTC when the target finished, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Execution duration in milliseconds. Null when unknown; negative values are clamped to 0.
        /// </summary>
        public long? DurationMs
        {
            get => _DurationMs;
            set => _DurationMs = value.HasValue && value.Value < 0 ? 0 : value;
        }

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.FleetActionRunTargetIdPrefix, 24);
        private string _VesselId = String.Empty;
        private string _VesselName = String.Empty;
        private long? _DurationMs = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionRunTarget()
        {
        }

        #endregion
    }
}
