namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Registers MCP tools for persona CRUD operations.
    /// </summary>
    public static class McpPersonaTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly object _PrioritySchema = new
        {
            type = "array",
            description = "Ordered captain classes (at most 256); at least one field per entry. Empty clears the hierarchy.",
            items = new
            {
                type = "object",
                properties = new
                {
                    runtime = new { type = "string", @enum = Enum.GetNames<AgentRuntimeEnum>() },
                    model = new { type = "string", minLength = 1, maxLength = 1024 },
                    endpoint = new { type = "string", minLength = 1, maxLength = 1024, description = "Mux endpoint name (runtime Mux) or exact managed inference endpoint ID (runtime ApiEndpoint; matches captain ModelEndpointId)" },
                    minimumTier = new { type = "string", @enum = Enum.GetNames<CaptainTierEnum>() }
                }
            }
        };

        /// <summary>
        /// Registers persona MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for persona data access.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database)
        {
            PersonaWriteService writes = new PersonaWriteService(database);
            register(
                "create_persona",
                "Create a new persona with a name and prompt template reference",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Persona name (e.g. 'Worker', 'Architect', 'Judge')" },
                        description = new { type = "string", description = "Human-readable description of what this persona does" },
                        promptTemplateName = new { type = "string", description = "Prompt template name for this persona (references PromptTemplate.Name)" },
                        defaultCaptainId = new { type = "string", description = "Optional default (preferred) captain id (cpt_ prefix) for this persona" },
                        scope = new { type = "string", @enum = Enum.GetNames<ScopeEnum>(), description = "Ownership: UserSpecific (only you) or TenantWide; a regular user can create only UserSpecific personas, and only an administrator can change it on update" },
                        captainPriorities = _PrioritySchema,
                        priorityAvailability = new { type = "string", @enum = Enum.GetNames<PersonaPriorityAvailabilityEnum>() },
                        priorityExhaustion = new { type = "string", @enum = Enum.GetNames<PersonaPriorityExhaustionEnum>() }
                    },
                    required = new[] { "name", "promptTemplateName" }
                },
                async (args) =>
                {
                    PersonaArgs request = JsonSerializer.Deserialize<PersonaArgs>(args!.Value, _JsonOptions)!;
                    if (String.IsNullOrEmpty(request.Name)) return (object)McpToolError.InvalidArgument("name is required");
                    if (String.IsNullOrEmpty(request.PromptTemplateName)) return (object)McpToolError.InvalidArgument("promptTemplateName is required");

                    Persona persona = new Persona(request.Name, request.PromptTemplateName);
                    if (request.Description != null) persona.Description = request.Description;
                    persona.DefaultCaptainId = String.IsNullOrEmpty(request.DefaultCaptainId) ? null : request.DefaultCaptainId;
                    if (request.CaptainPriorities != null) persona.CaptainPriorities = request.CaptainPriorities;
                    if (request.PriorityAvailability.HasValue) persona.PriorityAvailability = request.PriorityAvailability.Value;
                    if (request.PriorityExhaustion.HasValue) persona.PriorityExhaustion = request.PriorityExhaustion.Value;
                    if (request.Scope.HasValue) persona.Scope = request.Scope.Value;
                    return ToToolResult(await writes.CreateAsync(McpToolHelpers.ResolveCallerContext(), persona).ConfigureAwait(false));
                });

            register(
                "get_persona",
                "Get a persona by name",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Persona name" }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    PersonaArgs request = JsonSerializer.Deserialize<PersonaArgs>(args!.Value, _JsonOptions)!;
                    string name = request.Name;
                    if (String.IsNullOrEmpty(name)) return (object)McpToolError.InvalidArgument("name is required");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    Persona? persona = await McpCallerScope.ReadPersonaAsync(database, caller, name).ConfigureAwait(false);
                    if (persona == null) return (object)McpToolError.NotFound("Persona not found: " + name);
                    return (object)persona;
                });

            register(
                "update_persona",
                "Update an existing persona's properties",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Persona name (used to look up the persona)" },
                        description = new { type = "string", description = "New description" },
                        promptTemplateName = new { type = "string", description = "New prompt template name" },
                        defaultCaptainId = new { type = "string", description = "Default (preferred) captain id (cpt_ prefix) for this persona; an empty string clears it, and omitting it keeps it" },
                        scope = new { type = "string", @enum = Enum.GetNames<ScopeEnum>(), description = "Ownership: UserSpecific (only you) or TenantWide; a regular user can create only UserSpecific personas, and only an administrator can change it on update" },
                        captainPriorities = _PrioritySchema,
                        priorityAvailability = new { type = "string", @enum = Enum.GetNames<PersonaPriorityAvailabilityEnum>() },
                        priorityExhaustion = new { type = "string", @enum = Enum.GetNames<PersonaPriorityExhaustionEnum>() }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    PersonaArgs request = JsonSerializer.Deserialize<PersonaArgs>(args!.Value, _JsonOptions)!;
                    if (String.IsNullOrEmpty(request.Name)) return (object)McpToolError.InvalidArgument("name is required");

                    PersonaUpdateRequest update = new PersonaUpdateRequest
                    {
                        Description = request.Description,
                        PromptTemplateName = request.PromptTemplateName,
                        DefaultCaptainId = request.DefaultCaptainId,
                        Scope = request.Scope,
                        CaptainPriorities = request.CaptainPriorities,
                        PriorityAvailability = request.PriorityAvailability,
                        PriorityExhaustion = request.PriorityExhaustion
                    };
                    // Same rule as REST: the default captain changes only when the arguments carry it, and an empty string clears
                    // it (the schema takes a string, so MCP has no explicit null).
                    bool hasDefaultCaptain = PersonaUpdateFieldPresence.FromJson(JsonSerializer.Serialize(args!.Value), _JsonOptions).HasDefaultCaptainId;
                    return ToToolResult(await writes.UpdateAsync(McpToolHelpers.ResolveCallerContext(), request.Name, update, hasDefaultCaptain).ConfigureAwait(false));
                });

            register(
                "delete_persona",
                "Delete a persona by name. Built-in personas cannot be deleted.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Persona name" }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    PersonaArgs request = JsonSerializer.Deserialize<PersonaArgs>(args!.Value, _JsonOptions)!;
                    string name = request.Name;
                    if (String.IsNullOrEmpty(name)) return (object)McpToolError.InvalidArgument("name is required");
                    PersonaWriteResult deleted = await writes.DeleteAsync(McpToolHelpers.ResolveCallerContext(), name).ConfigureAwait(false);
                    if (deleted.Status != PersonaWriteStatusEnum.Ok) return ToToolResult(deleted);
                    return (object)new { Status = "deleted", Name = name };
                });
        }
    
        private static object ToToolResult(PersonaWriteResult result)
        {
            switch (result.Status)
            {
                case PersonaWriteStatusEnum.Ok: return result.Persona!;
                case PersonaWriteStatusEnum.NotFound: return McpToolError.NotFound(result.Message);
                case PersonaWriteStatusEnum.Forbidden: return McpToolError.Forbidden(result.Message);
                default: return McpToolError.InvalidArgument(result.Message);
            }
        }
}
}
