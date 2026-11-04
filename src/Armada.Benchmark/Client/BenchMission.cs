namespace Armada.Benchmark.Client
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>The mission fields the harness reads to time admission.</summary>
    public sealed class BenchMission
    {
        /// <summary>Mission identifier.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>Lifecycle status name.</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        /// <summary>Assigned captain, once admitted.</summary>
        [JsonPropertyName("captainId")]
        public string? CaptainId { get; set; }

        /// <summary>Server creation time.</summary>
        [JsonPropertyName("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        /// <summary>Server time of the last atomic slot assignment.</summary>
        [JsonPropertyName("assignedUtc")]
        public DateTime? AssignedUtc { get; set; }

        /// <summary>Server completion time.</summary>
        [JsonPropertyName("completedUtc")]
        public DateTime? CompletedUtc { get; set; }
    }
}
