namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The MCP part of an OpenCode config document.
    /// </summary>
    public class OpenCodeConfigFile
    {
        #region Public-Members

        /// <summary>
        /// MCP servers by name.
        /// </summary>
        [JsonPropertyName("mcp")]
        public Dictionary<string, OpenCodeMcpEntry>? Mcp { get; set; } = null;

        #endregion
    }
}
