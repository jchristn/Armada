namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Registers MCP tools for prompt template operations.
    /// </summary>
    public static class McpPromptTemplateTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers prompt template MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for prompt template data access.</param>
        /// <param name="templateService">Prompt template service for resolve and reset operations.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, IPromptTemplateService templateService)
        {
            register(
                "armada_get_prompt_template",
                "Get a prompt template by name. Resolves from database first, falls back to embedded defaults.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Template name (e.g. 'mission.rules', 'persona.worker')" }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    PromptTemplateArgs request = JsonSerializer.Deserialize<PromptTemplateArgs>(args!.Value, _JsonOptions)!;
                    string name = request.Name;
                    if (String.IsNullOrEmpty(name)) return (object)new { Error = "name is required" };
                    PromptTemplate? template = await templateService.ResolveAsync(name).ConfigureAwait(false);
                    if (template == null) return (object)new { Error = "Template not found: " + name };
                    return (object)template;
                });

            register(
                "armada_update_prompt_template",
                "Update a prompt template's content and description. Creates the template if it does not exist.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Template name (e.g. 'mission.rules', 'persona.worker')" },
                        content = new { type = "string", description = "Template content with {Placeholder} parameters" },
                        description = new { type = "string", description = "Human-readable description of the template" }
                    },
                    required = new[] { "name", "content" }
                },
                async (args) =>
                {
                    PromptTemplateArgs request = JsonSerializer.Deserialize<PromptTemplateArgs>(args!.Value, _JsonOptions)!;
                    string name = request.Name;
                    if (String.IsNullOrEmpty(name)) return (object)new { Error = "name is required" };
                    if (String.IsNullOrEmpty(request.Content)) return (object)new { Error = "content is required" };

                    PromptTemplate? existing = await database.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false);
                    if (existing != null)
                    {
                        existing.Content = request.Content;
                        if (request.Description != null)
                            existing.Description = request.Description;
                        existing.LastUpdateUtc = DateTime.UtcNow;
                        PromptTemplate updated = await database.PromptTemplates.UpdateAsync(existing).ConfigureAwait(false);
                        return (object)updated;
                    }
                    else
                    {
                        PromptTemplate template = new PromptTemplate(name, request.Content);
                        if (request.Description != null)
                            template.Description = request.Description;
                        PromptTemplate created = await database.PromptTemplates.CreateAsync(template).ConfigureAwait(false);
                        return (object)created;
                    }
                });

            register(
                "armada_reset_prompt_template",
                "Reset a prompt template to its embedded resource default content. Only works for built-in templates.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Template name to reset (e.g. 'mission.rules', 'persona.worker')" }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    PromptTemplateArgs request = JsonSerializer.Deserialize<PromptTemplateArgs>(args!.Value, _JsonOptions)!;
                    string name = request.Name;
                    if (String.IsNullOrEmpty(name)) return (object)new { Error = "name is required" };
                    PromptTemplate? template = await templateService.ResetToDefaultAsync(name).ConfigureAwait(false);
                    if (template == null) return (object)new { Error = "No embedded default exists for template: " + name };
                    return (object)template;
                });
        }
    }
}
