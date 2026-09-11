namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// Registers MCP tools for durable agent memory: search (consolidate against existing), get, create/upsert
    /// (idempotent by key), update, and delete. All tools are scoped to the authenticated caller.
    /// </summary>
    public static class McpMemoryTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers memory MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, LoggingModule logging)
        {
            MemoryService service = new MemoryService(database, logging);

            register(
                "search_memory",
                "Search durable memories (episodic/semantic/procedural). Use this BEFORE writing to find existing memories to consolidate against. Returns matches ordered by salience, newest first.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        search = new { type = "string", description = "Case-insensitive substring across content, topic, and tags" },
                        type = new { type = "string", description = "Filter by memory type: Episodic, Semantic, or Procedural" },
                        topic = new { type = "string", description = "Filter by exact topic/grouping" },
                        vesselId = new { type = "string", description = "Filter by associated or originating vessel id (vsl_ prefix)" },
                        pageNumber = new { type = "integer", description = "Page number (1-based)" },
                        pageSize = new { type = "integer", description = "Results per page" }
                    }
                },
                async (args) =>
                {
                    MemorySearchArgs request = args.HasValue ? JsonSerializer.Deserialize<MemorySearchArgs>(args.Value, _JsonOptions)! : new MemorySearchArgs();
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();

                    EnumerationQuery query = new EnumerationQuery();
                    if (request.PageNumber.HasValue) query.PageNumber = request.PageNumber.Value;
                    if (request.PageSize.HasValue) query.PageSize = request.PageSize.Value;
                    if (!String.IsNullOrWhiteSpace(request.VesselId)) query.VesselId = request.VesselId;

                    MemoryTypeEnum? type = ParseType(request.Type);
                    EnumerationResult<Memory> result = await service.EnumerateAsync(caller, query, request.Search, type, request.Topic).ConfigureAwait(false);
                    return (object)result;
                });

            register(
                "get_memory",
                "Read a single memory by id, including its full content.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        memoryId = new { type = "string", description = "Memory id (mem_ prefix)" }
                    },
                    required = new[] { "memoryId" }
                },
                async (args) =>
                {
                    MemoryIdArgs request = JsonSerializer.Deserialize<MemoryIdArgs>(args!.Value, _JsonOptions)!;
                    if (String.IsNullOrWhiteSpace(request.MemoryId)) return (object)new { Error = "memoryId is required" };
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    Memory? memory = await service.ReadAsync(caller, request.MemoryId).ConfigureAwait(false);
                    if (memory == null) return (object)new { Error = "Memory not found" };
                    return (object)memory;
                });

            register(
                "create_memory",
                "Create a durable memory, or update it in place when a memory with the same key already exists (idempotent consolidation). Classify the content as Episodic (what happened/when), Semantic (a standalone fact), or Procedural (how to do something). Working memory is never stored.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        type = new { type = "string", description = "Episodic, Semantic, or Procedural (default Semantic)" },
                        topic = new { type = "string", description = "Grouping within the type, e.g. 'code-style'" },
                        key = new { type = "string", description = "Stable idempotency key/slug; re-using it updates the existing memory in place" },
                        summary = new { type = "string", description = "One-line recall hook" },
                        content = new { type = "string", description = "The memory itself, written to stand on its own" },
                        salience = new { type = "number", description = "Importance 0.0-1.0 (default 0.5)" },
                        tags = new { type = "array", items = new { type = "string" }, description = "Tags for retrieval/consolidation" },
                        sourceKind = new { type = "string", description = "Voyage, Mission, Vessel, Conversation, Manual, or Other" },
                        sourceVoyageId = new { type = "string", description = "Originating voyage id (vyg_ prefix)" },
                        sourceMissionId = new { type = "string", description = "Originating mission id (msn_ prefix)" },
                        sourceVesselId = new { type = "string", description = "Originating vessel id (vsl_ prefix)" },
                        sourceDetail = new { type = "string", description = "Free-text note on where this came from" },
                        vesselId = new { type = "string", description = "Vessel this memory is about (vsl_ prefix)" },
                        scope = new { type = "string", description = "TenantWide or UserSpecific (admins only for TenantWide)" }
                    },
                    required = new[] { "content" }
                },
                async (args) =>
                {
                    MemoryUpsertArgs request = JsonSerializer.Deserialize<MemoryUpsertArgs>(args!.Value, _JsonOptions)!;
                    if (String.IsNullOrWhiteSpace(request.Content)) return (object)new { Error = "content is required" };
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();

                    Memory memory = new Memory();
                    memory.Type = ParseType(request.Type) ?? MemoryTypeEnum.Semantic;
                    memory.Topic = request.Topic;
                    memory.Key = request.Key;
                    memory.Summary = request.Summary;
                    memory.Content = request.Content;
                    if (request.Salience.HasValue) memory.Salience = request.Salience.Value;
                    memory.Tags = request.Tags ?? new List<string>();
                    memory.SourceKind = ParseSourceKind(request.SourceKind) ?? MemorySourceKindEnum.Manual;
                    memory.SourceVoyageId = request.SourceVoyageId;
                    memory.SourceMissionId = request.SourceMissionId;
                    memory.SourceVesselId = request.SourceVesselId;
                    memory.SourceDetail = request.SourceDetail;
                    memory.VesselId = request.VesselId;
                    if (ParseScope(request.Scope) is ScopeEnum scope) memory.Scope = scope;

                    Memory saved = await service.UpsertAsync(caller, memory).ConfigureAwait(false);
                    return (object)saved;
                });

            register(
                "update_memory",
                "Update an existing memory by id. Only supplied fields change. Use this to reconcile or augment an existing memory rather than creating a duplicate.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        memoryId = new { type = "string", description = "Memory id (mem_ prefix)" },
                        type = new { type = "string", description = "New type: Episodic, Semantic, or Procedural" },
                        topic = new { type = "string", description = "New topic/grouping" },
                        key = new { type = "string", description = "New idempotency key" },
                        summary = new { type = "string", description = "New one-line summary" },
                        content = new { type = "string", description = "New content" },
                        salience = new { type = "number", description = "New salience 0.0-1.0" },
                        tags = new { type = "array", items = new { type = "string" }, description = "Replacement tag list" },
                        vesselId = new { type = "string", description = "New vessel association" },
                        sourceDetail = new { type = "string", description = "New source detail" },
                        scope = new { type = "string", description = "New scope (admins only)" }
                    },
                    required = new[] { "memoryId" }
                },
                async (args) =>
                {
                    MemoryUpdateArgs request = JsonSerializer.Deserialize<MemoryUpdateArgs>(args!.Value, _JsonOptions)!;
                    if (String.IsNullOrWhiteSpace(request.MemoryId)) return (object)new { Error = "memoryId is required" };
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();

                    Memory? existing = await service.ReadAsync(caller, request.MemoryId).ConfigureAwait(false);
                    if (existing == null) return (object)new { Error = "Memory not found" };

                    if (ParseType(request.Type) is MemoryTypeEnum type) existing.Type = type;
                    if (request.Topic != null) existing.Topic = request.Topic;
                    if (request.Key != null) existing.Key = request.Key;
                    if (request.Summary != null) existing.Summary = request.Summary;
                    if (request.Content != null) existing.Content = request.Content;
                    if (request.Salience.HasValue) existing.Salience = request.Salience.Value;
                    if (request.Tags != null) existing.Tags = request.Tags;
                    if (request.VesselId != null) existing.VesselId = request.VesselId;
                    if (request.SourceDetail != null) existing.SourceDetail = request.SourceDetail;
                    if (ParseScope(request.Scope) is ScopeEnum scope) existing.Scope = scope;

                    Memory saved = await service.UpdateAsync(caller, existing).ConfigureAwait(false);
                    return (object)saved;
                });

            register(
                "delete_memory",
                "Delete a memory by id. Use this to remove a memory that has become stale or is no longer relevant.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        memoryId = new { type = "string", description = "Memory id (mem_ prefix)" }
                    },
                    required = new[] { "memoryId" }
                },
                async (args) =>
                {
                    MemoryIdArgs request = JsonSerializer.Deserialize<MemoryIdArgs>(args!.Value, _JsonOptions)!;
                    if (String.IsNullOrWhiteSpace(request.MemoryId)) return (object)new { Error = "memoryId is required" };
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    await service.DeleteAsync(caller, request.MemoryId).ConfigureAwait(false);
                    return (object)new { Status = "deleted", MemoryId = request.MemoryId };
                });
        }

        private static MemoryTypeEnum? ParseType(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return Enum.TryParse<MemoryTypeEnum>(value, true, out MemoryTypeEnum parsed) ? parsed : (MemoryTypeEnum?)null;
        }

        private static MemorySourceKindEnum? ParseSourceKind(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return Enum.TryParse<MemorySourceKindEnum>(value, true, out MemorySourceKindEnum parsed) ? parsed : (MemorySourceKindEnum?)null;
        }

        private static ScopeEnum? ParseScope(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return Enum.TryParse<ScopeEnum>(value, true, out ScopeEnum parsed) ? parsed : (ScopeEnum?)null;
        }
    }
}
