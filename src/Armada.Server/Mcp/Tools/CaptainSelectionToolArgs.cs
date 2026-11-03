namespace Armada.Server.Mcp.Tools
{
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>Typed selection arguments; account identities are never accepted from callers.</summary>
    public sealed class CaptainSelectionToolArgs
    {
        /// <summary>Visible mission identifier.</summary>
        public string? MissionId { get; set; }
        /// <summary>Retained decision identifier.</summary>
        public string? DecisionId { get; set; }
        /// <summary>Explicit tenant override, global administrator only.</summary>
        public string? TenantId { get; set; }
        /// <summary>One-based page.</summary>
        public int PageNumber { get; set; } = 1;
        /// <summary>Bounded page size, default 25.</summary>
        public int PageSize { get; set; } = 25;
        /// <summary>Complete policy group for an administrator update.</summary>
        public UsageSelectionSettings? Settings { get; set; }
        /// <summary>Optional optimistic update revision.</summary>
        public long? ExpectedRevision { get; set; }
        /// <summary>Optional history filters and paging.</summary>
        public CaptainSelectionHistoryQuery? Query { get; set; }
    }
}
