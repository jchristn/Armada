namespace Armada.Gate
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A model role in the reference pipelines: which runtime runs it and where its model comes from.
    /// </summary>
    public sealed class GateRole
    {
        /// <summary>Role name used by pipeline staffing, for example qwen or opus.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = String.Empty;

        /// <summary>Captain runtime: ClaudeCode, Codex, or ApiEndpoint.</summary>
        [JsonPropertyName("runtime")]
        public string Runtime { get; set; } = String.Empty;

        /// <summary>Model name for CLI runtimes.</summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        /// <summary>Captain tier to set explicitly, or null to classify from the model.</summary>
        [JsonPropertyName("tier")]
        public string? Tier { get; set; }

        /// <summary>For ApiEndpoint roles: conductor or ollamaCloud.</summary>
        [JsonPropertyName("source")]
        public string? Source { get; set; }

        /// <summary>For ApiEndpoint roles: the key of the model in the local endpoint file.</summary>
        [JsonPropertyName("modelKey")]
        public string? ModelKey { get; set; }
    }
}
