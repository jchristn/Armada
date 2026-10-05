namespace Armada.Server.RuntimeTools
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How JSON-RPC messages are framed on an MCP stdio server's standard input and output.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum McpStdioFramingEnum
    {
        /// <summary>
        /// One JSON message per newline-terminated line (the MCP stdio transport).
        /// </summary>
        JsonLine,

        /// <summary>
        /// LSP-style "Content-Length" header followed by the JSON payload.
        /// </summary>
        ContentLength
    }
}
