namespace Armada.Benchmark.Client
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>One page of usage rows.</summary>
    public sealed class BenchUsagePage
    {
        /// <summary>Rows on this page.</summary>
        [JsonPropertyName("objects")]
        public List<BenchUsageRow> Objects { get; set; } = new List<BenchUsageRow>();

        /// <summary>Total pages.</summary>
        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }
    }
}
