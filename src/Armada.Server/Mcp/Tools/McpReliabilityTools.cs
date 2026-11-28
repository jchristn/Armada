namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Registers the pipeline reliability MCP tools: get_mission_attempts, get_reliability_summary, list_gate_reports and
    /// get_gate_report. Every tool enforces the same rules as REST through <see cref="ReliabilityAccessService"/>.
    /// </summary>
    public static class McpReliabilityTools
    {
        #region Private-Members

        private const string UnavailableMessage = "Reliability data is not available on this server.";

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the tools. When <paramref name="service"/> is null every tool is still registered and answers a typed
        /// Unavailable error, so the tool list matches the HTTP server's.
        /// </summary>
        /// <param name="register">Registration delegate.</param>
        /// <param name="service">Reliability access service, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="register"/> is null.</exception>
        public static void Register(RegisterToolDelegate register, ReliabilityAccessService? service)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            register(
                "get_mission_attempts",
                "Get a mission's attempt history: its recovery counters (redispatchAttempts, outputRepairAttempts, noOpRedispatchAttempts, stallRecoveryAttempts), escalation state (escalationCount, escalatedFromCaptainId, escalatedFromModel, tier, reworkEscalated), persona and mode, and every recorded attempt oldest first with captain, model, disposition (Completed, Relaunched, OutputRepair, Escalated, Failed, Interrupted, Cancelled, Pending), failureKind, runtimeFailureKind, contractRepairKind, isRepair, escalated, nextCaptainId, judgeVerdict and timing.",
                new
                {
                    type = "object",
                    properties = new { missionId = new { type = "string", description = "Mission ID (msn_ prefix)" } },
                    required = new[] { "missionId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    MissionIdArgs request = Read<MissionIdArgs>(args);
                    return (object)await service.GetMissionAttemptsAsync(caller, request.MissionId).ConfigureAwait(false);
                });

            register(
                "get_reliability_summary",
                "Pipeline reliability per (model, persona) over a window from stage outcomes: attempts, completed, failed, relaunched, repairs, escalations, unclassified, completionRate, unclassifiedRate, failureKinds, runtimeFailureKinds and medianDurationMs, plus totals. Window defaults to the last 14 days (at most 90); production attempts only unless includeEvaluation. Sorted and paged on the server.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        tenantId = new { type = "string", description = "Tenant filter (global admins only)" },
                        model = new { type = "string", description = "Model (exact, case-insensitive)" },
                        persona = new { type = "string", description = "Persona name (exact, case-insensitive)" },
                        vesselId = new { type = "string", description = "Vessel filter (vsl_ prefix)" },
                        includeEvaluation = new { type = "boolean", description = "Include battery and evaluation attempts (default false)" },
                        fromUtc = new { type = "string", description = "Ended at or after (ISO 8601 UTC; default 14 days before toUtc)" },
                        toUtc = new { type = "string", description = "Ended before (ISO 8601 UTC; default now)" },
                        sort = new { type = "string", @enum = Enum.GetNames<ReliabilitySortEnum>(), description = "Sort field (default Attempts)" },
                        descending = new { type = "boolean", description = "Sort descending (default true)" },
                        pageNumber = new { type = "integer", description = "Page number (default 1)" },
                        pageSize = new { type = "integer", description = "Page size (1-1000, default 50)" }
                    }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    ReliabilitySummaryArgs request = Read<ReliabilitySummaryArgs>(args);
                    ReliabilitySummaryQuery query = new ReliabilitySummaryQuery
                    {
                        TenantId = request.TenantId,
                        Model = request.Model,
                        Persona = request.Persona,
                        VesselId = request.VesselId,
                        IncludeEvaluation = request.IncludeEvaluation ?? false,
                        FromUtc = ParseUtc(request.FromUtc, "fromUtc"),
                        ToUtc = ParseUtc(request.ToUtc, "toUtc"),
                        Descending = request.Descending ?? true
                    };
                    if (!String.IsNullOrWhiteSpace(request.Sort))
                    {
                        if (!Enum.TryParse(request.Sort.Trim(), true, out ReliabilitySortEnum sort) || !Enum.IsDefined(sort))
                            return (object)McpToolError.InvalidArgument("sort must be one of " + String.Join(", ", Enum.GetNames<ReliabilitySortEnum>()) + ".");
                        query.Sort = sort;
                    }

                    if (request.PageNumber.HasValue) query.PageNumber = request.PageNumber.Value;
                    if (request.PageSize.HasValue) query.PageSize = request.PageSize.Value;
                    return (object)await service.GetSummaryAsync(caller, query).ConfigureAwait(false);
                });

            register(
                "list_gate_reports",
                "List stored acceptance gate reports (G1 to G12 measures with per-pipeline counts), newest first. Global and tenant admins.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        pageNumber = new { type = "integer", description = "Page number (default 1)" },
                        pageSize = new { type = "integer", description = "Page size (1-100, default 10)" }
                    }
                },
                (args) =>
                {
                    if (service == null) return Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BatteryPackListArgs request = Read<BatteryPackListArgs>(args);
                    return Task.FromResult((object)service.ListGateReports(caller, request.PageNumber, request.PageSize));
                });

            register(
                "get_gate_report",
                "Get one stored acceptance gate report by id, or the newest with id latest. Global and tenant admins.",
                new
                {
                    type = "object",
                    properties = new { id = new { type = "string", description = "Report ID (gate- prefix) or latest" } },
                    required = new[] { "id" }
                },
                (args) =>
                {
                    if (service == null) return Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BatteryRunIdArgs request = Read<BatteryRunIdArgs>(args);
                    return Task.FromResult((object)service.GetGateReport(caller, request.Id));
                });
        }

        #endregion

        #region Private-Methods

        private static DateTime? ParseUtc(string? value, string name)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
                throw new ArgumentException(name + " must be an ISO 8601 UTC time.");
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

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
