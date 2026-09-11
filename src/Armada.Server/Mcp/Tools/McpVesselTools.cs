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
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, IDockService? dockService = null)
        {
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
                    VesselIdArgs request = JsonSerializer.Deserialize<VesselIdArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    Vessel? vessel = await database.Vessels.ReadAsync(vesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)new { Error = "Vessel not found" };
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
                        enableModelContext = new { type = "boolean", description = "Enable model context accumulation -- agents will update context with key information discovered during missions (default false)" },
                        defaultPipelineId = new { type = "string", description = "Default pipeline ID for dispatches to this vessel (ppl_ prefix)" }
                    },
                    required = new[] { "name", "repoUrl", "fleetId" }
                },
                async (args) =>
                {
                    VesselAddArgs request = JsonSerializer.Deserialize<VesselAddArgs>(args!.Value, _JsonOptions)!;
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
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
                    // When the repo is a local clone (file:// URL or an existing local path) and no explicit
                    // working directory was given, point the working directory at that clone so the vessel is
                    // immediately buildable/usable (notably for Settings > Rebuild Armada). We deliberately do
                    // NOT set LocalPath here: LocalPath is treated as an Armada-managed bare repo and is
                    // deleted on vessel removal, which must never wipe the user's own working clone.
                    if (String.IsNullOrWhiteSpace(vessel.WorkingDirectory)
                        && TryResolveLocalClonePath(request.RepoUrl, out string? localClone))
                    {
                        vessel.WorkingDirectory = localClone;
                    }
                    vessel.GitHubTokenOverride = request.GitHubTokenOverride;
                    vessel.NormalizeGitHubTokenOverride();
                    vessel.AllowConcurrentMissions = request.AllowConcurrentMissions ?? false;
                    vessel.EnableModelContext = request.EnableModelContext ?? true;
                    vessel.DefaultPipelineId = request.DefaultPipelineId;
                    vessel = await database.Vessels.CreateAsync(vessel).ConfigureAwait(false);
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
                        enableModelContext = new { type = "boolean", description = "Enable or disable model context accumulation" },
                        modelContext = new { type = "string", description = "Agent-accumulated context about this repository" },
                        defaultPipelineId = new { type = "string", description = "Default pipeline ID for dispatches to this vessel (ppl_ prefix)" }
                    },
                    required = new[] { "vesselId" }
                },
                async (args) =>
                {
                    VesselUpdateArgs request = JsonSerializer.Deserialize<VesselUpdateArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    Vessel? vessel = await database.Vessels.ReadAsync(vesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)new { Error = "Vessel not found" };
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
                    vessel = await database.Vessels.UpdateAsync(vessel).ConfigureAwait(false);
                    return (object)vessel;
                });

            register(
                "delete_vessel",
                "Delete a vessel by ID",
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
                    VesselIdArgs request = JsonSerializer.Deserialize<VesselIdArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    Vessel? vessel = await database.Vessels.ReadAsync(vesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)new { Error = "Vessel not found" };

                    List<string> warnings = await CleanupVesselResourcesAsync(vessel, database, dockService).ConfigureAwait(false);

                    await database.Vessels.DeleteAsync(vesselId).ConfigureAwait(false);
                    if (warnings.Count > 0)
                        return (object)new { Status = "deleted", VesselId = vesselId, Warnings = warnings };
                    return (object)new { Status = "deleted", VesselId = vesselId };
                });

            register(
                "delete_vessels",
                "Permanently delete multiple vessels from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ids = new { type = "array", items = new { type = "string" }, description = "List of vessel IDs to delete (vsl_ prefix)" }
                    },
                    required = new[] { "ids" }
                },
                async (args) =>
                {
                    DeleteMultipleArgs request = JsonSerializer.Deserialize<DeleteMultipleArgs>(args!.Value, _JsonOptions)!;
                    if (request.Ids == null || request.Ids.Count == 0)
                        return (object)new { Error = "ids is required and must not be empty" };

                    DeleteMultipleResult result = new DeleteMultipleResult();
                    foreach (string id in request.Ids)
                    {
                        if (String.IsNullOrEmpty(id))
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id ?? "", "Empty ID"));
                            continue;
                        }
                        Vessel? vessel = await database.Vessels.ReadAsync(id).ConfigureAwait(false);
                        if (vessel == null)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                            continue;
                        }

                        await CleanupVesselResourcesAsync(vessel, database, dockService).ConfigureAwait(false);
                        await database.Vessels.DeleteAsync(id).ConfigureAwait(false);
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
                    VesselContextArgs request = JsonSerializer.Deserialize<VesselContextArgs>(args!.Value, _JsonOptions)!;
                    string vesselId = request.VesselId;
                    Vessel? vessel = await database.Vessels.ReadAsync(vesselId).ConfigureAwait(false);
                    if (vessel == null) return (object)new { Error = "Vessel not found" };
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
        /// Resolve a repository URL to a local clone directory when it refers to one: a <c>file://</c> URL or
        /// an existing local filesystem path. Returns true and the normalized path when the target exists as a
        /// git repository (contains a <c>.git</c> entry); false otherwise. Used to auto-populate a vessel's
        /// working directory so a locally-cloned repo is immediately usable without a separate provision step.
        /// </summary>
        private static bool TryResolveLocalClonePath(string? repoUrl, out string? localPath)
        {
            localPath = null;
            if (String.IsNullOrWhiteSpace(repoUrl)) return false;

            string candidate;
            if (repoUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try { candidate = new Uri(repoUrl).LocalPath; }
                catch (Exception) { return false; }
            }
            else if (Path.IsPathRooted(repoUrl) && (repoUrl.Contains(Path.DirectorySeparatorChar) || repoUrl.Contains('/')))
            {
                candidate = repoUrl;
            }
            else
            {
                return false;
            }

            if (String.IsNullOrWhiteSpace(candidate)) return false;
            try
            {
                candidate = Path.GetFullPath(candidate);
                if (!Directory.Exists(candidate)) return false;
                // Only accept an actual git repository (working tree with .git, or a bare repo directory).
                bool isGit = Directory.Exists(Path.Combine(candidate, ".git"))
                    || File.Exists(Path.Combine(candidate, ".git"))
                    || Directory.Exists(Path.Combine(candidate, "hooks")) && File.Exists(Path.Combine(candidate, "HEAD"));
                if (!isGit) return false;
                localPath = candidate;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Cleans up ALL filesystem and database resources associated with a vessel before deletion.
        /// Cancels active missions, removes docks/worktrees, and deletes the bare repository.
        /// This method throws on failure -- vessel deletion should NOT proceed if cleanup fails.
        /// </summary>
        private static async Task<List<string>> CleanupVesselResourcesAsync(Vessel vessel, DatabaseDriver database, IDockService? dockService)
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
                    mission.Status = Armada.Core.Enums.MissionStatusEnum.Cancelled;
                    mission.FailureReason = "Vessel deleted";
                    mission.CompletedUtc = DateTime.UtcNow;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    await database.Missions.UpdateAsync(mission).ConfigureAwait(false);
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

            // Delete the vessel's dock directory (the parent containing all worktrees)
            string vesselDockDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".armada", "docks", vessel.Name);
            if (Directory.Exists(vesselDockDir))
            {
                try { Directory.Delete(vesselDockDir, true); }
                catch (Exception ex) { errors.Add("Failed to delete dock directory " + vesselDockDir + ": " + ex.Message); }
            }

            // Delete the bare repo
            if (!String.IsNullOrEmpty(vessel.LocalPath) && Directory.Exists(vessel.LocalPath))
            {
                try { Directory.Delete(vessel.LocalPath, true); }
                catch (Exception ex) { errors.Add("Failed to delete bare repo " + vessel.LocalPath + ": " + ex.Message); }
            }

            // If the bare repo STILL exists after deletion attempt, that's a hard failure
            if (!String.IsNullOrEmpty(vessel.LocalPath) && Directory.Exists(vessel.LocalPath))
            {
                errors.Add("Bare repo still exists after deletion: " + vessel.LocalPath);
            }

            return errors;
        }
    }
}
