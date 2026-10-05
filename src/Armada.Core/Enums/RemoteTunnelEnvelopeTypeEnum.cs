namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Kind of a remote tunnel envelope. On the wire the kind is the lowercase name in
    /// <see cref="Armada.Core.Models.RemoteTunnelEnvelope.Type"/>; convert with
    /// <see cref="Armada.Core.RemoteTunnelProtocol.ToWireType(RemoteTunnelEnvelopeTypeEnum)"/> and
    /// <see cref="Armada.Core.RemoteTunnelProtocol.ParseEnvelopeType(string?)"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RemoteTunnelEnvelopeTypeEnum
    {
        /// <summary>
        /// Missing or unrecognized type.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown,

        /// <summary>
        /// Request that expects a response with the same correlation id.
        /// </summary>
        [EnumMember(Value = "Request")]
        Request,

        /// <summary>
        /// Response to a request.
        /// </summary>
        [EnumMember(Value = "Response")]
        Response,

        /// <summary>
        /// Unsolicited event.
        /// </summary>
        [EnumMember(Value = "Event")]
        Event,

        /// <summary>
        /// Keepalive ping.
        /// </summary>
        [EnumMember(Value = "Ping")]
        Ping,

        /// <summary>
        /// Keepalive pong.
        /// </summary>
        [EnumMember(Value = "Pong")]
        Pong,

        /// <summary>
        /// Error, optionally correlated with a request.
        /// </summary>
        [EnumMember(Value = "Error")]
        Error,

        /// <summary>
        /// Subscription request.
        /// </summary>
        [EnumMember(Value = "Subscribe")]
        Subscribe,

        /// <summary>
        /// Subscription cancellation.
        /// </summary>
        [EnumMember(Value = "Unsubscribe")]
        Unsubscribe
    }
}
