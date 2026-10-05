namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why a requested ApproveInArmada policy runs as Refuse.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CliPermissionFallbackReasonEnum
    {
        /// <summary>
        /// The captain's runtime has no permission prompt hook Armada can drive.
        /// </summary>
        [EnumMember(Value = "RuntimeUnsupported")]
        RuntimeUnsupported,

        /// <summary>
        /// The launch has no mission- or thread-scoped MCP token, so the captain cannot reach Armada's permission prompt tool.
        /// </summary>
        [EnumMember(Value = "NoSessionToken")]
        NoSessionToken,

        /// <summary>
        /// The captain runs on a Harbor host, whose launch protocol does not carry the permission prompt tool yet.
        /// </summary>
        [EnumMember(Value = "RemoteHarbor")]
        RemoteHarbor
    }
}
