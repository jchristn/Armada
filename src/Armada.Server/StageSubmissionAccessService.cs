namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// Caller-scoped access to stage submissions (RELIABILITY_SIMPLICITY_PLAN.md RS-6.2), shared by REST and MCP. Only the
    /// captain of a running mission, holding that mission's scoped MCP token, may submit, and only for its own mission;
    /// reading follows the handoff artifact rules (a global admin sees every tenant, everyone else their own tenant, and a
    /// captain session its own voyage).
    /// </summary>
    public class StageSubmissionAccessService
    {
        #region Public-Members

        /// <summary>
        /// Most submissions one mission read returns (the newest 200).
        /// </summary>
        public const int MaxSubmissions = 200;

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly StageSubmissionService _Submissions;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="maxHierarchyDepth">Deepest allowed plan parent chain (Concurrency.MaxHierarchyDepth).</param>
        /// <exception cref="ArgumentNullException">Thrown when database or logging is null.</exception>
        public StageSubmissionAccessService(DatabaseDriver database, LoggingModule logging, int maxHierarchyDepth)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Submissions = new StageSubmissionService(database, logging ?? throw new ArgumentNullException(nameof(logging)), maxHierarchyDepth);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Submit for the caller's own mission.
        /// </summary>
        /// <param name="caller">Caller; must hold a mission-scoped token.</param>
        /// <param name="kind">What the tool submits.</param>
        /// <param name="argumentsJson">Tool arguments as JSON.</param>
        /// <param name="source">How the submission arrived.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Accepted with the stored id, or the errors to fix.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not the captain of a mission.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the caller's mission no longer exists.</exception>
        public async Task<StageSubmitResult> SubmitAsync(AuthContext caller, StageSubmissionKindEnum kind, string? argumentsJson, StageSubmissionSourceEnum source = StageSubmissionSourceEnum.Mcp, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!caller.IsAuthenticated) throw new UnauthorizedAccessException("Authentication required.");
            if (String.IsNullOrEmpty(caller.MissionId) || String.IsNullOrEmpty(caller.TenantId) || !String.IsNullOrEmpty(caller.AskThreadId))
                throw new UnauthorizedAccessException(StageSubmissionTools.ToolFor(kind) + " can only be called by the captain of a running mission, with that mission's scoped token.");
            Mission? mission = await _Database.Missions.ReadAsync(caller.TenantId!, caller.MissionId!, token).ConfigureAwait(false);
            if (mission == null) throw new KeyNotFoundException("Mission not found.");
            return await _Submissions.SubmitAsync(mission, kind, argumentsJson, source, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The newest submissions (at most <see cref="MaxSubmissions"/>) of a mission the caller can see, newest first.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="missionId">Mission id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submissions.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not authenticated, is an Ask session, or has no tenant.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the mission does not exist or is out of scope.</exception>
        public async Task<List<StageSubmission>> ListByMissionAsync(AuthContext caller, string? missionId, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!caller.IsAuthenticated) throw new UnauthorizedAccessException("Authentication required.");
            if (!String.IsNullOrEmpty(caller.AskThreadId)) throw new UnauthorizedAccessException("An Ask session cannot read stage submissions.");
            if (!caller.IsAdmin && String.IsNullOrEmpty(caller.TenantId)) throw new UnauthorizedAccessException("A tenant is required.");
            if (String.IsNullOrWhiteSpace(missionId)) throw new KeyNotFoundException("Mission not found.");
            Mission? mission = await _Database.Missions.ReadAsync(missionId.Trim(), token).ConfigureAwait(false);
            if (mission == null || !await CanSeeAsync(caller, mission, token).ConfigureAwait(false))
                throw new KeyNotFoundException("Mission not found.");
            List<StageSubmission> all = await _Database.StageSubmissions.EnumerateByMissionAsync(mission.Id, token).ConfigureAwait(false);
            return all.OrderByDescending(s => s.CreatedUtc).Take(MaxSubmissions).ToList();
        }

        #endregion

        #region Private-Methods

        private async Task<bool> CanSeeAsync(AuthContext caller, Mission mission, CancellationToken token)
        {
            if (caller.IsAdmin && String.IsNullOrEmpty(caller.MissionId)) return true;
            if (String.IsNullOrEmpty(mission.TenantId) || !String.Equals(caller.TenantId, mission.TenantId, StringComparison.Ordinal)) return false;
            if (String.IsNullOrEmpty(caller.MissionId)) return true;
            Mission? own = await _Database.Missions.ReadAsync(caller.TenantId!, caller.MissionId!, token).ConfigureAwait(false);
            if (own == null) return false;
            if (String.Equals(own.Id, mission.Id, StringComparison.Ordinal) || String.Equals(own.DependsOnMissionId, mission.Id, StringComparison.Ordinal)) return true;
            return !String.IsNullOrEmpty(own.VoyageId) && String.Equals(own.VoyageId, mission.VoyageId, StringComparison.Ordinal);
        }

        #endregion
    }
}
