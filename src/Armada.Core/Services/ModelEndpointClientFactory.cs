namespace Armada.Core.Services
{
    using System;
    using System.Net.Http;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using PolyPrompt.Options;
    using SyslogLogging;

    /// <summary>
    /// Builds a PolyPrompt provider client for a configured <see cref="ModelEndpoint"/>. Maps the
    /// endpoint's provider to the concrete PolyPrompt client, applies the base URL, API key, model, and
    /// timeout, and enforces which providers can serve which kinds of model.
    /// </summary>
    public static class ModelEndpointClientFactory
    {
        #region Private-Members

        private const string DefaultOpenAiEndpoint = "https://api.openai.com";
        private const string DefaultOllamaEndpoint = "http://localhost:11434";

        #endregion

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
        /// Build the PolyPrompt client that matches the endpoint's kind, configured for inference on Inference
        /// endpoints and for embeddings on Embedding endpoints. Use this when only the members shared by every
        /// client (connectivity probe, timeout, disposal) are needed. The caller owns the returned client and
        /// must dispose it.
        /// </summary>
        /// <param name="endpoint">Model endpoint.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>A configured client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> or <paramref name="logging"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider does not support the endpoint's kind or its configuration is incomplete.</exception>
        public static ProviderClientBase Create(ModelEndpoint endpoint, LoggingModule logging)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (endpoint.Kind == ModelEndpointKindEnum.Embedding) return CreateEmbedding(endpoint, logging);
            return CreateCompletion(endpoint, logging);
        }

        /// <summary>
        /// Build a PolyPrompt client configured for chat and tool calling on the endpoint. The caller owns the
        /// returned client and must dispose it. With <paramref name="usageSink"/> the client's transport also
        /// reports allowlisted rate-limit response headers (passive capture on existing inference calls;
        /// nothing extra is ever requested), on every authentication path including the provider default.
        /// </summary>
        /// <param name="endpoint">Model endpoint.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="usageSink">Receives rate-limit header observations, or null (the default) for no capture.</param>
        /// <returns>A configured client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> or <paramref name="logging"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider does not support inference or its configuration is incomplete.</exception>
        public static ProviderClientBase CreateCompletion(ModelEndpoint endpoint, LoggingModule logging, Action<UsageHeaderObservation>? usageSink = null)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            string? reason = UnsupportedReason(endpoint.Provider, ModelEndpointKindEnum.Inference);
            if (reason != null) throw new InvalidOperationException(reason);

            ProviderClientBase client = Build(endpoint, logging, ModelEndpointAuth.CreateHttpClient(endpoint, usageSink), false);
            CompletionOptions? defaults = client.Defaults.Completion;
            if (defaults != null && !String.IsNullOrWhiteSpace(endpoint.Model)) defaults.Model = endpoint.Model;

            // Ollama otherwise runs at the model's default window and drops the oldest history without saying so.
            int? contextLength = endpoint.InferenceOptions.ContextLength;
            if (contextLength.HasValue && defaults is OllamaCompletionOptions ollamaDefaults)
                ollamaDefaults.ContextLength = contextLength;
            return client;
        }

        /// <summary>
        /// Build a PolyPrompt client configured for embeddings on the endpoint. The caller owns the returned
        /// client and must dispose it.
        /// </summary>
        /// <param name="endpoint">Model endpoint.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>A configured client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> or <paramref name="logging"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the provider does not support embeddings or its configuration is incomplete.</exception>
        public static ProviderClientBase CreateEmbedding(ModelEndpoint endpoint, LoggingModule logging)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            string? reason = UnsupportedReason(endpoint.Provider, ModelEndpointKindEnum.Embedding);
            if (reason != null) throw new InvalidOperationException(reason);

            ProviderClientBase client = Build(endpoint, logging, ModelEndpointAuth.CreateHttpClient(endpoint), true);
            EmbeddingOptions? defaults = client.Defaults.Embedding;
            if (defaults != null && !String.IsNullOrWhiteSpace(endpoint.Model)) defaults.Model = endpoint.Model;
            return client;
        }

        #endregion

        #region Private-Methods

        private static ProviderClientBase Build(ModelEndpoint endpoint, LoggingModule logging, HttpClient? http, bool embedding)
        {
            string baseUrl = endpoint.BaseUrl ?? String.Empty;
            string apiKey = ModelEndpointAuth.ProviderSecret(endpoint);
            ClientOptions options = new ClientOptions { Logging = logging, HttpClient = http, TimeoutMs = endpoint.TimeoutMs };

            switch (endpoint.Provider)
            {
                case ModelProviderEnum.OllamaCloud:
                    ModelEndpointAuth.Validate(endpoint);
                    return new OllamaCloudClient(endpoint.ApiKey!, EndpointOverride(baseUrl), options);
                case ModelProviderEnum.XAI:
                    ModelEndpointAuth.Validate(endpoint);
                    string xaiEndpoint = String.IsNullOrWhiteSpace(baseUrl) ? "https://api.x.ai" : baseUrl;
                    // PolyPrompt 4.0 dropped xAI embeddings; xAI's /v1/embeddings keeps the OpenAI shape the 3.x client sent.
                    if (embedding) return new OpenAiCompatibleClient(xaiEndpoint, endpoint.ApiKey, options);
                    return new XAiClient(endpoint.ApiKey!, xaiEndpoint, options);
                case ModelProviderEnum.Ollama:
                    return new OllamaClient(String.IsNullOrWhiteSpace(baseUrl) ? DefaultOllamaEndpoint : baseUrl, NullIfEmpty(apiKey), options);
                case ModelProviderEnum.OpenAI:
                case ModelProviderEnum.OpenAICompatible:
                    // Chat Completions for both, as with PolyPrompt 3.x. PolyPrompt's OpenAiClient speaks the Responses API instead.
                    return new OpenAiCompatibleClient(String.IsNullOrWhiteSpace(baseUrl) ? DefaultOpenAiEndpoint : baseUrl, NullIfEmpty(apiKey), options);
                case ModelProviderEnum.Anthropic:
                    return new AnthropicClient(KeyOrPlaceholder(endpoint, apiKey), EndpointOverride(baseUrl), options);
                case ModelProviderEnum.Gemini:
                    return new GeminiClient(KeyOrPlaceholder(endpoint, apiKey), EndpointOverride(baseUrl), options);
                case ModelProviderEnum.VoyageAI:
                    return new VoyageAiClient(KeyOrPlaceholder(endpoint, apiKey), EndpointOverride(baseUrl), options);
                case ModelProviderEnum.AzureOpenAI:
                    RequireAzure(endpoint, baseUrl, apiKey);
                    return new OpenAiCompatibleClient(AzureV1Endpoint(baseUrl), NullIfEmpty(apiKey), new OpenAiCompatibleClientOptions
                    {
                        Logging = logging,
                        HttpClient = http,
                        TimeoutMs = endpoint.TimeoutMs,
                        AuthPlacement = AuthPlacement.Header("api-key")
                    });
                case ModelProviderEnum.VertexAI:
                    return new VertexAiClient(endpoint.Project!, endpoint.Region!, VertexCredential(endpoint, apiKey), EndpointOverride(baseUrl), options);
                case ModelProviderEnum.Bedrock:
                    return new BedrockClient(endpoint.Region!, BedrockCredential(endpoint, apiKey), EndpointOverride(baseUrl), options);
                default:
                    throw new InvalidOperationException("Unsupported provider: " + endpoint.Provider);
            }
        }

        // Azure OpenAI's v1 API takes the deployment name as the model and needs no api-version. Chat Completions keeps
        // the request shape the 3.x deployment route used.
        private static string AzureV1Endpoint(string baseUrl)
        {
            string trimmed = baseUrl.Trim().TrimEnd('/');
            if (trimmed.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase)) return trimmed;
            if (trimmed.EndsWith("/openai", StringComparison.OrdinalIgnoreCase)) return trimmed + "/v1";
            return trimmed + "/openai/v1";
        }

        // Providers whose PolyPrompt client requires a key at construction get a placeholder when Armada authenticates
        // the request itself (Bearer, Basic, header or query authentication); ModelEndpointAuthHandler strips it.
        private static string KeyOrPlaceholder(ModelEndpoint endpoint, string apiKey)
        {
            if (!String.IsNullOrEmpty(apiKey)) return apiKey;
            if (endpoint.AuthType != ModelEndpointAuthTypeEnum.ProviderDefault) return ModelEndpointAuthHandler.ExternalAuthPlaceholder;
            throw new InvalidOperationException(endpoint.Provider + " requires an API key.");
        }

        private static string? NullIfEmpty(string value)
        {
            return String.IsNullOrEmpty(value) ? null : value;
        }

        private static void RequireAzure(ModelEndpoint endpoint, string baseUrl, string apiKey)
        {
            Require(baseUrl, "Azure OpenAI requires a base URL (the resource endpoint, e.g. https://my-resource.openai.azure.com).");
            Require(endpoint.Model, "Azure OpenAI requires a model, which is the deployment name.");
            if (endpoint.AuthType == ModelEndpointAuthTypeEnum.ProviderDefault) Require(apiKey, "Azure OpenAI requires an API key.");
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
