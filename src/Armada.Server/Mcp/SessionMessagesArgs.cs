namespace Armada.Server.Mcp
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MCP arguments for reading one page of a planning or refinement session transcript.
    /// </summary>
    public class SessionMessagesArgs
    {
        /// <summary>
        /// Session identifier (psn_ or ors_ prefix).
        /// </summary>
        public string SessionId { get; set; } = String.Empty;

        /// <summary>
        /// 1-based page number. Default 1.
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Page size, 1 to 1000. Default 10.
        /// </summary>
        public int? PageSize { get; set; } = null;

        /// <summary>
        /// CreatedDescending (newest first, the default) or CreatedAscending (oldest first).
        /// </summary>
        public string? Order { get; set; } = null;

        /// <summary>
        /// Build the paging query.
        /// </summary>
        /// <returns>The query.</returns>
        public EnumerationQuery ToQuery()
        {
            EnumerationQuery query = new EnumerationQuery();
            if (PageNumber.HasValue) query.PageNumber = PageNumber.Value;
            if (PageSize.HasValue) query.PageSize = PageSize.Value;
            if (!String.IsNullOrWhiteSpace(Order) && Enum.TryParse<EnumerationOrderEnum>(Order, true, out EnumerationOrderEnum order)) query.Order = order;
            return query;
        }
    }
}
