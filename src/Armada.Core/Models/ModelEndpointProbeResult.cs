namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Outcome of probing a model endpoint: either a lightweight connectivity health check, or a full
    /// validation that issued a real embedding or completion request against the configured model.
    /// </summary>
    public class ModelEndpointProbeResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the probe succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// The endpoint's base URL that was probed.
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// Round-trip latency in milliseconds observed by the probe.
        /// </summary>
        public long LatencyMs { get; set; } = 0;

        /// <summary>
        /// HTTP status code returned by the provider, when available.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Error detail when the probe failed, or null on success.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// For a validation of an embedding endpoint, the number of dimensions returned by the model.
        /// </summary>
        public int? EmbeddingDimensions { get; set; } = null;

        /// <summary>
        /// For a validation of an inference endpoint, a short excerpt of the model's response text.
        /// </summary>
        public string? SampleText { get; set; } = null;

        /// <summary>
        /// Where the check ran and what it could not cover, or null. Set when the endpoint's address is local to the
        /// Admiral host, since an API captain running on a Harbor calls that address on the Harbor's computer instead.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Context window the model server reported during validation, in tokens, or null (RS-7.8). Read only when the
        /// endpoint configures no context length.
        /// </summary>
        public int? DiscoveredContextLength { get; set; } = null;

        /// <summary>
        /// Where <see cref="DiscoveredContextLength"/> was read, or null.
        /// </summary>
        public string? DiscoveredContextSource { get; set; } = null;

        /// <summary>
        /// Why discovery found no window, or null when it found one or did not run.
        /// </summary>
        public string? ContextDiscoveryNote { get; set; } = null;

        /// <summary>
        /// When the probe ran (UTC).
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelEndpointProbeResult()
        {
        }

        #endregion
    }
}
