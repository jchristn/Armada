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
        /// <param name="metrics">Harbor metrics service, for get_harbor_metrics (optional; without it the tool is not registered).</param>
        public static void Register(RegisterToolDelegate register, HarborService harbors, HarborMetricsService? metrics = null)
        {
            if (metrics != null)
            {
                register(
                    "get_harbor_metrics",
                    "Charts for one Harbor over a window: jobs finished and failed per bucket (missions and other launches apart), slot usage (peak and average concurrent jobs against capacity), launch speed per runtime (median and p95 time to first output and total runtime), link health (connected, reconnecting, and down stretches and heartbeat round-trip times), and token usage by runtime and model. range is 1h (1-minute buckets), 24h (30-minute buckets, default), or 7d (3-hour buckets).",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            harborId = new { type = "string", description = "Harbor ID (hbr_ prefix)" },
                            range = new { type = "string", description = "Window: 1h, 24h (default), or 7d", @enum = new[] { "1h", "24h", "7d" } }
                        },
                        required = new[] { "harborId" }
                    },
                    async (args) =>
                    {
                        HarborMetricsArgs request = JsonSerializer.Deserialize<HarborMetricsArgs>(args!.Value, _JsonOptions)
                            ?? throw new InvalidOperationException("Could not deserialize HarborMetricsArgs.");
                        if (!Armada.Core.Metrics.HarborMetricsRanges.TryParse(request.Range, out Armada.Core.Enums.HarborMetricsRangeEnum range))
                            return (object)McpToolError.InvalidArgument("range must be 1h, 24h, or 7d.");
                        AuthContext auth = McpToolHelpers.ResolveCallerContext();
                        HarborMetrics? result = await metrics.GetAsync(auth, request.HarborId, range).ConfigureAwait(false);
                        if (result == null) return (object)McpToolError.NotFound("Harbor not found");
                        return (object)result;
                    });
            }

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
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    Harbor? harbor = await harbors.ReadAsync(auth, request.HarborId).ConfigureAwait(false);
                    if (harbor == null) return (object)McpToolError.NotFound("Harbor not found");
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
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();

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
                        return (object)McpToolError.FromException(ex);
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
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();

                    if (String.IsNullOrWhiteSpace(request.HarborId)) return (object)McpToolError.InvalidArgument("harborId is required.");
                    Harbor? existing = await harbors.ReadAsync(auth, request.HarborId).ConfigureAwait(false);
                    if (existing == null) return (object)McpToolError.NotFound("Harbor not found");

                    if (!String.IsNullOrWhiteSpace(request.Name)) existing.Name = request.Name;
                    if (request.MaxConcurrentJobs.HasValue) existing.MaxConcurrentJobs = request.MaxConcurrentJobs.Value;
                    if (request.Enabled.HasValue) existing.Enabled = request.Enabled.Value;

                    try
                    {
                        return (object)await harbors.UpdateAsync(auth, existing).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                    {
                        return (object)McpToolError.FromException(ex);
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
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    try
                    {
                        await harbors.DeleteAsync(auth, request.HarborId).ConfigureAwait(false);
                        return (object)new { Deleted = true, HarborId = request.HarborId };
                    }
                    catch (Exception ex)
                    {
                        return (object)McpToolError.FromException(ex);
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
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    if (String.IsNullOrWhiteSpace(request.HarborId)) return (object)McpToolError.InvalidArgument("harborId is required.");
                    try
                    {
                        return (object)await harbors.SetEnabledAsync(auth, request.HarborId, request.Enabled ?? true).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        return (object)McpToolError.FromException(ex);
                    }
                });
        }
    }
}
