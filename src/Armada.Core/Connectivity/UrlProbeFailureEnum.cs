namespace Armada.Core.Connectivity
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why a <see cref="UrlProbe"/> failed.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UrlProbeFailureEnum
    {
        /// <summary>
        /// It did not fail.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// The text is not an absolute URL with a host.
        /// </summary>
        [EnumMember(Value = "InvalidUrl")]
        InvalidUrl,

        /// <summary>
        /// The scheme is not http, https, ws, or wss.
        /// </summary>
        [EnumMember(Value = "UnsupportedScheme")]
        UnsupportedScheme,

        /// <summary>
        /// The host name did not resolve.
        /// </summary>
        [EnumMember(Value = "DnsFailed")]
        DnsFailed,

        /// <summary>
        /// The host answered, but nothing listens on the port.
        /// </summary>
        [EnumMember(Value = "ConnectionRefused")]
        ConnectionRefused,

        /// <summary>
        /// The host or its network could not be reached.
        /// </summary>
        [EnumMember(Value = "Unreachable")]
        Unreachable,

        /// <summary>
        /// The probe did not finish within its timeout.
        /// </summary>
        [EnumMember(Value = "Timeout")]
        Timeout,

        /// <summary>
        /// TLS negotiation failed or the certificate was not accepted.
        /// </summary>
        [EnumMember(Value = "TlsFailed")]
        TlsFailed,

        /// <summary>
        /// The server closed the connection without answering.
        /// </summary>
        [EnumMember(Value = "ConnectionClosed")]
        ConnectionClosed,

        /// <summary>
        /// The server answered with something that is not HTTP.
        /// </summary>
        [EnumMember(Value = "NotHttp")]
        NotHttp,

        /// <summary>
        /// The server answered with an HTTP error status (other than one asking for credentials).
        /// </summary>
        [EnumMember(Value = "HttpError")]
        HttpError,

        /// <summary>
        /// The server did not accept the WebSocket upgrade.
        /// </summary>
        [EnumMember(Value = "UpgradeRejected")]
        UpgradeRejected,

        /// <summary>
        /// The caller cancelled the probe.
        /// </summary>
        [EnumMember(Value = "Cancelled")]
        Cancelled,

        /// <summary>
        /// A network error not covered above.
        /// </summary>
        [EnumMember(Value = "NetworkError")]
        NetworkError
    }
}
