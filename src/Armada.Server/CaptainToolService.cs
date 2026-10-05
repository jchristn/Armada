namespace Armada.Server
{
    using Armada.Core.Database;
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
        /// Describe the runtime-visible tool sources and named tools available to a captain.
        /// </summary>
        /// <param name="captain">Captain to inspect.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Availability summary and tool list.</returns>
        public async Task<CaptainToolAccessResult> DescribeAsync(Captain captain, CancellationToken token = default)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            CaptainToolAccessResult result = new CaptainToolAccessResult
            {
                CaptainId = captain.Id,
                CaptainName = captain.Name,
                Runtime = captain.Runtime.ToString(),
                AskApprovalGated = Armada.Core.Services.CaptainThreadMcpPlanner.SupportsApprovalGating(captain.Runtime),
                ArmadaToolCount = 0,
                Tools = new List<CaptainToolSummary>()
            };

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
                result.Servers = runtimeSnapshot.Servers;
                result.Tools = runtimeSnapshot.Tools;
                return result;
            }

            result.ToolsAccessible = false;
            result.AvailabilityVerified = false;
            result.AvailabilitySource = "unsupported-runtime";
            result.Summary = "Armada does not currently have a runtime-specific tool inventory implementation for this captain.";
            return result;
        }
    }
}
