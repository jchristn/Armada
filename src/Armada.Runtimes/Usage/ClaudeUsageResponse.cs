namespace Armada.Runtimes.Usage
{
    using System.Text.Json.Serialization;
    /// <summary>Claude usage v1 allowlist. Unrelated billing, promotion and experimental fields are ignored.</summary>
    public class ClaudeUsageResponse
    {
        /// <summary>Session quota.</summary>
        [JsonPropertyName("five_hour")]
        public ClaudeUsageWindowDto? FiveHour { get; set; }
        /// <summary>Account weekly quota.</summary>
        [JsonPropertyName("seven_day")]
        public ClaudeUsageWindowDto? SevenDay { get; set; }
        /// <summary>Opus weekly quota.</summary>
        [JsonPropertyName("seven_day_opus")]
        public ClaudeUsageWindowDto? SevenDayOpus { get; set; }
        /// <summary>Sonnet weekly quota.</summary>
        [JsonPropertyName("seven_day_sonnet")]
        public ClaudeUsageWindowDto? SevenDaySonnet { get; set; }
    }
}
