namespace Armada.Core.Connectivity
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One stage of a <see cref="UrlProbe"/>, in the order they run.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UrlProbeStepEnum
    {
        /// <summary>
        /// Read the URL: an absolute http, https, ws, or wss address with a host.
        /// </summary>
        [EnumMember(Value = "Parse")]
        Parse,

        /// <summary>
        /// Resolve the host name to addresses (skipped for an IP address).
        /// </summary>
        [EnumMember(Value = "Dns")]
        Dns,

        /// <summary>
        /// Open a TCP connection to the host and port.
        /// </summary>
        [EnumMember(Value = "Tcp")]
        Tcp,

        /// <summary>
        /// Negotiate TLS and check the server certificate (https and wss only).
        /// </summary>
        [EnumMember(Value = "Tls")]
        Tls,

        /// <summary>
        /// Send an HTTP GET and read the response status (http and https).
        /// </summary>
        [EnumMember(Value = "Http")]
        Http,

        /// <summary>
        /// Ask for a WebSocket upgrade and check the server's answer (ws and wss).
        /// </summary>
        [EnumMember(Value = "WebSocket")]
        WebSocket
    }
}
