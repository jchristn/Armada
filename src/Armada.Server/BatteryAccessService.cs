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
    public class BatteryAccessService
    {
        #region Private-Members

        private string _Header = "[BatteryAccessService] ";
        private readonly DatabaseDriver _Database;
        private readonly BatteryRunService _Service;
        private readonly LoggingModule _Logging;

        #endregion

        #region Public-Members

        /// <summary>
        /// Battery pack authoring.
        /// </summary>
        public BatteryPackAccessService Packs { get; }

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
            Packs = new BatteryPackAccessService(_Service);
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

            BatteryRunOptions? options = null;
            if (!String.IsNullOrWhiteSpace(request.Pipeline))
            {
                // Every staffed captain must be visible to the caller, as the primary captain is.
                foreach (CaptainAssignmentOverride entry in request.CaptainOverrides ?? new List<CaptainAssignmentOverride>())
                {
                    Captain? staffed = String.IsNullOrWhiteSpace(entry.CaptainId) ? null : await _Database.Captains.ReadAsync(entry.CaptainId!.Trim(), token).ConfigureAwait(false);
                    if (staffed == null || !CanSeeTenant(caller, staffed.TenantId)) throw new KeyNotFoundException("Captain not found for persona " + entry.Persona + ".");
                }

                options = new BatteryRunOptions { Pipeline = request.Pipeline.Trim(), CaptainOverrides = request.CaptainOverrides };
            }

            BatteryRun run = await _Service.StartAsync(captain, endpoint, request.Packs, request.RunCount, request.Seed,
                request.CreateFixtureVessel, caller.TenantId, caller.UserId, request.PromptVariant, options, token).ConfigureAwait(false);
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
        /// One page of the runs visible to the caller, newest first: a global admin sees every tenant (query.TenantId
        /// filters), everyone else their own tenant. Paged in the database; pageSize 1 to 1000 (default 25).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="query">Filters (captain, status) and paging; null uses the defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page of runs, without their per-task results.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown for anonymous callers and captain sessions.</exception>
        public async Task<EnumerationResult<BatteryRun>> EnumerateRunsAsync(AuthContext caller, BatteryRunQuery? query, CancellationToken token = default)
        {
            RequirePerson(caller);
            BatteryRunQuery source = query ?? new BatteryRunQuery();
            BatteryRunQuery effective = new BatteryRunQuery
            {
                TenantId = caller.IsAdmin ? (String.IsNullOrWhiteSpace(source.TenantId) ? null : source.TenantId.Trim()) : caller.TenantId,
                CaptainId = String.IsNullOrWhiteSpace(source.CaptainId) ? null : source.CaptainId.Trim(),
                Status = source.Status,
                PageNumber = Math.Max(1, source.PageNumber),
                PageSize = Math.Clamp(source.PageSize, 1, 1000)
            };
            return await _Service.EnumerateRunsAsync(effective, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancel a visible run: no further task is dispatched, its open mission is cancelled (releasing its dock and
        /// captain), and the run ends Cancelled with what finished kept. Cancelling a finished run returns it unchanged.
        /// Needs a global admin or a tenant admin of the run's tenant.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="runId">Run identifier.</param>
        /// <param name="reason">Reason recorded on the run, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run after the stop was requested (Cancelled, or still Running while its open mission stops).</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the reason is longer than 500 characters.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the run does not exist or is in another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin, or is a captain session.</exception>
        public async Task<BatteryRun> CancelRunAsync(AuthContext caller, string? runId, string? reason, CancellationToken token = default)
        {
            BatteryRun run = await GetRunAsync(caller, runId, token).ConfigureAwait(false);
            if (!caller.IsAdmin && !caller.IsTenantAdmin) throw new UnauthorizedAccessException("Only an admin can cancel a battery run.");
            if (reason != null && reason.Length > 500) throw new ArgumentException("reason must be at most 500 characters.");
            BatteryRun? stopped = await _Service.CancelAsync(run.Id, reason, TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            if (stopped == null) throw new KeyNotFoundException("Battery run not found.");
            _Logging.Info(_Header + "battery run " + run.Id + " cancel requested by " + caller.UserId + "; status " + stopped.Status);
            return stopped;
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

        internal static void RequirePerson(AuthContext caller)
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
