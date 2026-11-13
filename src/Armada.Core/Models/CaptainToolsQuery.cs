namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Paging for a captain's tool inventory (GET /api/v1/captains/{id}/tools, POST .../tools/enumerate, MCP
    /// get_captain_tools): the standard <see cref="EnumerationQuery"/> PageNumber and PageSize (1 to 1000, default 10) for
    /// the Tools page, an optional source-kind filter, and whether to probe the runtime again instead of reusing the
    /// recent snapshot.
    /// </summary>
    public class CaptainToolsQuery : EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Only tools of this source kind (McpServer, RuntimeBuiltIn, or Internal), or null for every tool. Applied
        /// before counting, so Tools.TotalRecords counts the filtered tools.
        /// </summary>
        public CaptainToolSourceKindEnum? SourceKind { get; set; } = null;

        /// <summary>
        /// When true, probe the captain's runtime and MCP servers again. When false (default), a snapshot taken for the
        /// same captain within <see cref="SnapshotReuseSeconds"/> is reused, so reading further pages does not probe again.
        /// </summary>
        public bool Refresh { get; set; } = false;

        /// <summary>
        /// How long a probe snapshot is reused for paging, in seconds (60).
        /// </summary>
        public const int SnapshotReuseSeconds = 60;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainToolsQuery()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Apply the querystring values: the standard paging keys plus sourceKind and refresh.
        /// </summary>
        /// <param name="queryGetter">Returns a querystring value by key, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when queryGetter is null.</exception>
        /// <exception cref="ArgumentException">Thrown when sourceKind is not a known source kind.</exception>
        public void ApplyToolQuerystring(Func<string, string?> queryGetter)
        {
            if (queryGetter == null) throw new ArgumentNullException(nameof(queryGetter));
            ApplyQuerystringOverrides(queryGetter);
            string? kind = queryGetter("sourceKind");
            if (!String.IsNullOrWhiteSpace(kind))
            {
                if (!EnumNames.TryParse(kind.Trim(), true, out CaptainToolSourceKindEnum parsed))
                    throw new ArgumentException("sourceKind must be McpServer, RuntimeBuiltIn, or Internal.");
                SourceKind = parsed;
            }

            string? refresh = queryGetter("refresh");
            if (!String.IsNullOrWhiteSpace(refresh)) Refresh = String.Equals(refresh.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
