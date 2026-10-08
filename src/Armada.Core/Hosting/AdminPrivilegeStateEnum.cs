namespace Armada.Core.Hosting
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Whether the credential a client (Harbor) uses is an Armada administrator, as the Admiral reports it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AdminPrivilegeStateEnum
    {
        /// <summary>
        /// Not checked yet, or the Admiral's answer did not say.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown,

        /// <summary>
        /// A global administrator: server-wide operations (restart, stop, rebuild) are allowed.
        /// </summary>
        [EnumMember(Value = "Admin")]
        Admin,

        /// <summary>
        /// Signed in, but not a global administrator (a tenant administrator or an ordinary user).
        /// </summary>
        [EnumMember(Value = "NotAdmin")]
        NotAdmin,

        /// <summary>
        /// The Admiral did not accept the credential, or none was configured.
        /// </summary>
        [EnumMember(Value = "Unauthenticated")]
        Unauthenticated,

        /// <summary>
        /// The Admiral could not be reached.
        /// </summary>
        [EnumMember(Value = "Unreachable")]
        Unreachable
    }
}
