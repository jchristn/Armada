namespace Armada.Core.Harbor
{
    using Armada.Core.Enums;

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

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborInferenceEndpoint()
        {
        }

        #endregion
    }
}
