namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a matching CLI permission rule does.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CliPermissionRuleActionEnum
    {
        /// <summary>
        /// Allow without prompting.
        /// </summary>
        [EnumMember(Value = "Allow")]
        Allow,

        /// <summary>
        /// Deny without prompting. Deny rules win over allow rules.
        /// </summary>
        [EnumMember(Value = "Deny")]
        Deny
    }
}
