namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// Typed view of one server entry in a keyed MCP client configuration.
    /// </summary>
    public sealed class KeyedMcpServerEntry
    {
        /// <summary>
        /// Transport type ("http").
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Server URL (Claude Code, Cursor, OpenCode).
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Server URL (Gemini).
        /// </summary>
        public string? HttpUrl { get; set; } = null;

        /// <summary>
        /// Extra request headers.
        /// </summary>
        public Dictionary<string, string>? Headers { get; set; } = null;
    }
}
