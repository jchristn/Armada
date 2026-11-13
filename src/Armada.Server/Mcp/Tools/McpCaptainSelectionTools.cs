namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>Configurable captain selection and scoped decision explanations shared with REST.</summary>
    public static class McpCaptainSelectionTools
    {
        /// <summary>Register tools on both transports; unavailable services return a precise capability error.</summary>
        public static void Register(RegisterToolDelegate register, CaptainSelectionAccessService? selections, UsageSelectionConfigurationService? configuration)
        {
            JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            object missionSchema = new { type = "object", properties = new { missionId = new { type = "string" }, pageNumber = new { type = "integer", minimum = 1 }, pageSize = new { type = "integer", minimum = 1, maximum = 1000 } }, required = new[] { "missionId" } };
            register("authorize_captain_selection_recovery", "Authorize or revoke preferred-captain fallback and a soft-reserve exception for a Pending mission. Requires tenant/system administrator, expectedRevision and a reason. Hard quota, cooldown, strict assignment, scope and capacity remain enforced.",
                new { type = "object", properties = new { missionId = new { type = "string" }, recovery = new { type = "object", properties = new { expectedRevision = new { type = "integer", minimum = 0 }, allowFallback = new { type = "boolean" }, reserveException = new { type = "boolean" }, reason = new { type = "string", minLength = 1, maxLength = 512 } }, required = new[] { "expectedRevision", "reason" } } }, required = new[] { "missionId", "recovery" } }, async args => {
                    if (selections == null) return (object)McpToolError.Unavailable("Selection service unavailable.");
                    CaptainSelectionToolArgs request = Read(args, options);
                    return await selections.AuthorizeRecoveryAsync(McpToolHelpers.ResolveCallerContext(), request.MissionId ?? throw new ArgumentException("missionId is required."), request.Recovery ?? throw new ArgumentException("recovery is required.")).ConfigureAwait(false);
                });
            register("get_captain_selection", "Read the current admitted attempt and latest retained decision for a visible mission. No account keys are returned.", missionSchema,
                async args => selections == null ? (object)McpToolError.Unavailable("Selection service unavailable.") : await selections.SummaryAsync(McpToolHelpers.ResolveCallerContext(), Read(args, options).MissionId ?? throw new ArgumentException("missionId is required.")).ConfigureAwait(false));
            register("preview_captain_selection", "Read-only current-state comparison using the configured strategy and shared routing/eligibility. Does not capture, persist, claim or launch.", missionSchema,
                async args => {
                    if (selections == null) return (object)McpToolError.Unavailable("Selection service unavailable.");
                    CaptainSelectionToolArgs request = Read(args, options);
                    return await selections.PreviewAsync(McpToolHelpers.ResolveCallerContext(), request.MissionId ?? throw new ArgumentException("missionId is required."), request.PageNumber, request.PageSize).ConfigureAwait(false);
                });
            register("get_captain_selection_history", "Page a visible mission's retained selection decisions. Optional query filters trigger, outcome and UTC time range before counts.",
                new { type = "object", properties = new { missionId = new { type = "string" }, query = new { type = "object" } }, required = new[] { "missionId" } }, async args => {
                    if (selections == null) return (object)McpToolError.Unavailable("Selection service unavailable.");
                    CaptainSelectionToolArgs request = Read(args, options);
                    return await selections.HistoryAsync(McpToolHelpers.ResolveCallerContext(), request.MissionId ?? throw new ArgumentException("missionId is required."), request.Query ?? new CaptainSelectionHistoryQuery()).ConfigureAwait(false);
                });
            register("get_captain_selection_decision", "Read an immutable decision, recorded policy, scoped candidate page, per-reason counts of unlisted candidates and runtime model evidence. tenantId override requires a global administrator.",
                new { type = "object", properties = new { decisionId = new { type = "string" }, tenantId = new { type = "string" }, pageNumber = new { type = "integer", minimum = 1 }, pageSize = new { type = "integer", minimum = 1, maximum = 1000 } }, required = new[] { "decisionId" } }, async args => {
                    if (selections == null) return (object)McpToolError.Unavailable("Selection service unavailable.");
                    CaptainSelectionToolArgs request = Read(args, options);
                    return await selections.DetailAsync(McpToolHelpers.ResolveCallerContext(), request.DecisionId ?? throw new ArgumentException("decisionId is required."), request.TenantId, request.PageNumber, request.PageSize).ConfigureAwait(false);
                });
            register("get_usage_selection_settings", "Read the effective server-wide captain-selection policy, live revision, strategies and override metadata. Requires global administrator.", new { type = "object", properties = new { } }, args => {
                RequireAdministrator();
                return Task.FromResult(configuration == null ? (object)McpToolError.Unavailable("Live selection configuration unavailable.") : configuration.Read());
            });
            register("update_usage_selection_settings", "Replace the complete server-wide captain-selection policy live. Requires global administrator. expectedRevision rejects stale editors; environment overrides are read-only.",
                new { type = "object", properties = new { settings = SettingsSchema(), expectedRevision = new { type = "integer", minimum = 0 } }, required = new[] { "settings" } }, async args => {
                    RequireAdministrator();
                    if (configuration == null) return (object)McpToolError.Unavailable("Live selection configuration unavailable.");
                    CaptainSelectionToolArgs request = Read(args, options);
                    try { return await configuration.ApplyAsync(request.Settings ?? throw new ArgumentException("settings is required."), McpToolHelpers.ResolveCallerContext(), request.ExpectedRevision).ConfigureAwait(false); }
                    catch (UsageSelectionConfigurationException error) { return McpToolError.Conflict(error.Message, error.Code); }
                    catch (Exception error) when (error is System.IO.IOException || error is System.Data.Common.DbException)
                    { return McpToolError.Unavailable("Selection settings could not be persisted. Reload settings before retrying."); }
                });
        }

        private static object SettingsSchema() => new
        {
            type = "object",
            description = "Complete replacement group; omitted members use defaults. Observe preserves actual routing and records the configured hypothetical strategy. Read capabilities before requesting Enforce.",
            properties = new
            {
                mode = new { type = "string", @enum = new[] { "Disabled", "Observe", "Enforce" }, @default = "Observe" },
                strategy = new { type = "string", @enum = new[] { "FirstAvailable", "MostRemainingAllowance", "MostUsedAllowance" }, @default = "FirstAvailable",
                    description = "Existing qualified routing order, greatest remaining headroom, or most utilized eligible account. Safety/capacity/reserve gates always apply." },
                unknownUsageBehavior = new { type = "string", @enum = new[] { "AllowDeprioritized", "Wait" }, @default = "AllowDeprioritized" },
                reservePercent = new { type = "number", minimum = 0, maximum = 95, @default = 10 },
                comfortableHeadroomPercent = new { type = "number", minimum = 1, maximum = 100, @default = 30, description = "Must exceed reservePercent." },
                perAttemptReservationPercent = new { type = "number", minimum = 0.1, maximum = 25, @default = 2 },
                maximumFreshnessSeconds = new { type = "integer", minimum = 120, maximum = 86400, @default = 900 },
                unknownRetrySeconds = new { type = "integer", minimum = 5, maximum = 3600, @default = 60 },
                providerCooldownSeconds = new { type = "integer", minimum = 5, maximum = 3600, @default = 60 },
                settlementLagSeconds = new { type = "integer", minimum = 0, maximum = 3600, @default = 60 },
                maximumUnsettledSeconds = new { type = "integer", minimum = 60, maximum = 86400, @default = 3600 },
                decisionRetentionDays = new { type = "integer", minimum = 7, maximum = 1825, @default = 90 },
                maximumCandidates = new { type = "integer", minimum = 1, maximum = 10000, @default = 1000 }
            }
        };

        private static void RequireAdministrator()
        {
            AuthContext caller = McpToolHelpers.ResolveCallerContext();
            if (!caller.IsAuthenticated || !caller.IsAdmin || caller.MissionId != null || caller.AskThreadId != null)
                throw new UnauthorizedAccessException("Global administrator access is required.");
        }

        private static CaptainSelectionToolArgs Read(JsonElement? args, JsonSerializerOptions options)
        {
            try { return args == null ? new CaptainSelectionToolArgs() : JsonSerializer.Deserialize<CaptainSelectionToolArgs>(args.Value, options) ?? new CaptainSelectionToolArgs(); }
            catch (JsonException) { throw new ArgumentException("Invalid captain selection arguments."); }
        }
    }
}
