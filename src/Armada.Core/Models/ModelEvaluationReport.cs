namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Services;

    /// <summary>
    /// The report of a Full Evaluate (docs/MODEL_HETEROGENEITY.md, Evaluate (Full)): per-persona mechanical outcomes with
    /// counts from the Quick probes and the fixture battery (the source of truth), and the evaluator's narrative, which
    /// may cite only those counts. Stored on the run and on the advisory model profile revision it records. A run that
    /// was cancelled or timed out keeps a partial report with Complete false and the reason.
    /// </summary>
    public class ModelEvaluationReport
    {
        #region Public-Members

        /// <summary>
        /// Evaluate run (mpr_ prefix).
        /// </summary>
        public string RunId { get; set; } = "";

        /// <summary>
        /// False when the run was cancelled, timed out, ran out of budget or could not run the battery; the report then
        /// holds only what finished.
        /// </summary>
        public bool Complete { get; set; } = false;

        /// <summary>
        /// Why the report is incomplete, or null.
        /// </summary>
        public string? IncompleteReason { get; set; } = null;

        /// <summary>
        /// Model family of the candidate (for example qwen, claude, gpt), or null when it cannot be told.
        /// </summary>
        public string? CandidateFamily { get; set; } = null;

        /// <summary>
        /// Battery run (bat_ prefix), or null when the battery did not start.
        /// </summary>
        public string? BatteryRunId { get; set; } = null;

        /// <summary>
        /// Captain that ran the battery tasks (the candidate, or for a model endpoint a captain on that endpoint), or null.
        /// </summary>
        public string? BatteryCaptainId { get; set; } = null;

        /// <summary>
        /// Final status of the battery run (Completed, Failed, Cancelled), or null when it did not start.
        /// </summary>
        public string? BatteryStatus { get; set; } = null;

        /// <summary>
        /// Estimated tokens the battery spent (prompt plus output of each attempt).
        /// </summary>
        public long BatteryTokensUsed { get; set; } = 0;

        /// <summary>
        /// Packs run.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Packs
        {
            get => _Packs;
            set => _Packs = value ?? new List<string>();
        }

        /// <summary>
        /// Personas scored.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Personas
        {
            get => _Personas;
            set => _Personas = value ?? new List<string>();
        }

        /// <summary>
        /// Evaluator captain (cpt_ prefix), or null when none graded the run.
        /// </summary>
        public string? EvaluatorCaptainId { get; set; } = null;

        /// <summary>
        /// Evaluator captain name, or null.
        /// </summary>
        public string? EvaluatorCaptainName { get; set; } = null;

        /// <summary>
        /// Model family of the evaluator, or null.
        /// </summary>
        public string? EvaluatorFamily { get; set; } = null;

        /// <summary>
        /// True when the caller named the evaluator; false when Armada chose it.
        /// </summary>
        public bool EvaluatorExplicit { get; set; } = false;

        /// <summary>
        /// Warnings, for example a same-family evaluator.
        /// </summary>
        [CollectionBound(EvaluationLimits.MaxNotes)]
        public List<string> Warnings
        {
            get => _Warnings;
            set => _Warnings = value ?? new List<string>();
        }

        /// <summary>
        /// Notes on how the packs, personas and evaluator were chosen.
        /// </summary>
        [CollectionBound(EvaluationLimits.MaxNotes)]
        public List<string> Notes
        {
            get => _Notes;
            set => _Notes = value ?? new List<string>();
        }

        /// <summary>
        /// One count per Quick probe (key "probe:Kind").
        /// </summary>
        [CollectionBound(EvaluationLimits.MaxCounts, Reason = "One per probe kind")]
        public List<ModelEvaluationCount> Probes
        {
            get => _Probes;
            set => _Probes = value ?? new List<ModelEvaluationCount>();
        }

        /// <summary>
        /// Per-persona battery outcomes with counts.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "One per persona scored")]
        public List<ModelEvaluationPersonaOutcome> PersonaOutcomes
        {
            get => _PersonaOutcomes;
            set => _PersonaOutcomes = value ?? new List<ModelEvaluationPersonaOutcome>();
        }

        /// <summary>
        /// The evaluator's narrative (Accepted, Rejected with the reason, or Skipped with the reason).
        /// </summary>
        public ModelEvaluationNarrative Narrative
        {
            get => _Narrative;
            set => _Narrative = value ?? new ModelEvaluationNarrative();
        }

        /// <summary>
        /// Model profile the report was recorded on (mdl_ prefix), or null when it could not be resolved.
        /// </summary>
        public string? ProfileId { get; set; } = null;

        /// <summary>
        /// Advisory profile revision that carries the report (mdv_ prefix), or null.
        /// </summary>
        public string? ProfileRevisionId { get; set; } = null;

        /// <summary>
        /// When the report was written, in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _Packs = new List<string>();
        private List<string> _Personas = new List<string>();
        private List<string> _Warnings = new List<string>();
        private List<string> _Notes = new List<string>();
        private List<ModelEvaluationCount> _Probes = new List<ModelEvaluationCount>();
        private List<ModelEvaluationPersonaOutcome> _PersonaOutcomes = new List<ModelEvaluationPersonaOutcome>();
        private ModelEvaluationNarrative _Narrative = new ModelEvaluationNarrative();

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelEvaluationReport()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize a report for storage.
        /// </summary>
        /// <param name="report">Report, or null.</param>
        /// <returns>JSON, or null for a null report.</returns>
        public static string? ToJson(ModelEvaluationReport? report)
        {
            return report == null ? null : JsonSerializer.Serialize(report, _Json);
        }

        /// <summary>
        /// Read a stored report. Unreadable JSON reads as null.
        /// </summary>
        /// <param name="json">JSON, or null.</param>
        /// <returns>The report, or null.</returns>
        public static ModelEvaluationReport? FromJson(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<ModelEvaluationReport>(json, _Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
