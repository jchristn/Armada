namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Registers MCP tools for voyage operations (dispatch, status, cancel, purge).
    /// </summary>
    public static class McpVoyageTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers voyage MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for voyage data access.</param>
        /// <param name="admiral">Admiral service for voyage orchestration.</param>
        /// <param name="settings">Optional settings for log/diff cleanup during purge.</param>
        /// <param name="logging">Logging module for playbook validation.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, IAdmiralService admiral, ArmadaSettings? settings, LoggingModule logging)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (admiral == null) throw new ArgumentNullException(nameof(admiral));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            register(
                "dispatch",
                "Dispatch a new voyage with missions to a vessel",
                new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Voyage title" },
                        description = new { type = "string", description = "Voyage description" },
                        vesselId = new { type = "string", description = "Target vessel ID" },
                        missions = new
                        {
                            type = "array",
                            description = "Missions to dispatch, at most 1000",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    title = new { type = "string" },
                                    description = new { type = "string" },
                                    requestedCaptainId = new { type = "string", description = "Optional captain (cpt_ prefix) to run this mission (the first stage in a pipeline); overrides persona captain priorities" },
                                    tier = new { type = "string", description = "Optional minimum captain tier for this mission: Economy, Standard, or Premium" },
                                    persona = new { type = "string", description = "Optional persona for a single-stage mission when no pipeline is given (defaults to Worker)" }
                                }
                            }
                        },
                        landingMode = new { type = "string", description = "Optional landing mode for this voyage: LocalMerge, MergeAndPush, PullRequest, MergeQueue, or None (overrides the vessel and server default)" },
                        isEvaluation = new { type = "boolean", description = "True for evaluation work (fixture battery, model evaluation): the voyage and its missions are flagged and their stage outcomes never count as production data. Default false." },
                        pipelineId = new { type = "string", description = "Pipeline ID to use for this dispatch (overrides vessel/fleet default)" },
                        pipeline = new { type = "string", description = "Pipeline name to use (convenience alias for pipelineId -- resolves by name)" },
                        objectiveId = new { type = "string", description = "Optional objective (obj_ prefix) to link this voyage to; must exist. Parity with REST dispatch." },
                        selectedPlaybooks = new
                        {
                            type = "array",
                            description = "Ordered playbooks to apply during dispatch, at most 256",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    playbookId = new { type = "string", description = "Playbook ID (pbk_ prefix)" },
                                    deliveryMode = new { type = "string", description = "InlineFullContent, InstructionWithReference (default), or AttachIntoWorktree" }
                                },
                                required = new[] { "playbookId", "deliveryMode" }
                            }
                        },
                        approvalOptions = new { type = "object", description = "Per-run tool and review approvals; ApproveAll and timeout Approve require admin.", properties = new {
                            approveAll = new { type = "boolean" }, timeoutSeconds = new { type = "integer", minimum = 10, maximum = 3600 }, timeoutDecision = new { type = "string", @enum = new[] { "Deny", "Approve" } }
                        } },
                        captainAssignments = new
                        {
                            type = "array",
                            description = "Optional per-persona captain overrides. Each entry binds a pipeline step (persona) to a preferred captain and a fallback tier.",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    persona = new { type = "string", description = "Persona name the override applies to (e.g. Worker, Architect, Judge)" },
                                    captainId = new { type = "string", description = "Preferred captain id (cpt_ prefix), or omit for tier-only routing" },
                                    fallbackTier = new { type = "string", description = "Fallback tier when the preferred captain is busy: Economy, Standard, or Premium" }
                                },
                                required = new[] { "persona" }
                            }
                        }
                    },
                    required = new[] { "title" }
                },
                async (args) =>
                {
                    VoyageDispatchArgs request = JsonSerializer.Deserialize<VoyageDispatchArgs>(args!.Value, _JsonOptions)!;
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (request.ApprovalOptions != null && (request.ApprovalOptions.ApproveAll || request.ApprovalOptions.TimeoutDecision == ApprovalTimeoutDecisionEnum.Approve)
                        && !McpToolHelpers.HasPresentedCredential())
                        return (object)McpToolError.Forbidden("Automatic approval requires an administrator's credential; unauthenticated loopback callers cannot enable it.");
                    request.ApprovalOptions?.ValidateFor(caller);
                    if (!String.IsNullOrEmpty(request.VesselId) && await McpCallerScope.ReadVesselAsync(database, caller, request.VesselId).ConfigureAwait(false) == null)
                        return (object)McpToolError.NotFound("Vessel not found");
                    if (!String.IsNullOrWhiteSpace(request.ObjectiveId) && await McpCallerScope.ReadObjectiveAsync(database, caller, request.ObjectiveId).ConfigureAwait(false) == null)
                        return (object)McpToolError.NotFound("Objective not found");
                    string title = request.Title;
                    string description = request.Description ?? "";
                    string vesselId = request.VesselId;
                    List<MissionDescription> missions = request.Missions;
                    List<SelectedPlaybook> selectedPlaybooks = request.SelectedPlaybooks ?? new List<SelectedPlaybook>();
                    CollectionLimits.RequireRequestItems(missions, "Missions");
                    CollectionLimits.RequireConfigItems(selectedPlaybooks, "SelectedPlaybooks");

                    // Shared validation: objective existence, pipeline-by-name resolution, and bare-voyage
                    // detection live in one place so MCP accepts and rejects the same inputs as REST.
                    DispatchValidationResult validation = await admiral.ValidateDispatchAsync(
                        request.ObjectiveId, request.PipelineId, request.Pipeline, vesselId, missions.Count, allowBareVoyage: true).ConfigureAwait(false);
                    if (!validation.IsValid)
                        return (object)McpToolError.InvalidArgument(validation.Message ?? "Invalid dispatch request", validation.Error.ToString());

                    Objective? linkObjective = null;
                    if (!String.IsNullOrWhiteSpace(request.ObjectiveId))
                        linkObjective = await database.Objectives.ReadAsync(request.ObjectiveId!).ConfigureAwait(false);

                    string? pipelineId = validation.ResolvedPipelineId;

                    Voyage voyage;
                    if (validation.IsBareVoyage)
                    {
                        // Bare voyage (missions added separately) -- parity with the REST bare-voyage path.
                        if (selectedPlaybooks.Count > 0)
                        {
                            PlaybookService playbookService = new PlaybookService(database, logging);
                            await playbookService.ResolveSelectionsAsync(
                                String.IsNullOrEmpty(caller.TenantId) ? ArmadaConstants.DefaultTenantId : caller.TenantId,
                                selectedPlaybooks).ConfigureAwait(false);
                        }

                        voyage = new Voyage(title, description);
                        voyage.TenantId = String.IsNullOrEmpty(caller.TenantId) ? ArmadaConstants.DefaultTenantId : caller.TenantId;
                        voyage.UserId = caller.UserId;
                        voyage.ApprovalOptions = request.ApprovalOptions?.Snapshot();
                        voyage.IsEvaluation = request.IsEvaluation;
                        voyage = await database.Voyages.CreateAsync(voyage).ConfigureAwait(false);
                        if (selectedPlaybooks.Count > 0)
                        {
                            voyage.SelectedPlaybooks = selectedPlaybooks;
                            await database.Playbooks.SetVoyageSelectionsAsync(voyage.Id, selectedPlaybooks).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        // The captain overrides go into the dispatch itself (parity with REST): the first stage is assigned
                        // inline during dispatch, so overrides saved afterwards would miss it.
                        string? overridesJson = request.CaptainAssignments != null && request.CaptainAssignments.Count > 0
                            ? Armada.Core.Services.MissionService.SerializeCaptainOverrides(request.CaptainAssignments)
                            : null;
                        voyage = await admiral.DispatchVoyageAsync(title, description, vesselId, missions, pipelineId, selectedPlaybooks, overridesJson, request.LandingMode, approvalOptions: request.ApprovalOptions, isEvaluation: request.IsEvaluation).ConfigureAwait(false);
                    }

                    // A bare voyage has no missions yet: keep its overrides for the missions added to it later.
                    if (validation.IsBareVoyage && request.CaptainAssignments != null && request.CaptainAssignments.Count > 0)
                    {
                        voyage.CaptainOverridesJson = Armada.Core.Services.MissionService.SerializeCaptainOverrides(request.CaptainAssignments);
                        voyage = await database.Voyages.UpdateAsync(voyage).ConfigureAwait(false);
                    }

                    // Link the voyage to the objective (parity with REST).
                    if (linkObjective != null && !linkObjective.VoyageIds.Contains(voyage.Id))
                    {
                        linkObjective.VoyageIds.Add(voyage.Id);
                        await database.Objectives.UpdateAsync(linkObjective).ConfigureAwait(false);
                    }

                    return (object)voyage;
                });

            register(
                "voyage_status",
                "Get status of a specific voyage: the voyage with its mission counts (TotalMissions, MissionCountsByStatus). Missions are never embedded; read them one page at a time with enumerate (entityType missions, voyageId).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        voyageId = new { type = "string", description = "Voyage ID (vyg_ prefix)" },
                        summary = new { type = "boolean", description = "Return a trimmed voyage (id, title, description, status, timestamps, approval options, evaluation flag) with the counts (default true). Set false for the full voyage object with the counts." }
                    },
                    required = new[] { "voyageId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    VoyageStatusArgs request = JsonSerializer.Deserialize<VoyageStatusArgs>(args!.Value, _JsonOptions)!;
                    if (request.IncludeMissions == true)
                        return (object)McpToolError.InvalidArgument("includeMissions was removed: voyage_status never embeds missions. Read them one page at a time with enumerate (entityType missions, voyageId " + request.VoyageId + ").");
                    string voyageId = request.VoyageId;
                    Voyage? voyage = await McpCallerScope.ReadVoyageAsync(database, caller, voyageId).ConfigureAwait(false);
                    if (voyage == null) return (object)McpToolError.NotFound("Voyage not found");

                    VoyageProgress progress = await VoyageProgressReader.ReadAsync(database, voyage).ConfigureAwait(false);
                    if (request.Summary != false)
                    {
                        return (object)new
                        {
                            Voyage = new { voyage.Id, voyage.Title, voyage.Description, voyage.Status, voyage.CreatedUtc, voyage.LastUpdateUtc, voyage.ApprovalOptions, voyage.IsEvaluation },
                            TotalMissions = progress.TotalMissions,
                            MissionCountsByStatus = progress.MissionCountsByStatus
                        };
                    }

                    return (object)new { Voyage = voyage, TotalMissions = progress.TotalMissions, MissionCountsByStatus = progress.MissionCountsByStatus };
                });

            register(
                "list_voyage_progress",
                "Page through voyages with their mission counts (the voyages the status tool counts in ActiveVoyages). With no status the view is the active voyages: InProgress first, then Open, newest first within each. A status selects one voyage status. Returns PageNumber, PageSize, TotalPages, TotalRecords, and Objects (VoyageProgress).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        pageNumber = new { type = "integer", description = "Page number (1-based, default 1)" },
                        pageSize = new { type = "integer", description = "Page size (1 to 1000, default 10; out-of-range values are normalized)" },
                        status = new { type = "string", description = "Optional voyage status: Open, InProgress, Complete, Failed, or Cancelled" },
                        order = new { type = "string", description = "CreatedDescending (default) or CreatedAscending" }
                    }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    VoyageProgressListArgs request = args == null
                        ? new VoyageProgressListArgs()
                        : JsonSerializer.Deserialize<VoyageProgressListArgs>(args.Value, _JsonOptions) ?? new VoyageProgressListArgs();
                    EnumerationQuery query = new EnumerationQuery { Status = request.Status };
                    if (request.PageNumber.HasValue) query.PageNumber = request.PageNumber.Value;
                    if (request.PageSize.HasValue) query.PageSize = request.PageSize.Value;
                    if (!String.IsNullOrWhiteSpace(request.Order))
                    {
                        if (!Enum.TryParse<EnumerationOrderEnum>(request.Order.Trim(), true, out EnumerationOrderEnum order))
                            return (object)McpToolError.InvalidArgument("Unknown order: " + request.Order);
                        query.Order = order;
                    }

                    try
                    {
                        return (object)await VoyageProgressReader.EnumerateAsync(database, caller, query).ConfigureAwait(false);
                    }
                    catch (ArgumentException ex) when (!(ex is CollectionLimitExceededException))
                    {
                        return (object)McpToolError.InvalidArgument(ex.Message);
                    }
                });

            register(
                "cancel_voyage",
                "Cancel an entire voyage and its Pending, Assigned, and Blocked missions. Returns the voyage with CancelledMissions and NotCancelledMissions counts; read which missions remain with enumerate (entityType missions, voyageId).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        voyageId = new { type = "string", description = "Voyage ID (vyg_ prefix)" }
                    },
                    required = new[] { "voyageId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    VoyageIdArgs request = JsonSerializer.Deserialize<VoyageIdArgs>(args!.Value, _JsonOptions)!;
                    string voyageId = request.VoyageId;
                    Voyage? voyage = await McpCallerScope.ReadVoyageAsync(database, caller, voyageId).ConfigureAwait(false);
                    if (voyage == null) return (object)McpToolError.NotFound("Voyage not found");

                    // Cancel only pending/assigned/blocked missions (in-progress work is left running)
                    VoyageCancelResult cancel = await VoyageCancellation.CancelAsync(database, admiral, voyage, McpCallerScope.IsGlobal(caller) ? null : caller.TenantId).ConfigureAwait(false);
                    return (object)cancel;
                });

            register(
                "purge_voyage",
                "Permanently delete a voyage and all its missions from the database. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        voyageId = new { type = "string", description = "Voyage ID (vyg_ prefix)" }
                    },
                    required = new[] { "voyageId" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    VoyageIdArgs request = JsonSerializer.Deserialize<VoyageIdArgs>(args!.Value, _JsonOptions)!;
                    string voyageId = request.VoyageId;
                    Voyage? voyage = await McpCallerScope.ReadVoyageAsync(database, caller, voyageId).ConfigureAwait(false);
                    if (voyage == null) return (object)McpToolError.NotFound("Voyage not found");

                    // Block deletion of active voyages
                    if (voyage.Status == VoyageStatusEnum.Open || voyage.Status == VoyageStatusEnum.InProgress)
                        return (object)McpToolError.Conflict("Cannot delete voyage while status is " + voyage.Status + ". Cancel the voyage first.");

                    List<Mission> missions = await database.Missions.EnumerateByVoyageAsync(voyageId).ConfigureAwait(false);

                    // Block deletion if any missions are actively assigned or in progress
                    int activeMissionCount = missions.Count(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress);
                    if (activeMissionCount > 0)
                        return (object)McpToolError.Conflict("Cannot delete voyage with " + activeMissionCount + " active mission(s) in Assigned or InProgress status. Cancel or complete them first.");

                    foreach (Mission m in missions)
                    {
                        await CleanupMissionResourcesAsync(m, database, settings).ConfigureAwait(false);
                        await database.Missions.DeleteAsync(m.Id).ConfigureAwait(false);
                    }

                    await database.Voyages.DeleteAsync(voyageId).ConfigureAwait(false);
                    return (object)new { Status = "deleted", VoyageId = voyageId, MissionsDeleted = missions.Count };
                });

            register(
                "delete_voyages",
                "Permanently delete multiple voyages and their associated missions from the database by ID. Voyages that are Open/InProgress or have active missions are skipped. Returns a summary of deleted and skipped entries. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ids = new { type = "array", items = new { type = "string" }, description = "List of voyage IDs to delete (vyg_ prefix), at most 1000" }
                    },
                    required = new[] { "ids" }
                },
                async (args) =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    DeleteMultipleArgs request = JsonSerializer.Deserialize<DeleteMultipleArgs>(args!.Value, _JsonOptions)!;
                    if (request.Ids == null || request.Ids.Count == 0)
                        return (object)McpToolError.InvalidArgument("ids is required and must not be empty");
                    CollectionLimits.RequireRequestItems(request.Ids, "Ids");

                    DeleteMultipleResult result = new DeleteMultipleResult();
                    foreach (string id in request.Ids)
                    {
                        if (String.IsNullOrEmpty(id))
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id ?? "", "Empty ID"));
                            continue;
                        }
                        Voyage? voyage = await McpCallerScope.ReadVoyageAsync(database, caller, id).ConfigureAwait(false);
                        if (voyage == null)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                            continue;
                        }
                        if (voyage.Status == VoyageStatusEnum.Open || voyage.Status == VoyageStatusEnum.InProgress)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete voyage while status is " + voyage.Status + ". Cancel the voyage first."));
                            continue;
                        }
                        List<Mission> missions = await database.Missions.EnumerateByVoyageAsync(id).ConfigureAwait(false);
                        int activeMissionCount = missions.Count(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress);
                        if (activeMissionCount > 0)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete voyage with " + activeMissionCount + " active mission(s). Cancel or complete them first."));
                            continue;
                        }
                        foreach (Mission m in missions)
                        {
                            await CleanupMissionResourcesAsync(m, database, settings).ConfigureAwait(false);
                            await database.Missions.DeleteAsync(m.Id).ConfigureAwait(false);
                        }
                        await database.Voyages.DeleteAsync(id).ConfigureAwait(false);
                        result.Deleted++;
                    }
                    result.ResolveStatus();
                    return (object)result;
                });
        }

        /// <summary>
        /// Cleans up filesystem resources associated with a mission (dock/worktree, log files, diff files).
        /// Cleanup failures are silently caught to avoid blocking the mission delete.
        /// </summary>
        /// <param name="mission">The mission being deleted.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Optional settings for log/diff paths.</param>
        private static async Task CleanupMissionResourcesAsync(Mission mission, DatabaseDriver database, ArmadaSettings? settings)
        {
            // Clean up associated dock/worktree
            if (!String.IsNullOrEmpty(mission.DockId))
            {
                try
                {
                    Dock? dock = await database.Docks.ReadAsync(mission.DockId).ConfigureAwait(false);
                    if (dock != null)
                    {
                        if (!String.IsNullOrEmpty(dock.WorktreePath) && Directory.Exists(dock.WorktreePath))
                        {
                            try { Directory.Delete(dock.WorktreePath, true); }
                            catch { }
                        }
                        await database.Docks.DeleteAsync(dock.Id).ConfigureAwait(false);
                    }
                }
                catch { }
            }

            // Clean up log and diff files
            if (settings != null)
            {
                try
                {
                    string logPath = Path.Combine(settings.LogDirectory, "missions", mission.Id + ".log");
                    if (File.Exists(logPath)) File.Delete(logPath);
                }
                catch { }
                try
                {
                    string diffPath = Path.Combine(settings.LogDirectory, "diffs", mission.Id + ".diff");
                    if (File.Exists(diffPath)) File.Delete(diffPath);
                }
                catch { }
            }
        }
    }
}
