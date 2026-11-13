namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using Armada.Core.Models;
    using Armada.Server.Routes;

    /// <summary>
    /// Registers the model profile MCP tools: get_model_profile, list_model_profiles and get_model_scorecard. Every tool enforces the same
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
                "get_model_scorecard",
                "Get the read-only scorecard of one (model profile, persona), optionally one vessel, over a rolling window: the last windowDays days or the last windowMissions missions, whichever is smaller (default 30 days, 50 missions). Every rate is a count with its denominator (count of of) plus a 95% Wilson score interval (lower, upper): first-pass success, accepted results, stage-check status counts (skipped by reason), Judge verdicts on this profile's work, protocol repairs and repairs that ran out, tool failures, resumes in place and stalls, escalations away and what followed, suspected causes (Unknown and Mixed as their own buckets), and tokens and wall time per accepted result. Metrics Armada does not record come back with available false and the reason. Revisions are separate groups unless marked compatible; exclusions lists each attribution rule and how many items it removed. Read-only: nothing changes routing or captains.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Model profile ID (mdl_ prefix)" },
                        persona = new { type = "string", description = "Persona, for example Worker or Judge" },
                        vesselId = new { type = "string", description = "Only stages on this vessel" },
                        windowDays = new { type = "integer", description = "Window length in days (1-365, default 30)" },
                        windowMissions = new { type = "integer", description = "Window length in missions (1-500, default 50)" }
                    },
                    required = new[] { "id", "persona" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    ModelScorecardArgs request = Read<ModelScorecardArgs>(args);
                    ModelScorecardRequest scorecard = new ModelScorecardRequest
                    {
                        Persona = request.Persona,
                        VesselId = request.VesselId,
                        WindowDays = request.WindowDays,
                        WindowMissions = request.WindowMissions
                    };
                    return (object)await service.GetScorecardAsync(caller, request.Id, scorecard).ConfigureAwait(false);
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
