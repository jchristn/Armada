namespace Armada.Core.Models
{
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One structured finding a Judge or verifier submitted (RELIABILITY_SIMPLICITY_PLAN.md RS-6.7).
    /// </summary>
    public class StageFinding
    {
        /// <summary>
        /// Severity token for a finding that must be fixed before the work moves on.
        /// </summary>
        public const string SeverityBlocking = "blocking";

        /// <summary>
        /// Severity token for a finding that does not block.
        /// </summary>
        public const string SeverityNonBlocking = "non_blocking";

        /// <summary>
        /// Repository-relative file, or null for a finding that is not about one file.
        /// </summary>
        [JsonPropertyName("file")]
        [Description("Repository-relative path of the affected file, or null.")]
        public string? File { get; set; } = null;

        /// <summary>
        /// Affected line, or null.
        /// </summary>
        [JsonPropertyName("line")]
        [Description("Affected line number, or null.")]
        public int? Line { get; set; } = null;

        /// <summary>
        /// <see cref="SeverityBlocking"/> or <see cref="SeverityNonBlocking"/>.
        /// </summary>
        [JsonPropertyName("severity")]
        [Required]
        [AllowedValues(SeverityBlocking, SeverityNonBlocking)]
        [Description("blocking when the work must not move on until it is fixed, otherwise non_blocking.")]
        public string Severity { get; set; } = "";

        /// <summary>
        /// The inputs or situation and the wrong result.
        /// </summary>
        [JsonPropertyName("scenario")]
        [Required]
        [Description("The inputs or situation, the violated requirement, and the expected and actual behavior.")]
        public string Scenario { get; set; } = "";

        /// <summary>
        /// The correction required, or null.
        /// </summary>
        [JsonPropertyName("requiredFix")]
        [Description("The correction required.")]
        public string? RequiredFix { get; set; } = null;
    }
}
