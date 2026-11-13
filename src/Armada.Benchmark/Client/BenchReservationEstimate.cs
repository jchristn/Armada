namespace Armada.Benchmark.Client
{
    using System.Text.Json.Serialization;

    /// <summary>Armada's own reservation estimate for one observed window of an account.</summary>
    public sealed class BenchReservationEstimate
    {
        /// <summary>Reserved by running attempts.</summary>
        [JsonPropertyName("activePercent")]
        public double ActivePercent { get; set; }

        /// <summary>Reserved by ended attempts awaiting settlement.</summary>
        [JsonPropertyName("unsettledPercent")]
        public double UnsettledPercent { get; set; }
    }
}
