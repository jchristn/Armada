namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Result of the MCP inbox tool: item counts and the items.
    /// </summary>
    public class E2eMcpInboxResult
    {
        #region Public-Members

        /// <summary>
        /// Number of items.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Inbox items, most urgent first.
        /// </summary>
        public List<InboxItem> Items { get; set; } = new List<InboxItem>();

        #endregion
    }
}
