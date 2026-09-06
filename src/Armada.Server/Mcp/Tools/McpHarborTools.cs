namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers MCP tools for managing registered Harbors (host runners).
    /// </summary>
    public static class McpHarborTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Registers Harbor MCP tools.
        /// </summary>
        /// <param name="register">Tool registration delegate.</param>
        /// <param name="harbors">Harbor service.</param>
        public static void Register(RegisterToolDelegate register, HarborService harbors)
        {
            register(
                "get_harbor",
                "Inspect one registered Harbor (host runner) by ID, including its advertised capabilities and connection status.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        harborId = new { type = "string", description = "Harbor ID (hbr_ prefix)" }
                    },
                    required = new[] { "harborId" }
                },
                async (args) =>
                {
                    HarborIdArgs request = JsonSerializer.Deserialize<HarborIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize HarborIdArgs.");
                    AuthContext auth = McpToolHelpers.CreateDefaultTenantAdminContext();
                    Harbor? harbor = await harbors.ReadAsync(auth, request.HarborId).ConfigureAwait(false);
                    if (harbor == null) return (object)new { Error = "Harbor not found" };
                    return (object)harbor;
                });

            register(
                "create_harbor",
                "Pre-register a Harbor. A Harbor also self-registers on first handshake; use this to reserve a name/capacity before it connects.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Human-facing Harbor name" },
                        maxConcurrentJobs = new { type = "integer", description = "Maximum concurrent jobs (default 4)" },
                        enabled = new { type = "boolean", description = "Whether the Harbor is enabled for routing (default true)" }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    HarborUpsertArgs request = JsonSerializer.Deserialize<HarborUpsertArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize HarborUpsertArgs.");
                    AuthContext auth = McpToolHelpers.CreateDefaultTenantAdminContext();

                    Harbor harbor = new Harbor();
                    if (!String.IsNullOrWhiteSpace(request.Name)) harbor.Name = request.Name;
                    if (request.MaxConcurrentJobs.HasValue) harbor.MaxConcurrentJobs = request.MaxConcurrentJobs.Value;
                    if (request.Enabled.HasValue) harbor.Enabled = request.Enabled.Value;

                    try
                    {
                        return (object)await harbors.CreateAsync(auth, harbor).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                    {
                        return (object)new { Error = ex.Message };
                    }
                });

            register(
                "update_harbor",
                "Update a Harbor's operator-editable fields (name, capacity, enabled).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        harborId = new { type = "string", description = "Harbor ID (hbr_ prefix)" },
                        name = new { type = "string", description = "Human-facing Harbor name" },
                        maxConcurrentJobs = new { type = "integer", description = "Maximum concurrent jobs" },
                        enabled = new { type = "boolean", description = "Whether the Harbor is enabled for routing" }
                    },
                    required = new[] { "harborId" }
                },
                async (args) =>
                {
                    HarborUpsertArgs request = JsonSerializer.Deserialize<HarborUpsertArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize HarborUpsertArgs.");
                    AuthContext auth = McpToolHelpers.CreateDefaultTenantAdminContext();

                    if (String.IsNullOrWhiteSpace(request.HarborId)) return (object)new { Error = "harborId is required." };
                    Harbor? existing = await harbors.ReadAsync(auth, request.HarborId).ConfigureAwait(false);
                    if (existing == null) return (object)new { Error = "Harbor not found" };

                    if (!String.IsNullOrWhiteSpace(request.Name)) existing.Name = request.Name;
                    if (request.MaxConcurrentJobs.HasValue) existing.MaxConcurrentJobs = request.MaxConcurrentJobs.Value;
                    if (request.Enabled.HasValue) existing.Enabled = request.Enabled.Value;

                    try
                    {
                        return (object)await harbors.UpdateAsync(auth, existing).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                    {
                        return (object)new { Error = ex.Message };
                    }
                });

            register(
                "delete_harbor",
                "Delete a Harbor registration by ID.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        harborId = new { type = "string", description = "Harbor ID (hbr_ prefix)" }
                    },
                    required = new[] { "harborId" }
                },
                async (args) =>
                {
                    HarborIdArgs request = JsonSerializer.Deserialize<HarborIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize HarborIdArgs.");
                    AuthContext auth = McpToolHelpers.CreateDefaultTenantAdminContext();
                    try
                    {
                        await harbors.DeleteAsync(auth, request.HarborId).ConfigureAwait(false);
                        return (object)new { Deleted = true, HarborId = request.HarborId };
                    }
                    catch (Exception ex)
                    {
                        return (object)new { Error = ex.Message };
                    }
                });

            register(
                "set_harbor_enabled",
                "Enable or disable a Harbor for routing. A disabled Harbor keeps its docks but receives no new missions.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        harborId = new { type = "string", description = "Harbor ID (hbr_ prefix)" },
                        enabled = new { type = "boolean", description = "Whether the Harbor is enabled for routing" }
                    },
                    required = new[] { "harborId", "enabled" }
                },
                async (args) =>
                {
                    HarborUpsertArgs request = JsonSerializer.Deserialize<HarborUpsertArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize HarborUpsertArgs.");
                    AuthContext auth = McpToolHelpers.CreateDefaultTenantAdminContext();
                    if (String.IsNullOrWhiteSpace(request.HarborId)) return (object)new { Error = "harborId is required." };
                    try
                    {
                        return (object)await harbors.SetEnabledAsync(auth, request.HarborId, request.Enabled ?? true).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        return (object)new { Error = ex.Message };
                    }
                });
        }
    }
}
