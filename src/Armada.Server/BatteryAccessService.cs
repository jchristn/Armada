namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Caller-scoped rules for the fixture battery, shared by REST and MCP: any signed-in person lists packs; a global
    /// admin, or a tenant admin of the captain's tenant, starts a run; runs are visible within their tenant (a global
    /// admin sees every tenant). Captain sessions are refused. Errors are typed exceptions the route and tool layers map
    /// to 400/403/404.
    /// </summary>
    public partial class BatteryAccessService
    {
        #region Private-Members

        private string _Header = "[BatteryAccessService] ";
        private readonly DatabaseDriver _Database;
        private readonly BatteryRunService _Service;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="service">Battery run service.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public BatteryAccessService(DatabaseDriver database, BatteryRunService service, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One page of battery packs, valid or not (malformed packs carry their validation errors).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="pageNumber">Page number (1-based; default 1).</param>
        /// <param name="pageSize">Page size (1 to 1000; default 100).</param>
        /// <returns>One page of packs, ordered by pack directory name. Only the page's manifests are read.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for anonymous callers and captain sessions.</exception>
        public EnumerationResult<BatteryPackInfo> ListPacks(AuthContext caller, int? pageNumber, int? pageSize)
        {
            RequirePerson(caller);
            int size = Math.Clamp(pageSize ?? 100, 1, 1000);
            int number = Math.Max(1, pageNumber ?? 1);
            EnumerationResult<BatteryPack> page = _Service.ListPacks(number, size);
            EnumerationResult<BatteryPackInfo> result = new EnumerationResult<BatteryPackInfo>();
            result.PageNumber = page.PageNumber;
            result.PageSize = page.PageSize;
            result.TotalRecords = page.TotalRecords;
            result.TotalPages = page.TotalPages;
            result.Objects = page.Objects.Select(p =>
            {
                BatteryPackInfo info = p.ToInfo();
                info.Id = System.IO.Path.GetFileName(p.Directory);
                return info;
            }).ToList();
            return result;
        }

        /// <summary>
        /// Start a battery run against a captain.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="ArgumentException">Thrown for a missing body, no packs, an unknown or malformed pack, a bad run
        /// count, or a missing fixture vessel that was not asked to be created.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the captain does not exist or is in another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin, or is a captain session.</exception>
        public async Task<BatteryRun> RunAsync(AuthContext caller, BatteryRunRequest? request, CancellationToken token = default)
        {
            RequirePerson(caller);
            if (request == null) throw new ArgumentException("A request body is required.");
            if (String.IsNullOrWhiteSpace(request.CaptainId)) throw new ArgumentException("captainId is required.");

            // Resolve and authorize the captain before validating the rest, so another tenant learns nothing but not-found.
            Captain? captain = await _Database.Captains.ReadAsync(request.CaptainId.Trim(), token).ConfigureAwait(false);
            if (captain == null || !CanSeeTenant(caller, captain.TenantId)) throw new KeyNotFoundException("Captain not found.");
            if (!caller.IsAdmin && !caller.IsTenantAdmin) throw new UnauthorizedAccessException("Only an admin can run the battery.");
            if (request.Packs.Count(p => !String.IsNullOrWhiteSpace(p)) == 0) throw new ArgumentException("packs must name at least one pack.");

            ModelEndpoint? endpoint = null;
            if (!String.IsNullOrWhiteSpace(captain.ModelEndpointId))
                endpoint = await _Database.ModelEndpoints.ReadAsync(captain.ModelEndpointId!, token).ConfigureAwait(false);

            BatteryRun run = await _Service.StartAsync(captain, endpoint, request.Packs, request.RunCount, request.Seed,
                request.CreateFixtureVessel, caller.TenantId, caller.UserId, request.PromptVariant, token).ConfigureAwait(false);
            _Logging.Info(_Header + "battery run " + run.Id + " of captain " + captain.Id + " started by " + caller.UserId);
            return run;
        }

        /// <summary>
        /// One run with its outcome counts. The per-task results are not embedded; page them with
        /// <see cref="EnumerateResultsAsync"/>.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the run does not exist or is in another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for anonymous callers and captain sessions.</exception>
        public async Task<BatteryRun> GetRunAsync(AuthContext caller, string? runId, CancellationToken token = default)
        {
            RequirePerson(caller);
            if (String.IsNullOrWhiteSpace(runId)) throw new KeyNotFoundException("Battery run not found.");
            BatteryRun? run = await _Service.ReadRunAsync(runId.Trim(), token).ConfigureAwait(false);
            if (run == null || !CanSeeTenant(caller, run.TenantId)) throw new KeyNotFoundException("Battery run not found.");
            return run;
        }

        /// <summary>
        /// One page of a visible run's per-task results (grader evidence and the captain's raw output), paged in the
        /// database. query.Status filters by outcome.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="query">Paging query; null uses the defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of results.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="ArgumentException">Thrown for an unknown outcome filter.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the run does not exist or is in another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for anonymous callers and captain sessions.</exception>
        public async Task<EnumerationResult<BatteryTaskResult>> EnumerateResultsAsync(AuthContext caller, string? runId, EnumerationQuery? query, CancellationToken token = default)
        {
            BatteryRun run = await GetRunAsync(caller, runId, token).ConfigureAwait(false);
            return await _Service.EnumerateResultsAsync(run.Id, query ?? new EnumerationQuery(), token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void RequirePerson(AuthContext caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!caller.IsAuthenticated) throw new UnauthorizedAccessException("Authentication required.");
            if (!String.IsNullOrEmpty(caller.MissionId) || !String.IsNullOrEmpty(caller.AskThreadId))
                throw new UnauthorizedAccessException("A captain session cannot use the battery.");
            if (!caller.IsAdmin && String.IsNullOrEmpty(caller.TenantId)) throw new UnauthorizedAccessException("A tenant is required.");
        }

        private static bool CanSeeTenant(AuthContext caller, string? tenantId)
        {
            if (caller.IsAdmin) return true;
            return !String.IsNullOrEmpty(tenantId) && String.Equals(caller.TenantId, tenantId, StringComparison.Ordinal);
        }

        #endregion
    }
}
