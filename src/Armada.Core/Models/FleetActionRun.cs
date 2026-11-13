namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One invocation of a fleet action over a set of target vessels. The run stores a snapshot of the
    /// definition it was started from (so editing the action later does not rewrite history) together with status and
    /// target counts. An ad hoc run has a null <see cref="ActionId"/>.
    /// </summary>
    public class FleetActionRun
    {
        #region Public-Members

        /// <summary>Optional Windows shell command; falls back to CommandText.</summary>
        public string? CommandWindows { get; set; }
        /// <summary>Registered output parser identifier.</summary>
        public string? OutputParser { get; set; }
        /// <summary>Source action built-in key (or ID) for {{scan.summary}}.</summary>
        public string? ScanSourceActionKey { get; set; }
        /// <summary>
        /// Optional applicability check: a shell command (POSIX; Windows uses <see cref="ApplicabilityCommandWindows"/>
        /// when set) rendered with the same variables and run in each vessel's working directory before the target
        /// executes or dispatches. A non-zero exit skips the vessel with reason NotApplicable, so a run touches only the
        /// vessels the action applies to (for example, the vessels that reference a package). A vessel without a working
        /// directory is not checked.
        /// </summary>
        public string? ApplicabilityCommand { get; set; } = null;
        /// <summary>Optional Windows (PowerShell) applicability check; falls back to ApplicabilityCommand.</summary>
        public string? ApplicabilityCommandWindows { get; set; } = null;
        /// <summary>
        /// Mission mode for Mission actions. Audit and Research are report-only: each mission runs read-only on a
        /// Worker-only pipeline with landing mode None, and the captain's report is recorded on the run target.
        /// </summary>
        public MissionModeEnum MissionMode { get; set; } = MissionModeEnum.Implementation;
        /// <summary>Executable names required on the target host PATH.</summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public System.Collections.Generic.List<string> RequiredTools { get => _RequiredTools; set => _RequiredTools = value ?? new(); }
        private System.Collections.Generic.List<string> _RequiredTools = new();
        /// <summary>Whether execution requires network access.</summary>
        public bool RequiresNetwork { get; set; }
        /// <summary>Validated runtime values, snapshotted at dispatch.</summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public System.Collections.Generic.List<FleetActionInput> Inputs { get; set; } = new();
        /// <summary>Validated runtime input values.</summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "One per declared input")]
        public System.Collections.Generic.Dictionary<string, string> InputValues { get => _InputValues; set => _InputValues = value ?? new(); }
        private System.Collections.Generic.Dictionary<string, string> _InputValues = new();
        /// <summary>Targets with parsed findings, counted separately from failures.</summary>
        public int FindingsCount { get; set; }


        /// <summary>Per-run tool permission and mission review approval choices.</summary>
        public Armada.Core.Models.RunApprovalOptions? ApprovalOptions { get; set; } = null;

        /// <summary>
        /// Unique identifier (far_ prefix). Defaults to a new identifier; a create backfills an empty value.
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
        /// Identifier of the user who started the run.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Source fleet action identifier, or null for an ad hoc run.
        /// </summary>
        public string? ActionId { get; set; } = null;

        /// <summary>
        /// Snapshot of the action name. Never null.
        /// </summary>
        public string ActionName
        {
            get => _ActionName;
            set => _ActionName = value ?? String.Empty;
        }

        /// <summary>
        /// Snapshot of the action kind. Defaults to Command.
        /// </summary>
        public FleetActionKindEnum Kind { get; set; } = FleetActionKindEnum.Command;

        /// <summary>
        /// Snapshot of the command template (Command kind).
        /// </summary>
        public string? CommandText { get; set; } = null;

        /// <summary>
        /// Snapshot of the prompt template (Mission kind).
        /// </summary>
        public string? PromptTemplate { get; set; } = null;

        /// <summary>
        /// Snapshot of the pipeline identifier (Mission kind).
        /// </summary>
        public string? PipelineId { get; set; } = null;

        /// <summary>
        /// Snapshot of the persona name (Mission kind).
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Per-target command timeout in seconds. Default 300, minimum 5, maximum 7200; out-of-range values are clamped.
        /// </summary>
        public int TimeoutSeconds
        {
            get => _TimeoutSeconds;
            set => _TimeoutSeconds = value < 5 ? 5 : (value > 7200 ? 7200 : value);
        }

        /// <summary>
        /// Whether targets with a dirty working tree are skipped. Default true.
        /// </summary>
        public bool RequiresCleanWorkingTree { get; set; } = true;

        /// <summary>
        /// Maximum number of targets processed (or voyages active) at once. Default 4, minimum 1, maximum 32; out-of-range values are clamped.
        /// </summary>
        public int Concurrency
        {
            get => _Concurrency;
            set => _Concurrency = value < 1 ? 1 : (value > 32 ? 32 : value);
        }

        /// <summary>
        /// Run status. Defaults to Pending.
        /// </summary>
        public FleetActionRunStatusEnum Status { get; set; } = FleetActionRunStatusEnum.Pending;

        /// <summary>
        /// Total number of targets. Minimum 0; negative values are clamped to 0.
        /// </summary>
        public int TargetCount
        {
            get => _TargetCount;
            set => _TargetCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of targets that succeeded. Minimum 0; negative values are clamped to 0.
        /// </summary>
        public int SucceededCount
        {
            get => _SucceededCount;
            set => _SucceededCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of targets that failed or timed out. Minimum 0; negative values are clamped to 0.
        /// </summary>
        public int FailedCount
        {
            get => _FailedCount;
            set => _FailedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of targets that were skipped. Minimum 0; negative values are clamped to 0.
        /// </summary>
        public int SkippedCount
        {
            get => _SkippedCount;
            set => _SkippedCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Number of targets that were cancelled. Minimum 0; negative values are clamped to 0.
        /// </summary>
        public int CancelledCount
        {
            get => _CancelledCount;
            set => _CancelledCount = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Timestamp in UTC when the run started, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Timestamp in UTC when the run finished, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.FleetActionRunIdPrefix, 24);
        private string _ActionName = String.Empty;
        private int _TimeoutSeconds = 300;
        private int _Concurrency = 4;
        private int _TargetCount = 0;
        private int _SucceededCount = 0;
        private int _FailedCount = 0;
        private int _SkippedCount = 0;
        private int _CancelledCount = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionRun()
        {
        }

        #endregion
    }
}
