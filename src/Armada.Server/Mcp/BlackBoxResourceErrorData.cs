namespace Armada.Server.Mcp
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// The data of the JSON-RPC error a black box resource read returns: the uri and the same typed error a black box
    /// tool reports (ErrorCode and Code).
    /// </summary>
    public class BlackBoxResourceErrorData
    {
        /// <summary>
        /// The resource uri that was read.
        /// </summary>
        public string Uri { get; set; } = String.Empty;

        /// <summary>
        /// English message.
        /// </summary>
        public string Error { get; set; } = String.Empty;

        /// <summary>
        /// Machine-readable category (NotFound, Conflict, Unavailable).
        /// </summary>
        public McpToolErrorCodeEnum ErrorCode { get; set; } = McpToolErrorCodeEnum.Failed;

        /// <summary>
        /// Black box code (for example BlackBoxNotFound or BlackBoxObjectMissing), or null.
        /// </summary>
        public string? Code { get; set; } = null;
    }
}
