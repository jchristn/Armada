namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Typed MCP content item.
    /// </summary>
    public sealed class McpContentItem
    {
        /// <summary>
        /// Content type, for example text.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Text payload.
        /// </summary>
        public string? Text { get; set; } = null;
    }
}
