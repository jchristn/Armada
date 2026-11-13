namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Registers the black box prevention, forensics and operations MCP tools (all read-only): preview_mission_overlap,
    /// list_vessel_reservations, lint_mission_brief, get_vessel_blackbox_doctor, diff_blackbox_entries,
    /// list_blackbox_nearby_successes, get_blackbox_timeline, get_voyage_blackbox_cascade and
    /// get_blackbox_recovery_stats. They call <see cref="BlackBoxInsightService"/> as the caller; without the service each
    /// answers a typed Unavailable error.
    /// </summary>
    public static class McpBlackBoxInsightTools
    {
        #region Private-Members

        private const string UnavailableMessage = "The black box is not available on this server.";

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the tools.
        /// </summary>
        /// <param name="register">Tool registration delegate.</param>
        /// <param name="insights">Insight service, or null (each tool then answers Unavailable).</param>
        /// <exception cref="ArgumentNullException">Thrown when register is null.</exception>
        public static void Register(RegisterToolDelegate register, BlackBoxInsightService? insights)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));
            object paging = new { type = "integer", description = "Page number (default 1)" };
            object size = new { type = "integer", description = "Page size, 1 to 1000 (default 10)" };

            register(
                "preview_mission_overlap",
                "Before dispatching a mission, preview what it would overlap on a vessel: its likely files (scoped-files directive plus repository paths in the brief that exist in the vessel), the active missions whose files it shares, black box conflict hotspots and directories reserved by active missions, strongest first (at most 50), the mission dispatch would chain it after when the vessel has serializeOverlappingMissions (suggestedDependsOnMissionId), and the brief lint findings. Read-only: nothing is dispatched. dispatch and create_mission return the same warnings as overlapWarnings.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" },
                        title = new { type = "string", description = "Mission title" },
                        description = new { type = "string", description = "Mission brief" },
                        dependsOnMissionId = new { type = "string", description = "Dependency the mission already has, if any (msn_ prefix)" },
                        excludeMissionId = new { type = "string", description = "Active mission to leave out of the comparison (msn_ prefix)" }
                    },
                    required = new[] { "vesselId" }
                },
                (args) => RunAsync(insights, args, (svc, caller, a) => Wrap(svc.PreviewOverlapAsync(caller, a.VesselId ?? String.Empty, new MissionOverlapPreviewRequest
                {
                    Title = a.Title,
                    Description = a.Description,
                    DependsOnMissionId = a.DependsOnMissionId,
                    ExcludeMissionId = a.ExcludeMissionId
                }))));

            register(
                "list_vessel_reservations",
                "List one page of a vessel's soft directory reservations: active missions whose scoped files sit in a directory the black box marks as a conflict hotspot (threshold blackBox.hotspotReservationThreshold; 0 turns reservations off). A mission dispatched into a reserved directory is warned, or chained after the reserving mission when the vessel has serializeOverlappingMissions. Ordered by directory then mission id; returns the page shape plus threshold, hotDirectoryCount, activeMissionsScanned and scanTruncated.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" },
                        pageNumber = paging,
                        pageSize = size
                    },
                    required = new[] { "vesselId" }
                },
                (args) => RunAsync(insights, args, (svc, caller, a) => Wrap(svc.EnumerateReservationsAsync(caller, a.VesselId ?? String.Empty, Page(a)))));

            register(
                "lint_mission_brief",
                "Lint a mission brief before dispatch: BRIEF-DEP-001 dependency language (\"after mission 2 lands\", \"depends on\", \"mission N\") without dependsOnMissionId, BRIEF-NOOP-001 a verification-only brief that is likely to fail as NoOp, BRIEF-PATH-001 a path whose top-level directory the vessel does not have (needs vesselId), BRIEF-PATH-002 a host or parent-relative path. At most 50 findings with a suggestion each. Warnings only; dispatch runs the same lint and never refuses a brief.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Mission title" },
                        description = new { type = "string", description = "Mission brief" },
                        vesselId = new { type = "string", description = "Vessel to check paths against (vsl_ prefix), optional" },
                        dependsOnMissionId = new { type = "string", description = "Dependency the mission has, if any (msn_ prefix)" }
                    }
                },
                (args) => RunAsync(insights, args, (svc, caller, a) => Wrap(svc.LintBriefAsync(caller, new BriefLintRequest
                {
                    Title = a.Title,
                    Description = a.Description,
                    VesselId = a.VesselId,
                    DependsOnMissionId = a.DependsOnMissionId
                }))));

            register(
                "get_vessel_blackbox_doctor",
                "Get a vessel's black box configuration doctor: the configuration recommendations that recur across its black box entries in a window (windowDays 1 to 365, default 30), one row per rule and setting with count, first and last seen, the suggested value and the newest entry as the example (apply it with apply_blackbox_recommendation). Most frequent first, paged; the newest 200 entries are read (entriesScanned, scanTruncated).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        vesselId = new { type = "string", description = "Vessel ID (vsl_ prefix)" },
                        windowDays = new { type = "integer", description = "Window in days, 1 to 365 (default 30)" },
                        pageNumber = paging,
                        pageSize = size
                    },
                    required = new[] { "vesselId" }
                },
                (args) => RunAsync(insights, args, (svc, caller, a) => Wrap(svc.GetDoctorAsync(caller, a.VesselId ?? String.Empty, a.WindowDays, Page(a)))));

            register(
                "diff_blackbox_entries",
                "Compare two black box entries (two attempts of a mission, or any two you can see): what differs in captain, runtime, model, persona, status, phase and step, Armada version, failure kind and fingerprint, base commit and target tips, every effective configuration value, and the files each mission changed. At most 100 differences and 200 files per side.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryId = new { type = "string", description = "Black box entry ID (bbx_ prefix)" },
                        otherEntryId = new { type = "string", description = "Other black box entry ID (bbx_ prefix)" }
                    },
                    required = new[] { "entryId", "otherEntryId" }
                },
                (args) => RunAsync(insights, args, (svc, caller, a) => Wrap(svc.DiffAsync(caller, a.EntryId ?? String.Empty, a.OtherEntryId ?? String.Empty))));

            register(
                "list_blackbox_nearby_successes",
                "List one page of the successful landings near a failure (success flight recorder): same vessel, within blackBox.nearbySuccessWindowHours of the failure, that changed files the failure also touched; most overlap first, then closest in time. Each has the mission, landed time, hours from the failure, base and landed commits, run time and the overlapping files.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryId = new { type = "string", description = "Black box entry ID (bbx_ prefix)" },
                        pageNumber = paging,
                        pageSize = size
                    },
                    required = new[] { "entryId" }
                },
                (args) => RunAsync(insights, args, (svc, caller, a) => Wrap(svc.EnumerateNearbySuccessesAsync(caller, a.EntryId ?? String.Empty, Page(a)))));

            register(
                "get_blackbox_timeline",
                "Get one page of a black box entry's combined timeline: the entry (captured, enriched, analyzed, resolved) and its notes, the mission or voyage and their events, the dock, the merge queue entry, the captain's events in the mission's window and the Harbor link events, in one order (oldest first unless order is CreatedDescending). Each source is capped at 200 items and the timeline at 1000; sources and truncated say where a cut applied.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryId = new { type = "string", description = "Black box entry ID (bbx_ prefix)" },
                        order = new { type = "string", description = "CreatedAscending (default, oldest first) or CreatedDescending" },
                        pageNumber = paging,
                        pageSize = size
                    },
                    required = new[] { "entryId" }
                },
                (args) => RunAsync(insights, args, (svc, caller, a) =>
                {
                    EnumerationQuery q = Page(a);
                    q.Order = String.Equals(a.Order?.Trim(), "CreatedDescending", StringComparison.OrdinalIgnoreCase) ? EnumerationOrderEnum.CreatedDescending : EnumerationOrderEnum.CreatedAscending;
                    return Wrap(svc.GetTimelineAsync(caller, a.EntryId ?? String.Empty, q));
                }));

            register(
                "get_voyage_blackbox_cascade",
                "Get a voyage's black box cascade graph: its missions (status, failure kind, current black box entry; at most 200) and the edges between them (DependsOn, CancelledBecause, BlockedBy, Sibling), with the cascade root (the earliest failure with no failed dependency) and its entry. Use it to see which failure took the others down.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        voyageId = new { type = "string", description = "Voyage ID (vyg_ prefix)" }
                    },
                    required = new[] { "voyageId" }
                },
                (args) => RunAsync(insights, args, (svc, caller, a) => Wrap(svc.GetVoyageCascadeAsync(caller, a.VoyageId ?? String.Empty))));

            register(
                "get_blackbox_recovery_stats",
                "Get one page of black box recovery path statistics over the confirmed recovery runs you can see: per rule and path, runs, succeeded (the mission completed afterwards), failed, pending, expired and successRate; most runs first. The advisor breaks ties between equally weighted recovery paths with these rates.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ruleId = new { type = "string", description = "Rule filter, for example BBX-LAND-001" },
                        vesselId = new { type = "string", description = "Vessel filter (vsl_ prefix)" },
                        pageNumber = paging,
                        pageSize = size
                    }
                },
                (args) => RunAsync(insights, args, (svc, caller, a) => Wrap(svc.EnumerateRecoveryStatsAsync(caller, a.RuleId, a.VesselId, Page(a)))));
        }

        #endregion

        #region Private-Methods

        private static async Task<object> RunAsync(BlackBoxInsightService? insights, JsonElement? args, Func<BlackBoxInsightService, AuthContext, BlackBoxInsightArgs, Task<object>> body)
        {
            if (insights == null) return McpToolError.Unavailable(UnavailableMessage);
            BlackBoxInsightArgs a;
            try
            {
                a = args.HasValue && args.Value.ValueKind == JsonValueKind.Object
                    ? (JsonSerializer.Deserialize<BlackBoxInsightArgs>(args.Value, _JsonOptions) ?? new BlackBoxInsightArgs())
                    : new BlackBoxInsightArgs();
            }
            catch (JsonException ex)
            {
                return McpToolError.InvalidArgument("Arguments are not valid: " + ex.Message);
            }
            AuthContext caller = McpToolHelpers.ResolveCallerContext();
            try
            {
                return await body(insights, caller, a).ConfigureAwait(false);
            }
            catch (BlackBoxNotFoundException ex)
            {
                return McpToolError.NotFound(ex.Message);
            }
            catch (BlackBoxDeletingException ex)
            {
                return McpToolError.Conflict(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return McpToolError.NotFound(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return McpToolError.InvalidArgument(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return McpToolError.Forbidden(ex.Message);
            }
        }

        private static async Task<object> Wrap<T>(Task<T> task)
        {
            T result = await task.ConfigureAwait(false);
            return result!;
        }

        private static EnumerationQuery Page(BlackBoxInsightArgs a)
        {
            EnumerationQuery q = new EnumerationQuery();
            if (a.PageNumber.HasValue) q.PageNumber = a.PageNumber.Value;
            if (a.PageSize.HasValue) q.PageSize = a.PageSize.Value;
            return q;
        }

        #endregion
    }
}
