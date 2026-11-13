namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Reads voyage progress (a voyage plus counts of its missions) for one voyage or one page of voyages. Every surface
    /// that reports progress (REST voyage detail and the progress enumeration, MCP voyage_status and
    /// list_voyage_progress, WebSocket get_voyage and list_voyage_progress) uses it, so none embeds a voyage's missions.
    /// Thread safe: stateless.
    /// </summary>
    public static class VoyageProgressReader
    {
        #region Public-Methods

        /// <summary>
        /// Progress for a voyage already read under the caller's scope.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="voyage">Voyage.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Voyage progress (counts only).</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task<VoyageProgress> ReadAsync(DatabaseDriver database, Voyage voyage, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (voyage == null) throw new ArgumentNullException(nameof(voyage));
            List<MissionSummary> missions = !String.IsNullOrEmpty(voyage.TenantId)
                ? await database.Missions.EnumerateSummariesByVoyageAsync(voyage.TenantId!, voyage.Id, token).ConfigureAwait(false)
                : await database.Missions.EnumerateSummariesByVoyageAsync(voyage.Id, token).ConfigureAwait(false);
            return BuildProgress(voyage, missions);
        }

        /// <summary>
        /// One page of voyage progress, paged in the database. With no Status filter the view is the active voyages
        /// (InProgress first, then Open, newest first within each, CreatedAscending reverses the time order); a Status
        /// filter selects one voyage status. The caller's tenant and user scope applies before counting.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="auth">Caller.</param>
        /// <param name="query">Paging and filters (Status, CreatedAfter, CreatedBefore, Order, and TenantId/UserId for admins).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of progress.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when Status is not a voyage status.</exception>
        public static async Task<EnumerationResult<VoyageProgress>> EnumerateAsync(DatabaseDriver database, AuthContext auth, EnumerationQuery query, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (query == null) throw new ArgumentNullException(nameof(query));

            List<PagedSection<Voyage>> sections = new List<PagedSection<Voyage>>();
            if (String.IsNullOrWhiteSpace(query.Status))
            {
                sections.Add(Section(database, auth, query, VoyageStatusEnum.InProgress.ToString()));
                sections.Add(Section(database, auth, query, VoyageStatusEnum.Open.ToString()));
            }
            else
            {
                if (!Enum.TryParse<VoyageStatusEnum>(query.Status.Trim(), true, out VoyageStatusEnum status) || !Enum.IsDefined(typeof(VoyageStatusEnum), status))
                    throw new ArgumentException("Unknown voyage status: " + query.Status, nameof(query));
                sections.Add(Section(database, auth, query, status.ToString()));
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            EnumerationResult<Voyage> voyages = await SectionedPaging.PageAsync(query, sections, token).ConfigureAwait(false);
            List<VoyageProgress> page = new List<VoyageProgress>();
            foreach (Voyage voyage in voyages.Objects)
            {
                page.Add(await ReadAsync(database, voyage, token).ConfigureAwait(false));
            }

            EnumerationResult<VoyageProgress> result = EnumerationResult<VoyageProgress>.Create(query, page, voyages.TotalRecords);
            result.TotalMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
            return result;
        }

        /// <summary>
        /// Build a voyage's progress from its missions' summaries.
        /// </summary>
        /// <param name="voyage">Voyage.</param>
        /// <param name="missions">Every mission summary of the voyage, or null for none.</param>
        /// <returns>Progress with counts only.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="voyage"/> is null.</exception>
        public static VoyageProgress BuildProgress(Voyage voyage, List<MissionSummary>? missions)
        {
            if (voyage == null) throw new ArgumentNullException(nameof(voyage));
            missions ??= new List<MissionSummary>();

            return new VoyageProgress
            {
                Voyage = voyage,
                TotalMissions = missions.Count,
                CompletedMissions = missions.Count(m =>
                    m.Status == MissionStatusEnum.Complete ||
                    m.Status == MissionStatusEnum.WorkProduced),
                FailedMissions = missions.Count(m => m.Status == MissionStatusEnum.Failed || m.Status == MissionStatusEnum.LandingFailed),
                InProgressMissions = missions.Count(m =>
                    m.Status == MissionStatusEnum.InProgress || m.Status == MissionStatusEnum.Verifying ||
                    m.Status == MissionStatusEnum.Assigned ||
                    m.Status == MissionStatusEnum.Testing ||
                    m.Status == MissionStatusEnum.Review ||
                    m.Status == MissionStatusEnum.PullRequestOpen),
                MissionCountsByStatus = missions
                    .GroupBy(m => m.Status.ToString(), StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
                VesselCount = missions
                    .Select(m => m.VesselId)
                    .Where(v => !String.IsNullOrEmpty(v))
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                VesselId = missions
                    .Where(m => !String.IsNullOrEmpty(m.VesselId))
                    .OrderBy(m => m.CreatedUtc)
                    .ThenBy(m => m.Id, StringComparer.Ordinal)
                    .Select(m => m.VesselId)
                    .FirstOrDefault()
            };
        }

        #endregion

        #region Private-Methods

        private static PagedSection<Voyage> Section(DatabaseDriver database, AuthContext auth, EnumerationQuery query, string status)
        {
            return new PagedSection<Voyage>(status, (pageNumber, pageSize, token) =>
            {
                EnumerationQuery sectionQuery = new EnumerationQuery
                {
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    Order = query.Order,
                    Status = status,
                    CreatedAfter = query.CreatedAfter,
                    CreatedBefore = query.CreatedBefore,
                    TenantId = query.TenantId,
                    UserId = query.UserId
                };
                return EnumerationScope.EnumerateScopedAsync(auth, sectionQuery,
                    q => database.Voyages.EnumerateAsync(q, token),
                    (t, q) => database.Voyages.EnumerateAsync(t, q, token),
                    (t, u, q) => database.Voyages.EnumerateAsync(t, u, q, token));
            });
        }

        #endregion
    }
}
