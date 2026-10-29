namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
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
    /// the paged object viewer), and the Notes panel. Header actions open the mission and voyage and download the
    /// entry's .zip. Not thread-safe.
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
        /// Notes read per page.
        /// </summary>
        public const int NotePageSize = 50;

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

            Action("mission", "Open Mission", () => Context.Navigate("/missions/" + Uri.EscapeDataString(Brief.Entry!.MissionId!)), "m", () => Brief.Entry != null && !String.IsNullOrEmpty(Brief.Entry.MissionId), true);
            Action("voyage", "Open Voyage", () => Context.Navigate("/voyages/" + Uri.EscapeDataString(Brief.Entry!.VoyageId!)), "V", () => Brief.Entry != null && !String.IsNullOrEmpty(Brief.Entry.VoyageId), true);
            Action("download", "Download .zip", Download, "w", () => Brief.Entry != null, true);
            Action("same-fingerprint", "Similar failures", () => Context.Navigate("/blackbox?fingerprint=" + Uri.EscapeDataString(Brief.Entry!.Fingerprint!)), "f", () => Brief.Entry != null && !String.IsNullOrEmpty(Brief.Entry.Fingerprint));
            Action("copy-id", "Copy ID", () => Copy(EntryId, "Black box entry ID"), "y");
            Action("delete", "Delete", Delete, "del", () => Brief.Entry != null, false, true);
            MissionScreen.AddFailureActions(this, Brief, "brief");

            Notes.Builder = BuildNotes;
            Notes.Empty = "No notes.";
            AddPanel("brief", "Brief", Brief);
            AddPanel("objects", "Objects", Objects);
            AddPanel("notes", "Notes", Notes);

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

        #endregion

        #region Private-Methods

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
                    Context.Navigate("/blackbox");
                }, "Delete failed");
            }, "Delete");
        }

        #endregion
    }
}
