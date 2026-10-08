namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
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
                "list_prompt_templates",
                "List prompt templates (lightweight, paginated), optionally filtered by category. Returns metadata only -- name, category, description, active flag, and a contentLength hint -- not the template body. Fetch a single template's full content with get_prompt_template.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        category = new { type = "string", description = "Optional category filter (for example 'persona' or 'mission')" },
                        pageNumber = new { type = "integer", description = "1-based page number (default 1)" },
                        pageSize = new { type = "integer", description = "Results per page (default 25, max 100)" }
                    }
                },
                async (args) =>
                {
                    PromptTemplateArgs request = args.HasValue
                        ? JsonSerializer.Deserialize<PromptTemplateArgs>(args.Value, _JsonOptions) ?? new PromptTemplateArgs()
                        : new PromptTemplateArgs();

                    int pageNumber = request.PageNumber.HasValue ? request.PageNumber.Value : 1;
                    if (pageNumber < 1) pageNumber = 1;
                    int pageSize = request.PageSize.HasValue ? request.PageSize.Value : 25;
                    if (pageSize < 1) pageSize = 1;
                    if (pageSize > 100) pageSize = 100;

                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    List<PromptTemplate> templates = await templateService.ListAsync(request.Category).ConfigureAwait(false);
                    List<PromptTemplate> ordered = templates
                        .Where(template => IsVisible(caller, template))
                        .OrderBy(template => template.Category, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    long totalRecords = ordered.Count;
                    int totalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalRecords / pageSize) : 0;

                    List<object> objects = ordered
                        .Skip((pageNumber - 1) * pageSize)
                        .Take(pageSize)
                        .Select(template => (object)new
                        {
                            name = template.Name,
                            category = template.Category,
                            description = template.Description,
                            active = template.Active,
                            isBuiltIn = template.IsBuiltIn,
                            contentLength = template.Content != null ? template.Content.Length : 0,
                            lastUpdateUtc = template.LastUpdateUtc
                        })
                        .ToList();

                    return (object)new
                    {
                        success = true,
                        pageNumber,
                        pageSize,
                        totalPages,
                        totalRecords,
                        objects
                    };
                });

            register(
                "create_prompt_template",
                "Create a new prompt template.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Template name (e.g. 'persona.worker.copy')" },
                        category = new { type = "string", description = "Template category such as 'persona' or 'mission'" },
                        content = new { type = "string", description = "Template content with {Placeholder} parameters" },
                        description = new { type = "string", description = "Human-readable description of the template" },
                        active = new { type = "boolean", description = "Whether the template should be active" }
                    },
                    required = new[] { "name", "category", "content" }
                },
                async (args) =>
                {
                    PromptTemplateArgs request = JsonSerializer.Deserialize<PromptTemplateArgs>(args!.Value, _JsonOptions)!;
                    if (String.IsNullOrEmpty(request.Name)) return (object)McpToolError.InvalidArgument("name is required");
                    if (String.IsNullOrEmpty(request.Category)) return (object)McpToolError.InvalidArgument("category is required");
                    if (String.IsNullOrEmpty(request.Content)) return (object)McpToolError.InvalidArgument("content is required");

                    PromptTemplate? existing = await database.PromptTemplates.ReadByNameAsync(request.Name).ConfigureAwait(false);
                    if (existing != null) return (object)McpToolError.FromException(DuplicateEntityGuard.NameTaken("PromptTemplate", "prompt template", request.Name));

                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    PromptTemplate template = new PromptTemplate(request.Name, request.Content);
                    template.TenantId = String.IsNullOrEmpty(caller.TenantId) ? Constants.DefaultTenantId : caller.TenantId;
                    template.UserId = caller.UserId;
                    template.Scope = ScopedVisibility.ResolveCreateScope(caller, null);
                    template.Category = request.Category;
                    template.Description = request.Description;
                    if (request.Active.HasValue) template.Active = request.Active.Value;

                    PromptTemplate created = await database.PromptTemplates.CreateAsync(template).ConfigureAwait(false);
                    return (object)created;
                });

            register(
                "get_prompt_template",
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
                    if (String.IsNullOrEmpty(name)) return (object)McpToolError.InvalidArgument("name is required");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    PromptTemplate? scoped = await McpCallerScope.ReadPromptTemplateAsync(database, caller, name).ConfigureAwait(false);
                    if (scoped != null) return (object)scoped;

                    // A stored template outside the caller's scope is reported exactly like a missing one; a name with
                    // no stored copy falls back to the embedded default, which carries no tenant data.
                    PromptTemplate? stored = await database.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false);
                    if (stored != null) return (object)McpToolError.NotFound("Template not found: " + name);
                    PromptTemplate? template = await templateService.ResolveAsync(name).ConfigureAwait(false);
                    if (template == null) return (object)McpToolError.NotFound("Template not found: " + name);
                    return (object)template;
                });

            register(
                "update_prompt_template",
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
                    if (String.IsNullOrEmpty(name)) return (object)McpToolError.InvalidArgument("name is required");
                    if (String.IsNullOrEmpty(request.Content)) return (object)McpToolError.InvalidArgument("content is required");

                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    PromptTemplate? existing = await database.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false);
                    if (existing != null && !IsVisible(caller, existing))
                    {
                        PromptTemplate? own = await McpCallerScope.ReadPromptTemplateAsync(database, caller, name).ConfigureAwait(false);
                        if (own == null) return (object)McpToolError.NotFound("Template not found: " + name);
                        existing = own;
                    }

                    if (existing != null)
                    {
                        if (!ScopedVisibility.CanEdit(caller, existing.Scope, existing.TenantId, existing.UserId))
                            return (object)McpToolError.Forbidden("You may only modify your own prompt templates; a tenant-wide template requires a tenant admin.");
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
                        template.TenantId = String.IsNullOrEmpty(caller.TenantId) ? Constants.DefaultTenantId : caller.TenantId;
                        template.UserId = caller.UserId;
                        template.Scope = ScopedVisibility.ResolveCreateScope(caller, null);
                        if (request.Description != null)
                            template.Description = request.Description;
                        PromptTemplate created = await database.PromptTemplates.CreateAsync(template).ConfigureAwait(false);
                        return (object)created;
                    }
                });

            register(
                "reset_prompt_template",
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
                    if (String.IsNullOrEmpty(name)) return (object)McpToolError.InvalidArgument("name is required");
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    PromptTemplate? current = await database.PromptTemplates.ReadByNameAsync(name).ConfigureAwait(false);
                    if (current != null && !IsVisible(caller, current)) return (object)McpToolError.NotFound("Template not found: " + name);
                    if (current != null && !ScopedVisibility.CanEdit(caller, current.Scope, current.TenantId, current.UserId))
                        return (object)McpToolError.Forbidden("You may only reset your own prompt templates; a tenant-wide template requires a tenant admin.");
                    PromptTemplate? template = await templateService.ResetToDefaultAsync(name).ConfigureAwait(false);
                    if (template == null) return (object)McpToolError.NotFound("No embedded default exists for template: " + name);
                    return (object)template;
                });
        }

        /// <summary>
        /// Whether a template is visible to the caller. Templates without a stored row (embedded defaults returned by
        /// the template service) carry no tenant data and are visible to everyone.
        /// </summary>
        private static bool IsVisible(AuthContext caller, PromptTemplate template)
        {
            if (String.IsNullOrEmpty(template.Id) || String.IsNullOrEmpty(template.TenantId)) return true;
            return ScopedVisibility.CanView(caller, template.Scope, template.TenantId, template.UserId);
        }
    }
}
