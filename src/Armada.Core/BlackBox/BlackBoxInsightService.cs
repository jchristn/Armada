namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// The black box prevention, forensics and operations reads every surface calls (REST, MCP, clients): overlap
    /// preview, soft directory reservations, brief lint, configuration doctor, attempt diff, nearby successes, combined
    /// timeline, voyage cascade graph and recovery statistics. Visibility matches <see cref="BlackBoxService"/>: a global
    /// admin sees every tenant, a tenant admin the tenant, a user their own entries (and their own vessels and voyages);
    /// anything else reads as not found. Every collection returned is bounded and paged. Thread-safe.
    /// </summary>
    public class BlackBoxInsightService
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly BlackBoxPipeline _Pipeline;
        private readonly MissionOverlapAnalyzer _Analyzer;
        private readonly LoggingModule _Logging;

        private static readonly HashSet<MissionStatusEnum> _FailedStatuses = new HashSet<MissionStatusEnum> { MissionStatusEnum.Failed, MissionStatusEnum.LandingFailed };
        private static readonly HashSet<MissionStatusEnum> _StoppedStatuses = new HashSet<MissionStatusEnum> { MissionStatusEnum.Failed, MissionStatusEnum.LandingFailed, MissionStatusEnum.Cancelled };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="pipeline">Pipeline (record loading).</param>
        /// <param name="analyzer">Overlap analyzer.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxInsightService(DatabaseDriver database, ArmadaSettings settings, BlackBoxPipeline pipeline, MissionOverlapAnalyzer analyzer, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _Analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Preview the overlaps of a mission about to be dispatched to a vessel. Nothing is dispatched or changed.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="request">The mission (title, brief, dependency).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The preview.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not authenticated or has no tenant.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the vessel is not visible.</exception>
        /// <exception cref="ArgumentException">Thrown when neither a title nor a description is given.</exception>
        public async Task<MissionOverlapPreview> PreviewOverlapAsync(AuthContext caller, string vesselId, MissionOverlapPreviewRequest? request, CancellationToken token = default)
        {
            Vessel vessel = await ReadVesselAsync(caller, vesselId, token).ConfigureAwait(false);
            MissionOverlapPreviewRequest r = request ?? new MissionOverlapPreviewRequest();
            if (String.IsNullOrWhiteSpace(r.Title) && String.IsNullOrWhiteSpace(r.Description)) throw new ArgumentException("Give the mission's title or description.");
            return await _Analyzer.PreviewAsync(vessel, r.Title, r.Description, Blank(r.DependsOnMissionId), Blank(r.ExcludeMissionId), token).ConfigureAwait(false);
        }

        /// <summary>
        /// One page of a vessel's soft directory reservations (active missions whose scoped files sit in a hotspot
        /// directory), ordered by directory then mission id.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="query">Paging, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not authenticated or has no tenant.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the vessel is not visible.</exception>
        public async Task<VesselReservationPage> EnumerateReservationsAsync(AuthContext caller, string vesselId, EnumerationQuery? query, CancellationToken token = default)
        {
            Vessel vessel = await ReadVesselAsync(caller, vesselId, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            VesselReservationPage page = new VesselReservationPage();
            page.VesselId = vessel.Id;
            page.Threshold = _Settings.BlackBox.HotspotReservationThreshold;
            List<VesselReservation> all = new List<VesselReservation>();
            if (page.Threshold > 0)
            {
                VesselOverlapContext context = await _Analyzer.LoadAsync(vessel, null, token).ConfigureAwait(false);
                all = context.Reservations;
                page.HotDirectoryCount = context.Hotspots.Values.Count(h => h.EntryCount >= page.Threshold);
                page.ActiveMissionsScanned = context.ActiveMissions.Count;
                page.ScanTruncated = context.ActiveMissionsTruncated || context.HotspotsTruncated;
            }
            Fill(page, q, all);
            return page;
        }

        /// <summary>
        /// Lint a mission brief. Findings are warnings and never block a dispatch.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="request">Brief, and optionally the vessel for the path check.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not authenticated or has no tenant.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the vessel is given and not visible.</exception>
        /// <exception cref="ArgumentException">Thrown when neither a title nor a description is given.</exception>
        public async Task<BriefLintResult> LintBriefAsync(AuthContext caller, BriefLintRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            RequireCaller(caller);
            Vessel? vessel = String.IsNullOrWhiteSpace(request.VesselId) ? null : await ReadVesselAsync(caller, request.VesselId!, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(request.Title) && String.IsNullOrWhiteSpace(request.Description)) throw new ArgumentException("Give the mission's title or description.");
            VesselTrackedFiles? tracked = vessel != null ? await _Analyzer.TrackedFilesAsync(vessel, token).ConfigureAwait(false) : null;
            return MissionBriefLinter.Lint(request.Title, request.Description, Blank(request.DependsOnMissionId), tracked);
        }

        /// <summary>
        /// One page of a vessel's configuration doctor: configuration recommendations that recur across the vessel's black
        /// box entries in a window, grouped by rule and setting, most frequent first.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="windowDays">Window in days (1 to 365, default 30), or null.</param>
        /// <param name="query">Paging, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not authenticated or has no tenant.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the vessel is not visible.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when windowDays is outside 1 to 365.</exception>
        public async Task<BlackBoxDoctorPage> GetDoctorAsync(AuthContext caller, string vesselId, int? windowDays, EnumerationQuery? query, CancellationToken token = default)
        {
            Vessel vessel = await ReadVesselAsync(caller, vesselId, token).ConfigureAwait(false);
            int days = windowDays ?? BlackBoxInsightLimits.DefaultDoctorWindowDays;
            if (days < 1 || days > BlackBoxInsightLimits.MaxDoctorWindowDays) throw new ArgumentOutOfRangeException(nameof(windowDays), "windowDays must be 1 to " + BlackBoxInsightLimits.MaxDoctorWindowDays + ".");
            EnumerationQuery q = query ?? new EnumerationQuery();

            Dictionary<string, BlackBoxDoctorFinding> groups = new Dictionary<string, BlackBoxDoctorFinding>(StringComparer.Ordinal);
            BlackBoxEnumerateRequest request = new BlackBoxEnumerateRequest { VesselId = vessel.Id, CreatedAfter = DateTime.UtcNow.AddDays(-days), PageNumber = 1, PageSize = 100, Order = EnumerationOrderEnum.CreatedDescending };
            ScopeBase(caller, request);
            int scanned = 0;
            long inWindow = 0;
            while (scanned < BlackBoxInsightLimits.MaxDoctorScan)
            {
                EnumerationResult<BlackBoxEntry> page = await _Database.BlackBoxEntries.EnumerateAsync(request, token).ConfigureAwait(false);
                inWindow = page.TotalRecords;
                foreach (BlackBoxEntry entry in page.Objects)
                {
                    if (scanned >= BlackBoxInsightLimits.MaxDoctorScan) break;
                    scanned++;
                    BlackBoxRecord record = await _Pipeline.LoadRecordAsync(entry, token).ConfigureAwait(false);
                    foreach (BlackBoxConfigRecommendation rec in record.Analysis?.ConfigRecommendations ?? new List<BlackBoxConfigRecommendation>())
                    {
                        string key = (rec.RuleId ?? "?") + "/" + rec.Setting;
                        if (!groups.TryGetValue(key, out BlackBoxDoctorFinding? finding))
                        {
                            finding = new BlackBoxDoctorFinding
                            {
                                Key = key,
                                RuleId = rec.RuleId,
                                RecommendationId = rec.Id,
                                Setting = rec.Setting,
                                Scope = rec.Scope,
                                CurrentValue = rec.CurrentValue,
                                SuggestedValue = rec.SuggestedValue,
                                Rationale = BlackBoxLimits.Clip(rec.Rationale, BlackBoxLimits.MaxReasonExcerptChars),
                                LastSeenUtc = entry.CreatedUtc,
                                FirstSeenUtc = entry.CreatedUtc,
                                ExampleEntryId = entry.Id,
                                ExampleMissionId = entry.MissionId,
                                Applicable = rec.ApplyAction != null
                            };
                            groups[key] = finding;
                        }
                        finding.Count++;
                        if (entry.CreatedUtc < finding.FirstSeenUtc) finding.FirstSeenUtc = entry.CreatedUtc;
                        if (entry.CreatedUtc > finding.LastSeenUtc) finding.LastSeenUtc = entry.CreatedUtc;
                    }
                }
                if (page.Objects.Count < request.PageSize || request.PageNumber >= page.TotalPages) break;
                request.PageNumber++;
            }

            BlackBoxDoctorPage result = new BlackBoxDoctorPage();
            result.VesselId = vessel.Id;
            result.WindowDays = days;
            result.EntriesInWindow = inWindow;
            result.EntriesScanned = scanned;
            result.ScanTruncated = inWindow > scanned;
            List<BlackBoxDoctorFinding> ordered = groups.Values.OrderByDescending(g => g.Count).ThenByDescending(g => g.LastSeenUtc).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            Fill(result, q, ordered);
            return result;
        }

        /// <summary>
        /// Compare two black box entries: configuration, captain, runtime, model, base commit, changed files, failure kind
        /// and step.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">First entry.</param>
        /// <param name="otherEntryId">Other entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The diff.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not authenticated or has no tenant.</exception>
        /// <exception cref="BlackBoxNotFoundException">Thrown when either entry is not visible.</exception>
        /// <exception cref="ArgumentException">Thrown when both ids name the same entry.</exception>
        public async Task<BlackBoxAttemptDiff> DiffAsync(AuthContext caller, string entryId, string otherEntryId, CancellationToken token = default)
        {
            BlackBoxEntry a = await ReadVisibleEntryAsync(caller, entryId, token).ConfigureAwait(false);
            BlackBoxEntry b = await ReadVisibleEntryAsync(caller, otherEntryId, token).ConfigureAwait(false);
            if (String.Equals(a.Id, b.Id, StringComparison.Ordinal)) throw new ArgumentException("Give two different entries to compare.");
            BlackBoxRecord ra = await _Pipeline.LoadRecordAsync(a, token).ConfigureAwait(false);
            BlackBoxRecord rb = await _Pipeline.LoadRecordAsync(b, token).ConfigureAwait(false);

            List<BlackBoxDiffField> diffs = new List<BlackBoxDiffField>();
            void Compare(string section, string field, string? x, string? y)
            {
                if (String.Equals(x ?? String.Empty, y ?? String.Empty, StringComparison.Ordinal)) return;
                diffs.Add(new BlackBoxDiffField
                {
                    Section = section,
                    Field = field,
                    Value = BlackBoxLimits.Clip(x, BlackBoxInsightLimits.MaxDiffValueChars),
                    OtherValue = BlackBoxLimits.Clip(y, BlackBoxInsightLimits.MaxDiffValueChars)
                });
            }

            BlackBoxSubject sa = ra.Subject ?? new BlackBoxSubject();
            BlackBoxSubject sb = rb.Subject ?? new BlackBoxSubject();
            Compare("Subject", "CaptainId", a.CaptainId ?? sa.CaptainId, b.CaptainId ?? sb.CaptainId);
            Compare("Subject", "CaptainName", sa.CaptainName, sb.CaptainName);
            Compare("Subject", "CaptainRuntime", sa.CaptainRuntime, sb.CaptainRuntime);
            Compare("Subject", "CaptainModel", sa.CaptainModel, sb.CaptainModel);
            Compare("Subject", "ModelEndpointId", sa.ModelEndpointId, sb.ModelEndpointId);
            Compare("Subject", "Persona", sa.Persona, sb.Persona);
            Compare("Subject", "HarborId", a.HarborId ?? sa.HarborId, b.HarborId ?? sb.HarborId);
            Compare("Subject", "BranchName", sa.BranchName, sb.BranchName);
            Compare("Status", "StatusBefore", a.StatusBefore, b.StatusBefore);
            Compare("Status", "StatusAfter", a.StatusAfter, b.StatusAfter);
            Compare("Site", "Phase", a.Phase.ToString(), b.Phase.ToString());
            Compare("Site", "Step", a.Step.ToString(), b.Step.ToString());
            Compare("Site", "Host", ra.Site?.Host, rb.Site?.Host);
            Compare("Site", "Method", ra.Site?.Method, rb.Site?.Method);
            Compare("Site", "ArmadaVersion", a.ArmadaVersion ?? ra.Site?.ArmadaVersion, b.ArmadaVersion ?? rb.Site?.ArmadaVersion);
            Compare("Site", "ArmadaCommit", a.ArmadaCommit ?? ra.Site?.ArmadaCommit, b.ArmadaCommit ?? rb.Site?.ArmadaCommit);
            Compare("Classification", "FailureKind", a.FailureKind?.ToString(), b.FailureKind?.ToString());
            Compare("Classification", "SuspectedCause", ra.Classification?.SuspectedCause.ToString(), rb.Classification?.SuspectedCause.ToString());
            Compare("Classification", "Fingerprint", a.Fingerprint, b.Fingerprint);
            Compare("Classification", "Triage", a.Triage.ToString(), b.Triage.ToString());
            Compare("Workspace", "StartCommit", ra.Workspace?.StartCommit, rb.Workspace?.StartCommit);
            Compare("Workspace", "TargetTipAtStart", ra.Workspace?.TargetTipAtStart, rb.Workspace?.TargetTipAtStart);
            Compare("Workspace", "TargetTipAtFailure", ra.Workspace?.TargetTipAtFailure, rb.Workspace?.TargetTipAtFailure);
            Compare("Workspace", "BranchTip", ra.Workspace?.BranchTip, rb.Workspace?.BranchTip);
            Compare("Workspace", "MissionCommit", ra.Workspace?.MissionCommit, rb.Workspace?.MissionCommit);
            Compare("Workspace", "CommitsBehindTarget", Int(ra.Workspace?.CommitsBehindTarget), Int(rb.Workspace?.CommitsBehindTarget));
            Compare("Conflict", "ConflictedFileTotal", Int(ra.Conflict?.ConflictedFileTotal), Int(rb.Conflict?.ConflictedFileTotal));

            Dictionary<string, string?> ca = (ra.Configuration?.Values ?? new List<BlackBoxConfigValue>()).GroupBy(v => v.Setting, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
            Dictionary<string, string?> cb = (rb.Configuration?.Values ?? new List<BlackBoxConfigValue>()).GroupBy(v => v.Setting, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
            foreach (string setting in ca.Keys.Union(cb.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
            {
                ca.TryGetValue(setting, out string? x);
                cb.TryGetValue(setting, out string? y);
                Compare("Configuration", setting, x, y);
            }

            HashSet<string> fa = await ChangedFilesAsync(a, token).ConfigureAwait(false);
            HashSet<string> fb = await ChangedFilesAsync(b, token).ConfigureAwait(false);
            List<string> onlyA = fa.Where(f => !fb.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
            List<string> onlyB = fb.Where(f => !fa.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToList();

            BlackBoxAttemptDiff diff = new BlackBoxAttemptDiff();
            diff.EntryId = a.Id;
            diff.OtherEntryId = b.Id;
            diff.SameMission = a.MissionId != null && String.Equals(a.MissionId, b.MissionId, StringComparison.Ordinal);
            diff.Attempt = a.Attempt;
            diff.OtherAttempt = b.Attempt;
            diff.SameFingerprint = a.Fingerprint != null && String.Equals(a.Fingerprint, b.Fingerprint, StringComparison.Ordinal);
            diff.DifferenceCount = diffs.Count;
            diff.Differences = diffs.Take(BlackBoxInsightLimits.MaxDiffFields).ToList();
            diff.ChangedFilesKnown = fa.Count > 0 && fb.Count > 0;
            diff.FilesOnlyInEntryCount = onlyA.Count;
            diff.FilesOnlyInEntry = onlyA.Take(BlackBoxInsightLimits.MaxDiffFiles).ToList();
            diff.FilesOnlyInOtherCount = onlyB.Count;
            diff.FilesOnlyInOther = onlyB.Take(BlackBoxInsightLimits.MaxDiffFiles).ToList();
            diff.FilesInBothCount = fa.Count(f => fb.Contains(f));
            return diff;
        }

        /// <summary>
        /// One page of the successful landings near an entry's failure: same vessel, within blackBox.nearbySuccessWindowHours,
        /// with files the failure also touched, most overlap first.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry.</param>
        /// <param name="query">Paging, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not authenticated or has no tenant.</exception>
        /// <exception cref="BlackBoxNotFoundException">Thrown when the entry is not visible.</exception>
        public async Task<EnumerationResult<BlackBoxNearbySuccess>> EnumerateNearbySuccessesAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default)
        {
            BlackBoxEntry entry = await ReadVisibleEntryAsync(caller, entryId, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            List<BlackBoxNearbySuccess> all = await FindNearbySuccessesAsync(entry, caller.IsAdmin ? null : entry.TenantId, caller.IsAdmin || caller.IsTenantAdmin ? null : caller.UserId, token).ConfigureAwait(false);
            EnumerationResult<BlackBoxNearbySuccess> result = new EnumerationResult<BlackBoxNearbySuccess>();
            Fill(result, q, all);
            return result;
        }

        /// <summary>
        /// Successful landings near an entry's failure (no paging; at most <see cref="BlackBoxInsightLimits.MaxNearbyCandidates"/>
        /// records are compared). Used by the nearby-success enricher and the nearby-successes reads.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="tenantId">Tenant scope, or null.</param>
        /// <param name="userId">Owner scope, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The nearby successes, most overlap first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when entry is null.</exception>
        public async Task<List<BlackBoxNearbySuccess>> FindNearbySuccessesAsync(BlackBoxEntry entry, string? tenantId, string? userId, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (String.IsNullOrEmpty(entry.VesselId)) return new List<BlackBoxNearbySuccess>();
            HashSet<string> files = await EntryFilesAsync(entry, token).ConfigureAwait(false);
            if (files.Count == 0) return new List<BlackBoxNearbySuccess>();
            int hours = _Settings.BlackBox.NearbySuccessWindowHours;
            EnumerationResult<BlackBoxSuccess> candidates = await _Database.BlackBoxSuccesses.EnumerateByVesselAsync(entry.VesselId!, tenantId, userId,
                entry.CreatedUtc.AddHours(-hours), entry.CreatedUtc.AddHours(hours), new EnumerationQuery { PageNumber = 1, PageSize = BlackBoxInsightLimits.MaxNearbyCandidates }, token).ConfigureAwait(false);
            return Nearby(entry.CreatedUtc, files, candidates.Objects);
        }

        /// <summary>
        /// Match success records against a failure's files. Pure.
        /// </summary>
        /// <param name="failureUtc">When the failure was captured.</param>
        /// <param name="failureFiles">Files the failure touched.</param>
        /// <param name="candidates">Success records.</param>
        /// <returns>Overlapping successes, most overlap first, then closest in time.</returns>
        /// <exception cref="ArgumentNullException">Thrown when failureFiles or candidates is null.</exception>
        public static List<BlackBoxNearbySuccess> Nearby(DateTime failureUtc, ICollection<string> failureFiles, IEnumerable<BlackBoxSuccess> candidates)
        {
            if (failureFiles == null) throw new ArgumentNullException(nameof(failureFiles));
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            HashSet<string> failure = new HashSet<string>(failureFiles.Select(MissionFileSet.Normalize).Where(f => f.Length > 0), StringComparer.OrdinalIgnoreCase);
            List<BlackBoxNearbySuccess> found = new List<BlackBoxNearbySuccess>();
            foreach (BlackBoxSuccess s in candidates)
            {
                List<string> overlap = s.FilesChanged.Select(MissionFileSet.Normalize).Where(f => failure.Contains(f)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.Ordinal).ToList();
                if (overlap.Count == 0) continue;
                found.Add(new BlackBoxNearbySuccess
                {
                    SuccessId = s.Id,
                    MissionId = s.MissionId,
                    MissionTitle = s.MissionTitle,
                    LandedUtc = s.LandedUtc,
                    HoursFromFailure = Math.Round((s.LandedUtc - failureUtc).TotalHours, 2),
                    BaseCommit = s.BaseCommit,
                    TargetCommit = s.TargetCommit,
                    RunMs = s.RunMs,
                    OverlappingFileCount = overlap.Count,
                    OverlappingFiles = overlap.Take(BlackBoxInsightLimits.MaxWarningFiles).ToList()
                });
            }
            return found.OrderByDescending(n => n.OverlappingFileCount).ThenBy(n => Math.Abs(n.HoursFromFailure)).ThenBy(n => n.SuccessId, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// One page of an entry's combined timeline: the entry and its notes, the mission (or voyage) and its events, the
        /// dock, the merge entry, the captain's events in the mission's window and the Harbor link events, merged and
        /// ordered by time (query Order; CreatedAscending is oldest first). Each source is capped at 200 items and the
        /// timeline at 1000, with Truncated set when either cut applied.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="entryId">Entry.</param>
        /// <param name="query">Paging and order, or null (oldest first).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not authenticated or has no tenant.</exception>
        /// <exception cref="BlackBoxNotFoundException">Thrown when the entry is not visible.</exception>
        public async Task<BlackBoxTimelinePage> GetTimelineAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default)
        {
            BlackBoxEntry entry = await ReadVisibleEntryAsync(caller, entryId, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery { Order = EnumerationOrderEnum.CreatedAscending };
            int cap = BlackBoxInsightLimits.MaxTimelinePerSource;
            Dictionary<BlackBoxTimelineSourceEnum, BlackBoxTimelineSourceCount> counts = new Dictionary<BlackBoxTimelineSourceEnum, BlackBoxTimelineSourceCount>();
            List<BlackBoxTimelineItem> items = new List<BlackBoxTimelineItem>();
            HashSet<string> eventIds = new HashSet<string>(StringComparer.Ordinal);

            void Add(BlackBoxTimelineSourceEnum source, DateTime? when, string kind, string? message, string? entityId)
            {
                if (!when.HasValue) return;
                if (!counts.TryGetValue(source, out BlackBoxTimelineSourceCount? count))
                {
                    count = new BlackBoxTimelineSourceCount { Source = source };
                    counts[source] = count;
                }
                if (count.Count >= cap) { count.Truncated = true; return; }
                count.Count++;
                items.Add(new BlackBoxTimelineItem
                {
                    TimestampUtc = when.Value,
                    Source = source,
                    Kind = kind,
                    Message = BlackBoxLimits.Clip((message ?? String.Empty).Replace('\r', ' ').Replace('\n', ' '), BlackBoxLimits.MaxReasonExcerptChars) ?? String.Empty,
                    EntityId = entityId
                });
            }

            void AddEvents(BlackBoxTimelineSourceEnum source, IEnumerable<ArmadaEvent> events, DateTime? from, DateTime? to)
            {
                foreach (ArmadaEvent e in events.OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id, StringComparer.Ordinal))
                {
                    if (from.HasValue && e.CreatedUtc < from.Value) continue;
                    if (to.HasValue && e.CreatedUtc > to.Value) continue;
                    if (!eventIds.Add(e.Id)) continue;
                    Add(source, e.CreatedUtc, e.EventType, e.Message, e.Id);
                }
            }

            Add(BlackBoxTimelineSourceEnum.Entry, entry.CreatedUtc, "entry.captured", "Black box entry captured: " + entry.Phase + " / " + entry.Step + (entry.FailureKind.HasValue ? " (" + entry.FailureKind.Value + ")" : String.Empty), entry.Id);
            Add(BlackBoxTimelineSourceEnum.Entry, entry.EnrichedUtc, "entry.enriched", "Black box entry enriched", entry.Id);
            Add(BlackBoxTimelineSourceEnum.Entry, entry.AnalyzedUtc, "entry.analyzed", "Black box entry analyzed: triage " + entry.Triage, entry.Id);
            Add(BlackBoxTimelineSourceEnum.Entry, entry.ResolvedUtc, "entry.resolved", "Black box entry resolved: " + entry.Resolution + (entry.ResolutionNote != null ? " (" + entry.ResolutionNote + ")" : String.Empty), entry.Id);
            EnumerationResult<BlackBoxNote> notes = await _Database.BlackBoxNotes.EnumerateAsync(entry.Id, new EnumerationQuery { PageNumber = 1, PageSize = cap, Order = EnumerationOrderEnum.CreatedAscending }, token).ConfigureAwait(false);
            foreach (BlackBoxNote n in notes.Objects) Add(BlackBoxTimelineSourceEnum.Note, n.CreatedUtc, "note." + n.AuthorKind.ToString().ToLowerInvariant(), n.Body, n.Id);
            if (notes.TotalRecords > notes.Objects.Count) Truncate(counts, BlackBoxTimelineSourceEnum.Note);

            Mission? mission = entry.MissionId != null ? await _Database.Missions.ReadAsync(entry.MissionId, token).ConfigureAwait(false) : null;
            Voyage? voyage = entry.VoyageId != null ? await _Database.Voyages.ReadAsync(entry.VoyageId, token).ConfigureAwait(false) : null;
            DateTime windowStart = mission?.CreatedUtc ?? voyage?.CreatedUtc ?? entry.CreatedUtc;
            DateTime windowEnd = entry.LastUpdateUtc > entry.CreatedUtc ? entry.LastUpdateUtc : entry.CreatedUtc;
            DateTime activeEnd = entry.CreatedUtc.AddMinutes(10);

            if (mission != null)
            {
                Add(BlackBoxTimelineSourceEnum.Mission, mission.CreatedUtc, "mission.created", "Mission created: " + mission.Title, mission.Id);
                Add(BlackBoxTimelineSourceEnum.Mission, mission.StartedUtc, "mission.started", "Mission started", mission.Id);
                Add(BlackBoxTimelineSourceEnum.Mission, mission.CompletedUtc, "mission.finished", "Mission finished as " + mission.Status, mission.Id);
                List<ArmadaEvent> missionEvents = entry.TenantId != null
                    ? await _Database.Events.EnumerateByMissionAsync(entry.TenantId, mission.Id, cap, token).ConfigureAwait(false)
                    : await _Database.Events.EnumerateByMissionAsync(mission.Id, cap, token).ConfigureAwait(false);
                if (missionEvents.Count >= cap) Truncate(counts, BlackBoxTimelineSourceEnum.Mission);
                AddEvents(BlackBoxTimelineSourceEnum.Mission, missionEvents, null, null);
            }
            if (voyage != null)
            {
                Add(BlackBoxTimelineSourceEnum.Voyage, voyage.CreatedUtc, "voyage.created", "Voyage created: " + voyage.Title, voyage.Id);
                if (entry.SubjectType == BlackBoxSubjectTypeEnum.Voyage)
                {
                    List<ArmadaEvent> voyageEvents = entry.TenantId != null
                        ? await _Database.Events.EnumerateByVoyageAsync(entry.TenantId, voyage.Id, cap, token).ConfigureAwait(false)
                        : await _Database.Events.EnumerateByVoyageAsync(voyage.Id, cap, token).ConfigureAwait(false);
                    if (voyageEvents.Count >= cap) Truncate(counts, BlackBoxTimelineSourceEnum.Voyage);
                    AddEvents(BlackBoxTimelineSourceEnum.Voyage, voyageEvents, null, null);
                }
            }

            string? dockId = entry.DockId ?? mission?.DockId;
            if (!String.IsNullOrEmpty(dockId))
            {
                Dock? dock = await _Database.Docks.ReadAsync(dockId!, token).ConfigureAwait(false);
                if (dock != null)
                {
                    Add(BlackBoxTimelineSourceEnum.Dock, dock.CreatedUtc, "dock.created", "Dock provisioned" + (dock.BranchName != null ? " on branch " + dock.BranchName : String.Empty), dock.Id);
                    Add(BlackBoxTimelineSourceEnum.Dock, dock.LastUpdateUtc, "dock.updated", "Dock last updated", dock.Id);
                }
                List<ArmadaEvent> dockEvents = entry.TenantId != null
                    ? await _Database.Events.EnumerateByEntityAsync(entry.TenantId, "dock", dockId!, cap, token).ConfigureAwait(false)
                    : await _Database.Events.EnumerateByEntityAsync("dock", dockId!, cap, token).ConfigureAwait(false);
                AddEvents(BlackBoxTimelineSourceEnum.Dock, dockEvents, null, null);
            }

            if (!String.IsNullOrEmpty(entry.MergeEntryId))
            {
                MergeEntry? merge = await _Database.MergeEntries.ReadAsync(entry.MergeEntryId!, token).ConfigureAwait(false);
                if (merge != null)
                {
                    Add(BlackBoxTimelineSourceEnum.MergeEntry, merge.CreatedUtc, "merge.queued", "Merge entry queued for " + merge.TargetBranch, merge.Id);
                    Add(BlackBoxTimelineSourceEnum.MergeEntry, merge.TestStartedUtc, "merge.tests_started", "Merge queue tests started" + (merge.TestCommand != null ? ": " + merge.TestCommand : String.Empty), merge.Id);
                    Add(BlackBoxTimelineSourceEnum.MergeEntry, merge.CompletedUtc, "merge.finished", "Merge entry finished as " + merge.Status + (merge.TestExitCode.HasValue ? " (tests exited " + merge.TestExitCode.Value + ")" : String.Empty), merge.Id);
                }
                List<ArmadaEvent> mergeEvents = entry.TenantId != null
                    ? await _Database.Events.EnumerateByEntityAsync(entry.TenantId, "merge_entry", entry.MergeEntryId!, cap, token).ConfigureAwait(false)
                    : await _Database.Events.EnumerateByEntityAsync("merge_entry", entry.MergeEntryId!, cap, token).ConfigureAwait(false);
                AddEvents(BlackBoxTimelineSourceEnum.MergeEntry, mergeEvents, null, null);
            }

            string? captainId = entry.CaptainId ?? mission?.CaptainId;
            if (!String.IsNullOrEmpty(captainId))
            {
                List<ArmadaEvent> captainEvents = entry.TenantId != null
                    ? await _Database.Events.EnumerateByCaptainAsync(entry.TenantId, captainId!, cap, token).ConfigureAwait(false)
                    : await _Database.Events.EnumerateByCaptainAsync(captainId!, cap, token).ConfigureAwait(false);
                AddEvents(BlackBoxTimelineSourceEnum.Captain, captainEvents.Where(e => e.MissionId == null || e.MissionId == entry.MissionId), windowStart, activeEnd);
            }

            if (!String.IsNullOrEmpty(entry.HarborId))
            {
                HarborLinkEvent? before = await _Database.HarborLinkEvents.ReadLatestBeforeAsync(entry.HarborId!, windowStart, token).ConfigureAwait(false);
                if (before != null) Add(BlackBoxTimelineSourceEnum.Harbor, before.OccurredUtc, "harbor." + before.EventType.ToString().ToLowerInvariant(), "Harbor link " + before.EventType + " (state when the mission started)" + (before.Detail != null ? ": " + before.Detail : String.Empty), entry.HarborId);
                List<HarborLinkEvent> link = await _Database.HarborLinkEvents.EnumerateAsync(entry.HarborId!, windowStart, activeEnd, token).ConfigureAwait(false);
                if (link.Count > cap) Truncate(counts, BlackBoxTimelineSourceEnum.Harbor);
                foreach (HarborLinkEvent e in link.OrderByDescending(x => x.OccurredUtc).Take(cap).OrderBy(x => x.OccurredUtc))
                    Add(BlackBoxTimelineSourceEnum.Harbor, e.OccurredUtc, "harbor." + e.EventType.ToString().ToLowerInvariant(), "Harbor link " + e.EventType + (e.Detail != null ? ": " + e.Detail : String.Empty), entry.HarborId);
            }

            bool ascending = q.Order == EnumerationOrderEnum.CreatedAscending;
            List<BlackBoxTimelineItem> ordered = ascending
                ? items.OrderBy(i => i.TimestampUtc).ThenBy(i => (int)i.Source).ThenBy(i => i.EntityId ?? String.Empty, StringComparer.Ordinal).ThenBy(i => i.Kind, StringComparer.Ordinal).ToList()
                : items.OrderByDescending(i => i.TimestampUtc).ThenByDescending(i => (int)i.Source).ThenByDescending(i => i.EntityId ?? String.Empty, StringComparer.Ordinal).ThenByDescending(i => i.Kind, StringComparer.Ordinal).ToList();
            BlackBoxTimelinePage page = new BlackBoxTimelinePage();
            page.EntryId = entry.Id;
            page.WindowStartUtc = windowStart;
            page.WindowEndUtc = windowEnd;
            page.Sources = counts.Values.OrderBy(c => (int)c.Source).Take(BlackBoxInsightLimits.MaxTimelineSources).ToList();
            page.Truncated = counts.Values.Any(c => c.Truncated) || ordered.Count > BlackBoxInsightLimits.MaxTimelineItems;
            Fill(page, q, ordered.Take(BlackBoxInsightLimits.MaxTimelineItems).ToList());
            return page;
        }

        /// <summary>
        /// A voyage's cascade graph: its missions (status and black box entry, at most 200, oldest first) and the edges
        /// between them: DependsOn, CancelledBecause (a dependent stopped because its dependency failed), BlockedBy (a
        /// dependent held behind a failed dependency) and Sibling (the cascade root to the voyage's other failures).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="voyageId">Voyage.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The graph.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not authenticated or has no tenant.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the voyage is not visible.</exception>
        public async Task<BlackBoxCascadeGraph> GetVoyageCascadeAsync(AuthContext caller, string voyageId, CancellationToken token = default)
        {
            Voyage voyage = await ReadVoyageAsync(caller, voyageId, token).ConfigureAwait(false);
            EnumerationResult<Mission> page = await _Database.Missions.EnumerateAsync(new EnumerationQuery { VoyageId = voyage.Id, PageNumber = 1, PageSize = BlackBoxInsightLimits.MaxCascadeNodes, Order = EnumerationOrderEnum.CreatedAscending }, token).ConfigureAwait(false);
            List<Mission> missions = page.Objects.Where(m => String.Equals(m.VoyageId, voyage.Id, StringComparison.Ordinal)).ToList();
            return BuildCascade(voyage, missions, page.TotalRecords);
        }

        /// <summary>
        /// Build a cascade graph from a voyage and its missions. Pure.
        /// </summary>
        /// <param name="voyage">Voyage.</param>
        /// <param name="missions">Missions (at most 200 are used).</param>
        /// <param name="missionCount">Missions in the voyage.</param>
        /// <returns>The graph.</returns>
        /// <exception cref="ArgumentNullException">Thrown when voyage or missions is null.</exception>
        public static BlackBoxCascadeGraph BuildCascade(Voyage voyage, List<Mission> missions, long missionCount)
        {
            if (voyage == null) throw new ArgumentNullException(nameof(voyage));
            if (missions == null) throw new ArgumentNullException(nameof(missions));
            List<Mission> nodes = missions.OrderBy(m => m.CreatedUtc).ThenBy(m => m.Id, StringComparer.Ordinal).Take(BlackBoxInsightLimits.MaxCascadeNodes).ToList();
            Dictionary<string, Mission> byId = nodes.ToDictionary(m => m.Id, m => m, StringComparer.Ordinal);

            bool HasFailedAncestor(Mission m)
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                string? cursor = m.DependsOnMissionId;
                while (cursor != null && seen.Add(cursor) && byId.TryGetValue(cursor, out Mission? up))
                {
                    if (_FailedStatuses.Contains(up.Status)) return true;
                    cursor = up.DependsOnMissionId;
                }
                return false;
            }

            Mission? root = nodes.Where(m => _FailedStatuses.Contains(m.Status) && !HasFailedAncestor(m))
                .OrderBy(m => m.CompletedUtc ?? m.LastUpdateUtc).ThenBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault();

            BlackBoxCascadeGraph graph = new BlackBoxCascadeGraph();
            graph.VoyageId = voyage.Id;
            graph.VoyageTitle = BlackBoxLimits.Clip(voyage.Title, BlackBoxLimits.MaxTitleChars);
            graph.VoyageStatus = voyage.Status.ToString();
            graph.EntryId = voyage.LastBlackBoxEntryId;
            graph.RootMissionId = root?.Id;
            graph.RootEntryId = root?.LastBlackBoxEntryId;
            graph.MissionCount = Math.Max(missionCount, nodes.Count);
            graph.Nodes = nodes.Select(m => new BlackBoxCascadeNode
            {
                MissionId = m.Id,
                Title = BlackBoxLimits.Clip(m.Title, BlackBoxLimits.MaxTitleChars),
                Status = m.Status.ToString(),
                FailureKind = m.FailureKind?.ToString(),
                EntryId = m.LastBlackBoxEntryId,
                DependsOnMissionId = m.DependsOnMissionId,
                IsRoot = root != null && m.Id == root.Id
            }).ToList();

            List<BlackBoxCascadeEdge> edges = new List<BlackBoxCascadeEdge>();
            HashSet<string> downstreamOfRoot = new HashSet<string>(StringComparer.Ordinal);
            foreach (Mission m in nodes)
            {
                if (String.IsNullOrEmpty(m.DependsOnMissionId) || !byId.TryGetValue(m.DependsOnMissionId!, out Mission? dep)) continue;
                BlackBoxCascadeEdgeKindEnum kind = BlackBoxCascadeEdgeKindEnum.DependsOn;
                if (_StoppedStatuses.Contains(dep.Status) && _StoppedStatuses.Contains(m.Status)) kind = BlackBoxCascadeEdgeKindEnum.CancelledBecause;
                else if (_StoppedStatuses.Contains(dep.Status) && (m.Status == MissionStatusEnum.Pending || m.Status == MissionStatusEnum.Blocked)) kind = BlackBoxCascadeEdgeKindEnum.BlockedBy;
                edges.Add(new BlackBoxCascadeEdge { FromMissionId = dep.Id, ToMissionId = m.Id, Kind = kind });
            }
            if (root != null)
            {
                Queue<string> pending = new Queue<string>();
                pending.Enqueue(root.Id);
                while (pending.Count > 0)
                {
                    string current = pending.Dequeue();
                    foreach (Mission child in nodes.Where(n => n.DependsOnMissionId == current))
                    {
                        if (downstreamOfRoot.Add(child.Id)) pending.Enqueue(child.Id);
                    }
                }
                foreach (Mission m in nodes.Where(n => n.Id != root.Id && _StoppedStatuses.Contains(n.Status) && !downstreamOfRoot.Contains(n.Id) && String.IsNullOrEmpty(n.DependsOnMissionId)))
                    edges.Add(new BlackBoxCascadeEdge { FromMissionId = root.Id, ToMissionId = m.Id, Kind = BlackBoxCascadeEdgeKindEnum.Sibling });
            }
            graph.Truncated = missionCount > nodes.Count || edges.Count > BlackBoxInsightLimits.MaxCascadeEdges;
            graph.Edges = edges.Take(BlackBoxInsightLimits.MaxCascadeEdges).ToList();
            return graph;
        }

        /// <summary>
        /// One page of recovery path statistics over the caller's tracked runs (grouped and paged in the database), most
        /// runs first.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="ruleId">Rule filter, or null.</param>
        /// <param name="vesselId">Vessel filter, or null.</param>
        /// <param name="query">Paging, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not authenticated or has no tenant.</exception>
        public Task<EnumerationResult<BlackBoxRecoveryStat>> EnumerateRecoveryStatsAsync(AuthContext caller, string? ruleId, string? vesselId, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireCaller(caller);
            EnumerationQuery q = query ?? new EnumerationQuery();
            string? tenant = caller.IsAdmin ? null : caller.TenantId;
            string? user = caller.IsAdmin || caller.IsTenantAdmin ? null : caller.UserId;
            return _Database.BlackBoxRecoveryOutcomes.EnumerateStatsAsync(tenant, user, Blank(ruleId), Blank(vesselId), q, token);
        }

        #endregion

        #region Private-Methods

        private static void RequireCaller(AuthContext caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!caller.IsAuthenticated) throw new UnauthorizedAccessException("Authentication required.");
            if (!caller.IsAdmin && String.IsNullOrEmpty(caller.TenantId)) throw new UnauthorizedAccessException("A tenant is required.");
        }

        private static bool Visible(AuthContext caller, BlackBoxEntry entry)
        {
            if (caller.IsAdmin) return true;
            if (String.IsNullOrEmpty(entry.TenantId) || !String.Equals(entry.TenantId, caller.TenantId, StringComparison.Ordinal)) return false;
            if (caller.IsTenantAdmin) return true;
            return String.Equals(entry.UserId, caller.UserId, StringComparison.Ordinal);
        }

        private static void ScopeBase(AuthContext caller, EnumerationQuery query)
        {
            if (caller.IsAdmin) return;
            query.TenantId = caller.TenantId;
            if (!caller.IsTenantAdmin) query.UserId = caller.UserId;
        }

        private async Task<BlackBoxEntry> ReadVisibleEntryAsync(AuthContext caller, string entryId, CancellationToken token)
        {
            RequireCaller(caller);
            if (String.IsNullOrWhiteSpace(entryId)) throw new BlackBoxNotFoundException("Black box entry id is required.");
            BlackBoxEntry? entry = await _Database.BlackBoxEntries.ReadAsync(entryId.Trim(), caller.IsAdmin ? null : caller.TenantId, true, token).ConfigureAwait(false);
            if (entry == null || !Visible(caller, entry)) throw new BlackBoxNotFoundException("Black box entry " + entryId + " not found.");
            if (entry.State == BlackBoxStateEnum.Deleting) throw new BlackBoxDeletingException("Black box entry " + entryId + " is being deleted.");
            return entry;
        }

        private async Task<Vessel> ReadVesselAsync(AuthContext caller, string vesselId, CancellationToken token)
        {
            RequireCaller(caller);
            if (String.IsNullOrWhiteSpace(vesselId)) throw new KeyNotFoundException("Vessel not found.");
            string id = vesselId.Trim();
            Vessel? vessel = caller.IsAdmin
                ? await _Database.Vessels.ReadAsync(id, token).ConfigureAwait(false)
                : caller.IsTenantAdmin
                    ? await _Database.Vessels.ReadAsync(caller.TenantId!, id, token).ConfigureAwait(false)
                    : await _Database.Vessels.ReadAsync(caller.TenantId!, caller.UserId!, id, token).ConfigureAwait(false);
            return vessel ?? throw new KeyNotFoundException("Vessel " + id + " not found.");
        }

        private async Task<Voyage> ReadVoyageAsync(AuthContext caller, string voyageId, CancellationToken token)
        {
            RequireCaller(caller);
            if (String.IsNullOrWhiteSpace(voyageId)) throw new KeyNotFoundException("Voyage not found.");
            string id = voyageId.Trim();
            Voyage? voyage = caller.IsAdmin
                ? await _Database.Voyages.ReadAsync(id, token).ConfigureAwait(false)
                : caller.IsTenantAdmin
                    ? await _Database.Voyages.ReadAsync(caller.TenantId!, id, token).ConfigureAwait(false)
                    : await _Database.Voyages.ReadAsync(caller.TenantId!, caller.UserId!, id, token).ConfigureAwait(false);
            return voyage ?? throw new KeyNotFoundException("Voyage " + id + " not found.");
        }

        private async Task<HashSet<string>> ChangedFilesAsync(BlackBoxEntry entry, CancellationToken token)
        {
            EnumerationResult<BlackBoxFile> rows = await _Database.BlackBoxFiles.EnumerateAsync(entry.Id, BlackBoxFileRoleEnum.ChangedByMission, new EnumerationQuery { PageNumber = 1, PageSize = CollectionLimits.MaxPageSize }, token).ConfigureAwait(false);
            return new HashSet<string>(rows.Objects.Select(f => MissionFileSet.Normalize(f.Path)).Where(p => p.Length > 0), StringComparer.Ordinal);
        }

        private async Task<HashSet<string>> EntryFilesAsync(BlackBoxEntry entry, CancellationToken token)
        {
            EnumerationResult<BlackBoxFile> rows = await _Database.BlackBoxFiles.EnumerateAsync(entry.Id, null, new EnumerationQuery { PageNumber = 1, PageSize = CollectionLimits.MaxPageSize }, token).ConfigureAwait(false);
            return new HashSet<string>(rows.Objects.Where(f => f.Role != BlackBoxFileRoleEnum.ChangedByConflictingMission).Select(f => MissionFileSet.Normalize(f.Path)).Where(p => p.Length > 0), StringComparer.OrdinalIgnoreCase);
        }

        private static void Truncate(Dictionary<BlackBoxTimelineSourceEnum, BlackBoxTimelineSourceCount> counts, BlackBoxTimelineSourceEnum source)
        {
            if (!counts.TryGetValue(source, out BlackBoxTimelineSourceCount? count))
            {
                count = new BlackBoxTimelineSourceCount { Source = source };
                counts[source] = count;
            }
            count.Truncated = true;
        }

        private static void Fill<T>(EnumerationResult<T> page, EnumerationQuery query, List<T> all)
        {
            EnumerationResult<T> cut = EnumerationResult<T>.Page(query, all);
            page.PageNumber = cut.PageNumber;
            page.PageSize = cut.PageSize;
            page.TotalPages = cut.TotalPages;
            page.TotalRecords = cut.TotalRecords;
            page.Objects = cut.Objects;
        }

        private static string? Int(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
