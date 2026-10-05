namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers MCP tools for reading captain-reported papercuts.
    /// </summary>
    public static class McpPapercutTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private const int _DefaultLimit = 25;
        private const int _MaxLimit = 200;
        private const int _DefaultScanLimit = 500;
        private const int _MaxScanLimit = 5000;

        /// <summary>
        /// Registers papercut MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for event data access.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database)
        {
            register(
                "papercut_summary",
                "List friction that captains reported during missions, collapsed into groups of the same vessel, category, and problem. Use the count and distinct-captain count to tell a one-off from a real defect, then promote a group to a backlog item or objective.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Filter to one vessel (vsl_ prefix)" },
                        category = new { type = "string", description = "Filter to one category: BriefContradiction, ToolFailure, MissingDoc, BrokenLink, RepoFriction, TestFlake, EnvSetup, PlatformBug, Other" },
                        minSeverity = new { type = "string", description = "Minimum severity: Low, Medium, or High" },
                        sinceHours = new { type = "integer", description = "Only include reports newer than this many hours" },
                        ungrouped = new { type = "boolean", description = "Return every report instead of groups (default false)" },
                        limit = new { type = "integer", description = "Maximum rows returned (default 25, maximum 200)" },
                        scanLimit = new { type = "integer", description = "Stored reports to scan before filtering (default 500, maximum 5000)" }
                    },
                    required = new string[] { }
                },
                async (args) =>
                {
                    PapercutListArgs request = args == null
                        ? new PapercutListArgs()
                        : JsonSerializer.Deserialize<PapercutListArgs>(args.Value, _JsonOptions) ?? new PapercutListArgs();

                    int scanLimit = Math.Clamp(request.ScanLimit ?? _DefaultScanLimit, 1, _MaxScanLimit);
                    int limit = Math.Clamp(request.Limit ?? _DefaultLimit, 1, _MaxLimit);

                    PapercutCategoryEnum? categoryFilter = null;
                    if (!String.IsNullOrWhiteSpace(request.Category))
                    {
                        PapercutCategoryEnum resolved = PapercutParser.ParseCategory(request.Category);

                        // ParseCategory falls back to Other, which would silently answer a misspelled
                        // filter with the wrong rows. An explicit filter must match an explicit name.
                        if (resolved == PapercutCategoryEnum.Other &&
                            !String.Equals(request.Category!.Trim(), "Other", StringComparison.OrdinalIgnoreCase))
                        {
                            return (object)McpToolError.InvalidArgument("Unknown category: " + request.Category);
                        }

                        categoryFilter = resolved;
                    }

                    PapercutSeverityEnum? severityFilter = null;
                    if (!String.IsNullOrWhiteSpace(request.MinSeverity))
                    {
                        PapercutSeverityEnum resolvedSeverity;
                        if (!Enum.TryParse<PapercutSeverityEnum>(request.MinSeverity!.Trim(), true, out resolvedSeverity))
                            return (object)McpToolError.InvalidArgument("Unknown severity: " + request.MinSeverity);

                        severityFilter = resolvedSeverity;
                    }

                    DateTime? since = null;
                    if (request.SinceHours.HasValue && request.SinceHours.Value > 0)
                        since = DateTime.UtcNow.AddHours(-request.SinceHours.Value);

                    List<ArmadaEvent> events = await database.Events
                        .EnumerateByTypeAsync(PapercutParser.EventType, scanLimit)
                        .ConfigureAwait(false);

                    // Papercut reports belong to the reporting mission's tenant and user. A global admin sees every
                    // report; everyone else only reports in their scope. Reports written before events carried a
                    // tenant are attributed through their vessel.
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    if (!McpCallerScope.IsGlobal(caller))
                    {
                        Dictionary<string, bool> vesselVisible = new Dictionary<string, bool>(StringComparer.Ordinal);
                        List<ArmadaEvent> visible = new List<ArmadaEvent>();
                        foreach (ArmadaEvent evt in events)
                        {
                            if (!String.IsNullOrEmpty(evt.TenantId))
                            {
                                if (McpCallerScope.CanAccess(caller, evt.TenantId, evt.UserId)) visible.Add(evt);
                                continue;
                            }

                            if (String.IsNullOrEmpty(evt.VesselId)) continue;
                            if (!vesselVisible.TryGetValue(evt.VesselId!, out bool canSee))
                            {
                                canSee = await McpCallerScope.ReadVesselAsync(database, caller, evt.VesselId).ConfigureAwait(false) != null;
                                vesselVisible[evt.VesselId!] = canSee;
                            }

                            if (canSee) visible.Add(evt);
                        }

                        events = visible;
                    }

                    List<Papercut> papercuts = new List<Papercut>();
                    foreach (ArmadaEvent evt in events)
                    {
                        Papercut? papercut = PapercutService.TryFromEvent(evt);
                        if (papercut == null) continue;
                        if (!String.IsNullOrWhiteSpace(request.VesselId) &&
                            !String.Equals(papercut.VesselId, request.VesselId, StringComparison.Ordinal)) continue;
                        if (categoryFilter.HasValue && papercut.Category != categoryFilter.Value) continue;
                        if (severityFilter.HasValue && papercut.Severity < severityFilter.Value) continue;
                        if (since.HasValue && papercut.ReportedUtc < since.Value) continue;
                        papercuts.Add(papercut);
                    }

                    if (request.Ungrouped == true)
                    {
                        return (object)new
                        {
                            Scanned = events.Count,
                            Matched = papercuts.Count,
                            Papercuts = papercuts
                                .OrderByDescending(p => p.ReportedUtc)
                                .Take(limit)
                                .ToList()
                        };
                    }

                    List<PapercutGroup> groups = PapercutService.Group(papercuts);
                    return (object)new
                    {
                        Scanned = events.Count,
                        Matched = papercuts.Count,
                        GroupCount = groups.Count,
                        Groups = groups.Take(limit).ToList()
                    };
                });
        }
    }
}
