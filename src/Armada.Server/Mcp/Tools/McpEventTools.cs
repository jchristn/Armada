namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Registers MCP tools for event operations (delete single, delete multiple).
    /// </summary>
    public static class McpEventTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers event MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for event data access.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database)
        {
            register(
                "armada_delete_event",
                "Delete a single event by ID. Permanently removes the event from the database.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        eventId = new { type = "string", description = "Event ID (evt_ prefix)" }
                    },
                    required = new[] { "eventId" }
                },
                async (args) =>
                {
                    EventIdArgs request = JsonSerializer.Deserialize<EventIdArgs>(args!.Value, _JsonOptions)!;
                    string eventId = request.EventId;
                    ArmadaEvent? evt = await database.Events.ReadAsync(eventId).ConfigureAwait(false);
                    if (evt == null) return (object)new { Error = "Event not found" };
                    await database.Events.DeleteAsync(eventId).ConfigureAwait(false);
                    return (object)new { Status = "deleted", EventId = eventId };
                });

            register(
                "armada_delete_events",
                "Permanently delete multiple events from the database by ID. Returns a summary of deleted and skipped entries. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ids = new { type = "array", items = new { type = "string" }, description = "List of event IDs to delete (evt_ prefix)" }
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
                        ArmadaEvent? evt = await database.Events.ReadAsync(id).ConfigureAwait(false);
                        if (evt == null)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                            continue;
                        }
                        await database.Events.DeleteAsync(id).ConfigureAwait(false);
                        result.Deleted++;
                    }
                    result.ResolveStatus();
                    return (object)result;
                });
        }
    }
}
