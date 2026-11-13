namespace Armada.Runtimes.Usage
{
    using System.Text.Json.Serialization;
    /// <summary>Versioned Claude OAuth usage window, observed by the CLI usage UI.</summary>
    public class ClaudeUsageWindowDto
    {
        /// <summary>Percentage consumed, when supplied.</summary>
        public double? Utilization { get; set; }
        /// <summary>Absolute reset timestamp, when supplied.</summary>
        [JsonPropertyName("resets_at")]
        public string? ResetsAt { get; set; }
    }
}
