namespace Armada.Benchmark.Reporting
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>What produced a report: code, machine, harness limits and configuration hash.</summary>
    public sealed class BenchmarkProvenance
    {
        /// <summary>Git commit of the working tree.</summary>
        [JsonPropertyName("commit")]
        public string Commit { get; set; } = "";

        /// <summary>True when the working tree had uncommitted changes.</summary>
        [JsonPropertyName("dirty")]
        public bool Dirty { get; set; }

        /// <summary>Operating system description.</summary>
        [JsonPropertyName("os")]
        public string Os { get; set; } = "";

        /// <summary>Processor architecture.</summary>
        [JsonPropertyName("architecture")]
        public string Architecture { get; set; } = "";

        /// <summary>Logical processors.</summary>
        [JsonPropertyName("processors")]
        public int Processors { get; set; }

        /// <summary>Harness runtime.</summary>
        [JsonPropertyName("framework")]
        public string Framework { get; set; } = "";

        /// <summary>Database provider of the system under test.</summary>
        [JsonPropertyName("databaseProvider")]
        public string DatabaseProvider { get; set; } = "";

        /// <summary>HTTP connection limit.</summary>
        [JsonPropertyName("maxConnections")]
        public int MaxConnections { get; set; }

        /// <summary>HTTP timeout in seconds.</summary>
        [JsonPropertyName("timeoutSeconds")]
        public int TimeoutSeconds { get; set; }

        /// <summary>SHA-256 of the scenario configuration (accounts, candidates, missions, policy).</summary>
        [JsonPropertyName("scenarioHash")]
        public string ScenarioHash { get; set; } = "";

        /// <summary>When the run started.</summary>
        [JsonPropertyName("startedUtc")]
        public DateTime StartedUtc { get; set; }
    }
}
