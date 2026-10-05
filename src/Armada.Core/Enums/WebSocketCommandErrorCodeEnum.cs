namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Machine-readable reason carried in the <c>code</c> field of a WebSocket <c>command.error</c> reply. Branch on
    /// this, not on the English <c>error</c> text.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum WebSocketCommandErrorCodeEnum
    {
        /// <summary>
        /// The action is not a command the handler accepts.
        /// </summary>
        UnknownAction,

        /// <summary>
        /// The entity the command names does not exist.
        /// </summary>
        NotFound,

        /// <summary>
        /// A required field is missing or a value is invalid.
        /// </summary>
        InvalidArgument,

        /// <summary>
        /// The entity's current state does not allow the command (for example deleting a working captain).
        /// </summary>
        Conflict,

        /// <summary>
        /// The caller is not allowed to run the command.
        /// </summary>
        Forbidden,

        /// <summary>
        /// A service or data the command needs is not available on this server.
        /// </summary>
        Unavailable,

        /// <summary>
        /// The command failed unexpectedly.
        /// </summary>
        InternalError
    }
}
