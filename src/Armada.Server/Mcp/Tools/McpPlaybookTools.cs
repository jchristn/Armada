namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// Registers MCP tools for playbook management.
    /// </summary>
    public static class McpPlaybookTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers playbook MCP tools.
        /// </summary>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, LoggingModule logging)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (logging == null) throw new ArgumentNullException(nameof(logging));

            register(
                "armada_get_playbook",
                "Get a playbook by ID.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Playbook ID (pbk_ prefix)" }
                    },
                    required = new[] { "id" }
                },
                async (args) =>
                {
                    PlaybookArgs request = JsonSerializer.Deserialize<PlaybookArgs>(args!.Value, _JsonOptions)!;
                    string id = request.Id?.Trim() ?? String.Empty;
                    if (String.IsNullOrWhiteSpace(id)) return (object)new { Error = "id is required" };

                    Playbook? playbook = await database.Playbooks.ReadAsync(id).ConfigureAwait(false);
                    if (playbook == null) return (object)new { Error = "Playbook not found: " + id };
                    return (object)playbook;
                });

            register(
                "armada_create_playbook",
                "Create a new markdown playbook.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        fileName = new { type = "string", description = "Markdown filename (must end with .md)" },
                        description = new { type = "string", description = "Optional human-readable description" },
                        content = new { type = "string", description = "Markdown content" },
                        active = new { type = "boolean", description = "Whether the playbook is active" }
                    },
                    required = new[] { "fileName", "content" }
                },
                async (args) =>
                {
                    PlaybookArgs request = JsonSerializer.Deserialize<PlaybookArgs>(args!.Value, _JsonOptions)!;
                    if (String.IsNullOrWhiteSpace(request.FileName)) return (object)new { Error = "fileName is required" };
                    if (String.IsNullOrWhiteSpace(request.Content)) return (object)new { Error = "content is required" };

                    Playbook playbook = new Playbook(request.FileName!, request.Content!)
                    {
                        Description = request.Description,
                        Active = request.Active ?? true
                    };
                    playbook.TenantId = Constants.DefaultTenantId;
                    playbook.UserId = Constants.DefaultUserId;

                    PlaybookService service = new PlaybookService(database, logging);
                    service.Validate(playbook);

                    if (await database.Playbooks.ExistsByFileNameAsync(Constants.DefaultTenantId, playbook.FileName).ConfigureAwait(false))
                    {
                        return (object)new { Error = "A playbook with that file name already exists." };
                    }

                    Playbook created = await database.Playbooks.CreateAsync(playbook).ConfigureAwait(false);
                    return (object)created;
                });

            register(
                "armada_update_playbook",
                "Update an existing playbook.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Playbook ID (pbk_ prefix)" },
                        fileName = new { type = "string", description = "Markdown filename (must end with .md)" },
                        description = new { type = "string", description = "Optional human-readable description" },
                        content = new { type = "string", description = "Markdown content" },
                        active = new { type = "boolean", description = "Whether the playbook is active" }
                    },
                    required = new[] { "id" }
                },
                async (args) =>
                {
                    PlaybookArgs request = JsonSerializer.Deserialize<PlaybookArgs>(args!.Value, _JsonOptions)!;
                    string id = request.Id?.Trim() ?? String.Empty;
                    if (String.IsNullOrWhiteSpace(id)) return (object)new { Error = "id is required" };

                    Playbook? playbook = await database.Playbooks.ReadAsync(id).ConfigureAwait(false);
                    if (playbook == null) return (object)new { Error = "Playbook not found: " + id };
                    playbook.TenantId ??= Constants.DefaultTenantId;
                    playbook.UserId ??= Constants.DefaultUserId;

                    if (request.FileName != null) playbook.FileName = request.FileName;
                    if (request.Description != null) playbook.Description = request.Description;
                    if (request.Content != null) playbook.Content = request.Content;
                    if (request.Active.HasValue) playbook.Active = request.Active.Value;

                    PlaybookService service = new PlaybookService(database, logging);
                    service.Validate(playbook);

                    Playbook? duplicate = await database.Playbooks.ReadByFileNameAsync(playbook.TenantId ?? Constants.DefaultTenantId, playbook.FileName).ConfigureAwait(false);
                    if (duplicate != null && !String.Equals(duplicate.Id, playbook.Id, StringComparison.Ordinal))
                    {
                        return (object)new { Error = "A playbook with that file name already exists." };
                    }

                    Playbook updated = await database.Playbooks.UpdateAsync(playbook).ConfigureAwait(false);
                    return (object)updated;
                });

            register(
                "armada_delete_playbook",
                "Delete a playbook by ID. Existing mission snapshots remain immutable.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Playbook ID (pbk_ prefix)" }
                    },
                    required = new[] { "id" }
                },
                async (args) =>
                {
                    PlaybookArgs request = JsonSerializer.Deserialize<PlaybookArgs>(args!.Value, _JsonOptions)!;
                    string id = request.Id?.Trim() ?? String.Empty;
                    if (String.IsNullOrWhiteSpace(id)) return (object)new { Error = "id is required" };

                    Playbook? playbook = await database.Playbooks.ReadAsync(id).ConfigureAwait(false);
                    if (playbook == null) return (object)new { Error = "Playbook not found: " + id };

                    await database.Playbooks.DeleteAsync(id).ConfigureAwait(false);
                    return (object)new { Status = "deleted", PlaybookId = id };
                });
        }
    }
}
