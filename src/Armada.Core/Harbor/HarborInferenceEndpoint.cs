namespace Armada.Core.Harbor
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// A fully resolved inference endpoint shipped to a Harbor so it can drive an API-endpoint captain. This
    /// is a deliberate transport type: unlike <see cref="Armada.Core.Models.ModelEndpoint"/> (whose API key is
    /// never serialized), this carries the API key explicitly so the Harbor can authenticate to the provider.
    /// It travels over the authenticated Harbor link; that link should be TLS-protected in production.
    /// </summary>
    public class HarborInferenceEndpoint
    {
        #region Public-Members

        /// <summary>
        /// Endpoint display name (for logs).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Provider that serves the endpoint.
        /// </summary>
        public ModelProviderEnum Provider { get; set; } = ModelProviderEnum.OpenAICompatible;

        /// <summary>
        /// Endpoint kind. Always Inference for an API-endpoint captain.
        /// </summary>
        public ModelEndpointKindEnum Kind { get; set; } = ModelEndpointKindEnum.Inference;

        /// <summary>
        /// Base URL of the provider API.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Model identifier.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// API key used to authenticate to the provider. Serialized (unlike ModelEndpoint's key) so the Harbor
        /// can use it.
        /// </summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>
        /// Per-request timeout in milliseconds.
        /// </summary>
        public int TimeoutMs { get; set; } = 120000;

        /// <summary>Inference generation settings and tool restrictions, enforced on the Harbor.</summary>
        public InferenceOptions InferenceOptions { get; set; } = new InferenceOptions();

        /// <summary>Whether this endpoint is enabled for inference execution.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Cloud region (Vertex AI, Bedrock), or null.
        /// </summary>
        public string? Region { get; set; } = null;

        /// <summary>
        /// GCP project id (Vertex AI), or null.
        /// </summary>
        public string? Project { get; set; } = null;

        /// <summary>
        /// API version (Azure OpenAI), or null.
        /// </summary>
        public string? ApiVersion { get; set; } = null;

        /// <summary>
        /// AWS access key id (Bedrock), or null.
        /// </summary>
        public string? AccessKeyId { get; set; } = null;

        /// <summary>
        /// How the endpoint authenticates (see <see cref="ModelEndpointAuthTypeEnum"/>); <see cref="ApiKey"/> is the secret.
        /// </summary>
        public ModelEndpointAuthTypeEnum AuthType { get; set; } = ModelEndpointAuthTypeEnum.ProviderDefault;

        /// <summary>
        /// Basic authentication user name, or null.
        /// </summary>
        public string? AuthUsername { get; set; } = null;

        /// <summary>
        /// Header that carries the secret for header authentication, or null.
        /// </summary>
        public string? AuthHeaderName { get; set; } = null;

        /// <summary>
        /// Query parameter that carries the secret for query parameter authentication, or null.
        /// </summary>
        public string? AuthQueryParameter { get; set; } = null;

        /// <summary>
        /// Extra headers with their values. Never null.
        /// </summary>
        public List<ModelEndpointHeaderRecord> Headers { get; set; } = new List<ModelEndpointHeaderRecord>();

        /// <summary>
        /// Context window the Admiral discovered for the model, or null (RS-7.8). A Harbor that advertises
        /// <see cref="HarborCapabilityFlags.ContextDiscovery"/> rediscovers it on its own host when the endpoint configures no
        /// window and reports what it found in <see cref="HarborExited.DiscoveredContextLength"/>; an older Harbor ignores it.
        /// </summary>
        public int? DiscoveredContextLength { get; set; } = null;

        /// <summary>
        /// Where <see cref="DiscoveredContextLength"/> was read, or null.
        /// </summary>
        public string? DiscoveredContextSource { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborInferenceEndpoint()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The transport form of a resolved endpoint, credentials included.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <returns>Transport endpoint.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public static HarborInferenceEndpoint FromEndpoint(ModelEndpoint endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            return new HarborInferenceEndpoint
            {
                Name = endpoint.Name,
                Provider = endpoint.Provider,
                Kind = endpoint.Kind,
                BaseUrl = endpoint.BaseUrl ?? String.Empty,
                Model = endpoint.Model,
                ApiKey = endpoint.ApiKey,
                TimeoutMs = endpoint.TimeoutMs,
                Enabled = endpoint.Enabled,
                InferenceOptions = System.Text.Json.JsonSerializer.Deserialize<InferenceOptions>(System.Text.Json.JsonSerializer.Serialize(endpoint.InferenceOptions))!,
                Region = endpoint.Region,
                Project = endpoint.Project,
                ApiVersion = endpoint.ApiVersion,
                AccessKeyId = endpoint.AccessKeyId,
                AuthType = endpoint.AuthType,
                AuthUsername = endpoint.AuthUsername,
                AuthHeaderName = endpoint.AuthHeaderName,
                AuthQueryParameter = endpoint.AuthQueryParameter,
                Headers = ModelEndpointHeaderRecord.FromHeaders(endpoint.Headers),
                DiscoveredContextLength = endpoint.DiscoveredContextLength,
                DiscoveredContextSource = endpoint.DiscoveredContextSource
            };
        }

        /// <summary>
        /// The endpoint this transport form describes, credentials included.
        /// </summary>
        /// <returns>Endpoint.</returns>
        public ModelEndpoint ToEndpoint()
        {
            ModelEndpoint endpoint = new ModelEndpoint
            {
                Name = Name,
                Provider = Provider,
                Kind = Kind,
                BaseUrl = BaseUrl,
                Model = Model,
                TimeoutMs = TimeoutMs,
                Enabled = Enabled,
                InferenceOptions = System.Text.Json.JsonSerializer.Deserialize<InferenceOptions>(System.Text.Json.JsonSerializer.Serialize(InferenceOptions))!,
                Region = Region,
                Project = Project,
                ApiVersion = ApiVersion,
                AccessKeyId = AccessKeyId,
                AuthType = AuthType,
                AuthUsername = AuthUsername,
                AuthHeaderName = AuthHeaderName,
                AuthQueryParameter = AuthQueryParameter,
                Headers = ModelEndpointHeaderRecord.ToHeaders(Headers),
                DiscoveredContextLength = DiscoveredContextLength,
                DiscoveredContextSource = DiscoveredContextSource
            };
            endpoint.ApiKey = ApiKey;
            return endpoint;
        }

        #endregion
    }
}
