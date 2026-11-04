namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>Caller-scoped usage access shared by REST and MCP.</summary>
    public class UsageAccessService
    {
        private readonly DatabaseDriver _Database;
        private readonly UsageCaptureService _Capture;
        private readonly UsageCaptureScheduler _Scheduler;
        private readonly UsageAggregationService _Aggregation;
        private readonly HarborConnectionManager? _Harbors;
        private readonly Armada.Core.Settings.UsageCaptureSettings _Settings;

        /// <summary>UsageAccessService: account usage contract.</summary>
        public UsageAccessService(DatabaseDriver database, UsageCaptureService capture, UsageCaptureScheduler scheduler, Armada.Core.Settings.UsageCaptureSettings? settings = null, HarborConnectionManager? harbors = null)
        {
            _Database = database;
            _Capture = capture;
            _Scheduler = scheduler;
            _Settings = settings ?? new Armada.Core.Settings.UsageCaptureSettings();
            _Aggregation = new UsageAggregationService(database, _Settings);
            _Harbors = harbors;
        }

        /// <summary>ScopeAsync: account usage contract.</summary>
        public async Task ScopeAsync(AuthContext caller, UsageQuery query)
        {
            if (!caller.IsAuthenticated) throw new UnauthorizedAccessException("Authentication required.");
            if (!String.IsNullOrEmpty(query.TenantId) && query.TenantId != caller.TenantId && !caller.IsAdmin)
                throw new UnauthorizedAccessException("An explicit foreign tenant requires a global admin.");
            query.TenantId = String.IsNullOrEmpty(query.TenantId) ? caller.TenantId : query.TenantId;
            if (String.IsNullOrEmpty(query.TenantId)) throw new UnauthorizedAccessException("A tenant is required.");
            if (query.CaptainIds != null)
            {
                if (query.CaptainIds.Count < 1 || query.CaptainIds.Count > 100 || query.CaptainIds.Any(String.IsNullOrWhiteSpace)
                    || query.CaptainIds.Distinct(StringComparer.Ordinal).Count() != query.CaptainIds.Count || query.CaptainId != null)
                    throw new ArgumentException("captainIds requires 1..100 distinct nonempty IDs and cannot be combined with captainId.");
                foreach (string id in query.CaptainIds)
                {
                    Captain? member = await _Database.Captains.ReadAsync(id).ConfigureAwait(false);
                    if (member == null || member.TenantId != query.TenantId) throw new KeyNotFoundException("Captain not found.");
                }
            }
            if (!String.IsNullOrEmpty(query.CaptainId))
            {
                Captain? captain = await _Database.Captains.ReadAsync(query.CaptainId).ConfigureAwait(false);
                if (captain == null || captain.TenantId != query.TenantId) throw new KeyNotFoundException("Captain not found.");
            }
            if (!String.IsNullOrEmpty(query.HarborId))
            {
                Harbor? harbor = await _Database.Harbors.ReadAsync(query.HarborId).ConfigureAwait(false);
                if (harbor == null || harbor.TenantId != query.TenantId) throw new KeyNotFoundException("Harbor not found.");
            }
            if (query.PageNumber < 1 || query.PageSize < 1 || query.PageSize > 100) throw new ArgumentException("pageNumber must be positive and pageSize must be 1..100.");
        }

        /// <summary>ValidateRange: account usage contract.</summary>
        public static void ValidateRange(UsageQuery query, bool required = false)
        {
            if (required && (!query.FromUtc.HasValue || !query.ToUtc.HasValue)) throw new ArgumentException("from and to are required.");
            query.ToUtc ??= DateTime.UtcNow;
            query.FromUtc ??= query.ToUtc.Value.AddDays(-1);
            if (query.FromUtc.Value.Kind != DateTimeKind.Utc || query.ToUtc.Value.Kind != DateTimeKind.Utc) throw new ArgumentException("from and to must be UTC timestamps.");
            if (query.FromUtc >= query.ToUtc || query.ToUtc - query.FromUtc > TimeSpan.FromDays(90)) throw new ArgumentException("Range must be increasing and at most 90 days.");
        }

        /// <summary>SnapshotsAsync: account usage contract.</summary>
        public async Task<EnumerationResult<UsageSnapshotView>> SnapshotsAsync(AuthContext caller, UsageQuery query)
        {
            using UsageTelemetryOperation telemetry = UsageTelemetry.Start("snapshots-query");
            await ScopeAsync(caller, query).ConfigureAwait(false);
            if (query.CaptainIds != null) throw new ArgumentException("captainIds is supported only by latest.");
            ValidateRange(query);
            int requestedPage = query.PageNumber, requestedSize = query.PageSize;
            bool filterHost = query.HostId != null;
            EnumerationResult<UsageSnapshot> page;
            if (filterHost)
            {
                query.PageNumber = 1; query.PageSize = 1000;
                List<UsageSnapshot> all = new List<UsageSnapshot>();
                do {
                    page = await _Database.UsageSnapshots.EnumerateAsync(query).ConfigureAwait(false);
                    all.AddRange(page.Objects.Where(s => s.HostId == query.HostId));
                    query.PageNumber++;
                } while (page.TotalPages >= query.PageNumber);
                query.PageNumber = requestedPage; query.PageSize = requestedSize;
                page = new EnumerationResult<UsageSnapshot> { Objects = all.Skip((requestedPage - 1) * requestedSize).Take(requestedSize).ToList(), PageNumber = requestedPage, PageSize = requestedSize, TotalRecords = all.Count, TotalPages = (int)Math.Ceiling(all.Count / (double)requestedSize) };
            }
            else page = await _Database.UsageSnapshots.EnumerateAsync(query).ConfigureAwait(false);
            List<UsageSnapshotView> rows = new List<UsageSnapshotView>();
            foreach (UsageSnapshot row in page.Objects)
            {
                rows.Add(new UsageSnapshotView { Id = row.Id, TenantId = row.TenantId, HostId = row.HostId, HarborId = row.HarborId,
                    Runtime = row.Runtime, Provider = row.Provider, AccountKey = row.AccountKey, CapturedUtc = row.CapturedUtc,
                    ObservedUtc = row.CapturedUtc, Status = row.Status, ErrorCode = row.ErrorCode, Windows = UsageWindowsJson.Deserialize(row.WindowsJson),
                    CaptainIds = await _Database.UsageSnapshots.ReadCaptainsAsync(row.Id, query.TenantId!).ConfigureAwait(false) });
            }
            telemetry.Complete();
            return Page(page, rows);
        }

        /// <summary>LatestAsync: account usage contract.</summary>
        public async Task<EnumerationResult<UsageSnapshotView>> LatestAsync(AuthContext caller, UsageQuery query)
        {
            using UsageTelemetryOperation telemetry = UsageTelemetry.Start("latest-query");
            Stopwatch timer = Stopwatch.StartNew();
            await ScopeAsync(caller, query).ConfigureAwait(false);
            string sortBy = query.SortBy == "totalTokens" ? "updated" : query.SortBy;
            if (!new[] { "captainId", "hostId", "runtime", "limits", "updated", "interval" }.Contains(sortBy)) throw new ArgumentException("Unsupported latest sortBy.");
            if (query.SortDirection != "ascending" && query.SortDirection != "descending") throw new ArgumentException("sortDirection must be ascending or descending.");
            // Current state has no implicit history window. Explicit dates still use the shared validation.
            if (query.FromUtc.HasValue || query.ToUtc.HasValue) ValidateRange(query);
            UsageLatestStateQuery storageQuery = new UsageLatestStateQuery {
                TenantId = query.TenantId, HostId = query.HostId, Runtime = query.Runtime, Provider = query.Provider, AccountKey = query.AccountKey,
                PageNumber = 1, PageSize = 1000 };
            List<UsageLatestState> states = new List<UsageLatestState>();
            EnumerationResult<UsageLatestState> page;
            do {
                page = await _Database.UsageSnapshots.EnumerateLatestStatesAsync(storageQuery).ConfigureAwait(false);
                states.AddRange(page.Objects); storageQuery.PageNumber++;
            } while (page.TotalPages >= storageQuery.PageNumber);
            List<UsageSnapshotView> rows = new List<UsageSnapshotView>();
            foreach (UsageLatestState state in states)
            {
                bool failed = state.LastErrorCapturedUtc.HasValue && (!state.LastGoodCapturedUtc.HasValue || state.LastErrorCapturedUtc >= state.LastGoodCapturedUtc);
                string? id = failed ? state.LastErrorSnapshotId : state.LastGoodSnapshotId;
                List<string> members = await _Database.UsageSnapshots.ReadLatestCaptainsAsync(state.Id, query.TenantId!).ConfigureAwait(false);
                // Upgrade fallback for legacy projections; new capture always commits durable member links.
                if (members.Count == 0)
                {
                    UsageMembershipQuery membershipQuery = new UsageMembershipQuery { TenantId = query.TenantId, HostId = state.HostId, Runtime = state.Runtime, AccountKey = state.AccountKey, OpenOnly = true, PageSize = 1000 };
                    EnumerationResult<UsageAccountMembership> membershipPage;
                    do {
                        membershipPage = await _Database.UsageMemberships.EnumerateAsync(membershipQuery).ConfigureAwait(false);
                        members.AddRange(membershipPage.Objects.Select(m => m.CaptainId)); membershipQuery.PageNumber++;
                    } while (membershipPage.TotalPages >= membershipQuery.PageNumber);
                }

                if (query.CaptainId != null && !members.Contains(query.CaptainId)) continue;
                if (query.CaptainIds != null && !query.CaptainIds.Any(members.Contains)) continue;
                UsageSnapshot? snapshot = id == null ? null : await _Database.UsageSnapshots.ReadAsync(id, query.TenantId!).ConfigureAwait(false);
                if (query.HarborId != null && state.HostId != query.HarborId) continue;
                DateTime capturedUtc = (failed ? state.LastErrorCapturedUtc : state.LastGoodCapturedUtc) ?? state.LastUpdateUtc;
                if ((query.FromUtc.HasValue && capturedUtc < query.FromUtc.Value) || (query.ToUtc.HasValue && capturedUtc >= query.ToUtc.Value)) continue;
                int requestedInterval = state.RequestedIntervalSeconds ?? 300;
                if (!state.RequestedIntervalSeconds.HasValue)
                {
                    List<int> configured = new List<int>();
                    foreach (string member in members.Distinct(StringComparer.Ordinal))
                    {
                        Captain? captain = await _Database.Captains.ReadAsync(query.TenantId!, member).ConfigureAwait(false);
                        if (captain != null) configured.Add(captain.UsageCaptureIntervalSeconds);
                    }
                    if (configured.Count > 0) requestedInterval = configured.Min();
                }
                int interval = state.EffectiveIntervalSeconds ?? Math.Max(requestedInterval, _Settings.MinimumIntervalSeconds);
                rows.Add(new UsageSnapshotView { Id = id, AccountId = state.Id, TenantId = state.TenantId, HostId = state.HostId, HarborId = snapshot?.HarborId ?? (state.HostId.StartsWith("hbr_", StringComparison.Ordinal) ? state.HostId : null),
                    Runtime = state.Runtime, Provider = state.Provider, AccountKey = state.AccountKey, CaptainIds = members.Distinct(StringComparer.Ordinal).OrderBy(member => member, StringComparer.Ordinal).ToList(),
                    CapturedUtc = (failed ? state.LastErrorCapturedUtc : state.LastGoodCapturedUtc) ?? state.LastUpdateUtc,
                    ObservedUtc = state.LastGoodCapturedUtc, Status = (failed ? state.LastErrorStatus : state.LastGoodStatus) ?? Armada.Core.Enums.UsageSnapshotStatusEnum.Failed,
                    ErrorCode = failed ? state.LastErrorCode : null, Windows = UsageWindowsJson.Deserialize(state.LastGoodWindowsJson),
                    Stale = failed || !state.LastGoodCapturedUtc.HasValue || state.LastGoodCapturedUtc.Value.AddSeconds(interval * 2) < DateTime.UtcNow,
                    RequestedIntervalSeconds = requestedInterval, EffectiveIntervalSeconds = interval, CapabilityReason = AgentRuntimeCapabilities.UsageCapture(state.Runtime).Reason });
            }
            telemetry.Complete();
            Func<UsageSnapshotView, IComparable?> key = sortBy switch {
                "captainId" => row => row.CaptainIds.FirstOrDefault(),
                "hostId" => row => row.HostId,
                "runtime" => row => row.Runtime.ToString(),
                "limits" => row => row.Windows.Select(window => window.UsedPercent).Max(),
                "interval" => row => row.EffectiveIntervalSeconds,
                _ => row => row.ObservedUtc ?? row.CapturedUtc
            };
            IEnumerable<UsageSnapshotView> ordered = query.SortDirection == "ascending"
                ? rows.OrderBy(key).ThenBy(row => row.AccountId, StringComparer.Ordinal)
                : rows.OrderByDescending(key).ThenBy(row => row.AccountId, StringComparer.Ordinal);
            int count = rows.Count;
            List<UsageSnapshotView> visible = ordered.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToList();
            await AttachReservationEstimatesAsync(query.TenantId!, visible).ConfigureAwait(false);
            await AttachCaptainNamesAsync(query.TenantId!, visible).ConfigureAwait(false);
            return new EnumerationResult<UsageSnapshotView> { TotalMs = Math.Round(timer.Elapsed.TotalMilliseconds, 2), Objects = visible, PageNumber = query.PageNumber, PageSize = query.PageSize, TotalRecords = count, TotalPages = (int)Math.Ceiling(count / (double)query.PageSize) };
        }

        // Reservation totals come from the same ledger selection reads, matched to each observed window by its budget
        // key (with or without the custom-capture configuration revision). Two batched reads cover the whole page.
        // Member captains are listed by name; one read per distinct captain on the visible page.
        private async Task AttachCaptainNamesAsync(string tenantId, List<UsageSnapshotView> rows)
        {
            Dictionary<string, string?> names = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (UsageSnapshotView row in rows)
            {
                List<string?> resolved = new List<string?>();
                foreach (string id in row.CaptainIds)
                {
                    if (!names.TryGetValue(id, out string? name))
                    {
                        Captain? captain = await _Database.Captains.ReadAsync(tenantId, id).ConfigureAwait(false);
                        name = captain?.Name;
                        names[id] = name;
                    }
                    resolved.Add(name);
                }
                row.CaptainNames = resolved;
            }
        }

        private async Task AttachReservationEstimatesAsync(string tenantId, List<UsageSnapshotView> rows)
        {
            List<string> members = rows.SelectMany(row => row.CaptainIds).Distinct(StringComparer.Ordinal).ToList();
            if (members.Count == 0) return;
            List<UsageSelectionBudgetTotals> totals = new List<UsageSelectionBudgetTotals>();
            List<UsageSelectionAccountEvidence> evidence = new List<UsageSelectionAccountEvidence>();
            for (int offset = 0; offset < members.Count; offset += 1000)
            {
                List<string> batch = members.Skip(offset).Take(1000).ToList();
                totals.AddRange(await _Database.CaptainSelections.ReadBudgetTotalsAsync(tenantId, batch).ConfigureAwait(false));
                evidence.AddRange(await _Database.CaptainSelections.ReadAccountEvidenceAsync(tenantId, batch).ConfigureAwait(false));
            }

            foreach (UsageSnapshotView row in rows)
            {
                bool Same(string host, string runtime, string account) => host == row.HostId && runtime == row.Runtime.ToString() && account == row.AccountKey;
                List<UsageSelectionBudgetTotals> account = totals.Where(t => Same(t.HostId, t.Runtime, t.AccountKey))
                    .DistinctBy(t => t.WindowKey + ":" + t.Generation).ToList();
                if (account.Count == 0) continue;
                row.ReservationUncertain = account.Any(t => t.HasUncertainDebt);
                List<string?> revisions = evidence.Where(e => Same(e.HostId, e.Runtime.ToString(), e.AccountKey)).Select(e => e.ConfigurationRevision).Distinct().ToList();
                foreach (UsageWindow window in row.Windows)
                {
                    HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal) { UsageBudgetIdentity.WindowKey(window) };
                    foreach (string? revision in revisions) if (revision != null) keys.Add(UsageBudgetIdentity.WindowKey(window, revision));
                    double active = account.Where(t => keys.Contains(t.WindowKey)).Sum(t => t.ActivePercent);
                    double unsettled = account.Where(t => keys.Contains(t.WindowKey)).Sum(t => t.UnsettledPercent);
                    if (active <= 0 && unsettled <= 0) continue;
                    row.ReservationEstimates.Add(new UsageReservationEstimate { WindowName = window.Name, WindowMinutes = window.WindowMinutes, Model = window.Model,
                        ActivePercent = Math.Round(active, 3), UnsettledPercent = Math.Round(unsettled, 3) });
                }
            }
        }

        /// <summary>SeriesAsync: account usage contract.</summary>
        public async Task<UsageSeriesResult> SeriesAsync(AuthContext caller, UsageQuery query)
        {
            await ScopeAsync(caller, query).ConfigureAwait(false);
            if (query.CaptainIds != null) throw new ArgumentException("captainIds is supported only by latest."); ValidateRange(query, true);
            return await _Aggregation.SeriesAsync(query).ConfigureAwait(false);
        }

        /// <summary>MissionsAsync: account usage contract.</summary>
        public async Task<UsageMissionResult> MissionsAsync(AuthContext caller, UsageQuery query)
        {
            await ScopeAsync(caller, query).ConfigureAwait(false);
            if (query.CaptainIds != null) throw new ArgumentException("captainIds is supported only by latest."); ValidateRange(query, true);
            return await _Aggregation.MissionsAsync(query).ConfigureAwait(false);
        }

        /// <summary>CaptureAsync: account usage contract.</summary>
        public async Task<object> CaptureAsync(AuthContext caller, string captainId, string? harborId)
        {
            UsageQuery query = new UsageQuery { CaptainId = captainId };
            await ScopeAsync(caller, query).ConfigureAwait(false);
            if (!caller.IsAdmin && !caller.IsTenantAdmin || caller.MissionId != null || caller.AskThreadId != null) throw new UnauthorizedAccessException("Only tenant administrators may capture usage.");
            Captain captain = (await _Database.Captains.ReadAsync(captainId).ConfigureAwait(false))!;
            if (AgentRuntimeCapabilities.UsageCapture(captain.Runtime).Kind == UsageCaptureKindEnum.Passive)
            {
                query.HarborId = harborId;
                return await LatestAsync(caller, query).ConfigureAwait(false);
            }
            if (!captain.UsageCaptureActive) throw new UsageRequestException(409, "CaptureDisabled");
            if (harborId != null)
            {
                if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentException("harborId must be a nonempty Harbor identifier.");
                harborId = harborId.Trim();
                await ScopeAsync(caller, new UsageQuery { CaptainId = captainId, HarborId = harborId }).ConfigureAwait(false);
                captain.UsageCaptureHarborId = harborId;
                string? error = await CaptainUsageCaptureValidator.ValidateAsync(_Database, captain).ConfigureAwait(false);
                if (error != null) throw new ArgumentException(error);
                if (!await _Database.Captains.UpdateUsageCaptureHarborAsync(captain.TenantId!, captain.Id, harborId).ConfigureAwait(false))
                    throw new KeyNotFoundException("Captain not found.");
            }
            await _Scheduler.RebuildAsync().ConfigureAwait(false);
            UsageCaptureGroup? group = _Scheduler.Groups().FirstOrDefault(g => g.TenantId == query.TenantId && g.MemberCaptainIds.Contains(captainId));
            if (group == null) throw new UsageRequestException(409, "HostUnresolved: select a credential Harbor in capture settings.");
            if (harborId != null && harborId != group.HarborId) throw new KeyNotFoundException("Harbor not found.");
            if (group.HarborId != null && !_Capture.IsInFlight(group.GroupKey))
            {
                Harbor? selected = await _Database.Harbors.ReadAsync(group.HarborId).ConfigureAwait(false);
                if (selected == null || selected.TenantId != captain.TenantId) throw new UsageRequestException(409, "HostUnresolved: select a credential Harbor in capture settings.");
                if (!selected.Enabled) throw new UsageRequestException(409, "HostDisabled: enable the selected credential Harbor or select another.");
                if (_Harbors == null || !_Harbors.IsConnected(group.HarborId)) throw new UsageRequestException(503, "HostUnavailable");
            }
            UsageCaptureAttempt attempt = await _Capture.TryStartAsync(group, captainId, manual: true).ConfigureAwait(false);
            if (attempt.SkippedReason != null && !attempt.JoinedExisting) throw new UsageRequestException(attempt.SkippedReason == "Cooldown" ? 429 : attempt.SkippedReason == "HostUnavailable" ? 503 : 409, attempt.SkippedReason, attempt.NextEligibleUtc);
            return new UsageCaptureReceipt { JobId = attempt.JobId, JoinedExisting = attempt.JoinedExisting, NextEligibleUtc = attempt.NextEligibleUtc };
        }

        private static EnumerationResult<UsageSnapshotView> Page<T>(EnumerationResult<T> source, List<UsageSnapshotView> rows) => new EnumerationResult<UsageSnapshotView> {
            PageNumber = source.PageNumber, PageSize = source.PageSize, TotalPages = source.TotalPages, TotalRecords = source.TotalRecords, Objects = rows };
    }
}
