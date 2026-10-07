namespace Armada.Proxy.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Kind of route a proxy session credential is read for. The scope decides which request locations may carry the
    /// proxy session token, so a credential meant for the Admiral is never mistaken for a proxy session.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProxyCredentialScopeEnum
    {
        /// <summary>
        /// Proxy-local routes under <c>/proxy-api/*</c>: <c>X-Armada-Proxy-Session</c>, then
        /// <c>Authorization: Bearer</c>, then the session cookie. Nothing on these routes is relayed, so the
        /// Authorization header is unambiguous here.
        /// </summary>
        [EnumMember(Value = "ProxyApi")]
        ProxyApi,

        /// <summary>
        /// Relayed Admiral routes (<c>/api/v1/*</c>) and the dashboard bundle (<c>/dashboard*</c>):
        /// <c>X-Armada-Proxy-Session</c>, then the session cookie. <c>Authorization</c>, <c>X-Token</c>, and
        /// <c>X-Api-Key</c> belong to the Admiral and are relayed untouched.
        /// </summary>
        [EnumMember(Value = "Relay")]
        Relay,

        /// <summary>
        /// The relayed <c>/ws</c> upgrade: <c>X-Armada-Proxy-Session</c>, then a Sec-WebSocket-Protocol entry
        /// <c>armada-proxy-session.&lt;base64url(token)&gt;</c>, then the session cookie. <c>armada-token.*</c>
        /// entries and the query string belong to the Admiral.
        /// </summary>
        [EnumMember(Value = "WebSocket")]
        WebSocket
    }
}
