namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;

    /// <summary>
    /// The Admiral's durable record of one captain launch delegated to a Harbor: what it was, where it ran, when it
    /// launched, started, produced its first output, and ended, and how it ended. Written by the connection manager as
    /// the Harbor reports the job's lifecycle; read by the Harbor metrics series.
    /// </summary>
    public class HarborJobRecord
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (hjb_ prefix).
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
        /// Job identifier the Admiral assigned to the launch (the <see cref="HarborLaunchRequest.JobId"/>).
        /// </summary>
        public string JobId { get; set; } = String.Empty;

        /// <summary>
        /// Harbor the job ran on.
        /// </summary>
        public string HarborId { get; set; } = String.Empty;

        /// <summary>
        /// Tenant of the Harbor's link, when known.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// What the launch was for.
        /// </summary>
        public HarborJobKindEnum Kind { get; set; } = HarborJobKindEnum.Unknown;

        /// <summary>
        /// Runtime name (an <see cref="AgentRuntimeEnum"/> member name, for example ClaudeCode).
        /// </summary>
        public string Runtime { get; set; } = String.Empty;

        /// <summary>
        /// Model passed to the runtime, or null for the runtime's default.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Mission the launch ran, when it was a mission.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Captain the launch ran as, when known.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// When the Admiral sent the launch (UTC).
        /// </summary>
        public DateTime LaunchedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the Harbor reported the process started (UTC), or null before then.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// When the Admiral received the job's first output (UTC), or null when it produced none.
        /// </summary>
        public DateTime? FirstOutputUtc { get; set; } = null;

        /// <summary>
        /// When the job ended (UTC), or null while it runs.
        /// </summary>
        public DateTime? EndedUtc { get; set; } = null;

        /// <summary>
        /// Time from launch to first output in milliseconds, as the Harbor measured it on its own clock (or, from a Harbor
        /// that does not report it, as the Admiral saw it), or null when the job produced no output.
        /// </summary>
        public long? TimeToFirstOutputMs { get; set; } = null;

        /// <summary>
        /// Total runtime in milliseconds, as the Harbor measured it (or, from a Harbor that does not report it, as the
        /// Admiral saw it), or null while the job runs.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Process exit code, or null when the job did not exit normally (it is running, could not launch, or was lost).
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// How the job ended.
        /// </summary>
        public HarborJobOutcomeEnum Outcome { get; set; } = HarborJobOutcomeEnum.Running;

        /// <summary>
        /// Whether the Admiral asked the Harbor to stop the job.
        /// </summary>
        public bool StopRequested { get; set; } = false;

        /// <summary>
        /// Creation timestamp (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp (UTC).
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.HarborJobIdPrefix, 24);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborJobRecord()
        {
        }

        #endregion
    }
}
