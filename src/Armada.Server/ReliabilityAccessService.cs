namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Caller-scoped pipeline reliability reads shared by REST and MCP (RELIABILITY_SIMPLICITY_PLAN.md RS-12.1): a
    /// mission's attempt history, the reliability summary per model and persona, and the uploaded acceptance gate
    /// reports. Missions follow the mission read rules (a global admin sees every tenant, a tenant admin the tenant, a
    /// user their own missions, a captain session its own voyage); the summary is tenant scoped like stage outcomes;
    /// gate reports are uploaded by a global admin and read by admins. Errors are typed exceptions the route and tool
    /// layers map to 400/403/404.
    /// </summary>
    public class ReliabilityAccessService
    {
        #region Private-Members

        private const int OutcomePageSize = 1000;
        private const int MaxAttemptPages = 5;

        private readonly DatabaseDriver _Database;
        private readonly GateReportStore _GateReports;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="gateReports">Gate report store.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public ReliabilityAccessService(DatabaseDriver database, GateReportStore gateReports)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _GateReports = gateReports ?? throw new ArgumentNullException(nameof(gateReports));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A mission's attempt history: its recovery counters and escalation state, and every recorded attempt (stage
        /// outcome), oldest first.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="missionId">Mission id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The history.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not authenticated, is an Ask session, or has no tenant.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the mission does not exist or is out of scope.</exception>
        public async Task<MissionAttemptHistory> GetMissionAttemptsAsync(AuthContext caller, string? missionId, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!caller.IsAuthenticated) throw new UnauthorizedAccessException("Authentication required.");
            if (!String.IsNullOrEmpty(caller.AskThreadId)) throw new UnauthorizedAccessException("An Ask session cannot read mission attempts.");
            if (!caller.IsAdmin && String.IsNullOrEmpty(caller.TenantId)) throw new UnauthorizedAccessException("A tenant is required.");
            if (String.IsNullOrWhiteSpace(missionId)) throw new KeyNotFoundException("Mission not found.");

            Mission? mission = await _Database.Missions.ReadAsync(missionId.Trim(), token).ConfigureAwait(false);
            if (mission == null || !await CanSeeMissionAsync(caller, mission, token).ConfigureAwait(false))
                throw new KeyNotFoundException("Mission not found.");

            List<StageOutcome> outcomes = new List<StageOutcome>();
            for (int page = 1; page <= MaxAttemptPages; page++)
            {
                StageOutcomeQuery query = new StageOutcomeQuery { TenantId = mission.TenantId, MissionId = mission.Id, PageNumber = page, PageSize = OutcomePageSize };
                EnumerationResult<StageOutcome> result = await _Database.StageOutcomes.EnumerateAsync(query, token).ConfigureAwait(false);
                outcomes.AddRange(result.Objects);
                if (result.Objects.Count < OutcomePageSize) break;
            }

            MissionAttemptHistory history = new MissionAttemptHistory
            {
                MissionId = mission.Id,
                VoyageId = mission.VoyageId,
                Persona = mission.Persona,
                Mode = mission.Mode,
                Status = mission.Status,
                CaptainId = mission.CaptainId,
                Tier = mission.Tier,
                EscalationCount = mission.EscalationCount,
                EscalatedFromCaptainId = mission.EscalatedFromCaptainId,
                EscalatedFromModel = mission.EscalatedFromModel,
                ReworkEscalated = mission.ReworkEscalated,
                RedispatchAttempts = mission.RedispatchAttempts,
                OutputRepairAttempts = mission.OutputRepairAttempts,
                NoOpRedispatchAttempts = mission.NoOpRedispatchAttempts,
                StallRecoveryAttempts = mission.StallRecoveryAttempts,
                Attempts = outcomes
                    .OrderBy(o => o.StartedUtc ?? o.EndedUtc)
                    .ThenBy(o => o.CreatedUtc)
                    .ThenBy(o => o.Id, StringComparer.Ordinal)
                    .Select(MissionAttempt.FromOutcome)
                    .ToList()
            };
            return history;
        }

        /// <summary>
        /// Reliability per model and persona over a window (default the last 14 days, at most 90), from stage outcomes:
        /// completion rate, failure kinds, repairs, escalations and the Unclassified rate. Filtered, sorted and paged on
        /// the server. A global admin sees every tenant (query.TenantId filters); everyone else their own tenant.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="query">Filters, sort and paging; null uses the defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The summary.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="ArgumentException">Thrown when fromUtc is after toUtc or the window is longer than 90 days.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for a captain or Ask session, or a caller without a tenant.</exception>
        public async Task<ReliabilitySummary> GetSummaryAsync(AuthContext caller, ReliabilitySummaryQuery? query, CancellationToken token = default)
        {
            RequirePerson(caller);
            ReliabilitySummaryQuery source = query ?? new ReliabilitySummaryQuery();
            DateTime toUtc = source.ToUtc?.ToUniversalTime() ?? DateTime.UtcNow;
            DateTime fromUtc = source.FromUtc?.ToUniversalTime() ?? toUtc.AddDays(-14);
            if (fromUtc > toUtc) throw new ArgumentException("fromUtc must not be after toUtc.");
            if ((toUtc - fromUtc).TotalDays > ReliabilitySummaryQuery.MaxWindowDays)
                throw new ArgumentException("The window must be at most " + ReliabilitySummaryQuery.MaxWindowDays + " days.");

            List<StageOutcome> outcomes = new List<StageOutcome>();
            bool truncated = false;
            for (int page = 1; ; page++)
            {
                StageOutcomeQuery outcomeQuery = new StageOutcomeQuery
                {
                    TenantId = caller.IsAdmin ? Blank(source.TenantId) : caller.TenantId,
                    Model = Blank(source.Model),
                    Persona = Blank(source.Persona),
                    VesselId = Blank(source.VesselId),
                    IsEvaluation = source.IncludeEvaluation ? null : false,
                    FromUtc = fromUtc,
                    ToUtc = toUtc,
                    PageNumber = page,
                    PageSize = OutcomePageSize
                };
                EnumerationResult<StageOutcome> result = await _Database.StageOutcomes.EnumerateAsync(outcomeQuery, token).ConfigureAwait(false);
                outcomes.AddRange(result.Objects);
                if (result.Objects.Count < OutcomePageSize) break;
                if (outcomes.Count >= ReliabilitySummaryQuery.MaxOutcomes)
                {
                    truncated = result.TotalRecords > outcomes.Count;
                    break;
                }
            }

            return ReliabilitySummaryBuilder.Build(outcomes, source, fromUtc, toUtc, truncated);
        }

        /// <summary>
        /// Store an acceptance gate report (the gate runner uploads it after writing benchmarks/results). Global admins only.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="report">Report.</param>
        /// <returns>The stored report with its id.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="ArgumentException">Thrown for a missing or empty report, or one larger than 1 MiB.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not a global admin, or is a captain session.</exception>
        public GateReport UploadGateReport(AuthContext caller, GateReport? report)
        {
            RequirePerson(caller);
            if (!caller.IsAdmin) throw new UnauthorizedAccessException("Only a global admin can upload a gate report.");
            if (report == null) throw new ArgumentException("A request body is required.");
            return _GateReports.Save(report);
        }

        /// <summary>
        /// One page of stored gate reports, newest first. Global and tenant admins.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="pageNumber">Page number (default 1).</param>
        /// <param name="pageSize">Page size (1 to 100, default 10).</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin, or is a captain session.</exception>
        public EnumerationResult<GateReport> ListGateReports(AuthContext caller, int? pageNumber, int? pageSize)
        {
            RequireAdminReader(caller);
            return _GateReports.List(pageNumber ?? 1, pageSize ?? 10);
        }

        /// <summary>
        /// One stored gate report, or the newest with id "latest". Global and tenant admins.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Report id, or "latest".</param>
        /// <returns>The report.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when no such report is stored.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin, or is a captain session.</exception>
        public GateReport GetGateReport(AuthContext caller, string? id)
        {
            RequireAdminReader(caller);
            return _GateReports.Read(id) ?? throw new KeyNotFoundException("Gate report not found.");
        }

        #endregion

        #region Private-Methods

        private static void RequirePerson(AuthContext caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!caller.IsAuthenticated) throw new UnauthorizedAccessException("Authentication required.");
            if (!String.IsNullOrEmpty(caller.MissionId) || !String.IsNullOrEmpty(caller.AskThreadId))
                throw new UnauthorizedAccessException("A captain session cannot read reliability data.");
            if (!caller.IsAdmin && String.IsNullOrEmpty(caller.TenantId)) throw new UnauthorizedAccessException("A tenant is required.");
        }

        private static void RequireAdminReader(AuthContext caller)
        {
            RequirePerson(caller);
            if (!caller.IsAdmin && !caller.IsTenantAdmin) throw new UnauthorizedAccessException("Only an admin can read gate reports.");
        }

        private async Task<bool> CanSeeMissionAsync(AuthContext caller, Mission mission, CancellationToken token)
        {
            if (caller.IsAdmin && String.IsNullOrEmpty(caller.MissionId)) return true;
            if (String.IsNullOrEmpty(mission.TenantId) || !String.Equals(caller.TenantId, mission.TenantId, StringComparison.Ordinal)) return false;
            if (!String.IsNullOrEmpty(caller.MissionId))
            {
                Mission? own = await _Database.Missions.ReadAsync(caller.TenantId!, caller.MissionId!, token).ConfigureAwait(false);
                if (own == null) return false;
                if (String.Equals(own.Id, mission.Id, StringComparison.Ordinal)) return true;
                return !String.IsNullOrEmpty(own.VoyageId) && String.Equals(own.VoyageId, mission.VoyageId, StringComparison.Ordinal);
            }

            if (caller.IsTenantAdmin) return true;
            return !String.IsNullOrEmpty(caller.UserId) && String.Equals(caller.UserId, mission.UserId, StringComparison.Ordinal);
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
