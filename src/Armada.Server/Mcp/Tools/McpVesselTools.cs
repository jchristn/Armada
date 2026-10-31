namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Registers MCP tools for vessel CRUD operations.
    /// </summary>
    public static class McpVesselTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers vessel MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for vessel data access.</param>
        /// <param name="dockService">Optional dock service for worktree cleanup during vessel deletion.</param>
        /// <param name="vesselService">Optional shared vessel creation service. Defaults to a new <see cref="VesselService"/>.</param>
        /// <param name="settings">Settings (managed docks and repos directories for safe vessel cleanup).</param>
        /// <param name="admiral">Admiral service; deleting a vessel cancels its active missions through it so their agents stop.</param>
        /// <param name="blackBox">Optional black box capture for the missions delete_vessel cancels (step VesselDeleted).</param>
        /// <param name="blackBoxHooks">Optional black box lifecycle hooks, called after deletes and updates (retention cascades, expiry recompute).</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, IDockService? dockService = null, IVesselService? vesselService = null, Armada.Core.Settings.ArmadaSettings? settings = null, IAdmiralService? admiral = null, Armada.Core.BlackBox.IBlackBoxCapture? blackBox = null, Armada.Core.BlackBox.IBlackBoxLifecycleHooks? blackBoxHooks = null)
        {
            IVesselService creator = vesselService ?? new VesselService(database);

            register(
                "get_vessel",
                "Get details of a specific vessel (repository)",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" }
                    },
                    required = new[] { "vesselId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    VesselIdArgs request = JsonSerializer.Deserialize<VesselIdArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    Vessel? vessel = await McpCallerScope.ReadVesselAsync(database, caller, vesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)McpToolError.NotFound("Vessel not found");
                    return (object)vessel;
                });

            register(
                "add_vessel",
                "Register a new vessel (git repository) in a fleet",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Display name for the vessel" },
                        repoUrl = new { type = "string", description = "Git repository URL (HTTPS or SSH)" },
                        fleetId = new { type = "string", description = "Fleet ID to add the vessel to" },
                        defaultBranch = new { type = "string", description = "Default branch name (defaults to main)" },
                        projectContext = new { type = "string", description = "Project context describing architecture, key files, and dependencies" },
                        styleGuide = new { type = "string", description = "Style guide describing naming conventions, patterns, and library preferences" },
                        workingDirectory = new { type = "string", description = "Optional local directory where completed mission changes will be pulled after merge. When repoUrl is a local clone (a file:// URL or an existing local path) and this is omitted, it is set automatically to that local clone so the vessel is immediately usable (e.g. for Rebuild Armada)." },
                        gitHubTokenOverride = new { type = "string", description = "Optional per-vessel GitHub token override. Leave unset to use the global configured token." },
                        allowConcurrentMissions = new { type = "boolean", description = "Allow multiple concurrent missions on this vessel (default false)" },
                        serializeOverlappingMissions = new { type = "boolean", description = "Chain a dispatched mission after an active mission whose files it overlaps (or whose reserved hotspot directory it touches) with dependsOnMissionId instead of running both in parallel (default false: overlaps are only reported as dispatch warnings)" },
                        autoApprove = new { type = "boolean", description = "Per-vessel auto-approve override for missions on this vessel: true or false wins over the captain's setting; omit to use the captain's setting" },
                        enableModelContext = new { type = "boolean", description = "Enable model context accumulation -- agents will update context with key information discovered during missions (default true)" },
                        defaultPipelineId = new { type = "string", description = "Default pipeline ID for dispatches to this vessel (ppl_ prefix)" },
                        testCommand = new { type = "string", description = "Per-vessel override for the Test intent (beats a repo script and the ecosystem default)" },
                        lintCommand = new { type = "string", description = "Per-vessel override for the Lint intent (beats a repo script and the ecosystem default)" },
                        auditCommand = new { type = "string", description = "Per-vessel override for the Audit intent (beats a repo script and the ecosystem default)" },
                        workerEnvelope = WorkerEnvelopeSchema("Optional default Worker eligibility envelope for missions on this vessel: only captains whose model profile meets it run Worker missions, and the Architect may tighten it per mission but never loosen it. Omit or pass {} for no envelope (routing unchanged).")
                    },
                    required = new[] { "name", "repoUrl", "fleetId" }
                },
                async (args) =>
                {
                    VesselAddArgs request = JsonSerializer.Deserialize<VesselAddArgs>(args!.Value, _JsonOptions)!;
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (!String.IsNullOrEmpty(request.FleetId) && await McpCallerScope.ReadFleetAsync(database, caller, request.FleetId).ConfigureAwait(false) == null)
                        return (object)McpToolError.NotFound("Fleet not found");
                    Vessel vessel = new Vessel();
                    vessel.TenantId = String.IsNullOrEmpty(caller.TenantId) ? ArmadaConstants.DefaultTenantId : caller.TenantId;
                    vessel.UserId = caller.UserId;
                    vessel.Name = request.Name;
                    vessel.RepoUrl = request.RepoUrl;
                    vessel.FleetId = request.FleetId;
                    vessel.DefaultBranch = request.DefaultBranch ?? "main";
                    vessel.ProjectContext = request.ProjectContext;
                    vessel.StyleGuide = request.StyleGuide;
                    vessel.WorkingDirectory = request.WorkingDirectory;
                    vessel.GitHubTokenOverride = request.GitHubTokenOverride;
                    vessel.AllowConcurrentMissions = request.AllowConcurrentMissions ?? false;
                    vessel.SerializeOverlappingMissions = request.SerializeOverlappingMissions ?? false;
                    vessel.AutoApprove = request.AutoApprove;
                    vessel.EnableModelContext = request.EnableModelContext ?? true;
                    vessel.DefaultPipelineId = request.DefaultPipelineId;
                    vessel.TestCommand = String.IsNullOrWhiteSpace(request.TestCommand) ? null : request.TestCommand;
                    vessel.LintCommand = String.IsNullOrWhiteSpace(request.LintCommand) ? null : request.LintCommand;
                    vessel.AuditCommand = String.IsNullOrWhiteSpace(request.AuditCommand) ? null : request.AuditCommand;
                    try
                    {
                        vessel.WorkerEnvelope = WorkerEnvelopeRules.Normalize(request.WorkerEnvelope);
                    }
                    catch (ArgumentException ex)
                    {
                        return (object)McpToolError.InvalidArgument(ex.Message);
                    }
                    // A local-clone repoUrl with no explicit working directory gets that clone as its working
                    // directory; LocalPath is deliberately never set (see VesselService.CreateAsync).
                    try
                    {
                        vessel = await creator.CreateAsync(vessel, true).ConfigureAwait(false);
                    }
                    catch (ArgumentException ex)
                    {
                        return (object)McpToolError.FromException(ex);
                    }
                    return (object)vessel;
                });

            register(
                "update_vessel",
                "Update an existing vessel's properties",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" },
                        name = new { type = "string", description = "New display name" },
                        repoUrl = new { type = "string", description = "New repository URL" },
                        defaultBranch = new { type = "string", description = "New default branch" },
                        projectContext = new { type = "string", description = "New project context" },
                        styleGuide = new { type = "string", description = "New style guide" },
                        workingDirectory = new { type = "string", description = "New local directory where completed mission changes will be pulled after merge" },
                        gitHubTokenOverride = new { type = "string", description = "Optional per-vessel GitHub token override. Empty string clears the existing override." },
                        allowConcurrentMissions = new { type = "boolean", description = "Allow multiple concurrent missions on this vessel" },
                        serializeOverlappingMissions = new { type = "boolean", description = "Chain a dispatched mission after an active mission whose files it overlaps (or whose reserved hotspot directory it touches) with dependsOnMissionId instead of running both in parallel; false only reports overlaps as dispatch warnings" },
                        autoApprove = new { type = "boolean", description = "Per-vessel auto-approve override for missions on this vessel: true or false wins over the captain's setting" },
                        clearAutoApprove = new { type = "boolean", description = "Remove the per-vessel auto-approve override so the captain's own setting applies" },
                        enableModelContext = new { type = "boolean", description = "Enable or disable model context accumulation" },
                        modelContext = new { type = "string", description = "Agent-accumulated context about this repository" },
                        defaultPipelineId = new { type = "string", description = "Default pipeline ID for dispatches to this vessel (ppl_ prefix)" },
                        autoLandEnabled = new { type = "boolean", description = "Whether the auto-land predicate gates unattended landing on this vessel" },
                        autoLandMaxFiles = new { type = "integer", description = "Maximum number of changed files that may auto-land unattended (0 = no limit)" },
                        autoLandMaxLines = new { type = "integer", description = "Maximum number of changed lines that may auto-land unattended (0 = no limit)" },
                        autoLandPathAllowGlobs = new { type = "array", items = new { type = "string" }, description = "Glob patterns a changed path must match to be auto-landable" },
                        autoLandPathDenyGlobs = new { type = "array", items = new { type = "string" }, description = "Glob patterns that force a hold: a change touching any matching path never auto-lands" },
                        definitionOfDoneEnabled = new { type = "boolean", description = "Whether the in-dock Definition-of-Done gate runs build and unit tests before acceptance" },
                        definitionOfDoneBuildCommand = new { type = "string", description = "Shell command that builds the project inside the mission checkout (for example dotnet build)" },
                        definitionOfDoneTestCommand = new { type = "string", description = "Shell command that runs unit tests inside the mission checkout (for example dotnet test)" },
                        definitionOfDoneTimeoutSeconds = new { type = "integer", description = "Per-phase timeout in seconds for each Definition-of-Done command (clamped to 30..7200)" },
                        testCommand = new { type = "string", description = "Per-vessel override for the Test intent (beats a repo script and the ecosystem default)" },
                        lintCommand = new { type = "string", description = "Per-vessel override for the Lint intent (beats a repo script and the ecosystem default)" },
                        auditCommand = new { type = "string", description = "Per-vessel override for the Audit intent (beats a repo script and the ecosystem default)" },
                        landingMode = new { type = "string", description = "Landing mode override for this vessel: " + McpToolHelpers.EnumNames<Armada.Core.Enums.LandingModeEnum>() + ", or Default to clear it so the global setting applies" },
                        workerEnvelope = WorkerEnvelopeSchema("Default Worker eligibility envelope for missions on this vessel: only captains whose model profile meets it run Worker missions, and the Architect may tighten it per mission but never loosen it. The object replaces the stored envelope; pass {} to clear it; omit it to keep the stored value.")
                    },
                    required = new[] { "vesselId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    VesselUpdateArgs request = JsonSerializer.Deserialize<VesselUpdateArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    Vessel? vessel = await McpCallerScope.ReadVesselAsync(database, caller, vesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)McpToolError.NotFound("Vessel not found");
                    if (request.Name != null)
                        vessel.Name = request.Name;
                    if (request.RepoUrl != null)
                        vessel.RepoUrl = request.RepoUrl;
                    if (request.DefaultBranch != null)
                        vessel.DefaultBranch = request.DefaultBranch;
                    if (request.ProjectContext != null)
                        vessel.ProjectContext = request.ProjectContext;
                    if (request.StyleGuide != null)
                        vessel.StyleGuide = request.StyleGuide;
                    if (request.WorkingDirectory != null)
                        vessel.WorkingDirectory = request.WorkingDirectory;
                    if (request.GitHubTokenOverride != null)
                    {
                        vessel.GitHubTokenOverride = request.GitHubTokenOverride;
                        vessel.NormalizeGitHubTokenOverride();
                    }
                    if (request.AllowConcurrentMissions.HasValue)
                        vessel.AllowConcurrentMissions = request.AllowConcurrentMissions.Value;
                    if (request.SerializeOverlappingMissions.HasValue)
                        vessel.SerializeOverlappingMissions = request.SerializeOverlappingMissions.Value;
                    if (request.ClearAutoApprove == true)
                        vessel.AutoApprove = null;
                    else if (request.AutoApprove.HasValue)
                        vessel.AutoApprove = request.AutoApprove.Value;
                    if (request.EnableModelContext.HasValue)
                        vessel.EnableModelContext = request.EnableModelContext.Value;
                    if (request.ModelContext != null)
                        vessel.ModelContext = request.ModelContext;
                    if (request.DefaultPipelineId != null)
                        vessel.DefaultPipelineId = request.DefaultPipelineId;
                    if (request.AutoLandEnabled.HasValue)
                        vessel.AutoLandEnabled = request.AutoLandEnabled.Value;
                    if (request.AutoLandMaxFiles.HasValue)
                        vessel.AutoLandMaxFiles = request.AutoLandMaxFiles.Value;
                    if (request.AutoLandMaxLines.HasValue)
                        vessel.AutoLandMaxLines = request.AutoLandMaxLines.Value;
                    if (request.AutoLandPathAllowGlobs != null)
                        vessel.AutoLandPathAllowGlobs = request.AutoLandPathAllowGlobs;
                    if (request.AutoLandPathDenyGlobs != null)
                        vessel.AutoLandPathDenyGlobs = request.AutoLandPathDenyGlobs;
                    if (request.DefinitionOfDoneEnabled.HasValue)
                        vessel.DefinitionOfDoneEnabled = request.DefinitionOfDoneEnabled.Value;
                    if (request.DefinitionOfDoneBuildCommand != null)
                        vessel.DefinitionOfDoneBuildCommand = request.DefinitionOfDoneBuildCommand;
                    if (request.DefinitionOfDoneTestCommand != null)
                        vessel.DefinitionOfDoneTestCommand = request.DefinitionOfDoneTestCommand;
                    if (request.DefinitionOfDoneTimeoutSeconds.HasValue)
                        vessel.DefinitionOfDoneTimeoutSeconds = request.DefinitionOfDoneTimeoutSeconds.Value;
                    if (request.TestCommand != null)
                        vessel.TestCommand = String.IsNullOrWhiteSpace(request.TestCommand) ? null : request.TestCommand;
                    if (request.LintCommand != null)
                        vessel.LintCommand = String.IsNullOrWhiteSpace(request.LintCommand) ? null : request.LintCommand;
                    if (request.AuditCommand != null)
                        vessel.AuditCommand = String.IsNullOrWhiteSpace(request.AuditCommand) ? null : request.AuditCommand;
                    if (request.LandingMode != null)
                    {
                        if (String.IsNullOrWhiteSpace(request.LandingMode) || String.Equals(request.LandingMode.Trim(), "Default", StringComparison.OrdinalIgnoreCase))
                            vessel.LandingMode = null;
                        else if (Enum.TryParse<Armada.Core.Enums.LandingModeEnum>(request.LandingMode.Trim(), true, out Armada.Core.Enums.LandingModeEnum landingMode) && Enum.IsDefined(typeof(Armada.Core.Enums.LandingModeEnum), landingMode))
                            vessel.LandingMode = landingMode;
                        else
                            return (object)McpToolError.InvalidArgument("landingMode must be one of " + McpToolHelpers.EnumNames<Armada.Core.Enums.LandingModeEnum>() + ", or Default.");
                    }
                    if (request.WorkerEnvelope != null)
                    {
                        try
                        {
                            vessel.WorkerEnvelope = WorkerEnvelopeRules.Normalize(request.WorkerEnvelope);
                        }
                        catch (ArgumentException ex)
                        {
                            return (object)McpToolError.InvalidArgument(ex.Message);
                        }
                    }
                    await DuplicateEntityGuard.EnsureVesselNameAvailableAsync(database, vessel).ConfigureAwait(false);
                    vessel = await database.Vessels.UpdateAsync(vessel).ConfigureAwait(false);
                    if (blackBoxHooks != null) await blackBoxHooks.OnVesselUpdatedAsync(vessel.Id).ConfigureAwait(false);
                    return (object)vessel;
                });

            register(
                "delete_vessel",
                "Delete a vessel by ID. Returns status, vesselId, warningCount, and (when cleanup left anything behind) the first 50 warnings.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" }
                    },
                    required = new[] { "vesselId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    VesselIdArgs request = JsonSerializer.Deserialize<VesselIdArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    Vessel? vessel = await McpCallerScope.ReadVesselAsync(database, caller, vesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)McpToolError.NotFound("Vessel not found");

                    List<string> warnings = await CleanupVesselResourcesAsync(vessel, database, dockService, settings, admiral, blackBox).ConfigureAwait(false);

                    await database.Vessels.DeleteAsync(vesselId).ConfigureAwait(false);
                    if (blackBoxHooks != null) await blackBoxHooks.OnVesselDeletedAsync(vesselId).ConfigureAwait(false);
                    return (object)VesselDeleteResult.Create(vesselId, warnings);
                });

            register(
                "delete_vessels",
                "Permanently delete multiple vessels from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ids = new { type = "array", items = new { type = "string" }, description = "List of vessel IDs to delete (vsl_ prefix; at most 1000)" }
                    },
                    required = new[] { "ids" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    DeleteMultipleArgs request = JsonSerializer.Deserialize<DeleteMultipleArgs>(args!.Value, _JsonOptions)!;
                    if (request.Ids == null || request.Ids.Count == 0)
                        return (object)McpToolError.InvalidArgument("ids is required and must not be empty");
                    CollectionLimits.RequireRequestItems(request.Ids, "ids");

                    DeleteMultipleResult result = new DeleteMultipleResult();
                    foreach (string id in request.Ids)
                    {
                        if (String.IsNullOrEmpty(id))
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id ?? "", "Empty ID"));
                            continue;
                        }
                        Vessel? vessel = await McpCallerScope.ReadVesselAsync(database, caller, id).ConfigureAwait(false);
                        if (vessel == null)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                            continue;
                        }

                        await CleanupVesselResourcesAsync(vessel, database, dockService, settings, admiral, blackBox).ConfigureAwait(false);
                        await database.Vessels.DeleteAsync(id).ConfigureAwait(false);
                        if (blackBoxHooks != null) await blackBoxHooks.OnVesselDeletedAsync(id).ConfigureAwait(false);
                        result.Deleted++;
                    }
                    result.ResolveStatus();
                    return (object)result;
                });

            register(
                "update_vessel_context",
                "Update a vessel's project context and style guide without modifying other properties",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" },
                        projectContext = new { type = "string", description = "Project context describing architecture, key files, and dependencies" },
                        styleGuide = new { type = "string", description = "Style guide describing naming conventions, patterns, and library preferences" },
                        modelContext = new { type = "string", description = "Agent-accumulated context about this repository -- key information discovered during missions" }
                    },
                    required = new[] { "vesselId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    VesselContextArgs request = JsonSerializer.Deserialize<VesselContextArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    Vessel? vessel = await McpCallerScope.ReadVesselAsync(database, caller, vesselId).ConfigureAwait(false)
                        ?? await McpMissionScope.ReadMissionVesselAsync(database, caller, vesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)McpToolError.NotFound("Vessel not found");
                    if (request.ProjectContext != null)
                        vessel.ProjectContext = request.ProjectContext;
                    if (request.StyleGuide != null)
                        vessel.StyleGuide = request.StyleGuide;
                    if (request.ModelContext != null)
                        vessel.ModelContext = request.ModelContext;
                    vessel = await database.Vessels.UpdateAsync(vessel).ConfigureAwait(false);
                    return (object)vessel;
                });
        }

        /// <summary>
        /// Cleans up ALL filesystem and database resources associated with a vessel before deletion.
        /// Cancels active missions, removes docks/worktrees, and deletes the bare repository.
        /// This method throws on failure -- vessel deletion should NOT proceed if cleanup fails.
        /// </summary>
        internal static object WorkerEnvelopeSchema(string description)
        {
            return new
            {
                type = "object",
                description = description,
                properties = new
                {
                    maxFiles = new { type = "integer", description = "Most files one Worker mission should change (1 to 1000); guides slicing and the Judge" },
                    maxSteps = new { type = "integer", description = "Most steps one Worker mission should take (1 to 1000); guides slicing and the Judge" },
                    minServedContextTokens = new { type = "integer", description = "Smallest served context in tokens a captain's model profile must record (1 to 100000000)" },
                    requiredTools = new { type = "array", items = new { type = "string", @enum = new[] { WorkerEnvelopeRules.ToolCalls, WorkerEnvelopeRules.Mcp } }, description = "Capabilities a captain's model profile must record as available: tools (tool calls) and mcp (MCP tools)" }
                }
            };
        }

        private static async Task<List<string>> CleanupVesselResourcesAsync(Vessel vessel, DatabaseDriver database, IDockService? dockService, Armada.Core.Settings.ArmadaSettings? settings, IAdmiralService? admiral, Armada.Core.BlackBox.IBlackBoxCapture? blackBox)
        {
            List<string> errors = new List<string>();

            // Cancel active missions on this vessel
            List<Mission> missions = await database.Missions.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);
            foreach (Mission mission in missions)
            {
                if (mission.Status == Armada.Core.Enums.MissionStatusEnum.Pending
                    || mission.Status == Armada.Core.Enums.MissionStatusEnum.Assigned
                    || mission.Status == Armada.Core.Enums.MissionStatusEnum.InProgress
                    || mission.Status == Armada.Core.Enums.MissionStatusEnum.Review
                    || mission.Status == Armada.Core.Enums.MissionStatusEnum.Testing)
                {
                    Armada.Core.Enums.MissionStatusEnum statusBefore = mission.Status;
                    if (admiral != null)
                    {
                        // Stop the agent before the mission is cancelled; a failed stop is reported, not hidden. The
                        // cancel records the single VesselDeleted entry.
                        try { await admiral.CancelMissionAsync(mission, BlackBoxOperatorCapture.VesselDeletedCancel(statusBefore, vessel, mission.VesselId, "MCP")).ConfigureAwait(false); }
                        catch (InvalidOperationException ex) { errors.Add("Mission " + mission.Id + ": " + ex.Message); continue; }
                    }
                    else
                    {
                        mission.Status = Armada.Core.Enums.MissionStatusEnum.Cancelled;
                        mission.FailureKind = Armada.Core.Enums.MissionFailureKindEnum.OperatorAction;
                        mission.FailureReason = "Vessel deleted";
                        mission.CompletedUtc = DateTime.UtcNow;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        await database.Missions.UpdateAsync(mission).ConfigureAwait(false);
                        await BlackBoxOperatorCapture.RecordVesselDeletedAsync(blackBox, mission, statusBefore, vessel, "MCP").ConfigureAwait(false);
                    }
                }
            }

            // Delete ALL missions for this vessel from the database
            foreach (Mission mission in missions)
            {
                try { await database.Missions.DeleteAsync(mission.Id).ConfigureAwait(false); }
                catch (Exception ex) { errors.Add("Failed to delete mission " + mission.Id + ": " + ex.Message); }
            }

            // Clean up docks/worktrees for this vessel
            List<Dock> docks = await database.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);
            foreach (Dock dock in docks)
            {
                try
                {
                    if (dockService != null)
                    {
                        await dockService.PurgeAsync(dock.Id).ConfigureAwait(false);
                    }
                    else
                    {
                        if (!String.IsNullOrEmpty(dock.WorktreePath) && Directory.Exists(dock.WorktreePath))
                            Directory.Delete(dock.WorktreePath, true);
                        await database.Docks.DeleteAsync(dock.Id).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) { errors.Add("Failed to purge dock " + dock.Id + ": " + ex.Message); }
            }

            // Delete the vessel's dock directory and bare repo, but only inside the managed docks and repos roots
            // (LocalPath and Name are caller-controlled).
            string docksRoot = settings?.DocksDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".armada", "docks");
            string reposRoot = settings?.ReposDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".armada", "repos");
            string vesselDockDir = Path.Combine(docksRoot, vessel.Name);
            if (Directory.Exists(vesselDockDir))
            {
                if (!ManagedPaths.IsStrictlyUnder(vesselDockDir, docksRoot))
                    errors.Add("Dock directory left in place (outside the managed docks directory): " + vesselDockDir);
                else
                {
                    try { Directory.Delete(vesselDockDir, true); }
                    catch (Exception ex) { errors.Add("Failed to delete dock directory " + vesselDockDir + ": " + ex.Message); }
                }
            }

            if (!String.IsNullOrEmpty(vessel.LocalPath) && Directory.Exists(vessel.LocalPath))
            {
                if (!ManagedPaths.IsStrictlyUnder(vessel.LocalPath, reposRoot))
                {
                    errors.Add("Bare repo left in place (outside the managed repos directory): " + vessel.LocalPath);
                }
                else
                {
                    try { Directory.Delete(vessel.LocalPath, true); }
                    catch (Exception ex) { errors.Add("Failed to delete bare repo " + vessel.LocalPath + ": " + ex.Message); }

                    // If the bare repo STILL exists after deletion attempt, that's a hard failure
                    if (Directory.Exists(vessel.LocalPath))
                        errors.Add("Bare repo still exists after deletion: " + vessel.LocalPath);
                }
            }

            return errors;
        }
    }
}
