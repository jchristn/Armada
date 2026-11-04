namespace Armada.Benchmark.Reporting
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>The seeded public inputs of one admission scenario; identical across arms.</summary>
    public sealed class BenchmarkScenario
    {
        /// <summary>Candidate captains.</summary>
        [JsonPropertyName("candidates")]
        public int Candidates { get; set; }

        /// <summary>Missions per arm.</summary>
        [JsonPropertyName("missions")]
        public int Missions { get; set; }

        /// <summary>Used percent of each synthetic account; captains are assigned round-robin.</summary>
        [JsonPropertyName("accountUsedPercents")]
        public List<double> AccountUsedPercents { get; set; } = new List<double>();

        /// <summary>Configured strategy.</summary>
        [JsonPropertyName("strategy")]
        public string Strategy { get; set; } = "";

        /// <summary>Protected reserve percent.</summary>
        [JsonPropertyName("reservePercent")]
        public double ReservePercent { get; set; }

        /// <summary>Per-attempt reservation percent.</summary>
        [JsonPropertyName("perAttemptReservationPercent")]
        public double PerAttemptReservationPercent { get; set; }

        /// <summary>Missions created at once.</summary>
        [JsonPropertyName("submitConcurrency")]
        public int SubmitConcurrency { get; set; }

        /// <summary>Random seed.</summary>
        [JsonPropertyName("seed")]
        public int Seed { get; set; }
    }
}
