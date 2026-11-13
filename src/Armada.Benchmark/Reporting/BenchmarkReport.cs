namespace Armada.Benchmark.Reporting
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>One admission benchmark run: provenance, scenario, per-arm results and the regression budget verdict.</summary>
    public sealed class BenchmarkReport
    {
        /// <summary>Report schema version.</summary>
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        /// <summary>Harness command that produced the report.</summary>
        [JsonPropertyName("command")]
        public string Command { get; set; } = "admission";

        /// <summary>What produced the report.</summary>
        [JsonPropertyName("provenance")]
        public BenchmarkProvenance Provenance { get; set; } = new BenchmarkProvenance();

        /// <summary>Seeded inputs.</summary>
        [JsonPropertyName("scenario")]
        public BenchmarkScenario Scenario { get; set; } = new BenchmarkScenario();

        /// <summary>Per-arm results in run order.</summary>
        [JsonPropertyName("arms")]
        public List<ArmResult> Arms { get; set; } = new List<ArmResult>();

        /// <summary>Allowed p95 admission regression of Observe and Enforce versus Disabled (0.20 = 20%).</summary>
        [JsonPropertyName("p95RegressionBudget")]
        public double P95RegressionBudget { get; set; } = 0.20;

        /// <summary>Budget and accounting findings; empty when the run passed.</summary>
        [JsonPropertyName("findings")]
        public List<string> Findings { get; set; } = new List<string>();

        /// <summary>True when any arm was incomplete or had request errors; such a run is not comparable.</summary>
        [JsonPropertyName("incomparable")]
        public bool Incomparable { get; set; }
    }
}
