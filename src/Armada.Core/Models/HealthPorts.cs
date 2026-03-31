namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Port configuration in health response.
    /// </summary>
    public class HealthPorts
    {
        /// <summary>
        /// Admiral REST API port.
        /// </summary>
        public int Admiral { get; set; }

        /// <summary>
        /// MCP server port.
        /// </summary>
        public int Mcp { get; set; }

        /// <summary>
        /// WebSocket port.
        /// </summary>
        public int WebSocket { get; set; }
    }
}
