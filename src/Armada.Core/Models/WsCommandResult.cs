namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// WebSocket command result envelope.
    /// </summary>
    public class WsCommandResult
    {
        /// <summary>
        /// Message type (e.g. "command.result", "command.error").
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// Action name (e.g. "list_fleets", "get_mission").
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        /// Error message (for command.error responses).
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Optional warning.
        /// </summary>
        public string? Warning { get; set; }
    }
}
