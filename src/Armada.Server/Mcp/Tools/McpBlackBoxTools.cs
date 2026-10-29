namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers the mission and voyage black box MCP tools (MISSION_BLACKBOX_PLAN.md section 8.2). Every tool calls only
    /// <see cref="IBlackBoxService"/> as the MCP caller, so tenant scope and visibility match REST: an entry the caller
    /// cannot see is NotFound (Code BlackBoxNotFound). Black box errors become typed tool errors with their code.
    /// </summary>
    public static class McpBlackBoxTools
    {
        #region Public-Members

        /// <summary>
        /// The read-only tools (also listed in the Ask Armada read-only allowlist).
        /// </summary>
        public static readonly IReadOnlyList<string> ReadTools = new List<string>
        {
            "get_blackbox", "read_blackbox_section", "list_blackbox_objects", "read_blackbox_object", "list_blackbox_entries",
            "search_blackbox", "list_blackbox_notes", "get_blackbox_bundle", "list_blackbox_hotspots", "list_blackbox_fingerprints",
            "get_blackbox_fingerprint_trend", "get_blackbox_retention", "get_blackbox_storage"
        };

        /// <summary>
        /// The safety guidance every black box action tool carries (plan section 9), also the body of the review_blackbox prompt.
        /// </summary>
        public const string SafetyGuidance = "Read the black box summary first. Quote evidence, not guesses. State the triage verdict and whether you agree. If you act, use only recovery paths or recommendations from the entry, do a dry run, tell the user what will happen, and confirm before running anything marked dataLossRisk. Annotate the entry with what you did.";

        #endregion

        #region Private-Members

        private const string UnavailableMessage = "The black box is not available on this server.";
        private const string TargetHint = "Name the entry with entryId (bbx_ prefix), or with missionId or voyageId for that mission's or voyage's current entry.";

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
        /// <param name="service">Black box service, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="register"/> is null.</exception>
        public static void Register(RegisterToolDelegate register, IBlackBoxService? service)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));
            RegisterReadTools(register, service);
            RegisterWriteTools(register, service);
        }

        /// <summary>
        /// Map a black box error to a typed tool error, or null when the exception is not a black box error:
        /// BlackBoxNotFound and BlackBoxObjectMissing are NotFound, BlackBoxStoreUnavailable is Unavailable, every other
        /// black box code (BlackBoxDeleting, BlackBoxObjectCorrupt, BlackBoxConfirmationRequired) is Conflict, and a
        /// wave-two operation that is not merged yet (<see cref="NotImplementedException"/>) is Unavailable with Code
        /// NotImplemented. Each carries its code in Code.
        /// </summary>
        /// <param name="ex">Exception.</param>
        /// <returns>The error, or null.</returns>
        public static McpToolError? ToToolError(Exception? ex)
        {
            if (ex == null) return null;
            if (ex is BlackBoxNotFoundException || ex is BlackBoxObjectMissingException) return McpToolError.NotFound(ex.Message, ((BlackBoxException)ex).Code);
            if (ex is BlackBoxStoreUnavailableException unavailable) return McpToolError.Unavailable(unavailable.Message, unavailable.Code);
            if (ex is BlackBoxException other) return McpToolError.Conflict(other.Message, other.Code);
            if (ex is NotImplementedException) return McpToolError.Unavailable(ex.Message, "NotImplemented");
            return null;
        }

        #endregion

        #region Private-Methods

        private static void RegisterReadTools(RegisterToolDelegate register, IBlackBoxService? service)
        {
            register(
                "get_blackbox",
                "Get the black box of a failed mission or voyage: start here when asked why something failed. " + TargetHint + " format summary (default, about 2 KB) returns where it failed (phase, step, host, source file and method), how (mechanism), status before and after, why (top findings), the triage verdict and confidence, and the top recovery paths; brief returns the Markdown FIX THIS brief for an audience (agent, user or maintainer); json returns the full record. Then read only the sections the question needs with read_blackbox_section and page evidence with read_blackbox_object. NotFound (Code BlackBoxNotFound) when there is no entry.",
                Schema(TargetProperties(new Dictionary<string, object>
                {
                    ["format"] = new { type = "string", @enum = new[] { "summary", "brief", "json" }, description = "summary (default), brief or json" },
                    ["audience"] = new { type = "string", @enum = new[] { "agent", "user", "maintainer" }, description = "Brief audience (format brief): agent (default), user or maintainer" },
                    ["redactHosts"] = new { type = "boolean", description = "Strip absolute paths, host names and user names from the brief (default false)" }
                })),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxGetArgs request = Read<BlackBoxGetArgs>(args);
                    string format = (request.Format ?? "summary").Trim().ToLowerInvariant();
                    if (format != "summary" && format != "brief" && format != "json") return McpToolError.InvalidArgument("format must be summary, brief or json.");
                    BlackBoxAudienceEnum audience = ParseAudience(request.Audience);
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    if (format == "json") return await svc.GetRecordAsync(caller, entryId).ConfigureAwait(false);
                    if (format == "brief")
                    {
                        string brief = await svc.GetBriefAsync(caller, entryId, audience, request.RedactHosts ?? false).ConfigureAwait(false);
                        return new BlackBoxBriefResult { EntryId = entryId, Audience = audience, Brief = brief };
                    }

                    return await svc.GetDigestAsync(caller, entryId).ConfigureAwait(false);
                }));

            register(
                "read_blackbox_section",
                "Read one section of a black box record: Subject, Status, Site, Mechanism, Classification, Workspace (dirty paths, checkout), Conflict (conflicted files and the missions that landed them), Scope, Configuration (each setting and where it came from), Attempts, Evidence (the object index), Analysis (findings, triage, recovery paths, recommendations), Related, Voyage, Redaction or Truncation. " + TargetHint,
                Schema(TargetProperties(new Dictionary<string, object>
                {
                    ["section"] = new { type = "string", description = "Section name (case-insensitive)" }
                }), "section"),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxSectionArgs request = Read<BlackBoxSectionArgs>(args);
                    if (!EnumNames.TryParse(request.Section, true, out BlackBoxSectionEnum section) || section == BlackBoxSectionEnum.Unknown)
                        return McpToolError.InvalidArgument("section must be one of Subject, Status, Site, Mechanism, Classification, Workspace, Conflict, Scope, Configuration, Attempts, Evidence, Analysis, Related, Voyage, Redaction or Truncation.");
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    return await svc.GetSectionAsync(caller, entryId, section).ConfigureAwait(false);
                }));

            register(
                "list_blackbox_objects",
                "List one page of a black box entry's objects (record.json, the briefs and evidence such as evidence/git-status.txt or evidence/diff.patch) with content type, sizes and SHA-256. Read one with read_blackbox_object. " + TargetHint,
                Schema(PageProperties(TargetProperties(new Dictionary<string, object>()))),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxPageArgs request = Read<BlackBoxPageArgs>(args);
                    EnumerationQuery query = PageQuery(request);
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    return await svc.EnumerateObjectsAsync(caller, entryId, query).ConfigureAwait(false);
                }));

            register(
                "read_blackbox_object",
                "Read a chunk of one black box object without flooding context: at most 64 KB per call (length 1 to 65536, default 65536) from offset (default 0). Returns content (UTF-8 text for text objects, base64 otherwise, cut at a character boundary), totalBytes, and nextOffset while more remains; call again with offset = nextOffset to page through a large log. " + TargetHint,
                Schema(TargetProperties(new Dictionary<string, object>
                {
                    ["name"] = new { type = "string", description = "Object name (for example evidence/git-status.txt)" },
                    ["offset"] = new { type = "integer", description = "Byte offset (default 0)" },
                    ["length"] = new { type = "integer", description = "Bytes to read (1-65536, default 65536)" }
                }), "name"),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxObjectReadArgs request = Read<BlackBoxObjectReadArgs>(args);
                    if (String.IsNullOrWhiteSpace(request.Name)) return McpToolError.InvalidArgument("name is required.");
                    if (request.Offset.HasValue && request.Offset.Value < 0) return McpToolError.InvalidArgument("offset must not be negative.");
                    if (request.Length.HasValue && (request.Length.Value < 1 || request.Length.Value > BlackBoxLimits.MaxMcpChunkBytes))
                        return McpToolError.InvalidArgument("length must be 1 to " + BlackBoxLimits.MaxMcpChunkBytes + ".");
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    return await svc.ReadObjectChunkAsync(caller, entryId, request.Name!, request.Offset ?? 0, request.Length).ConfigureAwait(false);
                }));

            register(
                "list_blackbox_entries",
                "List one page of black box entries visible to the caller, newest first, filtered by vesselId, voyageId, missionId, captainId, fleetId, phase, step, kind, triage, resolution, state, fingerprint (compare failures of the same mode), pinned, isCurrent, path (any file role) and createdAfter/createdBefore. Returns an enumeration result of entry summaries.",
                Schema(PageProperties(FilterProperties(new Dictionary<string, object>()))),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxEnumerateRequest request = Read<BlackBoxEnumerateRequest>(args);
                    return await svc.EnumerateAsync(caller, request).ConfigureAwait(false);
                }));

            register(
                "search_blackbox",
                "Full-text search over the reason, summary and notes of the black box entries visible to the caller (query 1 to 200 characters), with the same filters and paging as list_blackbox_entries. Returns an enumeration result of entry summaries.",
                Schema(PageProperties(FilterProperties(new Dictionary<string, object>
                {
                    ["query"] = new { type = "string", description = "Search text (1-200 characters)" }
                })), "query"),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxSearchRequest request = Read<BlackBoxSearchRequest>(args);
                    return await svc.SearchAsync(caller, request).ConfigureAwait(false);
                }));

            register(
                "list_blackbox_notes",
                "List one page of the notes on a black box entry (operator, agent, Ask Armada and system annotations, including what every recovery run did), newest first. " + TargetHint,
                Schema(PageProperties(TargetProperties(new Dictionary<string, object>()))),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxPageArgs request = Read<BlackBoxPageArgs>(args);
                    EnumerationQuery query = PageQuery(request);
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    return await svc.EnumerateNotesAsync(caller, entryId, query).ConfigureAwait(false);
                }));

            register(
                "get_blackbox_bundle",
                "Build a maintainer bundle: up to max (1-50, default 50) of the newest matching black box entries grouped by fingerprint, as one Markdown document of maintainer briefs (format Markdown, default) or JSON summaries. Use it to rank Armada improvements and write them as mission briefs on the Armada vessel. redactHosts strips absolute paths, host names and user names before sharing outside the installation.",
                Schema(FilterProperties(new Dictionary<string, object>
                {
                    ["max"] = new { type = "integer", description = "Entries (1-50, default 50)" },
                    ["format"] = new { type = "string", @enum = new[] { "Markdown", "Json" }, description = "Markdown (default) or Json" },
                    ["redactHosts"] = new { type = "boolean", description = "Strip absolute paths, host names and user names (default false)" }
                })),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxBundleRequest request = Read<BlackBoxBundleRequest>(args);
                    return await svc.GetBundleAsync(caller, request).ConfigureAwait(false);
                }));

            register(
                "list_blackbox_hotspots",
                "List one page of black box hotspots: directories that recur in failures (conflicted, dirty, out of scope), most entries first, with file and entry counts and when last seen. Filter by vesselId, role and createdAfter/createdBefore.",
                Schema(PageProperties(new Dictionary<string, object>
                {
                    ["vesselId"] = new { type = "string", description = "Vessel filter (vsl_ prefix)" },
                    ["role"] = new { type = "string", @enum = new[] { "Conflicted", "DirtyInCheckout", "OutOfScope", "ChangedByMission", "ChangedByConflictingMission", "BoundaryViolation" }, description = "File role filter" },
                    ["createdAfter"] = new { type = "string", description = "ISO 8601 timestamp (inclusive)" },
                    ["createdBefore"] = new { type = "string", description = "ISO 8601 timestamp (exclusive)" }
                })),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxHotspotEnumerateRequest request = Read<BlackBoxHotspotEnumerateRequest>(args);
                    return await svc.EnumerateHotspotsAsync(caller, request).ConfigureAwait(false);
                }));

            register(
                "list_blackbox_fingerprints",
                "List one page of black box failure modes (entries grouped by fingerprint) with count, open count, first and last seen, the newest entry and its phase, step and kind. Filter by vesselId, resolution and createdAfter/createdBefore. Compare one mode's entries with list_blackbox_entries {fingerprint}.",
                Schema(PageProperties(new Dictionary<string, object>
                {
                    ["vesselId"] = new { type = "string", description = "Vessel filter (vsl_ prefix)" },
                    ["resolution"] = new { type = "string", @enum = new[] { "Open", "Resolved", "Duplicate", "WontFix", "ArmadaFixed" }, description = "Resolution filter" },
                    ["createdAfter"] = new { type = "string", description = "ISO 8601 timestamp (inclusive)" },
                    ["createdBefore"] = new { type = "string", description = "ISO 8601 timestamp (exclusive)" }
                })),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxFingerprintEnumerateRequest request = Read<BlackBoxFingerprintEnumerateRequest>(args);
                    return await svc.EnumerateFingerprintsAsync(caller, request).ConfigureAwait(false);
                }));

            register(
                "get_blackbox_fingerprint_trend",
                "Get the trend of one black box failure mode: occurrences of the fingerprint per Armada version (ordered by first sighting, with open and ArmadaFixed counts; at most 1000 entries and 100 versions), to see whether a release fixed it.",
                Schema(new Dictionary<string, object>
                {
                    ["fingerprint"] = new { type = "string", description = "Failure fingerprint (from an entry or list_blackbox_fingerprints)" }
                }, "fingerprint"),
                (args) => Run(service, async svc =>
                {
                    BlackBoxFingerprintArgs request = Read<BlackBoxFingerprintArgs>(args);
                    if (String.IsNullOrWhiteSpace(request.Fingerprint)) return McpToolError.InvalidArgument("fingerprint is required.");
                    return await svc.GetFingerprintTrendAsync(McpToolHelpers.ResolveCallerContext(), request.Fingerprint!.Trim()).ConfigureAwait(false);
                }));

            register(
                "get_blackbox_retention",
                "Get the black box retention status: the policy, entry counts (total, pinned, deleting, expired), bytes, the next garbage collection run and the last run's report. Tenant admins see their tenant; global admins every tenant.",
                new { type = "object", properties = new { } },
                (args) => Run(service, async svc => await svc.GetRetentionStatusAsync(McpToolHelpers.ResolveCallerContext()).ConfigureAwait(false)));

            register(
                "get_blackbox_storage",
                "Get the black box storage status: the configured target, its health, whether writes fall back to the database, and bytes per target. Global admins only.",
                new { type = "object", properties = new { } },
                (args) => Run(service, async svc => await svc.GetStorageStatusAsync(McpToolHelpers.ResolveCallerContext()).ConfigureAwait(false)));
        }

        private static void RegisterWriteTools(RegisterToolDelegate register, IBlackBoxService? service)
        {
            register(
                "annotate_blackbox",
                "Add a note to a black box entry (body 1-8000 characters; recorded with authorKind Agent). Annotate what you found and what you did, so the next reader does not repeat it. " + TargetHint,
                Schema(TargetProperties(new Dictionary<string, object>
                {
                    ["body"] = new { type = "string", description = "Note text (1-8000 characters)" }
                }), "body"),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxNoteArgs request = Read<BlackBoxNoteArgs>(args);
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    BlackBoxNoteAddRequest note = new BlackBoxNoteAddRequest { Body = request.Body ?? String.Empty };
                    return await svc.AddNoteAsync(caller, entryId, note, BlackBoxNoteAuthorKindEnum.Agent).ConfigureAwait(false);
                }));

            register(
                "resolve_blackbox",
                "Set a black box entry's resolution (Open, Resolved, Duplicate, WontFix or ArmadaFixed) with an optional note (up to 2000 characters) and link (for example the landed commit or a pull request, up to 1000). " + TargetHint,
                Schema(TargetProperties(new Dictionary<string, object>
                {
                    ["resolution"] = new { type = "string", @enum = new[] { "Open", "Resolved", "Duplicate", "WontFix", "ArmadaFixed" }, description = "Resolution" },
                    ["note"] = new { type = "string", description = "Resolution note (optional)" },
                    ["link"] = new { type = "string", description = "Link (optional)" }
                }), "resolution"),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxResolveArgs request = Read<BlackBoxResolveArgs>(args);
                    if (!EnumNames.TryParse(request.Resolution, true, out BlackBoxResolutionEnum resolution) || resolution == BlackBoxResolutionEnum.Unknown)
                        return McpToolError.InvalidArgument("resolution must be Open, Resolved, Duplicate, WontFix or ArmadaFixed.");
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    BlackBoxResolveRequest body = new BlackBoxResolveRequest { Resolution = resolution, Note = request.Note, Link = request.Link };
                    return await svc.ResolveAsync(caller, entryId, body).ConfigureAwait(false);
                }));

            register(
                "run_blackbox_recovery",
                "Run one recovery path recorded in a black box entry's analysis (pathId, for example bbx-land-001.stash-retry). It never takes a free-form command: only the steps recorded in the entry run, each authorized as if you had called that tool directly. Always dry run first (dryRun defaults to true): the dry run returns the plan and a confirmToken (10 minutes, bound to the entry, the path and the mission's current version, so a mission that changed must be re-checked). Tell the user what will happen, then run with dryRun false and that confirmToken. A path marked dataLossRisk (deleting a branch, forcing a dock reclaim) is refused without acknowledgeDataLoss true; confirm with the user before setting it. Every dry run and run is annotated on the entry (authorKind Agent). A token is single use; a missing, expired, used or foreign token, a mission that changed, or a re-analyzed plan is Conflict with Code BlackBoxConfirmationRequired: dry run again. " + SafetyGuidance + " " + TargetHint,
                Schema(TargetProperties(new Dictionary<string, object>
                {
                    ["pathId"] = new { type = "string", description = "Recovery path id from the entry's analysis" },
                    ["dryRun"] = new { type = "boolean", description = "Dry run (default true): return the plan and a confirmToken without acting" },
                    ["confirmToken"] = new { type = "string", description = "Token from the dry run; required when dryRun is false" },
                    ["acknowledgeDataLoss"] = new { type = "boolean", description = "Required (true) for a path marked dataLossRisk, after the user confirmed" }
                }), "pathId"),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxRecoveryArgs request = Read<BlackBoxRecoveryArgs>(args);
                    if (String.IsNullOrWhiteSpace(request.PathId)) return McpToolError.InvalidArgument("pathId is required.");
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    BlackBoxRecoveryRunRequest body = new BlackBoxRecoveryRunRequest
                    {
                        DryRun = request.DryRun ?? true,
                        ConfirmToken = request.ConfirmToken,
                        AcknowledgeDataLoss = request.AcknowledgeDataLoss ?? false,
                        AuthorKind = BlackBoxNoteAuthorKindEnum.Agent
                    };
                    return await svc.RunRecoveryAsync(caller, entryId, request.PathId!.Trim(), body).ConfigureAwait(false);
                }));

            register(
                "apply_blackbox_recommendation",
                "Apply one configuration recommendation recorded in a black box entry's analysis (recommendationId) to prevent the failure next time; it runs only the recorded applyAction (for example an update_vessel patch), authorized as that tool. Same flow as run_blackbox_recovery: dry run first (dryRun defaults to true) to get the action and a confirmToken, tell the user what will change, then apply with dryRun false and the token. " + SafetyGuidance + " " + TargetHint,
                Schema(TargetProperties(new Dictionary<string, object>
                {
                    ["recommendationId"] = new { type = "string", description = "Recommendation id from the entry's analysis" },
                    ["dryRun"] = new { type = "boolean", description = "Dry run (default true)" },
                    ["confirmToken"] = new { type = "string", description = "Token from the dry run; required when dryRun is false" }
                }), "recommendationId"),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxRecommendationArgs request = Read<BlackBoxRecommendationArgs>(args);
                    if (String.IsNullOrWhiteSpace(request.RecommendationId)) return McpToolError.InvalidArgument("recommendationId is required.");
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    BlackBoxRecommendationApplyRequest body = new BlackBoxRecommendationApplyRequest { DryRun = request.DryRun ?? true, ConfirmToken = request.ConfirmToken, AuthorKind = BlackBoxNoteAuthorKindEnum.Agent };
                    return await svc.ApplyRecommendationAsync(caller, entryId, request.RecommendationId!.Trim(), body).ConfigureAwait(false);
                }));

            register(
                "pin_blackbox",
                "Pin (pinned true, the default) or unpin (pinned false) a black box entry. Garbage collection never deletes a pinned entry. " + TargetHint,
                Schema(TargetProperties(new Dictionary<string, object>
                {
                    ["pinned"] = new { type = "boolean", description = "True (default) to pin, false to unpin" }
                })),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxPinArgs request = Read<BlackBoxPinArgs>(args);
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    return await svc.PinAsync(caller, entryId, request.Pinned ?? true).ConfigureAwait(false);
                }));

            register(
                "delete_blackbox_entries",
                "Delete black box entries by id (entryIds, at most 1000; more is InvalidArgument with Code CollectionLimitExceeded before anything is deleted). Ids that are missing or not visible are skipped. Tenant admins and global admins only. This cannot be undone: pin entries you want to keep instead.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        entryIds = new { type = "array", items = new { type = "string" }, description = "Entries to delete (bbx_ prefix, at most 1000)" }
                    },
                    required = new[] { "entryIds" }
                },
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxDeleteArgs request = Read<BlackBoxDeleteArgs>(args);
                    BlackBoxDeleteManyRequest body = new BlackBoxDeleteManyRequest();
                    if (request.EntryIds != null) CollectionLimits.RequireRequestItems(request.EntryIds, "entryIds");
                    if (request.EntryIds != null) body.Ids.AddRange(request.EntryIds);
                    return await svc.DeleteManyAsync(caller, body).ConfigureAwait(false);
                }));

            register(
                "purge_blackbox",
                "Purge black box entries by filter (the list_blackbox_entries filters), oldest first, up to maxEntries (1-100000, default 10000). dryRun defaults to true and reports what would go (matched, a sample of ids, bytes) without deleting; pinned entries are skipped unless includePinned. Run the dry run, show the user the counts, and only then purge with dryRun false. Global admins only.",
                Schema(FilterProperties(new Dictionary<string, object>
                {
                    ["dryRun"] = new { type = "boolean", description = "Report without deleting (default true)" },
                    ["includePinned"] = new { type = "boolean", description = "Also delete pinned entries (default false)" },
                    ["maxEntries"] = new { type = "integer", description = "Most entries to delete (1-100000, default 10000)" }
                })),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxPurgeRequest request = Read<BlackBoxPurgeRequest>(args);
                    return await svc.PurgeAsync(caller, request).ConfigureAwait(false);
                }));

            register(
                "run_blackbox_gc",
                "Run one black box garbage collection pass now (expiry, quotas, cascades, retried deletes, orphan sweep) and return its report; dryRun true lists the candidates without deleting. Global admins only.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        dryRun = new { type = "boolean", description = "List the candidates without deleting (default false)" }
                    }
                },
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxGcArgs request = Read<BlackBoxGcArgs>(args);
                    return await svc.RunGcAsync(caller, new BlackBoxGcRunRequest { DryRun = request.DryRun ?? false }).ConfigureAwait(false);
                }));

            register(
                "export_blackbox_fixture",
                "Export a black box entry as a sanitized regression fixture (record without secrets, tenant and user ids, host paths or repository URL, plus the rule ids and triage the advisor produces now), to add under src/Test.Shared/Fixtures/BlackBox with a test. Global admins only. " + TargetHint,
                Schema(TargetProperties(new Dictionary<string, object>
                {
                    ["name"] = new { type = "string", description = "Fixture name (file name without .json; default the mission id)" }
                })),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxFixtureArgs request = Read<BlackBoxFixtureArgs>(args);
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    return await svc.ExportFixtureAsync(caller, entryId, String.IsNullOrWhiteSpace(request.Name) ? null : request.Name!.Trim()).ConfigureAwait(false);
                }));

            register(
                "reanalyze_blackbox",
                "Re-run enrichment and analysis of a black box entry with the current rules (rewrites record.json and the briefs). Tenant admins and global admins only. " + TargetHint,
                Schema(TargetProperties(new Dictionary<string, object>())),
                (args) => Run(service, async svc =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxTargetArgs request = Read<BlackBoxTargetArgs>(args);
                    string entryId = await ResolveAsync(svc, caller, request).ConfigureAwait(false);
                    return await svc.ReanalyzeAsync(caller, entryId).ConfigureAwait(false);
                }));
        }

        private static async Task<object> Run(IBlackBoxService? service, Func<IBlackBoxService, Task<object>> body)
        {
            if (service == null) return McpToolError.Unavailable(UnavailableMessage);
            try
            {
                return await body(service).ConfigureAwait(false);
            }
            catch (Exception ex) when (ToToolError(ex) != null)
            {
                return ToToolError(ex)!;
            }
        }

        private static Task<string> ResolveAsync(IBlackBoxService service, AuthContext caller, BlackBoxTargetArgs target)
        {
            if (String.IsNullOrWhiteSpace(target.EntryId) && String.IsNullOrWhiteSpace(target.MissionId) && String.IsNullOrWhiteSpace(target.VoyageId))
                throw new ArgumentException("Give an entryId, missionId or voyageId.");
            return service.ResolveEntryIdAsync(caller, target.MissionId, target.VoyageId, target.EntryId);
        }

        private static BlackBoxAudienceEnum ParseAudience(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return BlackBoxAudienceEnum.Agent;
            if (!EnumNames.TryParse(text.Trim(), true, out BlackBoxAudienceEnum audience) || audience == BlackBoxAudienceEnum.Unknown)
                throw new ArgumentException("audience must be agent, user or maintainer.");
            return audience;
        }

        private static EnumerationQuery PageQuery(BlackBoxPageArgs request)
        {
            EnumerationQuery query = new EnumerationQuery();
            if (request.PageNumber.HasValue) query.PageNumber = request.PageNumber.Value;
            if (request.PageSize.HasValue) query.PageSize = request.PageSize.Value;
            if (!String.IsNullOrWhiteSpace(request.Order))
            {
                if (!EnumNames.TryParse(request.Order.Trim(), true, out EnumerationOrderEnum order)) throw new ArgumentException("order must be CreatedDescending or CreatedAscending.");
                query.Order = order;
            }

            return query;
        }

        private static object Schema(Dictionary<string, object> properties, params string[] required)
        {
            if (required.Length == 0) return new { type = "object", properties = properties };
            return new { type = "object", properties = properties, required = required };
        }

        private static Dictionary<string, object> TargetProperties(Dictionary<string, object> extra)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["entryId"] = new { type = "string", description = "Black box entry ID (bbx_ prefix)" },
                ["missionId"] = new { type = "string", description = "Mission ID (msn_ prefix): its current entry" },
                ["voyageId"] = new { type = "string", description = "Voyage ID (vyg_ prefix): its current entry" }
            };
            foreach (KeyValuePair<string, object> kvp in extra) properties[kvp.Key] = kvp.Value;
            return properties;
        }

        private static Dictionary<string, object> PageProperties(Dictionary<string, object> properties)
        {
            properties["pageNumber"] = new { type = "integer", description = "Page number (default 1)" };
            properties["pageSize"] = new { type = "integer", description = "Page size (1-1000, default 10; out-of-range values are normalized)" };
            properties["order"] = new { type = "string", @enum = new[] { "CreatedDescending", "CreatedAscending" }, description = "Order by creation (default CreatedDescending)" };
            return properties;
        }

        private static Dictionary<string, object> FilterProperties(Dictionary<string, object> extra)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["vesselId"] = new { type = "string", description = "Vessel filter (vsl_ prefix)" },
                ["voyageId"] = new { type = "string", description = "Voyage filter (vyg_ prefix)" },
                ["missionId"] = new { type = "string", description = "Mission filter (msn_ prefix)" },
                ["captainId"] = new { type = "string", description = "Captain filter (cpt_ prefix)" },
                ["fleetId"] = new { type = "string", description = "Fleet filter (flt_ prefix)" },
                ["subjectType"] = new { type = "string", @enum = new[] { "Mission", "Voyage" }, description = "Subject type filter" },
                ["phase"] = new { type = "string", description = "Phase filter (Dispatch, Provision, Execution, Completion, Review, Handoff, Landing, MergeQueue, Recovery, Voyage, External)" },
                ["step"] = new { type = "string", description = "Step filter (BlackBoxStepEnum name, for example DirtyCheckout)" },
                ["kind"] = new { type = "string", description = "Failure kind filter (MissionFailureKindEnum name)" },
                ["triage"] = new { type = "string", @enum = new[] { "Unknown", "UserConfig", "MissionBrief", "Transient", "ArmadaDefect", "External" }, description = "Triage verdict filter" },
                ["resolution"] = new { type = "string", @enum = new[] { "Open", "Resolved", "Duplicate", "WontFix", "ArmadaFixed" }, description = "Resolution filter" },
                ["state"] = new { type = "string", @enum = new[] { "Captured", "Enriched", "Analyzed", "EnrichmentFailed", "ObjectMissing" }, description = "State filter" },
                ["fingerprint"] = new { type = "string", description = "Failure fingerprint (entries of the same failure mode)" },
                ["pinned"] = new { type = "boolean", description = "Pinned only (true) or unpinned only (false)" },
                ["isCurrent"] = new { type = "boolean", description = "Current entries only (true) or superseded only (false)" },
                ["path"] = new { type = "string", description = "Repository path recorded in any file role" },
                ["createdAfter"] = new { type = "string", description = "ISO 8601 timestamp (inclusive)" },
                ["createdBefore"] = new { type = "string", description = "ISO 8601 timestamp (exclusive)" }
            };
            foreach (KeyValuePair<string, object> kvp in extra) properties[kvp.Key] = kvp.Value;
            return properties;
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
