namespace Test.Shared.Infrastructure
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One window inside the sanitized Custom usage contract fixture.
    /// </summary>
    public class CustomUsageWindowFixture
    {
        /// <summary>
        /// Window name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Used percent, nullable.
        /// </summary>
        [JsonPropertyName("usedPercent")]
        public double? UsedPercent { get; set; } = null;

        /// <summary>
        /// Used count, nullable.
        /// </summary>
        [JsonPropertyName("used")]
        public double? Used { get; set; } = null;

        /// <summary>
        /// Limit count, nullable.
        /// </summary>
        [JsonPropertyName("limit")]
        public double? Limit { get; set; } = null;

        /// <summary>
        /// Counter unit.
        /// </summary>
        [JsonPropertyName("unit")]
        public string? Unit { get; set; } = null;

        /// <summary>
        /// Window length in minutes.
        /// </summary>
        [JsonPropertyName("windowMinutes")]
        public long? WindowMinutes { get; set; } = null;

        /// <summary>
        /// Absolute UTC reset, nullable.
        /// </summary>
        [JsonPropertyName("resetsUtc")]
        public DateTimeOffset? ResetsUtc { get; set; } = null;
    }
}
