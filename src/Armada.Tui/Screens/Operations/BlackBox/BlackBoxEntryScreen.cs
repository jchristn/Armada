namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// One black box entry (<c>/blackbox/:id</c>): the Brief panel (a <see cref="BlackBoxFailurePanel"/> with copy,
    /// export, recovery, Ask Armada, JSON, resolve, pin, and note keys), the Objects panel (evidence objects; Enter opens
    /// the paged object viewer), the Notes panel, the Timeline panel (the combined timeline across the entry, its
    /// notes, mission, dock, merge entry, captain and Harbor, server paged; Enter opens the row's entity), the Diff
    /// panel (what changed against another attempt or entry; <c>D</c> chooses it, the previous attempt by default),
    /// and the Nearby Successes panel (landings on the same vessel around the failure that touched the same files,
    /// server paged; Enter opens the mission). Header actions open the mission and voyage and download the entry's
    /// .zip. Not thread-safe.
    /// </summary>
    public class BlackBoxEntryScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// Entry id from the route.
        /// </summary>
        public string EntryId { get; }

        /// <summary>
        /// Brief panel.
        /// </summary>
        public BlackBoxFailurePanel Brief { get; }

        /// <summary>
        /// Evidence objects panel.
        /// </summary>
        public ArmadaGrid<BlackBoxObjectSummary> Objects { get; }

        /// <summary>
        /// Notes panel.
        /// </summary>
        public OpsDocumentView Notes { get; } = new OpsDocumentView();

        /// <summary>
        /// Loaded notes (newest page).
        /// </summary>
        public List<BlackBoxNote> NoteRows { get; private set; } = new List<BlackBoxNote>();

        /// <summary>
        /// Combined timeline panel (server paged).
        /// </summary>
        public ArmadaGrid<BlackBoxTimelineItem> Timeline { get; }

        /// <summary>
        /// Attempt diff panel.
        /// </summary>
        public OpsDocumentView DiffView { get; } = new OpsDocumentView();

        /// <summary>
        /// Nearby successes panel (server paged).
        /// </summary>
        public ArmadaGrid<BlackBoxNearbySuccess> Nearby { get; }

        /// <summary>
        /// The entry the Diff panel compares against, or null before one is chosen.
        /// </summary>
        public string? CompareWithId { get; private set; } = null;

        /// <summary>
        /// The last attempt diff, or null.
        /// </summary>
        public BlackBoxAttemptDiff? AttemptDiff { get; private set; } = null;

        /// <summary>
        /// The other entries of the entry's mission (the Diff picker), newest first.
        /// </summary>
        public List<BlackBoxEntrySummary> OtherAttempts { get; private set; } = new List<BlackBoxEntrySummary>();

        /// <summary>
        /// Notes read per page.
        /// </summary>
        public const int NotePageSize = 50;

        /// <summary>
        /// Most entries the Diff picker lists.
        /// </summary>
        public const int AttemptPageSize = 50;

        #endregion

        #region Private-Members

        private bool _TimelineLoaded = false;
        private bool _NearbyLoaded = false;
        private bool _DiffDefaulted = false;
        private bool _DiffLoading = false;
        private string? _DiffError = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public BlackBoxEntryScreen(RouteMatch route, TuiContext context)
            : base(route, context, "BlackBoxEntryScreen", "Black Box Entry")
        {
            EntryId = route.Param("id") ?? "";
            Brief = new BlackBoxFailurePanel(this, BlackBoxPanelSourceEnum.Entry, () => EntryId);
            Brief.OffersOpen = false;
            Brief.Changed = LoadNotes;

            Objects = new ArmadaGrid<BlackBoxObjectSummary>(o => o.Name);
            Objects.MultiSelect = false;
            Objects.ModalHost = context.Modals;
            Objects.Dispatcher = context.Dispatcher;
            Objects.Loader = LoadObjectsAsync;
            Objects.EmptyText = "No evidence objects.";
            Objects.AddColumn(new GridColumn<BlackBoxObjectSummary>("name", "Name", o => o.Name) { Weight = 4 });
            Objects.AddColumn(new GridColumn<BlackBoxObjectSummary>("type", "Type", o => o.ContentType) { Weight = 2 });
            Objects.AddColumn(new GridColumn<BlackBoxObjectSummary>("size", "Size", o => BlackBoxText.Bytes(o.UncompressedBytes > 0 ? o.UncompressedBytes : o.SizeBytes)) { Width = 12 });
            Objects.AddColumn(new GridColumn<BlackBoxObjectSummary>("state", "State", o => Tr(o.IsMissing ? "Missing" : "Stored")) { Width = 10 });
            Objects.Activated += (s, o) => OpenObject(o);

            Timeline = new ArmadaGrid<BlackBoxTimelineItem>(TimelineKey);
            Timeline.MultiSelect = false;
            Timeline.ModalHost = context.Modals;
            Timeline.Dispatcher = context.Dispatcher;
            Timeline.Loader = LoadTimelineAsync;
            Timeline.PageSize = 25;
            Timeline.EmptyText = "No timeline events.";
            Timeline.AddColumn(new GridColumn<BlackBoxTimelineItem>("time", "Time", i => Context.Loc.FormatDateTime(i.TimestampUtc)) { Width = 20 });
            Timeline.AddColumn(new GridColumn<BlackBoxTimelineItem>("source", "Source", i => i.Source.ToString()) { Width = 11 });
            Timeline.AddColumn(new GridColumn<BlackBoxTimelineItem>("kind", "Kind", i => i.Kind) { Weight = 2 });
            Timeline.AddColumn(new GridColumn<BlackBoxTimelineItem>("message", "Message", i => i.Message) { Weight = 6 });
            Timeline.AddColumn(new GridColumn<BlackBoxTimelineItem>("entity", "Entity", i => i.EntityId ?? "-") { Weight = 2 });
            Timeline.Activated += (s, i) => OpenEntity(i.EntityId);

            Nearby = new ArmadaGrid<BlackBoxNearbySuccess>(n => n.SuccessId);
            Nearby.MultiSelect = false;
            Nearby.ModalHost = context.Modals;
            Nearby.Dispatcher = context.Dispatcher;
            Nearby.Loader = LoadNearbyAsync;
            Nearby.PageSize = 25;
            Nearby.EmptyText = "No successful landings near this failure touched the same files.";
            Nearby.AddColumn(new GridColumn<BlackBoxNearbySuccess>("landed", "Landed", n => Context.Loc.FormatDateTime(n.LandedUtc)) { Width = 20 });
            Nearby.AddColumn(new GridColumn<BlackBoxNearbySuccess>("mission", "Mission", n => (String.IsNullOrEmpty(n.MissionTitle) ? "" : n.MissionTitle + "  ") + n.MissionId) { Weight = 4 });
            Nearby.AddColumn(new GridColumn<BlackBoxNearbySuccess>("hours", "From failure", n => (n.HoursFromFailure >= 0 ? "+" : "") + n.HoursFromFailure.ToString("0.0", CultureInfo.InvariantCulture) + " h") { Width = 13 });
            Nearby.AddColumn(new GridColumn<BlackBoxNearbySuccess>("files", "Same files", n => n.OverlappingFileCount.ToString(CultureInfo.InvariantCulture)) { Width = 10 });
            Nearby.AddColumn(new GridColumn<BlackBoxNearbySuccess>("base", "Base", n => Short(n.BaseCommit)) { Width = 10 });
            Nearby.Activated += (s, n) => OpenEntity(n.MissionId);

            Action("mission", "Open Mission", () => Context.Navigate("/missions/" + Uri.EscapeDataString(Brief.Entry!.MissionId!)), "m", () => Brief.Entry != null && !String.IsNullOrEmpty(Brief.Entry.MissionId), true);
            Action("voyage", "Open Voyage", () => Context.Navigate("/voyages/" + Uri.EscapeDataString(Brief.Entry!.VoyageId!)), "V", () => Brief.Entry != null && !String.IsNullOrEmpty(Brief.Entry.VoyageId), true);
            Action("download", "Download .zip", Download, "w", () => Brief.Entry != null, true);
            Action("same-fingerprint", "Similar failures", () => Context.Navigate(TabLinks.BlackBox(Brief.Entry!.Fingerprint)), "f", () => Brief.Entry != null && !String.IsNullOrEmpty(Brief.Entry.Fingerprint));
            Action("copy-id", "Copy ID", () => Copy(EntryId, "Black box entry ID"), "y");
            Action("delete", "Delete", Delete, "del", () => Brief.Entry != null, false, true);
            MissionScreen.AddFailureActions(this, Brief, "brief");
            OpsScreenAction compare = Action("compare", "Compare with another attempt", ChooseComparison, "D", () => Brief.Entry != null);
            compare.Panel = "diff";
            PanelAction("timeline", "timeline-open", "Open timeline entity", () => OpenEntity(Timeline.Current?.EntityId), null, () => EntityRouteOf(Timeline.Current?.EntityId) != null);
            PanelHint("timeline", "Enter", "Open");
            PanelHint("timeline", "< >", "Page");
            PanelHint("nearby", "Enter", "Open mission");
            PanelHint("nearby", "< >", "Page");
            DiffView.Builder = BuildDiff;
            DiffView.Empty = "Loading...";

            Notes.Builder = BuildNotes;
            Notes.Empty = "No notes.";
            AddPanel("brief", "Brief", Brief);
            AddPanel("objects", "Objects", Objects);
            AddPanel("notes", "Notes", Notes);
            AddPanel("timeline", "Timeline", Timeline);
            AddPanel("diff", "Diff", DiffView);
            AddPanel("nearby", "Nearby Successes", Nearby);
            Tabs.SelectedChanged += (s, e) => EnsurePanel();

            SubscribeCoalesced("blackbox.", Load);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Load()
        {
            bool initial = !Loaded;
            Call((c, t) => c.GetBlackBoxAsync(EntryId, false, t), e =>
            {
                if (e == null)
                {
                    if (initial) LoadError = Tr("Black box entry not found.");
                    return;
                }

                Loaded = true;
                LoadError = null;
                Heading = String.IsNullOrEmpty(e.MissionTitle) ? e.Id : e.MissionTitle;
                Status = e.Resolution.ToString();
                SubtitleText = Tr("Black Box") + " > " + BlackBoxText.Where(e.Phase, e.Step) + "   " + e.Id;
                Brief.Reload();
                Objects.Reload();
                LoadNotes();
                if (_TimelineLoaded) Timeline.Reload();
                if (_NearbyLoaded) Nearby.Reload();
            }, null, ex =>
            {
                if (!initial) return;
                LoadError = ex is Armada.Client.ArmadaApiException api && api.StatusCode == 404
                    ? Tr("Black box entry not found.")
                    : Tr("Failed to load the black box entry: {{message}}", LocalizationArgs.Of("message", ex.Message));
            });
        }

        /// <summary>
        /// Open the paged viewer for an object.
        /// </summary>
        /// <param name="obj">Object.</param>
        /// <returns>The viewer, or null for a missing object.</returns>
        public BlackBoxObjectView? OpenObject(BlackBoxObjectSummary obj)
        {
            if (obj == null) return null;
            if (obj.IsMissing)
            {
                Toast(NotificationSeverityEnum.Warning, Tr("The object is missing from storage (BlackBoxObjectMissing)."));
                return null;
            }

            return BlackBoxObjectView.Open(this, EntryId, obj.Name);
        }

        /// <summary>
        /// Compare this entry with another (the Diff panel) and select the panel.
        /// </summary>
        /// <param name="otherEntryId">Other entry id.</param>
        public void CompareWith(string otherEntryId)
        {
            string other = (otherEntryId ?? "").Trim();
            if (other.Length == 0) return;
            if (other == EntryId)
            {
                Toast(NotificationSeverityEnum.Warning, Tr("Choose a different entry to compare with."));
                return;
            }

            CompareWithId = other;
            AttemptDiff = null;
            _DiffError = null;
            _DiffLoading = true;
            DiffView.Invalidate();
            if (CurrentPanelKey != "diff") SelectPanel("diff");
            Call((c, t) => c.DiffBlackBoxEntriesAsync(EntryId, other, t), d =>
            {
                if (CompareWithId != other) return;
                _DiffLoading = false;
                AttemptDiff = d;
                DiffView.Invalidate();
            }, null, ex =>
            {
                if (CompareWithId != other) return;
                _DiffLoading = false;
                _DiffError = ex is Armada.Client.ArmadaApiException api && api.StatusCode == 404
                    ? Tr("Entry {{id}} was not found.", LocalizationArgs.Of("id", other))
                    : Tr("The diff could not be loaded: {{message}}", LocalizationArgs.Of("message", ex.Message));
                DiffView.Invalidate();
            });
        }

        /// <summary>
        /// The Diff picker: the mission's other attempts (newest first) and "Enter an entry id".
        /// </summary>
        public void ChooseComparison()
        {
            LoadAttempts(() =>
            {
                List<ActionMenuItem> items = new List<ActionMenuItem>();
                foreach (BlackBoxEntrySummary e in OtherAttempts)
                {
                    BlackBoxEntrySummary entry = e;
                    string label = Tr("Attempt {{n}}", LocalizationArgs.Of("n", entry.Attempt)) + "  " + Context.Loc.FormatDateTime(entry.CreatedUtc) + "  " + BlackBoxText.Where(entry.Phase, entry.Step)
                        + "  " + (entry.FailureKind?.ToString() ?? "-") + "  " + entry.Id;
                    items.Add(new ActionMenuItem(label, () => CompareWith(entry.Id), ""));
                }

                items.Add(new ActionMenuItem(Tr("Enter an entry id..."), PromptForEntry, ""));
                ShowMenu("Compare With", items);
            });
        }

        #endregion

        #region Private-Methods

        private static string TimelineKey(BlackBoxTimelineItem i)
        {
            return i.TimestampUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + i.Source + "|" + i.Kind + "|" + (i.EntityId ?? "") + "|" + i.Message.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }

        private static string Short(string? commit)
        {
            if (String.IsNullOrEmpty(commit)) return "-";
            return commit!.Length > 8 ? commit.Substring(0, 8) : commit;
        }

        private static string? EntityRouteOf(string? id)
        {
            return BlackBoxInsightText.EntityRoute(id);
        }

        private void OpenEntity(string? id)
        {
            string? route = EntityRouteOf(id);
            if (route == null)
            {
                if (!String.IsNullOrEmpty(id)) Toast(NotificationSeverityEnum.Info, Tr("No screen opens {{id}}.", LocalizationArgs.Of("id", id)));
                return;
            }

            if (id == EntryId)
            {
                SelectPanel("brief");
                return;
            }

            Context.Navigate(route);
        }

        private void EnsurePanel()
        {
            string? key = CurrentPanelKey;
            if (key == "timeline" && !_TimelineLoaded)
            {
                _TimelineLoaded = true;
                Timeline.Reload();
            }
            else if (key == "nearby" && !_NearbyLoaded)
            {
                _NearbyLoaded = true;
                Nearby.Reload();
            }
            else if (key == "diff" && CompareWithId == null && !_DiffDefaulted && Brief.Entry != null)
            {
                _DiffDefaulted = true;
                _DiffLoading = true;
                DiffView.Invalidate();
                LoadAttempts(() =>
                {
                    _DiffLoading = false;
                    BlackBoxEntrySummary? previous = OtherAttempts.Where(a => Brief.Entry == null || a.Attempt < Brief.Entry.Attempt).OrderByDescending(a => a.Attempt).FirstOrDefault()
                        ?? OtherAttempts.FirstOrDefault();
                    if (previous != null && CompareWithId == null) CompareWith(previous.Id);
                    else DiffView.Invalidate();
                });
            }
        }

        private void LoadAttempts(Action then)
        {
            string? missionId = Brief.Entry?.MissionId;
            if (String.IsNullOrEmpty(missionId))
            {
                OtherAttempts = new List<BlackBoxEntrySummary>();
                then();
                return;
            }

            EnumerationQuery q = new EnumerationQuery();
            q.PageNumber = 1;
            q.PageSize = AttemptPageSize;
            Call((c, t) => c.EnumerateMissionBlackBoxEntriesAsync(missionId!, q, t), r =>
            {
                OtherAttempts = (r?.Objects ?? new List<BlackBoxEntrySummary>()).Where(e => e.Id != EntryId).OrderByDescending(e => e.CreatedUtc).ToList();
                then();
            }, null, ex =>
            {
                OtherAttempts = new List<BlackBoxEntrySummary>();
                then();
            });
        }

        private void PromptForEntry()
        {
            OpsFormDialog dialog = NewForm("Compare With", "Compare");
            InputField id = new InputField();
            id.Placeholder = "bbx_...";
            id.Validator = v => String.IsNullOrWhiteSpace(v) ? "An entry id is required." : null;
            dialog.AddField("Entry", id);
            dialog.Submit = d =>
            {
                CompareWith(id.Value);
                return true;
            };
            Context.Modals.Show(dialog);
        }

        private OpsDocument BuildDiff(OpsDocument doc)
        {
            doc.Note("D " + Tr("Compare with another attempt"));
            if (_DiffLoading)
            {
                doc.Note("Loading the diff...");
                return doc;
            }

            if (_DiffError != null)
            {
                doc.Text(_DiffError, doc.Theme.Error);
                return doc;
            }

            BlackBoxAttemptDiff? d = AttemptDiff;
            if (d == null)
            {
                doc.Note(CompareWithId == null ? "No other attempt of this mission is recorded. Press D to compare with any entry." : "No diff.");
                return doc;
            }

            doc.Section("Attempt Diff");
            foreach (string line in BlackBoxInsightText.Diff(d)) doc.Text(line);
            doc.Section("Differences", "  (" + d.DifferenceCount.ToString(CultureInfo.InvariantCulture) + ")");
            if (d.Differences.Count == 0) doc.Note("The two entries agree on every compared field.");
            else doc.Table(new List<string> { Tr("Section"), Tr("Field"), Tr("This entry"), Tr("Other entry") }, d.Differences.Select(f => (IList<string>)new List<string> { f.Section, f.Field, f.Value ?? "-", f.OtherValue ?? "-" }), 48);
            doc.Section("Changed Files");
            if (!d.ChangedFilesKnown)
            {
                doc.Note("The changed files of one of the entries are not known.");
                return doc;
            }

            doc.Field("In both", d.FilesInBothCount.ToString(CultureInfo.InvariantCulture));
            doc.Field("Only here", d.FilesOnlyInEntryCount.ToString(CultureInfo.InvariantCulture));
            foreach (string f in d.FilesOnlyInEntry) doc.Text("  + " + f, doc.Theme.Code);
            doc.Field("Only there", d.FilesOnlyInOtherCount.ToString(CultureInfo.InvariantCulture));
            foreach (string f in d.FilesOnlyInOther) doc.Text("  - " + f, doc.Theme.Code);
            return doc;
        }

        private async Task<GridPage<BlackBoxTimelineItem>> LoadTimelineAsync(GridQuery query, CancellationToken token)
        {
            EnumerationQuery q = new EnumerationQuery();
            q.PageNumber = Math.Max(1, query.PageNumber);
            q.PageSize = Math.Max(1, query.PageSize);
            q.Order = Armada.Core.Enums.EnumerationOrderEnum.CreatedAscending;
            BlackBoxTimelinePage? page = await Context.Client.GetBlackBoxTimelineAsync(EntryId, q, token).ConfigureAwait(false);
            List<BlackBoxTimelineItem> rows = page?.Objects ?? new List<BlackBoxTimelineItem>();
            return new GridPage<BlackBoxTimelineItem>(rows, page?.TotalRecords ?? rows.Count);
        }

        private async Task<GridPage<BlackBoxNearbySuccess>> LoadNearbyAsync(GridQuery query, CancellationToken token)
        {
            EnumerationQuery q = new EnumerationQuery();
            q.PageNumber = Math.Max(1, query.PageNumber);
            q.PageSize = Math.Max(1, query.PageSize);
            EnumerationResult<BlackBoxNearbySuccess>? page = await Context.Client.ListBlackBoxNearbySuccessesAsync(EntryId, q, token).ConfigureAwait(false);
            List<BlackBoxNearbySuccess> rows = page?.Objects ?? new List<BlackBoxNearbySuccess>();
            return new GridPage<BlackBoxNearbySuccess>(rows, page?.TotalRecords ?? rows.Count);
        }

        private async Task<GridPage<BlackBoxObjectSummary>> LoadObjectsAsync(GridQuery query, CancellationToken token)
        {
            EnumerationResult<BlackBoxObjectSummary>? result = await Context.Client.ListBlackBoxObjectsAsync(EntryId, query.PageNumber, query.PageSize, token).ConfigureAwait(false);
            List<BlackBoxObjectSummary> rows = result?.Objects ?? new List<BlackBoxObjectSummary>();
            return new GridPage<BlackBoxObjectSummary>(rows, result?.TotalRecords ?? rows.Count);
        }

        private void LoadNotes()
        {
            Call((c, t) => c.ListBlackBoxNotesAsync(EntryId, 1, NotePageSize, t), r =>
            {
                NoteRows = r?.Objects ?? new List<BlackBoxNote>();
                Notes.Invalidate();
            }, null, ex => { });
        }

        private OpsDocument BuildNotes(OpsDocument doc)
        {
            if (NoteRows.Count == 0) return doc;
            doc.Note("n " + Tr("Add a note from the Brief tab"));
            foreach (BlackBoxNote n in NoteRows)
            {
                doc.Section(n.AuthorKind.ToString(), "  " + Context.Loc.FormatDateTime(n.CreatedUtc));
                doc.Text(n.Body);
            }

            return doc;
        }

        private void Download()
        {
            Call((c, t) => c.DownloadBlackBoxAsync(EntryId, t), d =>
            {
                string name = String.IsNullOrEmpty(d.FileName) ? "blackbox-" + EntryId + ".zip" : d.FileName;
                PathPrompt.AskSave(Context, "Download Black Box", name, path =>
                {
                    try
                    {
                        string saved = Context.External.SaveFile(path, d.Content);
                        Toast(NotificationSeverityEnum.Success, Tr("Saved to {{path}}.", LocalizationArgs.Of("path", saved)));
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                    {
                        Toast(NotificationSeverityEnum.Error, ex.Message);
                    }
                });
            }, "Download failed");
        }

        private void Delete()
        {
            Confirm("Delete Black Box Entry", Tr("Delete black box entry {{id}} and its evidence? This cannot be undone.", LocalizationArgs.Of("id", EntryId)), () =>
            {
                Call((c, t) => c.DeleteBlackBoxAsync(EntryId, t), r =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Black box entry {{id}} deleted.", LocalizationArgs.Of("id", EntryId)));
                    Context.Navigate(TabLinks.BlackBoxPath);
                }, "Delete failed");
            }, "Delete");
        }

        #endregion
    }
}
