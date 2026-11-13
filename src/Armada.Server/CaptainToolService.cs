namespace Armada.Server
{
    using System.Collections.Concurrent;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Server.RuntimeTools;
    using SyslogLogging;

    /// <summary>
    /// Evaluates captain access to the tool sources visible from a captain runtime.
    /// </summary>
    public class CaptainToolService
    {
        private readonly DatabaseDriver _database;
        private readonly CaptainRuntimeToolCatalogService _runtimeCatalog;
        private readonly ConcurrentDictionary<string, CaptainToolSnapshot> _Snapshots = new ConcurrentDictionary<string, CaptainToolSnapshot>(StringComparer.Ordinal);

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="harborConnections">Harbor connection manager, so runtime probes for captains that run
        /// on a Harbor can be executed on the Harbor host. Null in standalone mode (probes run locally).</param>
        /// <param name="discoverySource">Source for every host touch point of runtime tool discovery (config files, runtime
        /// CLIs, installed-package inventories, MCP server connections). Null uses
        /// <see cref="HostRuntimeToolDiscoverySource"/>; tests pass a fake so the host user's tools are never touched.</param>
        /// <param name="mcpPort">The Admiral MCP port, used to recognize configured MCP entries that point at Armada; 0
        /// recognizes only entries named "armada".</param>
        /// <param name="mcpHost">The host MCP clients must use to reach the Admiral (see
        /// <see cref="Armada.Core.Services.ArmadaMcpConfigBuilder.ClientHostFor"/>); null or empty means localhost.</param>
        public CaptainToolService(
            LoggingModule logging,
            DatabaseDriver database,
            HarborConnectionManager? harborConnections = null,
            IRuntimeToolDiscoverySource? discoverySource = null,
            int mcpPort = 0,
            string? mcpHost = null)
        {
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            if (database == null) throw new ArgumentNullException(nameof(database));

            _database = database;
            _runtimeCatalog = new CaptainRuntimeToolCatalogService(logging, harborConnections, discoverySource, mcpPort, mcpHost);
        }

        /// <summary>
        /// Describe the runtime-visible tool sources and one page of the named tools available to a captain. A snapshot
        /// probed for the same (unchanged) captain within <see cref="CaptainToolsQuery.SnapshotReuseSeconds"/> is reused
        /// unless the query asks to refresh, so paging does not probe again.
        /// </summary>
        /// <param name="captain">Captain to inspect.</param>
        /// <param name="query">Tools paging and filter, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Availability summary, sources, tool counts, and one page of tools.</returns>
        /// <exception cref="ArgumentNullException">Thrown when captain is null.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when the runtime reports more sources than <see cref="CollectionLimits.MaxConfigItems"/>.</exception>
        public async Task<CaptainToolAccessResult> DescribeAsync(Captain captain, CaptainToolsQuery? query = null, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            CaptainToolsQuery effective = query ?? new CaptainToolsQuery();

            CaptainToolSnapshot? snapshot = null;
            if (!effective.Refresh
                && _Snapshots.TryGetValue(captain.Id, out CaptainToolSnapshot? cached)
                && cached.CaptainLastUpdateUtc == captain.LastUpdateUtc
                && DateTime.UtcNow - cached.Result.SnapshotUtc < TimeSpan.FromSeconds(CaptainToolsQuery.SnapshotReuseSeconds))
            {
                snapshot = cached;
            }

            if (snapshot == null)
            {
                snapshot = await ProbeAsync(captain, token).ConfigureAwait(false);
                _Snapshots[captain.Id] = snapshot;
            }

            List<CaptainToolSummary> filtered = effective.SourceKind.HasValue
                ? snapshot.AllTools.Where(t => t.SourceKind == effective.SourceKind.Value).ToList()
                : snapshot.AllTools;

            CaptainToolAccessResult source = snapshot.Result;
            return new CaptainToolAccessResult
            {
                CaptainId = source.CaptainId,
                CaptainName = source.CaptainName,
                Runtime = source.Runtime,
                ToolsAccessible = source.ToolsAccessible,
                AvailabilityVerified = source.AvailabilityVerified,
                AvailabilitySource = source.AvailabilitySource,
                Summary = source.Summary,
                EndpointName = source.EndpointName,
                ToolsEnabled = source.ToolsEnabled,
                EffectiveToolCount = source.EffectiveToolCount,
                ArmadaToolCount = source.ArmadaToolCount,
                ConfiguredServerCount = source.ConfiguredServerCount,
                AskApprovalGated = source.AskApprovalGated,
                ReachableServerCount = source.ReachableServerCount,
                Servers = source.Servers,
                ToolCount = source.ToolCount,
                McpToolCount = source.McpToolCount,
                RuntimeBuiltInToolCount = source.RuntimeBuiltInToolCount,
                SnapshotUtc = source.SnapshotUtc,
                Tools = CollectionLimits.Page(effective, filtered)
            };
        }


        private async Task<CaptainToolSnapshot> ProbeAsync(Captain captain, CancellationToken token)
        {
            CaptainToolAccessResult result = new CaptainToolAccessResult
            {
                CaptainId = captain.Id,
                CaptainName = captain.Name,
                Runtime = captain.Runtime,
                AskApprovalGated = Armada.Core.Services.CaptainThreadMcpPlanner.SupportsApprovalGating(captain.Runtime),
                ArmadaToolCount = 0,
                SnapshotUtc = DateTime.UtcNow
            };
            List<CaptainToolSummary> tools = new List<CaptainToolSummary>();

            CaptainRuntimeToolCatalogService.RuntimeToolCatalogSnapshot? runtimeSnapshot =
                await _runtimeCatalog.TryDescribeAsync(captain, _database, token).ConfigureAwait(false);

            if (runtimeSnapshot != null)
            {
                result.ToolsAccessible = runtimeSnapshot.ToolsAccessible;
                result.AvailabilityVerified = runtimeSnapshot.AvailabilityVerified;
                result.AvailabilitySource = runtimeSnapshot.AvailabilitySource;
                result.Summary = runtimeSnapshot.Summary;
                result.ArmadaToolCount = runtimeSnapshot.ArmadaToolCount;
                result.ConfiguredServerCount = runtimeSnapshot.ConfiguredServerCount;
                result.ReachableServerCount = runtimeSnapshot.ReachableServerCount;
                result.EffectiveToolCount = runtimeSnapshot.EffectiveToolCount;
                // Reject, never truncate: a runtime configuration with more sources than the bound is reported as such.
                CollectionLimits.RequireConfigItems(runtimeSnapshot.Servers, "Servers");
                result.Servers = runtimeSnapshot.Servers;
                tools = (runtimeSnapshot.Tools ?? new List<CaptainToolSummary>())
                    .Where(t => t != null)
                    .OrderBy(t => t.SourceKind)
                    .ThenBy(t => t.RegistrationSource ?? String.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(t => t.Name ?? String.Empty, StringComparer.Ordinal)
                    .ToList();
            }
            else
            {
                result.ToolsAccessible = false;
                result.AvailabilityVerified = false;
                result.AvailabilitySource = "unsupported-runtime";
                result.Summary = "Armada does not currently have a runtime-specific tool inventory implementation for this captain.";
            }

            result.ToolCount = tools.Count;
            result.McpToolCount = tools.Count(t => t.SourceKind == CaptainToolSourceKindEnum.McpServer);
            result.RuntimeBuiltInToolCount = tools.Count(t => t.SourceKind == CaptainToolSourceKindEnum.RuntimeBuiltIn);
            return new CaptainToolSnapshot { Result = result, AllTools = tools, CaptainLastUpdateUtc = captain.LastUpdateUtc };
        }
    }
}
