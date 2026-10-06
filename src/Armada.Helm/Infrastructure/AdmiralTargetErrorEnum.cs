namespace Armada.Helm.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why the CLI could not use, or refused, the selected Admiral target.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AdmiralTargetErrorEnum
    {
        /// <summary>
        /// The server URL is not an absolute http or https URL.
        /// </summary>
        InvalidServerUrl,

        /// <summary>
        /// The named profile does not exist.
        /// </summary>
        UnknownProfile,

        /// <summary>
        /// Options that cannot be combined (for example <c>--server</c> with <c>--profile</c>).
        /// </summary>
        ConflictingOptions,

        /// <summary>
        /// The profile name is reserved (<c>local</c>).
        /// </summary>
        ReservedProfileName,

        /// <summary>
        /// The command acts only on this machine's Admiral and a remote target is selected.
        /// </summary>
        LocalOnlyCommand,

        /// <summary>
        /// The remote Admiral did not answer its health endpoint.
        /// </summary>
        Unreachable,

        /// <summary>
        /// The Admiral answered 401: no credential, or the credential was rejected.
        /// </summary>
        Unauthorized,

        /// <summary>
        /// The Admiral answered 403: the credential is valid but lacks permission.
        /// </summary>
        Forbidden,

        /// <summary>
        /// A remote operation needs a credential and none was supplied.
        /// </summary>
        MissingToken
    }
}
