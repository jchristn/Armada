namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where an effective CLI permission policy came from (most specific first).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CliPermissionPolicySourceEnum
    {
        /// <summary>
        /// The Ask thread's own override.
        /// </summary>
        [EnumMember(Value = "AskThread")]
        AskThread,

        /// <summary>
        /// The vessel's legacy AutoApprove override (missions on that vessel): true maps to Bypass; false caps the result below Bypass.
        /// </summary>
        [EnumMember(Value = "VesselAutoApprove")]
        VesselAutoApprove,

        /// <summary>
        /// The captain's CliPermissionPolicy.
        /// </summary>
        [EnumMember(Value = "Captain")]
        Captain,

        /// <summary>
        /// The captain's legacy autoApprove runtime option: true maps to Bypass, false to Refuse (for Ask turns only when Ask.CaptainAutoApprove is on).
        /// </summary>
        [EnumMember(Value = "CaptainAutoApprove")]
        CaptainAutoApprove,

        /// <summary>
        /// The server default (Permissions.AskDefaultPolicy or Permissions.MissionDefaultPolicy).
        /// </summary>
        [EnumMember(Value = "ServerDefault")]
        ServerDefault
    }
}
