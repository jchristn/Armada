namespace Armada.PerfSeed
{
    using System.Text.Json.Serialization;

    /// <summary>Identifiers of seeded rows that the measurement script substitutes into benchmark routes (perf-ids.json).</summary>
    public sealed class PerfSeedIds
    {
        /// <summary>Mission with the long selection history.</summary>
        [JsonPropertyName("selectionMissionId")]
        public string? SelectionMissionId { get; set; }

        /// <summary>Latest decision of that mission.</summary>
        [JsonPropertyName("selectionDecisionId")]
        public string? SelectionDecisionId { get; set; }
    }
}
