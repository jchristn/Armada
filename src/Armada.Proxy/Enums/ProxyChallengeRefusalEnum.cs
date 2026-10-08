namespace Armada.Proxy.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why the proxy refused to issue a login challenge (GET /proxy-api/v1/auth/challenge). Challenges are
    /// unauthenticated, so the store of outstanding ones is bounded per client address and in total.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProxyChallengeRefusalEnum
    {
        /// <summary>
        /// This client address already holds the maximum number of unused, unexpired challenges (HTTP 429).
        /// </summary>
        [EnumMember(Value = "AddressLimit")]
        AddressLimit,

        /// <summary>
        /// The proxy holds the maximum number of unused, unexpired challenges across all clients (HTTP 503).
        /// </summary>
        [EnumMember(Value = "GlobalLimit")]
        GlobalLimit
    }
}
