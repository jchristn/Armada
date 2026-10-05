namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Machine-readable category of an MCP tool error, so callers branch on a code instead of the English message.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum McpToolErrorCodeEnum
    {
        /// <summary>
        /// The referenced entity does not exist or is not visible to the caller.
        /// </summary>
        [EnumMember(Value = "NotFound")]
        NotFound,

        /// <summary>
        /// An argument is missing, malformed, or out of range.
        /// </summary>
        [EnumMember(Value = "InvalidArgument")]
        InvalidArgument,

        /// <summary>
        /// The entity's current state does not allow the operation (for example deleting a working captain), or the
        /// entity already exists.
        /// </summary>
        [EnumMember(Value = "Conflict")]
        Conflict,

        /// <summary>
        /// The caller is authenticated but lacks the permission the operation needs.
        /// </summary>
        [EnumMember(Value = "Forbidden")]
        Forbidden,

        /// <summary>
        /// A service the operation needs is not configured or not available on this server.
        /// </summary>
        [EnumMember(Value = "Unavailable")]
        Unavailable,

        /// <summary>
        /// The operation failed for another reason.
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed
    }
}
