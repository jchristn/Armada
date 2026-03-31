namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// WebSocket broadcast message.
    /// </summary>
    public class WsBroadcast
    {
        /// <summary>
        /// Message type (e.g. "mission.changed", "voyage.changed").
        /// </summary>
        public string? Type { get; set; }
    }
}
