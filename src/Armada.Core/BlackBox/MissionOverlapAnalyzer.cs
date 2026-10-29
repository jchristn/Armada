namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox.Enrichment;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Estimates the files a mission is likely to touch and compares them with the vessel's active missions, its black
    /// box hotspot directories and the soft reservations active missions hold on hot directories. A new mission's likely
    /// files are its scoped-files directive plus the repository paths its brief names that exist in the vessel (checked
    /// against the tracked files). An active mission adds the paths of its dock's recent path commits and its diff (or,
    /// for at most ten missions without one, its branch on the Admiral). Reads only; nothing is changed. Thread-safe.
    /// </summary>
    public class MissionOverlapAnalyzer
    {
        #region Public-Members

        /// <summary>
        /// How long a vessel's tracked-file list is reused. Default 5 minutes.
        /// </summary>
        public TimeSpan TrackedFilesLifetime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Test hook: supplies a vessel's tracked files instead of git (null result means unknown).
        /// </summary>
        public Func<Vessel, IReadOnlyList<string>?>? TrackedFilesOverride { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly string _Header = "[MissionOverlapAnalyzer] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly VesselGitInspector _Inspector;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, VesselTrackedFiles> _Tracked = new ConcurrentDictionary<string, VesselTrackedFiles>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="inspector">Host-aware git inspector.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public MissionOverlapAnalyzer(DatabaseDriver database, ArmadaSettings settings, VesselGitInspector inspector, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Preview the overlaps of a mission that is about to be dispatched.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="title">Mission title.</param>
        /// <param name="description">Mission brief.</param>
        /// <param name="dependsOnMissionId">The dependency already set, or null.</param>
        /// <param name="excludeMissionId">Active mission to leave out, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The preview.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vessel is null.</exception>
        public async Task<MissionOverlapPreview> PreviewAsync(Vessel vessel, string? title, string? description, string? dependsOnMissionId, string? excludeMissionId, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            VesselOverlapContext context = await LoadAsync(vessel, excludeMissionId, token).ConfigureAwait(false);
            MissionFileSet likely = LikelyFiles(title, description, context.Tracked, out int scoped, out int brief);
            MissionOverlapPreview preview = Compare(context, likely, dependsOnMissionId, null);
            preview.ScopedFileCount = scoped;
            preview.BriefPathCount = brief;
            preview.BriefLint = MissionBriefLinter.Lint(title, description, dependsOnMissionId, context.Tracked);
            return preview;
        }

        /// <summary>
        /// Read what the comparison needs about a vessel: tracked files, active missions with their likely files,
        /// hotspot directories and reservations.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="excludeMissionId">Active mission to leave out, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The context.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vessel is null.</exception>
        public async Task<VesselOverlapContext> LoadAsync(Vessel vessel, string? excludeMissionId, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            VesselOverlapContext context = new VesselOverlapContext(vessel);
            context.ReservationThreshold = _Settings.BlackBox.HotspotReservationThreshold;
            context.HotThreshold = context.ReservationThreshold > 0 ? context.ReservationThreshold : 3;
            context.Tracked = await TrackedFilesAsync(vessel, token).ConfigureAwait(false);

            List<Mission> active = await ReadActiveMissionsAsync(vessel, excludeMissionId, context, token).ConfigureAwait(false);
            int lookups = 0;
            foreach (Mission mission in active)
            {
                ActiveMissionFileSet set = new ActiveMissionFileSet(mission);
                foreach (string f in MissionService.ParseMissionScopedFiles(mission.Description ?? String.Empty))
                {
                    set.Scoped.AddFile(f);
                    set.Likely.AddFile(f);
                }
                AddBriefPaths(set.Likely, mission.Title, mission.Description, context.Tracked);
                Dock? dock = String.IsNullOrEmpty(mission.DockId) ? null : await _Database.Docks.ReadAsync(mission.DockId!, token).ConfigureAwait(false);
                foreach (string p in AnchorPaths(dock)) set.Likely.AddFile(p);
                if (!String.IsNullOrEmpty(mission.DiffSnapshot))
                {
                    foreach (string p in BlackBoxForensics.DiffPaths(mission.DiffSnapshot)) set.Likely.AddFile(p);
                }
                else if (!String.IsNullOrEmpty(mission.BranchName) && GitRevisionNames.IsSafeBranchName(mission.BranchName) && lookups < BlackBoxInsightLimits.MaxBranchLookups)
                {
                    lookups++;
                    foreach (string p in await BranchPathsAsync(vessel, dock, mission, token).ConfigureAwait(false)) set.Likely.AddFile(p);
                }
                context.ActiveMissions.Add(set);
            }

            await LoadHotspotsAsync(vessel, context, token).ConfigureAwait(false);
            BuildReservations(context);
            return context;
        }

        /// <summary>
        /// The likely files of a mission brief: its scoped-files directive plus the paths it names that exist in the
        /// vessel (files, or directories with a trailing slash in the result). Paths are only used when the tracked files
        /// are known.
        /// </summary>
        /// <param name="title">Title.</param>
        /// <param name="description">Brief.</param>
        /// <param name="tracked">Tracked files, or null.</param>
        /// <param name="scopedCount">Files from the scoped-files directive.</param>
        /// <param name="briefCount">Verified paths from the brief.</param>
        /// <returns>The set.</returns>
        public static MissionFileSet LikelyFiles(string? title, string? description, VesselTrackedFiles? tracked, out int scopedCount, out int briefCount)
        {
            MissionFileSet set = new MissionFileSet();
            HashSet<string> scoped = MissionService.ParseMissionScopedFiles(description ?? String.Empty);
            foreach (string f in scoped) set.AddFile(f);
            scopedCount = scoped.Count;
            briefCount = AddBriefPaths(set, title, description, tracked);
            return set;
        }

        /// <summary>
        /// Compare a mission's likely files with a loaded vessel context. Pure.
        /// </summary>
        /// <param name="context">Vessel context.</param>
        /// <param name="likely">The mission's likely files.</param>
        /// <param name="dependsOnMissionId">The dependency already set, or null.</param>
        /// <param name="missionId">The mission's id when known (set on each warning), or null.</param>
        /// <returns>The preview (without the brief lint).</returns>
        /// <exception cref="ArgumentNullException">Thrown when context or likely is null.</exception>
        public static MissionOverlapPreview Compare(VesselOverlapContext context, MissionFileSet likely, string? dependsOnMissionId, string? missionId)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (likely == null) throw new ArgumentNullException(nameof(likely));
            MissionOverlapPreview preview = new MissionOverlapPreview();
            preview.VesselId = context.Vessel.Id;
            preview.SerializeOverlappingMissions = context.Vessel.SerializeOverlappingMissions;
            preview.TrackedFilesKnown = context.Tracked != null;
            List<string> entries = likely.Entries.ToList();
            preview.LikelyFileCount = entries.Count;
            preview.LikelyFiles = entries.Take(BlackBoxInsightLimits.MaxLikelyFiles).ToList();
            preview.ActiveMissionsCompared = context.ActiveMissions.Count;
            preview.ActiveMissionsTruncated = context.ActiveMissionsTruncated;

            List<MissionOverlapWarning> warnings = new List<MissionOverlapWarning>();
            Dictionary<string, DateTime> created = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            if (!likely.IsEmpty)
            {
                foreach (ActiveMissionFileSet other in context.ActiveMissions)
                {
                    if (other.Likely.IsEmpty) continue;
                    List<string> overlap = likely.OverlapWith(other.Likely);
                    if (overlap.Count == 0) continue;
                    created[other.Mission.Id] = other.Mission.CreatedUtc;
                    warnings.Add(new MissionOverlapWarning
                    {
                        Kind = MissionOverlapKindEnum.ActiveMission,
                        MissionId = missionId,
                        OverlappingMissionId = other.Mission.Id,
                        OverlappingMissionTitle = BlackBoxLimits.Clip(other.Mission.Title, BlackBoxLimits.MaxTitleChars),
                        OverlappingMissionStatus = other.Mission.Status.ToString(),
                        FileCount = overlap.Count,
                        Files = overlap.Take(BlackBoxInsightLimits.MaxWarningFiles).ToList(),
                        Message = "Overlaps active mission " + other.Mission.Id + " (" + other.Mission.Status + ") on " + overlap.Count + " file" + (overlap.Count == 1 ? "" : "s") + ": " + String.Join(", ", overlap.Take(3)) + (overlap.Count > 3 ? ", ..." : "") + "."
                    });
                }

                HashSet<string> reservedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (VesselReservation reservation in context.Reservations)
                {
                    if (warnings.Any(w => w.Kind == MissionOverlapKindEnum.ActiveMission && w.OverlappingMissionId == reservation.MissionId)) continue;
                    List<string> inside = likely.EntriesIn(reservation.Directory);
                    if (inside.Count == 0 || !reservedSeen.Add(reservation.Directory + "|" + reservation.MissionId)) continue;
                    ActiveMissionFileSet? holder = context.ActiveMissions.FirstOrDefault(a => a.Mission.Id == reservation.MissionId);
                    if (holder != null) created[holder.Mission.Id] = holder.Mission.CreatedUtc;
                    warnings.Add(new MissionOverlapWarning
                    {
                        Kind = MissionOverlapKindEnum.Reservation,
                        MissionId = missionId,
                        OverlappingMissionId = reservation.MissionId,
                        OverlappingMissionTitle = reservation.MissionTitle,
                        OverlappingMissionStatus = reservation.MissionStatus,
                        Directory = reservation.Directory,
                        HotspotEntryCount = reservation.HotspotEntryCount,
                        FileCount = inside.Count,
                        Files = inside.Take(BlackBoxInsightLimits.MaxWarningFiles).ToList(),
                        Message = "Touches " + DirectoryLabel(reservation.Directory) + ", reserved by active mission " + reservation.MissionId + " (a conflict hotspot in " + reservation.HotspotEntryCount + " black box entries)."
                    });
                }

                foreach (BlackBoxHotspot hot in context.Hotspots.Values.Where(h => h.EntryCount >= context.HotThreshold).OrderByDescending(h => h.EntryCount).ThenBy(h => h.Directory, StringComparer.Ordinal))
                {
                    if (warnings.Any(w => w.Kind == MissionOverlapKindEnum.Reservation && String.Equals(w.Directory, hot.Directory, StringComparison.OrdinalIgnoreCase))) continue;
                    List<string> inside = likely.EntriesIn(hot.Directory);
                    if (inside.Count == 0) continue;
                    warnings.Add(new MissionOverlapWarning
                    {
                        Kind = MissionOverlapKindEnum.Hotspot,
                        MissionId = missionId,
                        Directory = hot.Directory,
                        HotspotEntryCount = hot.EntryCount,
                        FileCount = inside.Count,
                        Files = inside.Take(BlackBoxInsightLimits.MaxWarningFiles).ToList(),
                        Message = "Touches " + DirectoryLabel(hot.Directory) + ", a conflict hotspot in " + hot.EntryCount + " black box entries on this vessel."
                    });
                }
            }

            List<MissionOverlapWarning> ordered = warnings
                .OrderBy(w => w.Kind == MissionOverlapKindEnum.ActiveMission ? 0 : (w.Kind == MissionOverlapKindEnum.Reservation ? 1 : 2))
                .ThenByDescending(w => w.Kind == MissionOverlapKindEnum.Hotspot ? w.HotspotEntryCount : w.FileCount)
                .ThenBy(w => w.OverlappingMissionId ?? w.Directory ?? String.Empty, StringComparer.Ordinal)
                .ToList();
            preview.WarningCount = ordered.Count;
            preview.Warnings = ordered.Take(BlackBoxInsightLimits.MaxOverlapWarnings).ToList();

            if (String.IsNullOrWhiteSpace(dependsOnMissionId))
            {
                MissionOverlapWarning? chain = ordered
                    .Where(w => w.OverlappingMissionId != null && w.Kind != MissionOverlapKindEnum.Hotspot)
                    .OrderByDescending(w => w.FileCount)
                    .ThenByDescending(w => created.TryGetValue(w.OverlappingMissionId!, out DateTime c) ? c : DateTime.MinValue)
                    .ThenBy(w => w.OverlappingMissionId, StringComparer.Ordinal)
                    .FirstOrDefault();
                preview.SuggestedDependsOnMissionId = chain?.OverlappingMissionId;
            }
            return preview;
        }

        /// <summary>
        /// A vessel's tracked files at HEAD of its repository on the Admiral (cached for <see cref="TrackedFilesLifetime"/>),
        /// or null when the vessel has no local repository or git fails.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tracked files, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vessel is null.</exception>
        public async Task<VesselTrackedFiles?> TrackedFilesAsync(Vessel vessel, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (TrackedFilesOverride != null)
            {
                IReadOnlyList<string>? list = TrackedFilesOverride(vessel);
                return list == null ? null : VesselTrackedFiles.From(list);
            }
            if (_Tracked.TryGetValue(vessel.Id, out VesselTrackedFiles? cached) && DateTime.UtcNow - cached.ReadUtc < TrackedFilesLifetime) return cached;
            VesselRepositoryState repo = _Inspector.ResolveRepository(vessel, null);
            if (repo.Host == null || String.IsNullOrWhiteSpace(repo.Path)) return null;
            try
            {
                IReadOnlyList<string> files = await repo.Host.Git.ListTrackedFilesAsync(repo.Path, token).ConfigureAwait(false);
                VesselTrackedFiles tracked = VesselTrackedFiles.From(files);
                _Tracked[vessel.Id] = tracked;
                return tracked;
            }
            catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Debug(_Header + "could not list the tracked files of vessel " + vessel.Id + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Forget a vessel's cached tracked files.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        public void Invalidate(string vesselId)
        {
            if (!String.IsNullOrEmpty(vesselId)) _Tracked.TryRemove(vesselId, out _);
        }

        #endregion

        #region Private-Methods

        private static int AddBriefPaths(MissionFileSet set, string? title, string? description, VesselTrackedFiles? tracked)
        {
            if (tracked == null) return 0;
            int added = 0;
            foreach (string raw in GitAnchorInputs.ExtractPaths((title ?? String.Empty) + "\n" + (description ?? String.Empty), BlackBoxInsightLimits.MaxBriefPaths))
            {
                string p = MissionFileSet.Normalize(raw);
                if (p.Length == 0) continue;
                if (tracked.IsFile(p)) { set.AddFile(p); added++; }
                else if (tracked.IsDirectory(p)) { set.AddDirectory(p); added++; }
            }
            return added;
        }

        private static IEnumerable<string> AnchorPaths(Dock? dock)
        {
            if (dock == null || String.IsNullOrWhiteSpace(dock.GitAnchorsJson)) yield break;
            GitAnchorsSnapshot? anchors = null;
            try { anchors = JsonSerializer.Deserialize<GitAnchorsSnapshot>(dock.GitAnchorsJson!); }
            catch (JsonException) { anchors = null; }
            if (anchors == null) yield break;
            foreach (string line in anchors.RecentPathCommits.Take(BlackBoxInsightLimits.MaxBriefPaths))
            {
                int colon = (line ?? String.Empty).IndexOf(": ", StringComparison.Ordinal);
                string path = colon > 0 ? line!.Substring(0, colon) : String.Empty;
                if (path.Length > 0) yield return path;
            }
        }

        private async Task<List<Mission>> ReadActiveMissionsAsync(Vessel vessel, string? excludeMissionId, VesselOverlapContext context, CancellationToken token)
        {
            List<Mission> missions = new List<Mission>();
            foreach (MissionStatusEnum status in VesselGitInspector.ActiveMissionStatuses.OrderBy(s => (int)s))
            {
                if (missions.Count >= BlackBoxInsightLimits.MaxActiveMissions) { context.ActiveMissionsTruncated = true; break; }
                EnumerationQuery query = new EnumerationQuery { VesselId = vessel.Id, Status = status.ToString(), PageNumber = 1, PageSize = BlackBoxInsightLimits.MaxActiveMissions, Order = EnumerationOrderEnum.CreatedDescending };
                EnumerationResult<Mission> page = await _Database.Missions.EnumerateAsync(query, token).ConfigureAwait(false);
                if (page.TotalRecords > page.Objects.Count) context.ActiveMissionsTruncated = true;
                foreach (Mission m in page.Objects)
                {
                    if (m.Status != status) continue;
                    if (excludeMissionId != null && String.Equals(m.Id, excludeMissionId, StringComparison.Ordinal)) continue;
                    if (vessel.TenantId != null && m.TenantId != null && !String.Equals(m.TenantId, vessel.TenantId, StringComparison.Ordinal)) continue;
                    missions.Add(m);
                }
            }
            if (missions.Count > BlackBoxInsightLimits.MaxActiveMissions) context.ActiveMissionsTruncated = true;
            return missions.OrderByDescending(m => m.CreatedUtc).ThenByDescending(m => m.Id, StringComparer.Ordinal).Take(BlackBoxInsightLimits.MaxActiveMissions).ToList();
        }

        private async Task<List<string>> BranchPathsAsync(Vessel vessel, Dock? dock, Mission mission, CancellationToken token)
        {
            List<string> paths = new List<string>();
            VesselRepositoryState repo = _Inspector.ResolveRepository(vessel, dock);
            if (repo.Host == null || String.IsNullOrWhiteSpace(repo.Path)) return paths;
            string branch = mission.BranchName!;
            try
            {
                string? baseRef = mission.StartCommit;
                if (baseRef == null && !String.IsNullOrWhiteSpace(vessel.DefaultBranch))
                    baseRef = await repo.Host.Git.GetMergeBaseAsync(repo.Path, vessel.DefaultBranch, branch, token).ConfigureAwait(false);
                if (baseRef == null) return paths;
                GitCommitRange? range = await repo.Host.Git.GetCommitRangeAsync(repo.Path, baseRef, branch, 100, token).ConfigureAwait(false);
                if (range == null) return paths;
                return range.Commits.SelectMany(c => c.ChangedPaths).Distinct(StringComparer.Ordinal).Take(BlackBoxInsightLimits.MaxComparedFiles).ToList();
            }
            catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Debug(_Header + "could not read the branch of mission " + mission.Id + ": " + e.Message);
                return paths;
            }
        }

        private async Task LoadHotspotsAsync(Vessel vessel, VesselOverlapContext context, CancellationToken token)
        {
            BlackBoxHotspotEnumerateRequest request = new BlackBoxHotspotEnumerateRequest { VesselId = vessel.Id, TenantId = vessel.TenantId, PageNumber = 1, PageSize = BlackBoxInsightLimits.MaxHotspots };
            EnumerationResult<BlackBoxHotspot> page = await _Database.BlackBoxFiles.EnumerateHotspotsAsync(request, token).ConfigureAwait(false);
            if (page.TotalRecords > page.Objects.Count) context.HotspotsTruncated = true;
            foreach (BlackBoxHotspot h in page.Objects)
            {
                string dir = MissionFileSet.Normalize(h.Directory);
                if (!context.Hotspots.ContainsKey(dir)) context.Hotspots[dir] = h;
            }
        }

        private static void BuildReservations(VesselOverlapContext context)
        {
            context.Reservations.Clear();
            if (context.ReservationThreshold <= 0) return;
            foreach (ActiveMissionFileSet active in context.ActiveMissions)
            {
                if (active.Scoped.IsEmpty) continue;
                foreach (string dir in active.Scoped.FileDirectories())
                {
                    if (!context.Hotspots.TryGetValue(dir, out BlackBoxHotspot? hot) || hot.EntryCount < context.ReservationThreshold) continue;
                    context.Reservations.Add(new VesselReservation
                    {
                        VesselId = context.Vessel.Id,
                        Directory = dir,
                        MissionId = active.Mission.Id,
                        MissionTitle = BlackBoxLimits.Clip(active.Mission.Title, BlackBoxLimits.MaxTitleChars),
                        MissionStatus = active.Mission.Status.ToString(),
                        HotspotEntryCount = hot.EntryCount,
                        FileCount = active.Scoped.Files.Count(f => String.Equals(MissionFileSet.DirectoryOf(f), dir, StringComparison.OrdinalIgnoreCase)),
                        SinceUtc = active.Mission.StartedUtc ?? active.Mission.CreatedUtc
                    });
                }
            }
            List<VesselReservation> ordered = context.Reservations.OrderBy(r => r.Directory, StringComparer.Ordinal).ThenBy(r => r.MissionId, StringComparer.Ordinal).ToList();
            context.Reservations.Clear();
            context.Reservations.AddRange(ordered);
        }

        private static string DirectoryLabel(string directory)
        {
            return directory.Length == 0 ? "the repository root" : directory + "/";
        }

        #endregion
    }
}
