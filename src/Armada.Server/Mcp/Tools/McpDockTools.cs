namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Registers MCP tools for dock operations (get, delete, purge).
    /// </summary>
    public static class McpDockTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers dock MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for dock data access.</param>
        /// <param name="dockService">Optional dock service for worktree operations.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, IDockService? dockService = null)
        {
            register(
                "armada_get_dock",
                "Get a dock (git worktree) by ID.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        dockId = new { type = "string", description = "Dock ID (dck_ prefix)" }
                    },
                    required = new[] { "dockId" }
                },
                async (args) =>
                {
                    DockIdArgs request = JsonSerializer.Deserialize<DockIdArgs>(args!.Value, _JsonOptions)!;
                    Dock? dock = await database.Docks.ReadAsync(request.DockId).ConfigureAwait(false);
                    if (dock == null) return (object)new { Error = "Dock not found" };
                    return (object)dock;
                });

            register(
                "armada_delete_dock",
                "Delete a dock and clean up its git worktree. Blocked if the dock is actively in use by a captain.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        dockId = new { type = "string", description = "Dock ID (dck_ prefix)" }
                    },
                    required = new[] { "dockId" }
                },
                async (args) =>
                {
                    if (dockService == null) return (object)new { Error = "Dock service not available" };
                    DockIdArgs request = JsonSerializer.Deserialize<DockIdArgs>(args!.Value, _JsonOptions)!;
                    Dock? dock = await database.Docks.ReadAsync(request.DockId).ConfigureAwait(false);
                    if (dock == null) return (object)new { Error = "Dock not found" };

                    bool deleted = await dockService.DeleteAsync(request.DockId).ConfigureAwait(false);
                    if (!deleted) return (object)new { Error = "Cannot delete dock while it is actively in use by a captain" };
                    return (object)new { Status = "deleted", DockId = request.DockId };
                });

            register(
                "armada_purge_dock",
                "Force purge a dock and its git worktree, even if a mission references it. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        dockId = new { type = "string", description = "Dock ID (dck_ prefix)" }
                    },
                    required = new[] { "dockId" }
                },
                async (args) =>
                {
                    if (dockService == null) return (object)new { Error = "Dock service not available" };
                    DockIdArgs request = JsonSerializer.Deserialize<DockIdArgs>(args!.Value, _JsonOptions)!;
                    Dock? dock = await database.Docks.ReadAsync(request.DockId).ConfigureAwait(false);
                    if (dock == null) return (object)new { Error = "Dock not found" };

                    await dockService.PurgeAsync(request.DockId).ConfigureAwait(false);
                    return (object)new { Status = "purged", DockId = request.DockId };
                });

            register(
                "armada_delete_docks",
                "Permanently delete multiple docks and their git worktrees from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ids = new { type = "array", items = new { type = "string" }, description = "List of dock IDs to delete (dck_ prefix)" }
                    },
                    required = new[] { "ids" }
                },
                async (args) =>
                {
                    if (dockService == null) return (object)new { Error = "Dock service not available" };
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
                        Dock? dock = await database.Docks.ReadAsync(id).ConfigureAwait(false);
                        if (dock == null)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                            continue;
                        }
                        await dockService.PurgeAsync(id).ConfigureAwait(false);
                        result.Deleted++;
                    }
                    result.ResolveStatus();
                    return (object)result;
                });
        }
    }
}
