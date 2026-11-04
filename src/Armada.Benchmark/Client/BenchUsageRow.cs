namespace Armada.Benchmark.Client
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>One account usage row (latest or historical snapshot).</summary>
    public sealed class BenchUsageRow
    {
        /// <summary>Capture status name.</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        /// <summary>Capture time.</summary>
        [JsonPropertyName("capturedUtc")]
        public DateTime CapturedUtc { get; set; }

        /// <summary>Captains sharing the observed account.</summary>
        [JsonPropertyName("captainIds")]
        public List<string> CaptainIds { get; set; } = new List<string>();
    }
}
