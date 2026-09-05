namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using PolyPrompt.Clients;
    using SyslogLogging;

    /// <summary>
    /// Builds a PolyPrompt completion/embedding client for a configured <see cref="ModelEndpoint"/>. Maps the
    /// endpoint's provider to the concrete PolyPrompt client, applies the base URL, API key, model, and
    /// timeout, and enforces which providers can serve which kinds of model.
    /// </summary>
    public static class ModelEndpointClientFactory
    {
        #region Public-Methods

        /// <summary>
        /// Whether the given provider can serve the given kind of model. Anthropic has no embedding API and
        /// Voyage AI has no inference API, so those combinations are rejected before a client is built.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="kind">Endpoint kind.</param>
        /// <returns>True when the provider supports the kind.</returns>
        public static bool SupportsKind(ModelProviderEnum provider, ModelEndpointKindEnum kind)
        {
            if (kind == ModelEndpointKindEnum.Embedding && provider == ModelProviderEnum.Anthropic) return false;
            if (kind == ModelEndpointKindEnum.Inference && provider == ModelProviderEnum.VoyageAI) return false;
            return true;
        }

        /// <summary>
        /// Human-readable reason a provider cannot serve a kind, or null when the combination is valid.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="kind">Endpoint kind.</param>
        /// <returns>Reason string or null.</returns>
        public static string? UnsupportedReason(ModelProviderEnum provider, ModelEndpointKindEnum kind)
        {
            if (kind == ModelEndpointKindEnum.Embedding && provider == ModelProviderEnum.Anthropic)
                return "Anthropic does not provide an embeddings API; choose the Inference kind or a different provider.";
            if (kind == ModelEndpointKindEnum.Inference && provider == ModelProviderEnum.VoyageAI)
                return "Voyage AI provides embeddings only; choose the Embedding kind or a different provider.";
            return null;
        }

        /// <summary>
        /// Build a PolyPrompt client for the endpoint. The caller owns the returned client and must dispose it.
        /// </summary>
        /// <param name="endpoint">Model endpoint.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>A configured completion/embedding client.</returns>
        public static CompletionClientBase Create(ModelEndpoint endpoint, LoggingModule logging)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            string? reason = UnsupportedReason(endpoint.Provider, endpoint.Kind);
            if (reason != null) throw new InvalidOperationException(reason);

            string baseUrl = endpoint.BaseUrl ?? String.Empty;
            string apiKey = endpoint.ApiKey ?? String.Empty;

            CompletionClientBase client;
            switch (endpoint.Provider)
            {
                case ModelProviderEnum.Ollama:
                    client = new OllamaClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.OpenAI:
                case ModelProviderEnum.OpenAICompatible:
                    client = new OpenAiClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.Anthropic:
                    client = new AnthropicClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.Gemini:
                    client = new GeminiClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.VoyageAI:
                    client = new VoyageAiClient(baseUrl, apiKey, logging, null);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported provider: " + endpoint.Provider);
            }

            if (!String.IsNullOrWhiteSpace(endpoint.Model)) client.Model = endpoint.Model;
            client.TimeoutMs = endpoint.TimeoutMs;
            return client;
        }

        #endregion
    }
}
