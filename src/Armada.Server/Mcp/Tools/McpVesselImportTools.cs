namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Registers MCP tools for bulk vessel import (discover_vessels, import_vessels). Both require a tenant admin
    /// caller, matching the REST API, because they read the Admiral host filesystem.
    /// </summary>
    public static class McpVesselImportTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers vessel import MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="importService">Vessel import service.</param>
        /// <exception cref="ArgumentNullException">Thrown when importService is null.</exception>
        public static void Register(RegisterToolDelegate register, IVesselImportService importService)
        {
            if (importService == null) throw new ArgumentNullException(nameof(importService));

            register(
                "discover_vessels",
                "Discover git repositories on the Admiral host to onboard as vessels. Scans the given directories and roots (breadth-first, skipping excluded and dot-prefixed folders, worktrees, and Armada's own directories) and saves the result as an import batch. Creates no vessels; call import_vessels with the returned batchId to import. Requires tenant admin. Paths must be inside Import.AllowedRoots (or the user profile when none are configured).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        directories = new { type = "array", items = new { type = "string" }, description = "Absolute directories. A git repository becomes one candidate; any other directory is scanned like a root." },
                        roots = new { type = "array", items = new { type = "string" }, description = "Absolute roots to scan for git repositories." },
                        maxDepth = new { type = "integer", description = "Maximum scan depth below each root (1-16, default Import.MaxDepth)" }
                    }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (!caller.IsAdmin && !caller.IsTenantAdmin) return (object)new { Error = "discover_vessels requires a tenant admin" };

                    DiscoverVesselsArgs request = args.HasValue
                        ? JsonSerializer.Deserialize<DiscoverVesselsArgs>(args.Value, _JsonOptions) ?? new DiscoverVesselsArgs()
                        : new DiscoverVesselsArgs();
                    VesselDiscoveryRequest discovery = new VesselDiscoveryRequest();
                    discovery.Directories = request.Directories ?? new List<string>();
                    discovery.Roots = request.Roots ?? new List<string>();
                    discovery.MaxDepth = request.MaxDepth;

                    try
                    {
                        VesselImportDiscoverResponse result = await importService.DiscoverAsync(ResolveTenant(caller), caller.UserId, discovery).ConfigureAwait(false);
                        return (object)result;
                    }
                    catch (VesselImportPathNotAllowedException ex)
                    {
                        return (object)new { Error = ex.Message, Code = VesselImportCodes.PathNotAllowed };
                    }
                    catch (NotSupportedException ex)
                    {
                        return (object)new { Error = ex.Message, Code = VesselImportCodes.HarborNotSupported };
                    }
                    catch (ArgumentException ex)
                    {
                        return (object)new { Error = ex.Message, Code = VesselImportCodes.InvalidRequest };
                    }
                });

            register(
                "import_vessels",
                "Import candidates from a discover_vessels batch as vessels. Pass the candidate paths to import, or allNew=true to import every candidate with status New. Each vessel gets RepoUrl = origin URL (or the local path), WorkingDirectory = the discovered path, and no LocalPath. Paths that already have a vessel are skipped. Large selections run as a background job (returns jobId; poll the batch with enumerate entityType vessel_import_batch or get the job). Requires tenant admin.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        batchId = new { type = "string", description = "Batch ID (vib_ prefix) from discover_vessels" },
                        paths = new { type = "array", items = new { type = "string" }, description = "Candidate paths to import, exactly as returned by discover_vessels" },
                        allNew = new { type = "boolean", description = "Import every candidate with status New (paths is then ignored)" },
                        fleetId = new { type = "string", description = "Fleet ID (flt_ prefix) to assign the vessels to" },
                        defaultPipelineId = new { type = "string", description = "Default pipeline ID (ppl_ prefix) for the vessels" },
                        landingMode = new { type = "string", description = "Landing mode for the vessels: LocalMerge, PullRequest, MergeQueue, or None" }
                    },
                    required = new[] { "batchId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (!caller.IsAdmin && !caller.IsTenantAdmin) return (object)new { Error = "import_vessels requires a tenant admin" };

                    ImportVesselsArgs request = JsonSerializer.Deserialize<ImportVesselsArgs>(args!.Value, _JsonOptions)!;
                    string tenantId = ResolveTenant(caller);

                    VesselImportRequest import = new VesselImportRequest();
                    import.BatchId = request.BatchId;
                    import.FleetId = request.FleetId;

                    if (request.AllNew == true)
                    {
                        VesselImportBatchDetail? detail = await importService.ReadBatchAsync(tenantId, request.BatchId).ConfigureAwait(false);
                        if (detail == null) return (object)new { Error = "Import batch not found", Code = VesselImportCodes.BatchNotFound };
                        import.Paths = detail.Items.Where(i => i.CandidateStatus == VesselImportCandidateStatusEnum.New).Select(i => i.Path).ToList();
                        if (import.Paths.Count == 0) return (object)new { Error = "Batch has no candidates with status New", Code = VesselImportCodes.InvalidRequest };
                    }
                    else
                    {
                        import.Paths = request.Paths ?? new List<string>();
                    }

                    if (!String.IsNullOrWhiteSpace(request.DefaultPipelineId) || !String.IsNullOrWhiteSpace(request.LandingMode))
                    {
                        VesselImportDefaults defaults = new VesselImportDefaults();
                        defaults.DefaultPipelineId = request.DefaultPipelineId;
                        if (!String.IsNullOrWhiteSpace(request.LandingMode))
                        {
                            if (!Enum.TryParse(request.LandingMode, true, out LandingModeEnum landingMode))
                                return (object)new { Error = "Unknown landingMode: " + request.LandingMode, Code = VesselImportCodes.InvalidRequest };
                            defaults.LandingMode = landingMode;
                        }

                        import.Defaults = defaults;
                    }

                    try
                    {
                        VesselImportResponse result = await importService.ImportAsync(tenantId, caller.UserId, import).ConfigureAwait(false);
                        return (object)result;
                    }
                    catch (KeyNotFoundException ex)
                    {
                        return (object)new { Error = ex.Message, Code = VesselImportCodes.BatchNotFound };
                    }
                    catch (InvalidOperationException ex)
                    {
                        return (object)new { Error = ex.Message, Code = VesselImportCodes.BatchBusy };
                    }
                    catch (ArgumentException ex)
                    {
                        return (object)new { Error = ex.Message, Code = VesselImportCodes.InvalidRequest };
                    }
                });
        }

        private static string ResolveTenant(AuthContext caller)
        {
            return String.IsNullOrEmpty(caller.TenantId) ? Constants.DefaultTenantId : caller.TenantId;
        }
    }
}
