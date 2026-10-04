namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers MCP tools for reading token-usage summaries.
    /// </summary>
    public static class McpTokenUsageTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private const int _DefaultSinceHours = 24;
        private const int _DefaultBucketMinutes = 60;

        /// <summary>
        /// Registers token-usage MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for token-usage data access.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database)
        {
            register(
                "token_usage_summary",
                "Summarize model token usage over a time window: time buckets with a per-model breakdown, a whole-window per-model aggregate ordered most-used first, and grand totals (input, output, cached, total). Counts are real where the runtime reports usage (for example Claude Code) and estimated otherwise -- estimatedCount reports how many aggregated records were estimated.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        sinceHours = new { type = "integer", description = "Only include usage newer than this many hours (default 24; ignored when fromUtc is set)" },
                        fromUtc = new { type = "string", description = "Explicit UTC window start (ISO-8601); overrides sinceHours" },
                        toUtc = new { type = "string", description = "Explicit UTC window end (ISO-8601; default now)" },
                        bucketMinutes = new { type = "number", description = "Time-bucket width in minutes; fractional allowed, e.g. 0.5 for 30-second buckets (default 60)" },
                        model = new { type = "string", description = "Filter to one model" },
                        runtime = new { type = "string", description = "Filter to one runtime (for example claudecode, codex, mux)" },
                        source = new { type = "string", description = "Filter to one source: mission, chat, or planning" },
                        vesselId = new { type = "string", description = "Filter to one vessel (vsl_ prefix)" },
                        captainId = new { type = "string", description = "Filter to one captain (cpt_ prefix)" }
                    },
                    required = new string[] { }
                },
                async (args) =>
                {
                    TokenUsageSummaryArgs request = args == null
                        ? new TokenUsageSummaryArgs()
                        : JsonSerializer.Deserialize<TokenUsageSummaryArgs>(args.Value, _JsonOptions) ?? new TokenUsageSummaryArgs();

                    TokenUsageQuery query = new TokenUsageQuery();

                    DateTime toUtc = DateTime.UtcNow;
                    if (!String.IsNullOrWhiteSpace(request.ToUtc) &&
                        DateTime.TryParse(request.ToUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsedTo))
                    {
                        toUtc = parsedTo;
                    }

                    DateTime fromUtc;
                    if (!String.IsNullOrWhiteSpace(request.FromUtc) &&
                        DateTime.TryParse(request.FromUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsedFrom))
                    {
                        fromUtc = parsedFrom;
                    }
                    else
                    {
                        int sinceHours = request.SinceHours.HasValue && request.SinceHours.Value > 0 ? request.SinceHours.Value : _DefaultSinceHours;
                        fromUtc = toUtc.AddHours(-sinceHours);
                    }

                    query.FromUtc = fromUtc;
                    query.ToUtc = toUtc;
                    query.BucketMinutes = request.BucketMinutes.HasValue && request.BucketMinutes.Value > 0 ? request.BucketMinutes.Value : _DefaultBucketMinutes;
                    query.Model = String.IsNullOrWhiteSpace(request.Model) ? null : request.Model;
                    query.Runtime = String.IsNullOrWhiteSpace(request.Runtime) ? null : request.Runtime;
                    query.Source = String.IsNullOrWhiteSpace(request.Source) ? null : request.Source;
                    query.VesselId = String.IsNullOrWhiteSpace(request.VesselId) ? null : request.VesselId;
                    query.CaptainId = String.IsNullOrWhiteSpace(request.CaptainId) ? null : request.CaptainId;

                    // Same scoping as GET /api/v1/token-usage/summary: a global admin sees every tenant, a tenant admin
                    // their tenant, and a regular user their own usage.
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (!McpCallerScope.IsGlobal(caller))
                    {
                        query.TenantId = caller.TenantId;
                        if (!caller.IsTenantAdmin) query.UserId = caller.UserId;
                    }

                    List<TokenUsageRecord> records = await database.TokenUsage.EnumerateForSummaryAsync(query).ConfigureAwait(false);
                    TokenUsageSummaryResult summary = TokenUsageSummaryBuilder.Build(records, query);
                    return (object)summary;
                });
        }
    }
}
