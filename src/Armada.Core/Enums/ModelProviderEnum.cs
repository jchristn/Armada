namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The provider/wire-format an endpoint speaks. Determines the request/response shape and the
    /// authentication header used when validating or calling the endpoint.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelProviderEnum
    {
        /// <summary>
        /// Ollama's local API (/api/embeddings, /api/chat).
        /// </summary>
        [EnumMember(Value = "Ollama")]
        Ollama,

        /// <summary>
        /// OpenAI's hosted API (/v1/embeddings, /v1/chat/completions), Bearer auth.
        /// </summary>
        [EnumMember(Value = "OpenAI")]
        OpenAI,

        /// <summary>
        /// An OpenAI-compatible server at a custom base URL (vLLM, LM Studio, LiteLLM, etc.).
        /// </summary>
        [EnumMember(Value = "OpenAICompatible")]
        OpenAICompatible,

        /// <summary>
        /// Anthropic's Messages API (x-api-key auth). Inference only.
        /// </summary>
        [EnumMember(Value = "Anthropic")]
        Anthropic,

        /// <summary>
        /// Google Gemini (x-goog-api-key auth).
        /// </summary>
        [EnumMember(Value = "Gemini")]
        Gemini,

        /// <summary>
        /// Voyage AI embeddings (/v1/embeddings, Bearer auth). Embedding only.
        /// </summary>
        [EnumMember(Value = "VoyageAI")]
        VoyageAI
    }
}
