namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Hosting;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The dashboard's vessel Cleanup modal: a read-only inventory of the vessel's docks, orphaned worktrees, and
    /// Armada-managed branches, grouped as safe, in use, or unknown with the reason for each. <c>g</c> switches the
    /// group; only safe resources can be marked (Space, <c>a</c> marks every safe resource across pages); <c>c</c>
    /// cleans up the marked resources after confirmation (the server revalidates each one); <c>l</c> loads more of the
    /// group; <c>r</c> rescans; <c>v</c> shows the last run's per-item outcomes. Fills 95% of the terminal, as the
    /// dashboard modal fills 95% of the viewport. Not thread-safe.
    /// </summary>
    public class VesselCleanupDialog : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Rows shown per group page; the server pages each group and reports the exact total.
        /// </summary>
        public const int GroupPageSize = 50;

        /// <summary>
        /// Resource table for the current group.
        /// </summary>
        public ArmadaGrid<VesselCleanupResource> Grid { get; } = new ArmadaGrid<VesselCleanupResource>(r => r.Key);

        /// <summary>
        /// Group shown in the table.
        /// </summary>
        public VesselCleanupDispositionEnum Group { get; private set; } = VesselCleanupDispositionEnum.Safe;

        /// <summary>
        /// Inventory counts from the last scan or run, or null before the first scan.
        /// </summary>
        public VesselCleanupInventory? Inventory { get; private set; } = null;

        /// <summary>
        /// Per-item outcomes of the last run, or null.
        /// </summary>
        public List<VesselCleanupItemResult>? Results { get; private set; } = null;

        /// <summary>
        /// Marked safe keys with their measured size (null when unmeasured); survives paging and group switches.
        /// </summary>
        public Dictionary<string, long?> Selected { get; } = new Dictionary<string, long?>(StringComparer.Ordinal);

        /// <summary>
        /// Error from the last call, or null.
        /// </summary>
        public string? Error { get; private set; } = null;

        /// <summary>
        /// Current busy state (<c>scan</c>, <c>select</c>, <c>run</c>, or <c>page</c>), or empty.
        /// </summary>
        public string Busy { get; private set; } = "";

        #endregion

        #region Private-Members

        private const int _SelectAllPageSize = 1000;
        private const int _MaxSelectAllPages = 100;

        private readonly OpsScreen _Screen;
        private readonly string _VesselId;
        private readonly FocusScope _Scope = new FocusScope();
        private readonly Dictionary<VesselCleanupDispositionEnum, List<VesselCleanupResource>> _Rows = new Dictionary<VesselCleanupDispositionEnum, List<VesselCleanupResource>>();
        private readonly Dictionary<VesselCleanupDispositionEnum, int> _Totals = new Dictionary<VesselCleanupDispositionEnum, int>();
        private readonly Dictionary<VesselCleanupDispositionEnum, int> _PagesLoaded = new Dictionary<VesselCleanupDispositionEnum, int>();
        private int _ScreenWidth = 100;
        private int _ScreenHeight = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and scan.
        /// </summary>
        /// <param name="screen">Owning screen (API calls, confirmations, and toasts).</param>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="vesselName">Vessel name.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screen"/> or <paramref name="vesselId"/> is null.</exception>
        public VesselCleanupDialog(OpsScreen screen, string vesselId, string vesselName)
            : base(TitleOf(screen, vesselName), screen?.Context.Loc, screen?.Context.Theme.Current)
        {
            _Screen = screen ?? throw new ArgumentNullException(nameof(screen));
            _VesselId = vesselId ?? throw new ArgumentNullException(nameof(vesselId));
            foreach (VesselCleanupDispositionEnum d in Groups())
            {
                _Rows[d] = new List<VesselCleanupResource>();
                _Totals[d] = 0;
                _PagesLoaded[d] = 0;
            }

            Grid.ShowPagingBar = false;
            Grid.PageSize = 100000;
            Grid.ModalHost = screen.Context.Modals;
            Grid.EmptyText = "None.";
            Grid.AddColumn(new GridColumn<VesselCleanupResource>("type", "Type", r => T(r.ResourceType.ToString())) { Width = 10 });
            Grid.AddColumn(new GridColumn<VesselCleanupResource>("resource", "Resource", ResourceText) { Weight = 4 });
            Grid.AddColumn(new GridColumn<VesselCleanupResource>("owner", "Owner", r => String.Join(", ", Owner(r))) { Weight = 3 });
            Grid.AddColumn(new GridColumn<VesselCleanupResource>("reason", "Reason", r => r.Reason) { Weight = 4 });
            Grid.AddColumn(new GridColumn<VesselCleanupResource>("size", "Size", r => r.SizeBytes.HasValue ? DirectoryUsage.FormatBytes(r.SizeBytes.Value) : "-") { Width = 10 });
            Grid.SelectionChanged += (s, keys) => SyncSelection();
            Grid.Localizer = Localizer;
            Grid.ApplyTheme(Theme);
            _Scope.Add(Grid);
            _Scope.Focus(Grid);
            _Scope.SetActive(true);
            FooterHint = " g " + T("Group") + "  Space " + T("Select") + "  a " + T("Select all safe") + "  c " + T("Clean up") + "  l " + T("Load more") + "  r " + T("Rescan") + "  v " + T("Results") + "  Esc " + T("Close") + " ";
            MinContentWidth = 60;
            MaxContentWidth = 100000;
            ShowGroup(VesselCleanupDispositionEnum.Safe);
            Scan();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the inventory again and reload every group. Clears the selection. Deletes nothing.
        /// </summary>
        public void Scan()
        {
            if (Busy.Length > 0) return;
            Busy = "scan";
            Error = null;
            _Screen.Call((c, t) => ScanAsync(c, t), snapshot =>
            {
                Busy = "";
                Selected.Clear();
                Apply(snapshot);
            }, null, ex =>
            {
                Busy = "";
                Error = ex.Message;
            });
        }

        /// <summary>
        /// Show a disposition group in the table.
        /// </summary>
        /// <param name="group">Group.</param>
        public void ShowGroup(VesselCleanupDispositionEnum group)
        {
            Group = group;
            Grid.MultiSelect = group == VesselCleanupDispositionEnum.Safe;
            Grid.SetLocalRows(_Rows[group]);
            RestoreMarks();
        }

        /// <summary>
        /// Show the next group (safe, in use, unknown, then safe again).
        /// </summary>
        public void NextGroup()
        {
            List<VesselCleanupDispositionEnum> groups = Groups();
            ShowGroup(groups[(groups.IndexOf(Group) + 1) % groups.Count]);
        }

        /// <summary>
        /// Load the next page of the current group when the server has more.
        /// </summary>
        public void LoadMore()
        {
            if (Busy.Length > 0) return;
            VesselCleanupDispositionEnum group = Group;
            if (_Rows[group].Count >= _Totals[group]) return;
            int page = _PagesLoaded[group] + 1;
            Busy = "page";
            _Screen.Call((c, t) => c.ListVesselCleanupResourcesAsync(_VesselId, PageQuery(group, page, GroupPageSize), t), result =>
            {
                Busy = "";
                if (result == null) return;
                _Rows[group].AddRange(result.Objects ?? new List<VesselCleanupResource>());
                _Totals[group] = (int)result.TotalRecords;
                _PagesLoaded[group] = page;
                if (Group == group) ShowGroup(group);
            }, null, ex =>
            {
                Busy = "";
                Error = ex.Message;
            });
        }

        /// <summary>
        /// Mark every safe resource (reading the safe group page by page), or clear the selection when all are marked.
        /// </summary>
        public void ToggleAllSafe()
        {
            if (Busy.Length > 0) return;
            int safeTotal = _Totals[VesselCleanupDispositionEnum.Safe];
            if (safeTotal > 0 && Selected.Count >= safeTotal)
            {
                Selected.Clear();
                RestoreMarks();
                return;
            }

            Busy = "select";
            Error = null;
            _Screen.Call((c, t) => ReadAllSafeAsync(c, t), all =>
            {
                Busy = "";
                Selected.Clear();
                foreach (VesselCleanupResource r in all) Selected[r.Key] = r.SizeBytes;
                RestoreMarks();
            }, null, ex =>
            {
                Busy = "";
                Error = ex.Message;
            });
        }

        /// <summary>
        /// Ask for confirmation, then clean up the selected safe resources and show the outcomes with the refreshed
        /// inventory.
        /// </summary>
        public void CleanUp()
        {
            if (Busy.Length > 0) return;
            if (Selected.Count == 0)
            {
                _Screen.Toast(NotificationSeverityEnum.Warning, _Screen.Tr("Select the safe resources to clean up."));
                return;
            }

            List<string> keys = Selected.Keys.ToList();
            string message = _Screen.Tr("Remove {{count}} selected resources ({{size}} measured)? The server revalidates each one first; this cannot be undone.",
                LocalizationArgs.Of("count", keys.Count, "size", DirectoryUsage.FormatBytes(SelectedBytes())));
            _Screen.Confirm("Clean up", message, () => Run(keys), "Clean up");
        }

        /// <summary>
        /// Show the last run's per-item outcomes.
        /// </summary>
        public void ShowResults()
        {
            if (Results == null) return;
            List<string> lines = new List<string> { ResultsSummary() };
            foreach (VesselCleanupItemResult r in Results)
                lines.Add(T(r.Status.ToString()) + "  " + (String.IsNullOrEmpty(r.Identity) ? r.Key : r.Identity) + ": " + r.Message);
            _Screen.ShowText(_Screen.Tr("Cleanup results"), String.Join("\n", lines));
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            bool plain = key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None;
            if (plain)
            {
                switch (key.Rune)
                {
                    case 'g':
                        NextGroup();
                        return true;
                    case 'a':
                        ToggleAllSafe();
                        return true;
                    case 'c':
                        CleanUp();
                        return true;
                    case 'l':
                        LoadMore();
                        return true;
                    case 'r':
                        Scan();
                        return true;
                    case 'v':
                        ShowResults();
                        return true;
                }
            }

            _Scope.HandleKey(key);
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            _ScreenWidth = surface.Size.Width;
            _ScreenHeight = surface.Size.Height;
            base.Render(surface);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, Math.Max(60, (int)(_ScreenWidth * 0.95) - 2));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Max(10, (int)(_ScreenHeight * 0.95) - 4);
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int height = content.Size.Height;
            int y = 0;
            SurfaceText.Draw(content, 0, y++, T("Read-only inventory: opening this deletes nothing. Only safe resources can be selected, and the server revalidates each one before removing it."), Dim(), width);
            if (Error != null) SurfaceText.Draw(content, 0, y++, "! " + Error, On(Theme.Error), width);
            if (Busy.Length > 0) SurfaceText.Draw(content, 0, y++, BusyText(), Dim(), width);
            if (Results != null && y < height) SurfaceText.Draw(content, 0, y++, T("Cleanup results") + ": " + ResultsSummary() + "  (v " + T("Results") + ")", On(Results.Any(r => r.Status == VesselCleanupItemStatusEnum.Failed) ? Theme.Error : Theme.Success), width);
            if (Inventory != null && y < height)
            {
                string summary = Inventory.ResourceCount == 0
                    ? T("Nothing to clean up: this vessel has no docks, orphaned worktrees, or Armada branches.")
                    : _Screen.Tr("{{count}} resources; {{safe}} safe ({{size}} measured).",
                        LocalizationArgs.Of("count", Inventory.ResourceCount, "safe", Inventory.SafeCount, "size", DirectoryUsage.FormatBytes(Inventory.SafeMeasuredBytes)))
                      + " " + _Screen.Tr("{{selected}} selected ({{size}}).", LocalizationArgs.Of("selected", Selected.Count, "size", DirectoryUsage.FormatBytes(SelectedBytes())));
                SurfaceText.Draw(content, 0, y++, summary, Body(), width);
            }

            if (y >= height) return;
            int x = 0;
            foreach (VesselCleanupDispositionEnum d in Groups())
            {
                string label = " " + T(GroupTitle(d)) + " (" + _Totals[d] + ") ";
                x += SurfaceText.Draw(content, x, y, label, d == Group ? On(Theme.Selection) : Dim(), Math.Max(0, width - x));
                x++;
                if (x >= width) break;
            }

            y++;
            if (y < height) SurfaceText.Draw(content, 0, y++, T(GroupHint(Group)), Dim(), width);
            int footerRows = _Rows[Group].Count < _Totals[Group] ? 1 : 0;
            int gridHeight = Math.Max(1, height - y - footerRows);
            if (Inventory == null && Busy == "scan") SurfaceText.Draw(content, 0, y, T("Scanning..."), Dim(), width);
            else _Scope.RenderChild(content, Grid, new Rect(0, y, width, gridHeight));
            y += gridHeight;
            if (footerRows > 0 && y < height)
                SurfaceText.Draw(content, 0, y, _Screen.Tr("Showing {{shown}} of {{total}}; l loads more.", LocalizationArgs.Of("shown", _Rows[Group].Count, "total", _Totals[Group])), Dim(), width);
        }

        #endregion

        #region Private-Methods

        private static string TitleOf(OpsScreen? screen, string vesselName)
        {
            string name = vesselName ?? "";
            return screen != null ? screen.Tr("Cleanup: {{name}}", LocalizationArgs.Of("name", name)) : "Cleanup: " + name;
        }

        private static List<VesselCleanupDispositionEnum> Groups()
        {
            return new List<VesselCleanupDispositionEnum> { VesselCleanupDispositionEnum.Safe, VesselCleanupDispositionEnum.InUse, VesselCleanupDispositionEnum.Unknown };
        }

        private static string GroupTitle(VesselCleanupDispositionEnum d)
        {
            switch (d)
            {
                case VesselCleanupDispositionEnum.Safe: return "Safe to clean up";
                case VesselCleanupDispositionEnum.InUse: return "Still in use";
                default: return "Unknown disposition";
            }
        }

        private static string GroupHint(VesselCleanupDispositionEnum d)
        {
            switch (d)
            {
                case VesselCleanupDispositionEnum.Safe: return "Evidence shows nothing needs these resources. Select the ones to remove.";
                case VesselCleanupDispositionEnum.InUse: return "Running, queued, review, landing, retry, captain, or merge-queue work still references these. They cannot be cleaned up.";
                default: return "These could not be proven safe (uncommitted or unmerged work, shared paths, unreachable Harbors, or unclear ownership). They cannot be cleaned up.";
            }
        }

        private static EnumerationQuery PageQuery(VesselCleanupDispositionEnum group, int pageNumber, int pageSize)
        {
            EnumerationQuery query = new EnumerationQuery();
            query.PageNumber = pageNumber;
            query.PageSize = pageSize;
            query.Status = group.ToString();
            return query;
        }

        private async Task<VesselCleanupSnapshot> ScanAsync(ArmadaClient client, CancellationToken token)
        {
            VesselCleanupSnapshot snapshot = new VesselCleanupSnapshot();
            snapshot.Inventory = await client.GetVesselCleanupInventoryAsync(_VesselId, token).ConfigureAwait(false);
            await ReadGroupsAsync(client, snapshot, token).ConfigureAwait(false);
            return snapshot;
        }

        private async Task ReadGroupsAsync(ArmadaClient client, VesselCleanupSnapshot snapshot, CancellationToken token)
        {
            if (snapshot.Inventory == null || snapshot.Inventory.ResourceCount == 0) return;
            foreach (VesselCleanupDispositionEnum d in Groups())
            {
                EnumerationResult<VesselCleanupResource>? page = await client.ListVesselCleanupResourcesAsync(_VesselId, PageQuery(d, 1, GroupPageSize), token).ConfigureAwait(false);
                if (page != null) snapshot.Pages[d] = page;
            }
        }

        private async Task<List<VesselCleanupResource>> ReadAllSafeAsync(ArmadaClient client, CancellationToken token)
        {
            List<VesselCleanupResource> all = new List<VesselCleanupResource>();
            for (int pageNumber = 1; pageNumber <= _MaxSelectAllPages; pageNumber++)
            {
                EnumerationResult<VesselCleanupResource>? page = await client.ListVesselCleanupResourcesAsync(_VesselId, PageQuery(VesselCleanupDispositionEnum.Safe, pageNumber, _SelectAllPageSize), token).ConfigureAwait(false);
                if (page?.Objects == null || page.Objects.Count == 0) break;
                all.AddRange(page.Objects);
                if (pageNumber >= page.TotalPages) break;
            }

            return all;
        }

        private async Task<VesselCleanupSnapshot> RunAsync(ArmadaClient client, List<string> keys, CancellationToken token)
        {
            VesselCleanupSnapshot snapshot = new VesselCleanupSnapshot();
            snapshot.Result = await client.RunVesselCleanupAsync(_VesselId, keys, token).ConfigureAwait(false);
            snapshot.Inventory = snapshot.Result?.Inventory;
            await ReadGroupsAsync(client, snapshot, token).ConfigureAwait(false);
            return snapshot;
        }

        private void Run(List<string> keys)
        {
            if (Busy.Length > 0) return;
            Busy = "run";
            Error = null;
            _Screen.Call((c, t) => RunAsync(c, keys, t), snapshot =>
            {
                Busy = "";
                Selected.Clear();
                Apply(snapshot);
                if (snapshot.Result == null) return;
                Results = snapshot.Result.Results ?? new List<VesselCleanupItemResult>();
                NotificationSeverityEnum severity = snapshot.Result.Failed > 0 ? NotificationSeverityEnum.Error : NotificationSeverityEnum.Success;
                _Screen.Toast(severity, ResultsSummary());
            }, null, ex =>
            {
                Busy = "";
                Error = _Screen.Tr("Cleanup failed: {{message}}", LocalizationArgs.Of("message", ex.Message));
            });
        }

        private void Apply(VesselCleanupSnapshot snapshot)
        {
            Inventory = snapshot.Inventory;
            foreach (VesselCleanupDispositionEnum d in Groups())
            {
                bool read = snapshot.Pages.TryGetValue(d, out EnumerationResult<VesselCleanupResource>? page);
                _Rows[d] = read ? page!.Objects ?? new List<VesselCleanupResource>() : new List<VesselCleanupResource>();
                _Totals[d] = read ? (int)page!.TotalRecords : 0;
                _PagesLoaded[d] = read ? 1 : 0;
            }

            ShowGroup(Group);
        }

        // The grid marks only rows on the current page; keep the full selection here so it survives paging.
        private void SyncSelection()
        {
            if (Group != VesselCleanupDispositionEnum.Safe) return;
            foreach (VesselCleanupResource r in _Rows[Group])
            {
                if (Grid.Marked.Contains(r.Key)) Selected[r.Key] = r.SizeBytes;
                else Selected.Remove(r.Key);
            }
        }

        private void RestoreMarks()
        {
            Grid.Marked.Clear();
            if (Group != VesselCleanupDispositionEnum.Safe) return;
            foreach (VesselCleanupResource r in _Rows[Group])
                if (Selected.ContainsKey(r.Key)) Grid.Marked.Add(r.Key);
        }

        private long SelectedBytes()
        {
            long total = 0;
            foreach (long? size in Selected.Values) total += size ?? 0;
            return total;
        }

        private string ResultsSummary()
        {
            List<VesselCleanupItemResult> results = Results ?? new List<VesselCleanupItemResult>();
            return _Screen.Tr("{{deleted}} removed, {{skipped}} skipped, {{failed}} failed.", LocalizationArgs.Of(
                "deleted", results.Count(r => r.Status == VesselCleanupItemStatusEnum.Deleted),
                "skipped", results.Count(r => r.Status == VesselCleanupItemStatusEnum.Skipped),
                "failed", results.Count(r => r.Status == VesselCleanupItemStatusEnum.Failed)));
        }

        private string BusyText()
        {
            switch (Busy)
            {
                case "scan": return T("Scanning...");
                case "select": return T("Selecting every safe resource...");
                case "run": return T("Cleaning up...");
                default: return T("Loading...");
            }
        }

        private string ResourceText(VesselCleanupResource r)
        {
            string text = r.Identity;
            if (!String.IsNullOrEmpty(r.Path) && r.Path != r.Identity) text += "  " + r.Path;
            if (!String.IsNullOrEmpty(r.BranchName) && r.BranchName != r.Identity) text += "  " + r.BranchName;
            return text;
        }

        private List<string> Owner(VesselCleanupResource r)
        {
            List<string> parts = new List<string>();
            if (!String.IsNullOrEmpty(r.VoyageId)) parts.Add(T("Voyage") + " " + r.VoyageId);
            if (!String.IsNullOrEmpty(r.MissionId)) parts.Add(T("Mission") + " " + r.MissionId + (r.MissionStatus.HasValue ? " (" + r.MissionStatus.Value + ")" : ""));
            if (!String.IsNullOrEmpty(r.DockId) && r.ResourceType != VesselCleanupResourceTypeEnum.Dock) parts.Add(T("Dock") + " " + r.DockId);
            if (!String.IsNullOrEmpty(r.CaptainId)) parts.Add(T("Captain") + " " + r.CaptainId);
            parts.Add(!String.IsNullOrEmpty(r.HarborId) ? T("Harbor") + " " + r.HarborId : T("Admiral host"));
            return parts;
        }

        #endregion
    }
}
