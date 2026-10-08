namespace Armada.Client.Metrics
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Outcome of asking the Admiral for a Harbor's own metrics (see <see cref="HarborMetricsFeed"/>).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborMetricsLoadStateEnum
    {
        /// <summary>
        /// The metrics arrived.
        /// </summary>
        [EnumMember(Value = "Loaded")]
        Loaded,

        /// <summary>
        /// There is no Admiral address to ask (the link URL has no REST equivalent).
        /// </summary>
        [EnumMember(Value = "NoAddress")]
        NoAddress,

        /// <summary>
        /// The Harbor has no ID yet.
        /// </summary>
        [EnumMember(Value = "NoHarborId")]
        NoHarborId,

        /// <summary>
        /// The Admiral did not accept the credential (401).
        /// </summary>
        [EnumMember(Value = "Unauthorized")]
        Unauthorized,

        /// <summary>
        /// The credential may not read this Harbor (403).
        /// </summary>
        [EnumMember(Value = "Forbidden")]
        Forbidden,

        /// <summary>
        /// The Admiral has no Harbor with this ID (404 for the Harbor itself).
        /// </summary>
        [EnumMember(Value = "NotRegistered")]
        NotRegistered,

        /// <summary>
        /// The Admiral knows the Harbor but has no metrics endpoint: it is older than this Harbor and needs updating.
        /// </summary>
        [EnumMember(Value = "AdmiralOutdated")]
        AdmiralOutdated,

        /// <summary>
        /// The Admiral could not be reached (connection refused, DNS, or timeout).
        /// </summary>
        [EnumMember(Value = "Offline")]
        Offline,

        /// <summary>
        /// Any other failure (a 5xx, a malformed response).
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed
    }
}
