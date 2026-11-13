namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One probe-only (Quick) Evaluate of a captain or model endpoint: the configuration it ran with (provenance), the
    /// probe set, the evaluation budget, and the per-probe observations. An evaluation run, never a production stage: it
    /// writes no stage outcome and never counts in production scorecards (docs/MODEL_HETEROGENEITY.md, Part 2).
    /// </summary>
    public class ModelProbeRun
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (mpr_ prefix).
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
        /// What was asked to be probed.
        /// </summary>
        public ModelProbeTargetEnum TargetKind { get; set; } = ModelProbeTargetEnum.Captain;

        /// <summary>
        /// Captain probed (cpt_ prefix), or null for a model endpoint run.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Captain name at the time of the run, or null.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Model endpoint the run used (mep_ prefix), or null.
        /// </summary>
        public string? ModelEndpointId { get; set; } = null;

        /// <summary>
        /// Depth. Only Quick is implemented.
        /// </summary>
        public ModelProbeDepthEnum Depth { get; set; } = ModelProbeDepthEnum.Quick;

        /// <summary>
        /// Lifecycle status.
        /// </summary>
        public ModelProbeRunStatusEnum Status { get; set; } = ModelProbeRunStatusEnum.Queued;

        /// <summary>
        /// Always true: the run is an evaluation, excluded from production stage outcomes and scorecards.
        /// </summary>
        public bool IsEvaluation { get; set; } = true;

        /// <summary>
        /// Version of the probe set (prompts and scoring) the run used.
        /// </summary>
        public string ProbeSetVersion { get; set; } = "";

        /// <summary>
        /// Runtime the probes ran through (for example ClaudeCode, Codex, ApiEndpoint).
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Runtime version (CLI --version, or the Ollama server version), or null when not known.
        /// </summary>
        public string? RuntimeVersion { get; set; } = null;

        /// <summary>
        /// Model provider of the endpoint (for example Ollama), or null.
        /// </summary>
        public string? ModelProvider { get; set; } = null;

        /// <summary>
        /// Model the probes ran, or null for the runtime's default.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Model digest (Ollama), or null.
        /// </summary>
        public string? ModelDigest { get; set; } = null;

        /// <summary>
        /// Context length configured on the endpoint, or null.
        /// </summary>
        public int? ConfiguredContextTokens { get; set; } = null;

        /// <summary>
        /// Context length the endpoint was observed to serve, or null when not observed.
        /// </summary>
        public int? ServedContextTokens { get; set; } = null;

        /// <summary>
        /// Harbor the probes ran on, or null for the Admiral's host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Inference host key the per-host concurrency limit and budget apply to (for example the endpoint's host name,
        /// or vendor:ClaudeCode for a vendor CLI).
        /// </summary>
        public string? InferenceHost { get; set; } = null;

        /// <summary>
        /// Token budget the run was allowed (estimated tokens), at start.
        /// </summary>
        public long TokenBudget { get; set; } = 0;

        /// <summary>
        /// Estimated tokens the run used (prompt plus output characters divided by four).
        /// </summary>
        public long TokensUsed { get; set; } = 0;

        /// <summary>
        /// Probes that passed.
        /// </summary>
        public int PassedCount { get; set; } = 0;

        /// <summary>
        /// Probes that observed a problem.
        /// </summary>
        public int FailedCount { get; set; } = 0;

        /// <summary>
        /// Probes that could not run (with a reason each).
        /// </summary>
        public int CouldNotRunCount { get; set; } = 0;

        /// <summary>
        /// Why the run stopped early, or null.
        /// </summary>
        public string? FailureReason { get; set; } = null;

        /// <summary>
        /// When the run was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When probing started, or null while queued.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// When the run finished, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// When the row was last written.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Battery run a Full Evaluate started (bat_ prefix), or null.
        /// </summary>
        public string? BatteryRunId { get; set; } = null;

        /// <summary>
        /// The Full Evaluate report, or null for a Quick run (or a Full run that has not reached its report yet). A
        /// cancelled or timed-out Full run keeps a partial report with Complete false.
        /// </summary>
        public ModelEvaluationReport? Report { get; set; } = null;

        /// <summary>
        /// Per-probe results (filled on read by id; empty in list pages).
        /// </summary>
        [CollectionBound(ModelProbeCatalog.ProbeCount, Reason = "One result per probe; ModelProbeService runs the fixed six-probe set once per run")]
        public List<ModelProbeResult> Results
        {
            get => _Results;
            set => _Results = value ?? new List<ModelProbeResult>();
        }

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.ModelProbeRunIdPrefix, 24);
        private List<ModelProbeResult> _Results = new List<ModelProbeResult>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProbeRun()
        {
        }

        #endregion
    }
}
