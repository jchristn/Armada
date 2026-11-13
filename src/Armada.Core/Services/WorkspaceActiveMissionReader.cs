namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Reads the active missions (Pending, Assigned, InProgress, Review, Testing) of one vessel for Workspace, one page at
    /// a time from the database: each active status is counted and read with a bounded LIMIT/OFFSET query (newest
    /// first, id tie-break), and the pages are merged in the same order. Each mission's scoped files are parsed from its
    /// description ("Touch only ...", "Edit only ...", "Modify only ...").
    /// Thread safe: holds no mutable state.
    /// </summary>
    public class WorkspaceActiveMissionReader
    {
        #region Public-Members

        /// <summary>
        /// The mission statuses Workspace treats as active.
        /// </summary>
        public static readonly IReadOnlyList<MissionStatusEnum> ActiveStatuses = new List<MissionStatusEnum>
        {
            MissionStatusEnum.Pending,
            MissionStatusEnum.Assigned,
            MissionStatusEnum.InProgress,
            MissionStatusEnum.Review,
            MissionStatusEnum.Testing
        };

        #endregion

        #region Private-Members

        private static readonly Regex _ScopedFilesDirectiveRegex =
            new Regex(@"^\s*(?:Touch|Edit|Modify)\s+only\s+(?<files>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex _ScopedFileTokenRegex =
            new Regex(
                @"(?<path>(?:[A-Za-z0-9_.-]+[\\/])+[A-Za-z0-9_.-]+|[A-Za-z0-9_.-]+\.(?:cs|csproj|sln|md|json|yaml|yml|ts|tsx|js|jsx|css|html|sh|bat))",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public WorkspaceActiveMissionReader(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Count a vessel's active missions in the caller's scope (one COUNT per active status).
        /// </summary>
        /// <param name="tenantId">Tenant scope, or null for every tenant (global admin).</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The count.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vesselId is null or empty.</exception>
        public async Task<int> CountAsync(string? tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            long total = 0;
            foreach (MissionStatusEnum status in ActiveStatuses)
            {
                EnumerationResult<Mission> page = await ReadAsync(tenantId, vesselId, status, 1, 1, token).ConfigureAwait(false);
                total += page.TotalRecords;
            }

            return (int)Math.Min(total, Int32.MaxValue);
        }

        /// <summary>
        /// Read one page of a vessel's active missions in the caller's scope, newest first (id tie-break).
        /// </summary>
        /// <param name="tenantId">Tenant scope, or null for every tenant (global admin).</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="query">Paging (page size 1 to 1000); null is the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page with an exact total.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vesselId is null or empty.</exception>
        public async Task<EnumerationResult<WorkspaceActiveMission>> EnumerateAsync(string? tenantId, string vesselId, EnumerationQuery? query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (query == null) query = new EnumerationQuery();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            // Each status contributes at most Offset + PageSize rows to the merged page; read only that many from each,
            // in bounded pages, never the whole table.
            int needed = query.Offset + query.PageSize;
            long total = 0;
            List<Mission> candidates = new List<Mission>();
            foreach (MissionStatusEnum status in ActiveStatuses)
            {
                int read = 0;
                int pageNumber = 1;
                while (read < needed)
                {
                    token.ThrowIfCancellationRequested();
                    int pageSize = Math.Min(CollectionLimits.MaxPageSize, needed);
                    EnumerationResult<Mission> page = await ReadAsync(tenantId, vesselId, status, pageNumber, pageSize, token).ConfigureAwait(false);
                    if (pageNumber == 1) total += page.TotalRecords;
                    candidates.AddRange(page.Objects);
                    read += page.Objects.Count;
                    if (page.Objects.Count < pageSize || pageNumber >= page.TotalPages) break;
                    pageNumber++;
                }
            }

            List<WorkspaceActiveMission> rows = candidates
                .OrderByDescending(m => m.CreatedUtc)
                .ThenByDescending(m => m.Id, StringComparer.Ordinal)
                .Skip(query.Offset)
                .Take(query.PageSize)
                .Select(ToActiveMission)
                .ToList();

            EnumerationResult<WorkspaceActiveMission> result = new EnumerationResult<WorkspaceActiveMission>();
            result.PageNumber = query.PageNumber;
            result.PageSize = query.PageSize;
            result.TotalRecords = total;
            result.TotalPages = (int)Math.Ceiling((double)total / query.PageSize);
            result.Objects = rows;
            result.TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
            return result;
        }

        /// <summary>
        /// Parse the repository-relative scoped files named in a mission description, distinct and in order.
        /// </summary>
        /// <param name="description">Mission description, or null.</param>
        /// <returns>The scoped paths.</returns>
        public static List<string> ParseScopedFiles(string? description)
        {
            List<string> files = new List<string>();
            if (String.IsNullOrWhiteSpace(description)) return files;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match directiveMatch in _ScopedFilesDirectiveRegex.Matches(description))
            {
                string fileSegment = directiveMatch.Groups["files"].Value;
                foreach (Match pathMatch in _ScopedFileTokenRegex.Matches(fileSegment))
                {
                    string normalizedPath = (pathMatch.Groups["path"].Value ?? String.Empty).Trim().Replace('\\', '/');
                    if (!String.IsNullOrWhiteSpace(normalizedPath) && seen.Add(normalizedPath)) files.Add(normalizedPath);
                }
            }

            return files;
        }

        #endregion

        #region Private-Methods

        private Task<EnumerationResult<Mission>> ReadAsync(string? tenantId, string vesselId, MissionStatusEnum status, int pageNumber, int pageSize, CancellationToken token)
        {
            EnumerationQuery query = new EnumerationQuery
            {
                VesselId = vesselId,
                Status = status.ToString(),
                PageNumber = pageNumber,
                PageSize = pageSize,
                Order = EnumerationOrderEnum.CreatedDescending
            };
            return String.IsNullOrEmpty(tenantId)
                ? _Database.Missions.EnumerateAsync(query, token)
                : _Database.Missions.EnumerateAsync(tenantId, query, token);
        }

        private static WorkspaceActiveMission ToActiveMission(Mission mission)
        {
            List<string> scoped = ParseScopedFiles(mission.Description);
            return new WorkspaceActiveMission
            {
                MissionId = mission.Id,
                Title = mission.Title ?? String.Empty,
                Status = mission.Status.ToString(),
                ScopedFiles = scoped.Take(CollectionLimits.MaxConfigItems).ToList(),
                ScopedFileCount = scoped.Count
            };
        }

        #endregion
    }
}
