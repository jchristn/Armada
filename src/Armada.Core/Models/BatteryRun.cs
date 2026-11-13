namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One run of the fixture battery against a captain: the packs, the run count, the seed, the sampling
    /// settings, the battery version and the full configuration revision, plus outcome counts (the per-task results are a
    /// separate paged enumeration). Battery missions are
    /// evaluation work: their stage outcomes are tagged IsEvaluation and never count in production stage outcomes or
    /// scorecards (docs/MODEL_HETEROGENEITY.md, Part 2).
    /// </summary>
    public class BatteryRun
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (bat_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant that started the run.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User that started the run.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Captain that ran the tasks (cpt_ prefix).
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Captain name at the time of the run.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Packs run (identifiers).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Packs
        {
            get => _Packs;
            set => _Packs = value ?? new List<string>();
        }

        /// <summary>
        /// Battery version: each pack with its version, for example csharp@1.
        /// </summary>
        public string BatteryVersion
        {
            get => _BatteryVersion;
            set => _BatteryVersion = value ?? String.Empty;
        }

        /// <summary>
        /// Runs of each task.
        /// </summary>
        public int RunCount { get; set; } = 3;

        /// <summary>
        /// Status.
        /// </summary>
        public BatteryRunStatusEnum Status { get; set; } = BatteryRunStatusEnum.Queued;

        /// <summary>
        /// Always true: battery runs never count in production stage outcomes or scorecards.
        /// </summary>
        public bool IsEvaluation { get; set; } = true;

        /// <summary>
        /// Pipeline the missions ran under.
        /// </summary>
        public string Pipeline
        {
            get => _Pipeline;
            set => _Pipeline = value ?? "ModelEvaluation";
        }

        /// <summary>
        /// Seed recorded with the run.
        /// </summary>
        public int? Seed { get; set; } = null;

        /// <summary>
        /// Sampling settings in effect (temperature, top_p, max tokens, context length, reasoning effort) as JSON.
        /// </summary>
        public string? SamplingJson { get; set; } = null;

        /// <summary>
        /// SHA-256 (first 16 hex digits) of ConfigurationJson.
        /// </summary>
        public string? ConfigurationRevision { get; set; } = null;

        /// <summary>
        /// The full configuration the run used: Armada version, captain runtime and model, endpoint and its
        /// inference options, pipeline, and each pack's version and manifest hash.
        /// </summary>
        public string? ConfigurationJson { get; set; } = null;

        /// <summary>
        /// Captain runtime.
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Model endpoint (mep_ prefix), or null.
        /// </summary>
        public string? ModelEndpointId { get; set; } = null;

        /// <summary>
        /// Inference host key the evaluation concurrency limit applied to.
        /// </summary>
        public string? InferenceHost { get; set; } = null;

        /// <summary>
        /// Task attempts planned (tasks times run count).
        /// </summary>
        public int TotalTasks { get; set; } = 0;

        /// <summary>
        /// Attempts that passed.
        /// </summary>
        public int PassedCount { get; set; } = 0;

        /// <summary>
        /// Attempts that failed.
        /// </summary>
        public int FailedCount { get; set; } = 0;

        /// <summary>
        /// Attempts that could not run.
        /// </summary>
        public int CouldNotRunCount { get; set; } = 0;

        /// <summary>
        /// Attempts waiting for the evaluator.
        /// </summary>
        public int PendingEvaluationCount { get; set; } = 0;

        /// <summary>
        /// Why the run failed, or null.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Started (UTC).
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Completed (UTC).
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Last update (UTC).
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Per-task results, filled only in-process by <c>BatteryRunService.WaitAsync</c> and <c>ReadAsync</c>. Never
        /// serialized: a run read over REST or MCP does not embed its results (docs/ENUMERATION.md); page them with
        /// <c>GET /api/v1/battery/runs/{id}/results</c> (or <c>POST .../results/enumerate</c>, MCP
        /// <c>list_battery_results</c>).
        /// </summary>
        [JsonIgnore]
        public List<BatteryTaskResult> Results
        {
            get => _Results;
            set => _Results = value ?? new List<BatteryTaskResult>();
        }

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.BatteryRunIdPrefix, 24);
        private List<string> _Packs = new List<string>();
        private string _BatteryVersion = String.Empty;
        private string _Pipeline = "ModelEvaluation";
        private List<BatteryTaskResult> _Results = new List<BatteryTaskResult>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatteryRun()
        {
        }

        #endregion
    }
}
