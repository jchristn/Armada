namespace Armada.Benchmark.Client
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>One page of missions.</summary>
    public sealed class BenchMissionPage
    {
        /// <summary>Missions on this page.</summary>
        [JsonPropertyName("objects")]
        public List<BenchMission> Objects { get; set; } = new List<BenchMission>();

        /// <summary>Total matching missions.</summary>
        [JsonPropertyName("totalRecords")]
        public long TotalRecords { get; set; }
    }
}
