namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The <c>params</c> members of a JSON-RPC request that <see cref="McpStubServerHandler"/> reads.
    /// </summary>
    public sealed class McpStubRequestParams
    {
        #region Public-Members

        /// <summary>
        /// Pagination cursor of a <c>tools/list</c> request, or null for the first page.
        /// </summary>
        [JsonPropertyName("cursor")]
        public string? Cursor { get; set; } = null;

        #endregion
    }
}
