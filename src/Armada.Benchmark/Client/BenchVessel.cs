namespace Armada.Benchmark.Client
{
    using System.Text.Json.Serialization;

    /// <summary>The vessel fields the harness creates and reads.</summary>
    public sealed class BenchVessel
    {
        /// <summary>Vessel identifier.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
    }
}
