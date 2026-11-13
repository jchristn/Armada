namespace Armada.Benchmark.Client
{
    using System.Text.Json.Serialization;

    /// <summary>The captain fields the harness creates and reads.</summary>
    public sealed class BenchCaptain
    {
        /// <summary>Captain identifier.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>Captain name.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }
}
