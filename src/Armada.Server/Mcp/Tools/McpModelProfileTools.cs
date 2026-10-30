namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using Armada.Core.Models;
    using Armada.Server.Routes;

    /// <summary>
    /// Registers the model profile MCP tools: get_model_profile and list_model_profiles. Every tool enforces the same
    /// rules as REST through <see cref="ModelProfileAccessService"/>; its typed exceptions become typed tool errors.
    /// </summary>
    public static class McpModelProfileTools
    {
        #region Private-Members

        private const string UnavailableMessage = "Model profiles are not available on this server.";

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the tools. When <paramref name="service"/> is null every tool is still registered and answers a typed
        /// Unavailable error (the standalone stdio server), so the tool list matches the HTTP server's.
        /// </summary>
        /// <param name="register">Registration delegate.</param>
        /// <param name="service">Model profile access service, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="register"/> is null.</exception>
        public static void Register(RegisterToolDelegate register, ModelProfileAccessService? service)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            register(
                "get_model_profile",
                "Get one model profile visible to the caller (the model as served: runtime, provider, model and served context) with its capabilities, rendering overrides, notes and immutable revisions (runtime version, model digest, sampling, tool configuration, compatibleWith, firstSeenUtc), oldest first. Profiles are advisory: they do not set a captain's tier or change routing. A captain's modelProfileId points at its profile.",
                new
                {
                    type = "object",
                    properties = new { id = new { type = "string", description = "Model profile ID (mdl_ prefix)" } },
                    required = new[] { "id" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    ModelProfileIdArgs request = Read<ModelProfileIdArgs>(args);
                    return (object)await service.GetAsync(caller, request.Id).ConfigureAwait(false);
                });

            register(
                "list_model_profiles",
                "List model profiles, newest first, one page at a time, without their revisions. A global admin sees every tenant; everyone else sees their own tenant. Returns an enumeration result (objects, totalRecords, totalPages).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        runtime = new { type = "string", description = "Runtime filter (case-insensitive), for example ClaudeCode or Mux" },
                        provider = new { type = "string", description = "Provider filter (case-insensitive), for example Ollama" },
                        model = new { type = "string", description = "Only models whose name contains this text (case-insensitive)" },
                        pageNumber = new { type = "integer", description = "Page number (default 1)" },
                        pageSize = new { type = "integer", description = "Page size (1-1000, default 25)" }
                    }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    ModelProfileListArgs request = Read<ModelProfileListArgs>(args);
                    Dictionary<string, string?> values = new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["runtime"] = request.Runtime,
                        ["provider"] = request.Provider,
                        ["model"] = request.Model,
                        ["pageNumber"] = request.PageNumber?.ToString(CultureInfo.InvariantCulture),
                        ["pageSize"] = (request.PageSize ?? 25).ToString(CultureInfo.InvariantCulture)
                    };
                    ModelProfileQuery query = ModelProfileRoutes.ParseQuery(key => values.TryGetValue(key, out string? v) ? v : null);
                    return (object)await service.ListAsync(caller, query).ConfigureAwait(false);
                });
        }

        #endregion

        #region Private-Methods

        private static T Read<T>(JsonElement? args) where T : class, new()
        {
            if (!args.HasValue || args.Value.ValueKind != JsonValueKind.Object) return new T();
            try
            {
                return JsonSerializer.Deserialize<T>(args.Value, _JsonOptions) ?? new T();
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid arguments: " + ex.Message, ex);
            }
        }

        #endregion
    }
}
