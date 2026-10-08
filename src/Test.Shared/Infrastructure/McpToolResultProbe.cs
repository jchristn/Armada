namespace Test.Shared.Infrastructure
{
    using System;
    using System.Text.Json;
    using Armada.Core.Enums;

    /// <summary>
    /// The fields of an Armada MCP tool's JSON result that tests branch on: the typed error (<see cref="ErrorCode"/>)
    /// and the counts delete and purge tools return. Built by deserializing the result text, never by matching it.
    /// </summary>
    public class McpToolResultProbe
    {
        #region Public-Members

        /// <summary>
        /// English error message, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Typed error category, or null when the tool succeeded.
        /// </summary>
        public McpToolErrorCodeEnum? ErrorCode { get; set; } = null;

        /// <summary>
        /// Feature-specific error code (for example DuplicateEntity), or null.
        /// </summary>
        public string? Code { get; set; } = null;

        /// <summary>
        /// Rows deleted by a delete tool, or null.
        /// </summary>
        public int? Deleted { get; set; } = null;

        /// <summary>
        /// Entries purged by a purge tool, or null.
        /// </summary>
        public int? EntriesPurged { get; set; } = null;

        /// <summary>
        /// Status a tool reports (for example stop_all), or null.
        /// </summary>
        public string? Status { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Deserialize a tool result's text. Returns an empty probe when the text is not a JSON object (for example a
        /// plain-text result or an error the server wrote as text).
        /// </summary>
        /// <param name="result">Tool call result.</param>
        /// <returns>Probe.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="result"/> is null.</exception>
        public static McpToolResultProbe From(Armada.Runtimes.Mcp.McpToolCallResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return FromText(result.Text);
        }

        /// <summary>
        /// Deserialize a tool result's text content. Returns an empty probe when the text is not a JSON object.
        /// </summary>
        /// <param name="text">Tool result text.</param>
        /// <returns>Probe.</returns>
        public static McpToolResultProbe FromText(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return new McpToolResultProbe();
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.PropertyNameCaseInsensitive = true;
                return JsonSerializer.Deserialize<McpToolResultProbe>(text, options) ?? new McpToolResultProbe();
            }
            catch (JsonException)
            {
                // Not a JSON object (a plain-text result): no typed fields.
                return new McpToolResultProbe();
            }
        }

        #endregion
    }
}
