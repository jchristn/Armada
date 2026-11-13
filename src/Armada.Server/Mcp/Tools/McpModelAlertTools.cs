namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Server.Routes;

    /// <summary>
    /// Registers the model alert MCP tools: list_model_alerts, get_model_alert, evaluate_model_alerts,
    /// get_model_alert_thresholds, update_model_alert_thresholds, apply_model_alert_action and ignore_model_alert. Every
    /// tool enforces the same rules as REST through <see cref="ModelAlertAccessService"/>; its typed exceptions become
    /// typed tool errors.
    /// </summary>
    public static class McpModelAlertTools
    {
        #region Private-Members

        private const string UnavailableMessage = "Model alerts are not available on this server.";

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
        /// <param name="service">Model alert access service, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="register"/> is null.</exception>
        public static void Register(RegisterToolDelegate register, ModelAlertAccessService? service)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            register(
                "list_model_alerts",
                "List model alerts (a scorecard threshold crossed for a model profile, revision group, persona and vessel scope), most recently raised first, one page at a time, each with its counts, applied-action record and suggested actions (with availability and the reason when unavailable). A global admin sees every tenant; everyone else sees their own tenant. Alerts are advisory: nothing changes until a suggested action is applied. Returns an enumeration result (objects, totalRecords, totalPages).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        status = new { type = "string", description = "Open or Resolved (default both)" },
                        profileId = new { type = "string", description = "Model profile ID (mdl_ prefix)" },
                        persona = new { type = "string", description = "Persona name (case-insensitive)" },
                        includeIgnored = new { type = "boolean", description = "Include alerts hidden by Ignore (default true)" },
                        pageNumber = new { type = "integer", description = "Page number (default 1)" },
                        pageSize = new { type = "integer", description = "Page size (1-1000, default 25)" }
                    }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    ModelAlertListArgs request = Read<ModelAlertListArgs>(args);
                    Dictionary<string, string?> values = new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["status"] = request.Status,
                        ["profileId"] = request.ProfileId,
                        ["persona"] = request.Persona,
                        ["includeIgnored"] = request.IncludeIgnored.HasValue ? (request.IncludeIgnored.Value ? "true" : "false") : null,
                        ["pageNumber"] = request.PageNumber?.ToString(CultureInfo.InvariantCulture),
                        ["pageSize"] = (request.PageSize ?? 25).ToString(CultureInfo.InvariantCulture)
                    };
                    ModelAlertQuery query = ModelAlertRoutes.ParseQuery(key => values.TryGetValue(key, out string? v) ? v : null);
                    return (object)await service.ListAsync(caller, query).ConfigureAwait(false);
                });

            register(
                "get_model_alert",
                "Get one model alert visible to the caller with its counts, applied-action record and suggested actions (MoveDown, RemoveFromPersona, CompactTemplate, Reevaluate, Ignore), each with availability and the reason when unavailable.",
                new
                {
                    type = "object",
                    properties = new { id = new { type = "string", description = "Model alert ID (mal_ prefix)" } },
                    required = new[] { "id" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    ModelAlertIdArgs request = Read<ModelAlertIdArgs>(args);
                    return (object)await service.GetAsync(caller, request.Id).ConfigureAwait(false);
                });

            register(
                "evaluate_model_alerts",
                "Run one model alert evaluation pass now: the scorecard of every (model profile, persona) with production attempts in the last 30 days is checked against the thresholds; crossed conditions raise or keep one alert each and conditions that no longer hold are resolved. Targets lists every rule result, including inactive (Unavailable) rules and rules whose minimum count is not met, with the reason. Reads scorecard data only; never changes captains, personas, templates or routing. Tenant admins evaluate their tenant, global admins every tenant.",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    return (object)await service.EvaluateAsync(caller).ConfigureAwait(false);
                });

            register(
                "get_model_alert_thresholds",
                "Get the model alert thresholds: defaults (every value set; built-in: first pass below 0.5 over at least 8 missions, 3 escalations away in a row, protocol repairs above 0.3 over at least 8 attempts, tokens per accepted result above 2x the persona median across other profiles with at least 5 accepted results on each side) and per-persona overrides (a null value keeps the default).",
                new { type = "object", properties = new { } },
                (args) =>
                {
                    if (service == null) return System.Threading.Tasks.Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    return System.Threading.Tasks.Task.FromResult((object)service.GetThresholds(caller));
                });

            register(
                "update_model_alert_thresholds",
                "Replace the model alert thresholds (defaults and per-persona overrides) in the server settings; the next evaluation pass uses them. Rates are 0 to 1, minimum counts 1 to 1000, the cost multiplier 1 to 100; each persona may have one override. Global admins only.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        defaults = new { type = "object", description = "Defaults: firstPassMinRate, firstPassMinMissions, escalationsInARow, protocolRepairMaxRate, protocolRepairMinAttempts, costMedianMultiplier, costMinAccepted (null uses the built-in default)" },
                        overrides = new { type = "array", description = "Per-persona overrides: persona plus any of the default fields (null keeps the default)", items = new { type = "object" } }
                    }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    ModelAlertThresholds request = Read<ModelAlertThresholds>(args);
                    return (object)await service.UpdateThresholdsAsync(caller, request).ConfigureAwait(false);
                });

            register(
                "apply_model_alert_action",
                "Apply one suggested action of a model alert, only now and only this one: MoveDown moves the persona captain priority entries that select only this profile's captains to the bottom; RemoveFromPersona removes them and clears the default captain when it is on this profile; Reevaluate starts a Quick Evaluate of a captain on this profile within the evaluation budgets; Ignore hides the alert for days (1-365, default 7); CompactTemplate is unavailable (no prompt variant per profile and persona). The result is recorded on the alert. Tenant admins of the alert's tenant and global admins only.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Model alert ID (mal_ prefix)" },
                        action = new { type = "string", description = "MoveDown, RemoveFromPersona, CompactTemplate, Reevaluate or Ignore" },
                        days = new { type = "integer", description = "For Ignore: days to hide the alert (1-365, default 7)" }
                    },
                    required = new[] { "id", "action" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    ModelAlertActionArgs request = Read<ModelAlertActionArgs>(args);
                    ModelAlertApplyRequest apply = new ModelAlertApplyRequest { Days = request.Days };
                    if (!String.IsNullOrWhiteSpace(request.Action)
                        && Enum.TryParse(request.Action.Trim(), true, out ModelAlertActionEnum action)
                        && Enum.IsDefined(typeof(ModelAlertActionEnum), action))
                        apply.Action = action;
                    if (apply.Action == null)
                    {
                        // Resolve the alert first so another tenant's id answers not found, not a validation error.
                        await service.GetAsync(caller, request.Id).ConfigureAwait(false);
                        throw new ArgumentException("action must be MoveDown, RemoveFromPersona, CompactTemplate, Reevaluate or Ignore.");
                    }
                    return (object)await service.ApplyAsync(caller, request.Id, apply).ConfigureAwait(false);
                });

            register(
                "ignore_model_alert",
                "Hide a model alert from the inbox for a number of days (1-365, default 7); it shows again afterwards if the condition still holds. Recorded on the alert. Tenant admins of the alert's tenant and global admins only.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Model alert ID (mal_ prefix)" },
                        days = new { type = "integer", description = "Days to hide the alert (1-365, default 7)" }
                    },
                    required = new[] { "id" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    ModelAlertActionArgs request = Read<ModelAlertActionArgs>(args);
                    return (object)await service.IgnoreAsync(caller, request.Id, request.Days).ConfigureAwait(false);
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
