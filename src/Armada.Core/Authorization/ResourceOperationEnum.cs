namespace Armada.Core.Authorization
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Operation a REST route or MCP tool performs on its resource type (AUTHENTICATION.md operation types).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ResourceOperationEnum
    {
        /// <summary>
        /// Retrieve, list, enumerate, search, or validate without side effects.
        /// </summary>
        Read,

        /// <summary>
        /// Create a new resource.
        /// </summary>
        Create,

        /// <summary>
        /// Modify an existing resource.
        /// </summary>
        Update,

        /// <summary>
        /// Remove one or more resources.
        /// </summary>
        Delete,

        /// <summary>
        /// Run or trigger an operation (dispatch, stop, process, run a command).
        /// </summary>
        Execute,

        /// <summary>
        /// Privileged control-plane operation (server lifecycle, settings, backup, restore).
        /// </summary>
        Admin
    }
}
