namespace Armada.Gate
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// benchmarks/config/reference-pipelines.json: the roles, reference pipelines, and packs of the acceptance gate.
    /// </summary>
    public sealed class GatePresetFile
    {
        /// <summary>Model roles.</summary>
        [JsonPropertyName("roles")]
        public List<GateRole> Roles { get; set; } = new List<GateRole>();

        /// <summary>Reference pipelines.</summary>
        [JsonPropertyName("pipelines")]
        public List<GatePipeline> Pipelines { get; set; } = new List<GatePipeline>();

        /// <summary>Battery packs to run.</summary>
        [JsonPropertyName("packs")]
        public List<string> Packs { get; set; } = new List<string>();
    }
}
