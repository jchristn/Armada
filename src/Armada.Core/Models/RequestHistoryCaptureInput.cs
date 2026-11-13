using System;
using System.Collections.Generic;

namespace Armada.Core.Models
{
    using Armada.Core.Services;

    /// <summary>
    /// Primitive request and response fields used to build a request-history record.
    /// </summary>
    public class RequestHistoryCaptureInput
    {
        #region Public-Members

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; set; } = "GET";

        /// <summary>
        /// Matched or raw route path.
        /// </summary>
        public string Route { get; set; } = "/";

        /// <summary>
        /// Optional route template.
        /// </summary>
        public string? RouteTemplate { get; set; } = null;

        /// <summary>
        /// Optional raw query string.
        /// </summary>
        public string? QueryString { get; set; } = null;

        /// <summary>
        /// Response status code.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Total request duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Request size in bytes.
        /// </summary>
        public long RequestSizeBytes { get; set; } = 0;

        /// <summary>
        /// Response size in bytes.
        /// </summary>
        public long ResponseSizeBytes { get; set; } = 0;

        /// <summary>
        /// Request content type.
        /// </summary>
        public string? RequestContentType { get; set; } = null;

        /// <summary>
        /// Response content type.
        /// </summary>
        public string? ResponseContentType { get; set; } = null;

        /// <summary>
        /// Remote client IP address if known.
        /// </summary>
        public string? ClientIp { get; set; } = null;

        /// <summary>
        /// Optional correlation identifier.
        /// </summary>
        public string? CorrelationId { get; set; } = null;

        /// <summary>
        /// Request headers captured during execution.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "Captured headers past the limit are counted in an X-Armada-Omitted-Headers entry")]
        public Dictionary<string, string?> RequestHeaders { get; set; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Response headers captured during execution.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "Captured headers past the limit are counted in an X-Armada-Omitted-Headers entry")]
        public Dictionary<string, string?> ResponseHeaders { get; set; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Request body text snapshot.
        /// </summary>
        public string? RequestBodyText { get; set; } = null;

        /// <summary>
        /// Response body text snapshot.
        /// </summary>
        public string? ResponseBodyText { get; set; } = null;

        #endregion
    }
}
