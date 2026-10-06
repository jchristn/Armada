namespace Armada.Helm.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where the credential the CLI sends to the Admiral came from.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AdmiralCredentialSourceEnum
    {
        /// <summary>
        /// No credential is sent.
        /// </summary>
        None,

        /// <summary>
        /// The local Admiral's API key from settings.json (sent as X-Api-Key).
        /// </summary>
        LocalApiKey,

        /// <summary>
        /// The <c>--token</c> option.
        /// </summary>
        Flag,

        /// <summary>
        /// The <c>ARMADA_TOKEN</c> environment variable.
        /// </summary>
        Environment,

        /// <summary>
        /// The token stored for the profile in the shared credential store (OS keychain or 0600 file).
        /// </summary>
        ProfileStore
    }
}
