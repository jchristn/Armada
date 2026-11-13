namespace Armada.Benchmark.Client
{
    using System.Text.Json.Serialization;

    /// <summary>The settings fields the harness reads.</summary>
    public sealed class BenchSettings
    {
        /// <summary>Live usage-selection group.</summary>
        [JsonPropertyName("usageSelection")]
        public BenchUsageSelection? UsageSelection { get; set; }

        /// <summary>Revision for optimistic updates.</summary>
        [JsonPropertyName("usageSelectionRevision")]
        public long UsageSelectionRevision { get; set; }
    }
}
