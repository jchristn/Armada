namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// The stable identity of a model profile: the model as served. Two observations with the same tenant and identity
    /// (compared case-insensitively after trimming) belong to the same profile.
    /// </summary>
    public class ModelProfileIdentity
    {
        /// <summary>
        /// Runtime that serves the model (for example ClaudeCode, Codex, Mux, ApiEndpoint).
        /// </summary>
        public string Runtime { get; set; } = "";

        /// <summary>
        /// Inference provider (for example Ollama, OpenAI), or null when the runtime does not say (vendor CLIs).
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Model name, or null when the runtime uses its own default.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Context window the model is served with, in tokens, or null when unknown.
        /// </summary>
        public int? ServedContextTokens { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileIdentity()
        {
        }
    }
}
