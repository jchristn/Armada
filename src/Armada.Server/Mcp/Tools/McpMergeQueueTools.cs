namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Registers MCP tools for merge queue operations (get, enqueue, cancel, process, delete, purge).
    /// </summary>
    public static class McpMergeQueueTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers merge queue MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="mergeQueue">Merge queue service for queue operations.</param>
        public static void Register(RegisterToolDelegate register, IMergeQueueService mergeQueue)
        {
            register(
                "armada_get_merge_entry",
                "Get details of a specific merge queue entry",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryId = new { type = "string", description = "Merge entry ID (mrg_ prefix)" }
                    },
                    required = new[] { "entryId" }
                },
                async (args) =>
                {
                    MergeEntryIdArgs request = JsonSerializer.Deserialize<MergeEntryIdArgs>(args!.Value, _JsonOptions)!;
                    string entryId = request.EntryId;
                    MergeEntry? entry = await mergeQueue.GetAsync(entryId).ConfigureAwait(false);
                    if (entry == null) return (object)new { Error = "Merge entry not found" };
                    return (object)entry;
                });

            register(
                "armada_enqueue_merge",
                "Add a branch to the merge queue for testing and merging",
                new
                {
                    type = "object",
                    properties = new
                    {
                        missionId = new { type = "string", description = "Associated mission ID (msn_ prefix)" },
                        vesselId = new { type = "string", description = "Target vessel ID (vsl_ prefix)" },
                        branchName = new { type = "string", description = "Branch name to merge" },
                        targetBranch = new { type = "string", description = "Target branch (defaults to main)" },
                        priority = new { type = "integer", description = "Queue priority (lower = higher, default 0)" },
                        testCommand = new { type = "string", description = "Custom test command to run" }
                    },
                    required = new[] { "vesselId", "branchName" }
                },
                async (args) =>
                {
                    MergeEnqueueArgs request = JsonSerializer.Deserialize<MergeEnqueueArgs>(args!.Value, _JsonOptions)!;
                    MergeEntry entry = new MergeEntry();
                    entry.VesselId = request.VesselId;
                    entry.BranchName = request.BranchName;
                    if (request.MissionId != null)
                        entry.MissionId = request.MissionId;
                    entry.TargetBranch = request.TargetBranch ?? "main";
                    if (request.Priority.HasValue)
                        entry.Priority = request.Priority.Value;
                    if (request.TestCommand != null)
                        entry.TestCommand = request.TestCommand;
                    entry = await mergeQueue.EnqueueAsync(entry).ConfigureAwait(false);
                    return (object)entry;
                });

            register(
                "armada_cancel_merge",
                "Cancel a queued merge entry",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryId = new { type = "string", description = "Merge entry ID (mrg_ prefix)" }
                    },
                    required = new[] { "entryId" }
                },
                async (args) =>
                {
                    MergeEntryIdArgs request = JsonSerializer.Deserialize<MergeEntryIdArgs>(args!.Value, _JsonOptions)!;
                    string entryId = request.EntryId;
                    await mergeQueue.CancelAsync(entryId).ConfigureAwait(false);
                    return (object)new { Status = "cancelled", EntryId = entryId };
                });

            register(
                "armada_process_merge_entry",
                "Process a single merge queue entry by ID: creates integration branch, runs tests, and lands if passing",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryId = new { type = "string", description = "Merge entry ID (mrg_ prefix)" }
                    },
                    required = new[] { "entryId" }
                },
                async (args) =>
                {
                    MergeEntryIdArgs request = JsonSerializer.Deserialize<MergeEntryIdArgs>(args!.Value, _JsonOptions)!;
                    string entryId = request.EntryId;
                    MergeEntry? entry = await mergeQueue.ProcessSingleAsync(entryId).ConfigureAwait(false);
                    if (entry == null) return (object)new { Error = "Merge entry not found or not in Queued status" };
                    return (object)entry;
                });

            register(
                "armada_process_merge_queue",
                "Process the merge queue: creates integration branches, runs tests, and lands passing batches",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    await mergeQueue.ProcessQueueAsync().ConfigureAwait(false);
                    return (object)new { Status = "processed" };
                });

            register(
                "armada_delete_merge",
                "Permanently delete a terminal merge queue entry from the database. Only entries in Landed, Failed, or Cancelled status can be deleted. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryId = new { type = "string", description = "Merge entry ID (mrg_ prefix)" }
                    },
                    required = new[] { "entryId" }
                },
                async (args) =>
                {
                    MergeEntryIdArgs request = JsonSerializer.Deserialize<MergeEntryIdArgs>(args!.Value, _JsonOptions)!;
                    string entryId = request.EntryId;
                    MergeEntry? entry = await mergeQueue.GetAsync(entryId).ConfigureAwait(false);
                    if (entry == null) return (object)new { Error = "Merge entry not found" };

                    bool deleted = await mergeQueue.DeleteAsync(entryId).ConfigureAwait(false);
                    if (!deleted) return (object)new { Error = "Cannot delete merge entry in non-terminal status " + entry.Status + ". Only Landed, Failed, or Cancelled entries can be deleted." };

                    return (object)new { Status = "deleted", EntryId = entryId };
                });

            register(
                "armada_purge_merge_queue",
                "Permanently delete all terminal merge queue entries (Landed, Failed, Cancelled) from the database. Optionally filter by vessel ID and/or status. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Optional vessel ID filter (vsl_ prefix)" },
                        status = new { type = "string", description = "Optional status filter: Landed, Failed, or Cancelled" }
                    }
                },
                async (args) =>
                {
                    PurgeMergeQueueArgs request = args != null
                        ? JsonSerializer.Deserialize<PurgeMergeQueueArgs>(args.Value, _JsonOptions)!
                        : new PurgeMergeQueueArgs();

                    MergeStatusEnum? statusFilter = null;
                    if (!String.IsNullOrEmpty(request.Status))
                    {
                        if (!Enum.TryParse<MergeStatusEnum>(request.Status, true, out MergeStatusEnum parsed))
                            return (object)new { Error = "Invalid status. Must be one of: Landed, Failed, Cancelled" };
                        statusFilter = parsed;
                    }

                    int deleted = await mergeQueue.PurgeTerminalAsync(request.VesselId, statusFilter).ConfigureAwait(false);
                    return (object)new { Status = "purged", EntriesDeleted = deleted };
                });

            register(
                "armada_purge_merge_entry",
                "Permanently delete a single terminal merge queue entry from the database by ID. Only entries in Landed, Failed, or Cancelled status can be purged. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryId = new { type = "string", description = "Merge entry ID (mrg_ prefix)" }
                    },
                    required = new[] { "entryId" }
                },
                async (args) =>
                {
                    MergeEntryIdArgs request = JsonSerializer.Deserialize<MergeEntryIdArgs>(args!.Value, _JsonOptions)!;
                    string entryId = request.EntryId;
                    MergeEntry? entry = await mergeQueue.GetAsync(entryId).ConfigureAwait(false);
                    if (entry == null) return (object)new { Error = "Merge entry not found" };

                    bool deleted = await mergeQueue.DeleteAsync(entryId).ConfigureAwait(false);
                    if (!deleted) return (object)new { Error = "Cannot purge merge entry in non-terminal status " + entry.Status + ". Only Landed, Failed, or Cancelled entries can be purged." };

                    return (object)new { Status = "purged", EntryId = entryId };
                });

            register(
                "armada_purge_merge_entries",
                "Permanently delete multiple terminal merge queue entries from the database by ID. Only entries in Landed, Failed, or Cancelled status can be purged. Returns a summary of purged and skipped entries. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryIds = new { type = "array", items = new { type = "string" }, description = "List of merge entry IDs to purge (mrg_ prefix)" }
                    },
                    required = new[] { "entryIds" }
                },
                async (args) =>
                {
                    PurgeMergeEntriesArgs request = JsonSerializer.Deserialize<PurgeMergeEntriesArgs>(args!.Value, _JsonOptions)!;
                    if (request.EntryIds == null || request.EntryIds.Count == 0)
                        return (object)new { Error = "entryIds is required and must not be empty" };

                    MergeQueuePurgeResult result = await mergeQueue.DeleteMultipleAsync(request.EntryIds).ConfigureAwait(false);
                    return (object)result;
                });
        }
    }
}
