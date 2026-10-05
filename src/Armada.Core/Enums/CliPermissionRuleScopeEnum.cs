namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a CLI permission rule applies.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CliPermissionRuleScopeEnum
    {
        /// <summary>
        /// Every captain of the rule's tenant (or of every tenant when the rule has no tenant; global admins only).
        /// </summary>
        [EnumMember(Value = "Global")]
        Global,

        /// <summary>
        /// Missions on one vessel.
        /// </summary>
        [EnumMember(Value = "Vessel")]
        Vessel,

        /// <summary>
        /// One captain (missions and Ask turns).
        /// </summary>
        [EnumMember(Value = "Captain")]
        Captain
    }
}
