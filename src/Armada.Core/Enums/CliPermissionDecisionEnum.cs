namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A decision on a pending CLI permission request.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CliPermissionDecisionEnum
    {
        /// <summary>
        /// Allow this one call.
        /// </summary>
        [EnumMember(Value = "AllowOnce")]
        AllowOnce,

        /// <summary>
        /// Allow this call and store an allow rule (admins only).
        /// </summary>
        [EnumMember(Value = "AllowAndRemember")]
        AllowAndRemember,

        /// <summary>
        /// Deny this call.
        /// </summary>
        [EnumMember(Value = "Deny")]
        Deny
    }
}
