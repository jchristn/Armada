namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// A tools/call result: content items and the isError flag.
    /// </summary>
    public class E2eMcpToolResult
    {
        #region Public-Members

        /// <summary>
        /// Content items.
        /// </summary>
        public List<McpContentItem> Content { get; set; } = new List<McpContentItem>();

        /// <summary>
        /// True when the tool call failed.
        /// </summary>
        public bool IsError { get; set; } = false;

        #endregion
    }
}
