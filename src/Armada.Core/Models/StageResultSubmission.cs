namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A work or verifier stage result submitted through <c>armada_submit_result</c> (RELIABILITY_SIMPLICITY_PLAN.md
    /// RS-6.2, RS-6.7).
    /// </summary>
    public class StageResultSubmission
    {
        /// <summary>
        /// Status token for finished work.
        /// </summary>
        public const string StatusComplete = "complete";

        /// <summary>
        /// Status token for a verifier that found a defect in code its role must not change.
        /// </summary>
        public const string StatusBlocked = "blocked";

        /// <summary>
        /// <see cref="StatusComplete"/> or <see cref="StatusBlocked"/>.
        /// </summary>
        [JsonPropertyName("status")]
        [Required]
        [AllowedValues(StatusComplete, StatusBlocked)]
        [Description("complete when the work is done; blocked (Test Engineer, Linter and Usability Engineer only) for a defect in code your role must not change.")]
        public string Status { get; set; } = "";

        /// <summary>
        /// Brief plain-text summary.
        /// </summary>
        [JsonPropertyName("summary")]
        [Required]
        [Description("A brief plain-text summary of what you did.")]
        public string Summary { get; set; } = "";

        /// <summary>
        /// The report sections the persona's contract requires.
        /// </summary>
        [JsonPropertyName("sections")]
        [Description("The report sections your output contract lists, each with its heading and text.")]
        public List<StageReportSection> Sections { get; set; } = new List<StageReportSection>();

        /// <summary>
        /// Findings, blocking first.
        /// </summary>
        [JsonPropertyName("findings")]
        [Description("Defects you found. A blocked result needs at least one blocking finding with a file or a concrete scenario.")]
        public List<StageFinding> Findings { get; set; } = new List<StageFinding>();

        /// <summary>
        /// What the stage checked.
        /// </summary>
        [JsonPropertyName("evidence")]
        [Description("What you checked: commands run and files inspected.")]
        public StageEvidence Evidence { get; set; } = new StageEvidence();
    }
}
