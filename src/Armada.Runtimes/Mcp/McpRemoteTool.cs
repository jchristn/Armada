namespace Armada.Runtimes.Mcp
{
    using System.Text.Json.Serialization;
    using Armada.Core.Protocol;

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
        [JsonPropertyName("name")]
        public string Name
        {
            get { return _Name; }
            set { _Name = value ?? string.Empty; }
        }

        /// <summary>
        /// Human-readable description of what the tool does; may be empty.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description
        {
            get { return _Description; }
            set { _Description = value ?? string.Empty; }
        }

        /// <summary>
        /// The tool's JSON-Schema input definition as serialized JSON. Empty when the server advertised no
        /// schema (or a schema that is not a JSON object); callers should substitute an empty object schema in that case.
        /// </summary>
        [JsonPropertyName("inputSchema")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        public string InputSchemaJson
        {
            get { return _InputSchemaJson; }
            set { _InputSchemaJson = value != null && value.TrimStart().StartsWith("{", System.StringComparison.Ordinal) ? value : string.Empty; }
        }

        #endregion

        #region Private-Members

        private string _Name = string.Empty;
        private string _Description = string.Empty;
        private string _InputSchemaJson = string.Empty;

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
