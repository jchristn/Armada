namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A fleet action run target without its captured output, rendered text, or error text. Length hints let a
    /// client decide whether to fetch the full target.
    /// </summary>
    public class FleetActionRunTargetSummary
    {
        #region Public-Members

        /// <summary>
        /// Target identifier (fat_ prefix).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Run identifier.
        /// </summary>
        public string? RunId { get; set; } = null;

        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = String.Empty;

        /// <summary>
        /// Vessel name snapshot.
        /// </summary>
        public string VesselName { get; set; } = String.Empty;

        /// <summary>
        /// Target status.
        /// </summary>
        public FleetActionTargetStatusEnum Status { get; set; } = FleetActionTargetStatusEnum.Pending;

        /// <summary>
        /// Stable skip reason code, or null.
        /// </summary>
        public string? SkipReason { get; set; } = null;

        /// <summary>
        /// Stable failure reason code, or null.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// Exit code (Command kind), or null.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Whether output or error text was truncated.
        /// </summary>
        public bool OutputTruncated { get; set; } = false;

        /// <summary>
        /// Length in characters of the captured standard output (0 when none).
        /// </summary>
        public int OutputLength { get; set; } = 0;

        /// <summary>
        /// Length in characters of the captured standard error or error detail (0 when none).
        /// </summary>
        public int ErrorLength { get; set; } = 0;

        /// <summary>
        /// Length in characters of the rendered command or prompt (0 when not rendered yet).
        /// </summary>
        public int RenderedLength { get; set; } = 0;

        /// <summary>
        /// Dispatched voyage identifier (Mission kind), or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Start timestamp in UTC, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Completion timestamp in UTC, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Duration in milliseconds, or null.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionRunTargetSummary()
        {
        }

        /// <summary>
        /// Build from a full target.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> is null.</exception>
        public static FleetActionRunTargetSummary FromTarget(FleetActionRunTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return new FleetActionRunTargetSummary
            {
                Id = target.Id,
                RunId = target.RunId,
                VesselId = target.VesselId,
                VesselName = target.VesselName,
                Status = target.Status,
                SkipReason = target.SkipReason,
                FailureReason = target.FailureReason,
                ExitCode = target.ExitCode,
                OutputTruncated = target.OutputTruncated,
                OutputLength = target.OutputText?.Length ?? 0,
                ErrorLength = target.ErrorText?.Length ?? 0,
                RenderedLength = target.RenderedText?.Length ?? 0,
                VoyageId = target.VoyageId,
                StartedUtc = target.StartedUtc,
                CompletedUtc = target.CompletedUtc,
                DurationMs = target.DurationMs,
                CreatedUtc = target.CreatedUtc,
                LastUpdateUtc = target.LastUpdateUtc
            };
        }

        #endregion
    }
}
