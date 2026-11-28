namespace Armada.Gate
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One reference pipeline: its stages, which role staffs each persona, and how many runs of each pack it gets.
    /// </summary>
    public sealed class GatePipeline
    {
        /// <summary>Pipeline name created in the gate Admiral, for example Gate-A.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = String.Empty;

        /// <summary>What the pipeline measures.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>Persona names in stage order.</summary>
        [JsonPropertyName("stages")]
        public List<string> Stages { get; set; } = new List<string>();

        /// <summary>Role per persona.</summary>
        [JsonPropertyName("staffing")]
        public Dictionary<string, string> Staffing { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Runs of each pack task. Default 1, minimum 1, maximum 10.</summary>
        [JsonPropertyName("runsPerPack")]
        public int RunsPerPack { get; set; } = 1;
    }
}
