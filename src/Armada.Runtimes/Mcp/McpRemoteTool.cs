namespace Armada.Runtimes.Mcp
{
    /// <summary>
    /// A tool advertised by a remote MCP server, as returned from a <c>tools/list</c> call. Carries the
    /// tool's name, human-readable description, and its JSON-Schema input definition (serialized JSON), so
    /// the caller can present it to a model and later route a matching tool call back to the server.
    /// </summary>
    public class McpRemoteTool
    {
        #region Public-Members

        /// <summary>
        /// The tool name as advertised by the server (used verbatim when invoking <c>tools/call</c>).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable description of what the tool does; may be empty.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The tool's JSON-Schema input definition as serialized JSON. Empty when the server advertised no
        /// schema; callers should substitute an empty object schema in that case.
        /// </summary>
        public string InputSchemaJson { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public McpRemoteTool()
        {
        }

        #endregion
    }
}
