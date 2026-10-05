namespace Armada.Server.RuntimeTools
{
    using System;
    using System.Text.Json.Serialization;
    using Armada.Runtimes.Mcp;

    /// <summary>
    /// A <c>tools/list</c> result that also accepts the non-standard snake_case <c>next_cursor</c> member some servers
    /// send in place of <c>nextCursor</c>.
    /// </summary>
    public class RuntimeMcpListToolsResult : McpListToolsResult
    {
        #region Public-Members

        /// <summary>
        /// Non-standard snake_case cursor for the next page, or null.
        /// </summary>
        [JsonPropertyName("next_cursor")]
        public string? SnakeCaseNextCursor { get; set; } = null;

        /// <summary>
        /// The cursor for the next page from either member, or null on the last page.
        /// </summary>
        [JsonIgnore]
        public string? EffectiveNextCursor
        {
            get { return !String.IsNullOrEmpty(NextCursor) ? NextCursor : SnakeCaseNextCursor; }
        }

        #endregion
    }
}
