namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Models;

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

        /// <summary>
        /// Registers persona MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for persona data access.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database)
        {
            register(
                "armada_create_persona",
                "Create a new persona with a name and prompt template reference",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Persona name (e.g. 'Worker', 'Architect', 'Judge')" },
                        description = new { type = "string", description = "Human-readable description of what this persona does" },
                        promptTemplateName = new { type = "string", description = "Prompt template name for this persona (references PromptTemplate.Name)" }
                    },
                    required = new[] { "name", "promptTemplateName" }
                },
                async (args) =>
                {
                    PersonaArgs request = JsonSerializer.Deserialize<PersonaArgs>(args!.Value, _JsonOptions)!;
                    if (String.IsNullOrEmpty(request.Name)) return (object)new { Error = "name is required" };
                    if (String.IsNullOrEmpty(request.PromptTemplateName)) return (object)new { Error = "promptTemplateName is required" };

                    Persona persona = new Persona(request.Name, request.PromptTemplateName);
                    persona.TenantId = ArmadaConstants.DefaultTenantId;
                    if (request.Description != null)
                        persona.Description = request.Description;
                    persona = await database.Personas.CreateAsync(persona).ConfigureAwait(false);
                    return (object)persona;
                });

            register(
                "armada_get_persona",
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
                    if (String.IsNullOrEmpty(name)) return (object)new { Error = "name is required" };
                    Persona? persona = await database.Personas.ReadByNameAsync(name).ConfigureAwait(false);
                    if (persona == null) return (object)new { Error = "Persona not found: " + name };
                    return (object)persona;
                });

            register(
                "armada_update_persona",
                "Update an existing persona's properties",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Persona name (used to look up the persona)" },
                        description = new { type = "string", description = "New description" },
                        promptTemplateName = new { type = "string", description = "New prompt template name" }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    PersonaArgs request = JsonSerializer.Deserialize<PersonaArgs>(args!.Value, _JsonOptions)!;
                    string name = request.Name;
                    if (String.IsNullOrEmpty(name)) return (object)new { Error = "name is required" };

                    Persona? persona = await database.Personas.ReadByNameAsync(name).ConfigureAwait(false);
                    if (persona == null) return (object)new { Error = "Persona not found: " + name };

                    if (request.Description != null)
                        persona.Description = request.Description;
                    if (request.PromptTemplateName != null)
                        persona.PromptTemplateName = request.PromptTemplateName;
                    persona.LastUpdateUtc = DateTime.UtcNow;
                    persona = await database.Personas.UpdateAsync(persona).ConfigureAwait(false);
                    return (object)persona;
                });

            register(
                "armada_delete_persona",
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
                    if (String.IsNullOrEmpty(name)) return (object)new { Error = "name is required" };

                    Persona? persona = await database.Personas.ReadByNameAsync(name).ConfigureAwait(false);
                    if (persona == null) return (object)new { Error = "Persona not found: " + name };
                    if (persona.IsBuiltIn) return (object)new { Error = "Cannot delete built-in persona: " + name };

                    await database.Personas.DeleteAsync(persona.Id).ConfigureAwait(false);
                    return (object)new { Status = "deleted", Name = name };
                });
        }
    }
}
