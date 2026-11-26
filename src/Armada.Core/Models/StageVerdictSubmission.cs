namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A Judge verdict submitted through <c>armada_submit_verdict</c> (RELIABILITY_SIMPLICITY_PLAN.md RS-6.2, RS-6.7).
    /// </summary>
    public class StageVerdictSubmission
    {
        /// <summary>
        /// Verdict token: PASS, FAIL, NEEDS_REVISION or SPEC_CONFLICT.
        /// </summary>
        [JsonPropertyName("verdict")]
        [Required]
        [AllowedValues("PASS", "FAIL", "NEEDS_REVISION", "SPEC_CONFLICT")]
        [Description("PASS, FAIL, NEEDS_REVISION or SPEC_CONFLICT.")]
        public string Verdict { get; set; } = "";

        /// <summary>
        /// The correctness lens.
        /// </summary>
        [JsonPropertyName("correctness")]
        [Required]
        [Description("Correctness: is the change logically correct for the inputs it can receive?")]
        public string Correctness { get; set; } = "";

        /// <summary>
        /// The blast-radius lens.
        /// </summary>
        [JsonPropertyName("blastRadius")]
        [Required]
        [Description("Blast Radius: what else could this change break, and how far do its effects reach?")]
        public string BlastRadius { get; set; } = "";

        /// <summary>
        /// The source-fidelity lens.
        /// </summary>
        [JsonPropertyName("sourceFidelity")]
        [Required]
        [Description("Source Fidelity: does the change implement the mission, stay in scope, and match the real codebase?")]
        public string SourceFidelity { get; set; } = "";

        /// <summary>
        /// Short verdict rationale, or null.
        /// </summary>
        [JsonPropertyName("summary")]
        [Description("One or two sentences explaining the verdict.")]
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Findings, blocking first.
        /// </summary>
        [JsonPropertyName("findings")]
        [Description("Each defect you found. FAIL or NEEDS_REVISION needs at least one blocking finding with a file or a concrete scenario.")]
        public List<StageFinding> Findings { get; set; } = new List<StageFinding>();

        /// <summary>
        /// The criterion that cannot be satisfied as written and the evidence, required for SPEC_CONFLICT.
        /// </summary>
        [JsonPropertyName("specConflict")]
        [Description("Required for SPEC_CONFLICT: the quoted acceptance criterion and the evidence it cannot be satisfied as written.")]
        public string? SpecConflict { get; set; } = null;

        /// <summary>
        /// What the Judge checked.
        /// </summary>
        [JsonPropertyName("evidence")]
        [Description("What you checked: commands run and files inspected.")]
        public StageEvidence Evidence { get; set; } = new StageEvidence();
    }
}
