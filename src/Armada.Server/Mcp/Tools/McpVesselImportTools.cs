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
    /// Registers MCP tools for bulk vessel import (discover_vessels, import_vessels) and fleet categorization
    /// (categorize_vessel_import, apply_fleet_recommendations). All require a tenant admin caller, matching the REST
    /// API, because they read the Admiral host filesystem or change vessels and fleets.
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
        /// <param name="categorizationService">Fleet categorization service, or null to omit the categorization tools.</param>
        /// <exception cref="ArgumentNullException">Thrown when importService is null.</exception>
        public static void Register(RegisterToolDelegate register, IVesselImportService importService, IFleetCategorizationService? categorizationService = null)
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
                        maxDepth = new { type = "integer", description = "Maximum scan depth below each root (1-16, default Import.MaxDepth)" },
                        runInBackground = new { type = "boolean", description = "Run discovery as a background job: returns jobId and the batch in status Discovering; poll the batch until it is Discovered or Failed" }
                    }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (!caller.IsAdmin && !caller.IsTenantAdmin) return (object)McpToolError.Forbidden("discover_vessels requires a tenant admin");

                    DiscoverVesselsArgs request = args.HasValue
                        ? JsonSerializer.Deserialize<DiscoverVesselsArgs>(args.Value, _JsonOptions) ?? new DiscoverVesselsArgs()
                        : new DiscoverVesselsArgs();
                    VesselDiscoveryRequest discovery = new VesselDiscoveryRequest();
                    discovery.Directories = request.Directories ?? new List<string>();
                    discovery.Roots = request.Roots ?? new List<string>();
                    discovery.MaxDepth = request.MaxDepth;
                    discovery.RunInBackground = request.RunInBackground == true;

                    try
                    {
                        VesselImportDiscoverResponse result = await importService.DiscoverAsync(ResolveTenant(caller), caller.UserId, discovery).ConfigureAwait(false);
                        return (object)result;
                    }
                    catch (VesselImportPathNotAllowedException ex)
                    {
                        return (object)McpToolError.Forbidden(ex.Message, VesselImportCodes.PathNotAllowed);
                    }
                    catch (NotSupportedException ex)
                    {
                        return (object)McpToolError.FromException(ex, VesselImportCodes.HarborNotSupported);
                    }
                    catch (ArgumentException ex)
                    {
                        return (object)McpToolError.FromException(ex, VesselImportCodes.InvalidRequest);
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
                        landingMode = new { type = "string", description = "Landing mode for the vessels: LocalMerge, PullRequest, MergeQueue, or None" },
                        categorize = new { type = "boolean", description = "After the import, have a captain analyze the selected repositories and recommend fleets (FleetCategorization job). Requires captainId" },
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix) that recommends fleets; must exist in your tenant and should be Idle" },
                        prompt = new { type = "string", description = "Categorization instructions; omit to use the import.fleet_categorization prompt template. The output-format contract is always appended" },
                        applyFleetsAutomatically = new { type = "boolean", description = "Apply the recommended fleets automatically when categorization completes (default false: review, then call apply_fleet_recommendations)" }
                    },
                    required = new[] { "batchId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (!caller.IsAdmin && !caller.IsTenantAdmin) return (object)McpToolError.Forbidden("import_vessels requires a tenant admin");

                    ImportVesselsArgs request = JsonSerializer.Deserialize<ImportVesselsArgs>(args!.Value, _JsonOptions)!;
                    string tenantId = ResolveTenant(caller);

                    VesselImportRequest import = new VesselImportRequest();
                    import.BatchId = request.BatchId;
                    import.FleetId = request.FleetId;
                    if (request.Categorize == true)
                    {
                        VesselImportCategorizationRequest categorization = new VesselImportCategorizationRequest();
                        categorization.Enabled = true;
                        categorization.CaptainId = request.CaptainId;
                        categorization.Prompt = request.Prompt;
                        categorization.ApplyAutomatically = request.ApplyFleetsAutomatically == true;
                        import.Categorization = categorization;
                    }

                    if (request.AllNew == true)
                    {
                        VesselImportBatchDetail? detail = await importService.ReadBatchAsync(tenantId, request.BatchId).ConfigureAwait(false);
                        if (detail == null) return (object)McpToolError.NotFound("Import batch not found", VesselImportCodes.BatchNotFound);
                        import.Paths = detail.Items.Where(i => i.CandidateStatus == VesselImportCandidateStatusEnum.New).Select(i => i.Path).ToList();
                        if (import.Paths.Count == 0) return (object)McpToolError.Conflict("Batch has no candidates with status New", VesselImportCodes.InvalidRequest);
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
                                return (object)McpToolError.InvalidArgument("Unknown landingMode: " + request.LandingMode, VesselImportCodes.InvalidRequest);
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
                        return (object)McpToolError.FromException(ex, VesselImportCodes.BatchNotFound);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return (object)McpToolError.FromException(ex, VesselImportCodes.BatchBusy);
                    }
                    catch (ArgumentException ex)
                    {
                        return (object)McpToolError.FromException(ex, VesselImportCodes.InvalidRequest);
                    }
                });

            if (categorizationService != null) RegisterCategorizationTools(register, importService, categorizationService);
        }

        private static void RegisterCategorizationTools(RegisterToolDelegate register, IVesselImportService importService, IFleetCategorizationService categorizationService)
        {
            register(
                "categorize_vessel_import",
                "Run or retry fleet categorization for an import batch whose import finished: a captain reads a manifest of the batch's selected repositories and recommends fleets (FleetCategorization background job). Omitted arguments reuse the batch's previous captain, prompt, and auto-apply choice. Poll the batch (enumerate entityType vessel_import_batch, or the job) until categorizationStatus is Completed, Failed, or Applied. Requires tenant admin.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        batchId = new { type = "string", description = "Batch ID (vib_ prefix)" },
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix); omit to reuse the previous captain" },
                        prompt = new { type = "string", description = "Instructions; omit to reuse the previous run's instructions" },
                        applyFleetsAutomatically = new { type = "boolean", description = "Apply the recommendations automatically when the run completes" }
                    },
                    required = new[] { "batchId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (!caller.IsAdmin && !caller.IsTenantAdmin) return (object)McpToolError.Forbidden("categorize_vessel_import requires a tenant admin");

                    CategorizeVesselImportArgs request = JsonSerializer.Deserialize<CategorizeVesselImportArgs>(args!.Value, _JsonOptions)!;
                    VesselImportCategorizationRequest overrides = new VesselImportCategorizationRequest();
                    overrides.Enabled = true;
                    overrides.CaptainId = request.CaptainId;
                    overrides.Prompt = request.Prompt;
                    try
                    {
                        string tenantId = ResolveTenant(caller);
                        if (request.ApplyFleetsAutomatically.HasValue) overrides.ApplyAutomatically = request.ApplyFleetsAutomatically.Value;
                        else
                        {
                            VesselImportBatchDetail? existing = await importService.ReadBatchAsync(tenantId, request.BatchId).ConfigureAwait(false);
                            if (existing == null) return (object)McpToolError.NotFound("Import batch not found", VesselImportCodes.BatchNotFound);
                            overrides.ApplyAutomatically = existing.Batch.CategorizationApplyAutomatically;
                        }

                        VesselImportBatch batch = await categorizationService.CategorizeAsync(tenantId, request.BatchId, caller.UserId, overrides).ConfigureAwait(false);
                        return (object)batch;
                    }
                    catch (KeyNotFoundException ex)
                    {
                        return (object)McpToolError.FromException(ex, VesselImportCodes.BatchNotFound);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return (object)McpToolError.FromException(ex, VesselImportCodes.BatchBusy);
                    }
                    catch (ArgumentException ex)
                    {
                        return (object)McpToolError.FromException(ex, VesselImportCodes.InvalidRequest);
                    }
                });

            register(
                "apply_fleet_recommendations",
                "Apply fleet recommendations to an import batch: reuses tenant fleets with the same name (case-insensitive) or creates them, and assigns each listed vessel. Pass fleets to apply an edited list, or omit it to apply the captain's stored recommendations unchanged. A fleet named Uncategorized is not created. Requires tenant admin.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        batchId = new { type = "string", description = "Batch ID (vib_ prefix)" },
                        fleets = new
                        {
                            type = "array",
                            description = "Fleets to apply; omit to apply the stored recommendations",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string", description = "Fleet name" },
                                    description = new { type = "string", description = "Description for a newly created fleet" },
                                    vesselIds = new { type = "array", items = new { type = "string" }, description = "Vessel IDs (vsl_ prefix) from the batch" }
                                },
                                required = new[] { "name", "vesselIds" }
                            }
                        }
                    },
                    required = new[] { "batchId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (!caller.IsAdmin && !caller.IsTenantAdmin) return (object)McpToolError.Forbidden("apply_fleet_recommendations requires a tenant admin");

                    ApplyFleetRecommendationsArgs request = JsonSerializer.Deserialize<ApplyFleetRecommendationsArgs>(args!.Value, _JsonOptions)!;
                    string tenantId = ResolveTenant(caller);
                    FleetRecommendationApplyRequest apply = new FleetRecommendationApplyRequest();
                    if (request.Fleets != null && request.Fleets.Count > 0)
                    {
                        apply.Fleets = request.Fleets;
                    }
                    else
                    {
                        VesselImportBatchDetail? detail = await importService.ReadBatchAsync(tenantId, request.BatchId).ConfigureAwait(false);
                        if (detail == null) return (object)McpToolError.NotFound("Import batch not found", VesselImportCodes.BatchNotFound);
                        if (detail.FleetRecommendations.Count == 0) return (object)McpToolError.Conflict("Batch has no fleet recommendations to apply", VesselImportCodes.InvalidRequest);
                        foreach (VesselImportFleetRecommendation recommendation in detail.FleetRecommendations)
                        {
                            FleetRecommendationApplyFleet fleet = new FleetRecommendationApplyFleet();
                            fleet.Name = recommendation.Name;
                            fleet.Description = recommendation.Description;
                            fleet.VesselIds = new List<string>(recommendation.VesselIds);
                            apply.Fleets.Add(fleet);
                        }
                    }

                    try
                    {
                        FleetRecommendationApplyResult result = await categorizationService.ApplyAsync(tenantId, request.BatchId, caller.UserId, apply).ConfigureAwait(false);
                        return (object)result;
                    }
                    catch (KeyNotFoundException ex)
                    {
                        return (object)McpToolError.FromException(ex, VesselImportCodes.BatchNotFound);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return (object)McpToolError.FromException(ex, VesselImportCodes.BatchBusy);
                    }
                    catch (ArgumentException ex)
                    {
                        return (object)McpToolError.FromException(ex, VesselImportCodes.InvalidRequest);
                    }
                });
        }

        private static string ResolveTenant(AuthContext caller)
        {
            return String.IsNullOrEmpty(caller.TenantId) ? Constants.DefaultTenantId : caller.TenantId;
        }
    }
}
