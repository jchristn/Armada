namespace Armada.Benchmark.Reporting
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Benchmark.Metrics;

    /// <summary>Measurements for one arm (selection mode) of a scenario.</summary>
    public sealed class ArmResult
    {
        /// <summary>Selection mode: Disabled, Observe or Enforce.</summary>
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "";

        /// <summary>Configured strategy.</summary>
        [JsonPropertyName("strategy")]
        public string Strategy { get; set; } = "";

        /// <summary>Missions submitted.</summary>
        [JsonPropertyName("missions")]
        public int Missions { get; set; }

        /// <summary>Missions that were admitted (have an assignment time).</summary>
        [JsonPropertyName("admitted")]
        public int Admitted { get; set; }

        /// <summary>Missions that did not reach a terminal state before the arm timeout; any value makes the run incomparable.</summary>
        [JsonPropertyName("incomplete")]
        public int Incomplete { get; set; }

        /// <summary>Missions by final status.</summary>
        [JsonPropertyName("finalStatuses")]
        public Dictionary<string, int> FinalStatuses { get; set; } = new Dictionary<string, int>();

        /// <summary>Failed REST requests during the arm.</summary>
        [JsonPropertyName("errors")]
        public int Errors { get; set; }

        /// <summary>Server-side creation to assignment time.</summary>
        [JsonPropertyName("admissionLatency")]
        public LatencyStatistics AdmissionLatency { get; set; } = new LatencyStatistics();

        /// <summary>Admissions per second from the first creation to the last assignment.</summary>
        [JsonPropertyName("throughputPerSecond")]
        public double ThroughputPerSecond { get; set; }

        /// <summary>Missions whose admission took longer than five seconds.</summary>
        [JsonPropertyName("waitedOverFiveSeconds")]
        public int WaitedOverFiveSeconds { get; set; }

        /// <summary>Distribution of admissions over synthetic accounts.</summary>
        [JsonPropertyName("accounts")]
        public List<AccountShare> Accounts { get; set; } = new List<AccountShare>();

        /// <summary>Accounts admitted past their reservation capacity (Enforce only).</summary>
        [JsonPropertyName("accountingViolations")]
        public int AccountingViolations { get; set; }

        /// <summary>Wall-clock duration of the arm in seconds.</summary>
        [JsonPropertyName("durationSeconds")]
        public double DurationSeconds { get; set; }
    }
}
