namespace Armada.Core.Models
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// Generic tunnel envelope shared by Armada instances and the proxy.
    /// </summary>
    public class RemoteTunnelEnvelope
    {
        #region Public-Members

        /// <summary>
        /// Envelope type: request, response, event, ping, pong, error, subscribe, unsubscribe.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Parsed <see cref="Type"/>; <see cref="RemoteTunnelEnvelopeTypeEnum.Unknown"/> when missing or unrecognized.
        /// Not serialized.
        /// </summary>
        [JsonIgnore]
        public RemoteTunnelEnvelopeTypeEnum Kind
        {
            get => RemoteTunnelProtocol.ParseEnvelopeType(Type);
        }

        /// <summary>
        /// Correlation identifier for request/response pairing.
        /// </summary>
        public string? CorrelationId { get; set; } = null;

        /// <summary>
        /// Method or event name associated with this envelope.
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// Envelope creation timestamp in UTC.
        /// </summary>
        public DateTime? TimestampUtc { get; set; } = null;

        /// <summary>
        /// Optional response status code.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Optional success indicator for response envelopes.
        /// </summary>
        public bool? Success { get; set; } = null;

        /// <summary>
        /// Optional machine-readable error code.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Optional human-readable message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Optional original requester IP forwarded by Armada.Proxy for tunneled commands.
        /// </summary>
        public string? RequesterIp { get; set; } = null;

        /// <summary>
        /// Optional JSON payload.
        /// </summary>
        public JsonElement? Payload { get; set; } = null;

        #endregion
    }
}
