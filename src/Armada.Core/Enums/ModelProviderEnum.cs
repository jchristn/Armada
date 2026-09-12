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
        VoyageAI,

        /// <summary>
        /// Azure OpenAI Service. Base URL is the resource endpoint, the model is the deployment name, and an
        /// optional API version applies; authenticates with an api-key.
        /// </summary>
        [EnumMember(Value = "AzureOpenAI")]
        AzureOpenAI,

        /// <summary>
        /// Google Vertex AI (Gemini on Vertex). Requires a GCP project and region; authenticates with a
        /// service-account JSON supplied as the credential.
        /// </summary>
        [EnumMember(Value = "VertexAI")]
        VertexAI,

        /// <summary>
        /// AWS Bedrock (Converse API). Requires an AWS region and an access key id + secret access key;
        /// requests are SigV4-signed.
        /// </summary>
        [EnumMember(Value = "Bedrock")]
        Bedrock
    }
}
