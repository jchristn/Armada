namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using SyslogLogging;

    /// <summary>
    /// Builds a PolyPrompt completion or embedding client for a configured <see cref="ModelEndpoint"/>. Maps the
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
        /// Build the PolyPrompt client that matches the endpoint's kind: a completion client for Inference
        /// endpoints, an embedding client for Embedding endpoints. Use this when only the members shared by
        /// every client (connectivity probe, timeout, disposal) are needed. The caller owns the returned client
        /// and must dispose it.
        /// </summary>
        /// <param name="endpoint">Model endpoint.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>A configured client.</returns>
        public static ClientBase Create(ModelEndpoint endpoint, LoggingModule logging)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (endpoint.Kind == ModelEndpointKindEnum.Embedding) return CreateEmbedding(endpoint, logging);
            return CreateCompletion(endpoint, logging);
        }

        /// <summary>
        /// Build a PolyPrompt completion (chat / tool-chat) client for the endpoint. The caller owns the
        /// returned client and must dispose it.
        /// </summary>
        /// <param name="endpoint">Model endpoint.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>A configured completion client.</returns>
        public static CompletionClientBase CreateCompletion(ModelEndpoint endpoint, LoggingModule logging)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            string? reason = UnsupportedReason(endpoint.Provider, ModelEndpointKindEnum.Inference);
            if (reason != null) throw new InvalidOperationException(reason);

            string baseUrl = endpoint.BaseUrl ?? String.Empty;
            string apiKey = endpoint.ApiKey ?? String.Empty;

            CompletionClientBase client;
            switch (endpoint.Provider)
            {
                case ModelProviderEnum.Ollama:
                    client = new OllamaCompletionClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.OpenAI:
                case ModelProviderEnum.OpenAICompatible:
                    client = new OpenAiCompletionClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.Anthropic:
                    client = new AnthropicCompletionClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.Gemini:
                    client = new GeminiCompletionClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.AzureOpenAI:
                    RequireAzure(endpoint, baseUrl, apiKey);
                    client = new AzureOpenAiCompletionClient(baseUrl, endpoint.Model!, apiKey, endpoint.ApiVersion, logging, null);
                    break;
                case ModelProviderEnum.VertexAI:
                    client = new VertexAiCompletionClient(endpoint.Project!, endpoint.Region!, VertexCredential(endpoint, apiKey), EndpointOverride(baseUrl), logging, null);
                    break;
                case ModelProviderEnum.Bedrock:
                    client = new BedrockCompletionClient(BedrockCredential(endpoint, apiKey), endpoint.Region!, EndpointOverride(baseUrl), logging, null);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported provider: " + endpoint.Provider);
            }

            // Azure OpenAI derives its model from the deployment name at construction; do not overwrite it.
            if (endpoint.Provider != ModelProviderEnum.AzureOpenAI && !String.IsNullOrWhiteSpace(endpoint.Model))
                client.Model = endpoint.Model;
            client.TimeoutMs = endpoint.TimeoutMs;
            return client;
        }

        /// <summary>
        /// Build a PolyPrompt embedding client for the endpoint. The caller owns the returned client and must
        /// dispose it.
        /// </summary>
        /// <param name="endpoint">Model endpoint.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>A configured embedding client.</returns>
        public static EmbeddingClientBase CreateEmbedding(ModelEndpoint endpoint, LoggingModule logging)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            string? reason = UnsupportedReason(endpoint.Provider, ModelEndpointKindEnum.Embedding);
            if (reason != null) throw new InvalidOperationException(reason);

            string baseUrl = endpoint.BaseUrl ?? String.Empty;
            string apiKey = endpoint.ApiKey ?? String.Empty;

            EmbeddingClientBase client;
            switch (endpoint.Provider)
            {
                case ModelProviderEnum.Ollama:
                    client = new OllamaEmbeddingClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.OpenAI:
                case ModelProviderEnum.OpenAICompatible:
                    client = new OpenAiEmbeddingClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.Gemini:
                    client = new GeminiEmbeddingClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.VoyageAI:
                    client = new VoyageAiEmbeddingClient(baseUrl, apiKey, logging, null);
                    break;
                case ModelProviderEnum.AzureOpenAI:
                    RequireAzure(endpoint, baseUrl, apiKey);
                    client = new AzureOpenAiEmbeddingClient(baseUrl, endpoint.Model!, apiKey, endpoint.ApiVersion, logging, null);
                    break;
                case ModelProviderEnum.VertexAI:
                    client = new VertexAiEmbeddingClient(endpoint.Project!, endpoint.Region!, VertexCredential(endpoint, apiKey), EndpointOverride(baseUrl), logging, null);
                    break;
                case ModelProviderEnum.Bedrock:
                    client = new BedrockEmbeddingClient(BedrockCredential(endpoint, apiKey), endpoint.Region!, EndpointOverride(baseUrl), logging, null);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported provider: " + endpoint.Provider);
            }

            // Azure OpenAI derives its model from the deployment name at construction; do not overwrite it.
            if (endpoint.Provider != ModelProviderEnum.AzureOpenAI && !String.IsNullOrWhiteSpace(endpoint.Model))
                client.Model = endpoint.Model;
            client.TimeoutMs = endpoint.TimeoutMs;
            return client;
        }

        #endregion

        #region Private-Methods

        private static void RequireAzure(ModelEndpoint endpoint, string baseUrl, string apiKey)
        {
            Require(baseUrl, "Azure OpenAI requires a base URL (the resource endpoint, e.g. https://my-resource.openai.azure.com).");
            Require(endpoint.Model, "Azure OpenAI requires a model, which is the deployment name.");
            Require(apiKey, "Azure OpenAI requires an API key.");
        }

        private static ICredentialProvider VertexCredential(ModelEndpoint endpoint, string apiKey)
        {
            Require(endpoint.Project, "Vertex AI requires a GCP project id.");
            Require(endpoint.Region, "Vertex AI requires a region (e.g. us-central1).");
            Require(apiKey, "Vertex AI requires a service-account JSON supplied as the credential.");
            return ServiceAccountCredential.FromJson(apiKey);
        }

        private static IAwsCredentialProvider BedrockCredential(ModelEndpoint endpoint, string apiKey)
        {
            Require(endpoint.AccessKeyId, "AWS Bedrock requires an access key id.");
            Require(apiKey, "AWS Bedrock requires a secret access key.");
            Require(endpoint.Region, "AWS Bedrock requires a region (e.g. us-east-1).");
            return new StaticAwsCredential(endpoint.AccessKeyId!, apiKey, endpoint.Region!, null);
        }

        private static string? EndpointOverride(string baseUrl)
        {
            return String.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl;
        }

        private static void Require(string? value, string message)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(message);
        }

        #endregion
    }
}
