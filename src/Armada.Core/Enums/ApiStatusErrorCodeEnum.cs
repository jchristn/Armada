namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// REST error codes for HTTP statuses that the web framework's error code set does not cover. Each value names its
    /// status so that the <c>Error</c> field of an error body always matches the HTTP status.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApiStatusErrorCodeEnum
    {
        /// <summary>
        /// 422: the request was valid but the operation was rejected (for example a git push or merge refused by the
        /// remote).
        /// </summary>
        UnprocessableEntity,

        /// <summary>
        /// 501: the operation is not supported here (for example a planning or refinement session on a runtime that
        /// does not support it, or model context building on a server without it).
        /// </summary>
        NotImplemented,

        /// <summary>
        /// 503: a service the operation needs is not available on this server.
        /// </summary>
        ServiceUnavailable,

        /// <summary>
        /// 504: the operation timed out waiting on a dependency.
        /// </summary>
        GatewayTimeout
    }
}
