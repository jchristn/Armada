namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Health;

    /// <summary>
    /// Registers MCP tools for vessel health: inspect one vessel, start an evaluation job, and set or remove a manual
    /// override. Enumeration of health rows is available through enumerate with entityType vessel_health.
    /// </summary>
    public static class McpVesselHealthTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Register the vessel health tools.
        /// </summary>
        /// <param name="register">Tool registration delegate.</param>
        /// <param name="health">Vessel health service.</param>
        public static void Register(RegisterToolDelegate register, VesselHealthService health)
        {
            register(
                "vessel_health",
                "Get one vessel's health: the row with effective (override-aware) statuses, raw findings (criterion, status, detailCode, valueA, valueB), and manual overrides. Dependency rows are excluded by default (dependencyCount hint returned); set includeDependencies to include them.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" },
                        includeDependencies = new { type = "boolean", description = "Include outdated/vulnerable dependency rows (default false)" }
                    },
                    required = new[] { "vesselId" }
                },
                async (args) =>
                {
                    VesselHealthArgs request = JsonSerializer.Deserialize<VesselHealthArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize VesselHealthArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    VesselHealthDetail? detail = await health.GetDetailAsync(auth.TenantId!, request.VesselId).ConfigureAwait(false);
                    if (detail == null) return (object)McpToolError.NotFound("Vessel not found");
                    return (object)new
                    {
                        Health = detail.Health,
                        Findings = detail.Findings,
                        Overrides = detail.Overrides,
                        DependencyCount = detail.Dependencies.Count,
                        Dependencies = request.IncludeDependencies ? detail.Dependencies : null
                    };
                });

            register(
                "evaluate_vessel_health",
                "Start a background vessel health evaluation job for specific vessels, a fleet, or every active vessel. Returns jobId; alreadyRunning is true (and nothing starts) when an evaluation is already running for the tenant. Poll the job with enumerate entityType jobs or GET /api/v1/jobs/{id}. Requires tenant admin.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselIds = new { type = "array", items = new { type = "string" }, description = "Vessel IDs to evaluate (default: all active vessels)" },
                        fleetId = new { type = "string", description = "Evaluate the active vessels of this fleet (ignored when vesselIds is set)" },
                        force = new { type = "boolean", description = "Force dependency and vulnerability checks even when fresh (default true)" }
                    }
                },
                async (args) =>
                {
                    EvaluateVesselHealthArgs request = args.HasValue
                        ? (JsonSerializer.Deserialize<EvaluateVesselHealthArgs>(args.Value, _JsonOptions) ?? new EvaluateVesselHealthArgs())
                        : new EvaluateVesselHealthArgs();
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    if (!auth.IsAdmin && !auth.IsTenantAdmin) return (object)McpToolError.Forbidden("Tenant admin permission is required to evaluate vessel health");
                    VesselHealthEvaluateRequest evaluate = new VesselHealthEvaluateRequest
                    {
                        VesselIds = request.VesselIds,
                        FleetId = request.FleetId,
                        Force = request.Force
                    };
                    try
                    {
                        return (object)await health.StartEvaluationAsync(auth.TenantId!, auth.UserId, evaluate, false).ConfigureAwait(false);
                    }
                    catch (System.Collections.Generic.KeyNotFoundException ex)
                    {
                        return (object)McpToolError.FromException(ex);
                    }
                });

            register(
                "set_vessel_health_override",
                "Set (or with remove=true, remove) a manual status override for one vessel health criterion, or for Overall. Effective statuses are recomputed immediately from stored findings. Requires tenant admin.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" },
                        criterion = new { type = "string", description = "GitDivergence, WorkingTree, Branches, CommitRecency, Dependencies, Vulnerabilities, TestInfrastructure, ContinuousIntegration, ArmadaReadiness, MissionOutcomes, or Overall" },
                        status = new { type = "string", description = "Pass, Warn, Fail, NotApplicable, or Unknown (required unless remove is true)" },
                        note = new { type = "string", description = "Optional note explaining the override" },
                        remove = new { type = "boolean", description = "Remove the override instead of setting it (default false)" }
                    },
                    required = new[] { "vesselId", "criterion" }
                },
                async (args) =>
                {
                    VesselHealthOverrideArgs request = JsonSerializer.Deserialize<VesselHealthOverrideArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize VesselHealthOverrideArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    if (!auth.IsAdmin && !auth.IsTenantAdmin) return (object)McpToolError.Forbidden("Tenant admin permission is required to change vessel health overrides");
                    if (!TryParseName(request.Criterion, out VesselHealthCriterionEnum criterion))
                        return (object)McpToolError.InvalidArgument("Unknown criterion '" + request.Criterion + "'");

                    VesselHealthDetail? detail;
                    if (request.Remove)
                    {
                        detail = await health.DeleteOverrideAsync(auth.TenantId!, request.VesselId, criterion).ConfigureAwait(false);
                    }
                    else
                    {
                        if (!TryParseName(request.Status, out VesselHealthStatusEnum status))
                            return (object)McpToolError.InvalidArgument("status must be Pass, Warn, Fail, NotApplicable, or Unknown");
                        VesselHealthOverrideRequest normalized = new VesselHealthOverrideRequest { Status = status, Note = request.Note };
                        detail = await health.SetOverrideAsync(auth.TenantId!, request.VesselId, criterion, status, normalized.Note, auth.UserId).ConfigureAwait(false);
                    }

                    if (detail == null) return (object)McpToolError.NotFound("Vessel not found");
                    return (object)new { Health = detail.Health, Overrides = detail.Overrides };
                });
        }

        private static bool TryParseName<TEnum>(string? value, out TEnum parsed) where TEnum : struct, Enum
        {
            parsed = default(TEnum);
            if (String.IsNullOrWhiteSpace(value)) return false;
            string trimmed = value.Trim();
            if (Char.IsDigit(trimmed[0]) || trimmed[0] == '-') return false;
            return Enum.TryParse<TEnum>(trimmed, true, out parsed) && Enum.IsDefined(typeof(TEnum), parsed);
        }
    }
}
