namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The Workspace tab of <c>/vessels</c> without a vessel (the dashboard's WorkspaceVesselPicker): recent vessels
    /// first, then the rest, filtered by name, id, or working directory, with each vessel's workspace state (ready or
    /// no working directory), branch, active missions, and ahead and behind for the recent and first vessels.
    /// <c>Enter</c> opens the vessel's Workspace. Not thread-safe.
    /// </summary>
    public class WorkspacePickerScreen : OpsListScreen<Vessel>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Open a vessel as a browsable, editable repository workspace inside Armada."; }
        }

        /// <summary>
        /// Search filter.
        /// </summary>
        public TextInput Search { get; }

        /// <summary>
        /// Workspace status by vessel id.
        /// </summary>
        public ConcurrentDictionary<string, WorkspaceStatusResult?> StatusByVessel { get; } = new ConcurrentDictionary<string, WorkspaceStatusResult?>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public WorkspacePickerScreen(RouteMatch route, TuiContext context)
            : base(route, context, v => v.Id, "WorkspacePickerScreen", "Workspace")
        {
            Grid.MultiSelect = false;
            Grid.EmptyText = "No vessels match the current filter.";
            Search = TextFilter("Find", 40, "Find a vessel by name, id, or working directory");
            Column("name", "Name", v => (IsRecent(v.Id) ? "* " : "  ") + v.Name, 3).Pinned = true;
            Column("state", "Workspace", StateText, 0, 22, null, (v, t) => StatusByVessel.TryGetValue(v.Id, out WorkspaceStatusResult? s) && s != null && !s.HasWorkingDirectory ? t.Warning : t.Text);
            Column("id", "ID", v => v.Id, 0, 26);
            Column("workdir", "Working Directory", v => String.IsNullOrEmpty(v.WorkingDirectory) ? Tr("No working directory configured") : v.WorkingDirectory!, 4);
            Column("status", "Status", StatusText, 3);
            RowActions.Add(new OpsAction<Vessel>("open", "Open Workspace", v => OpenRow(v), "o"));
            Start(null, false, 50);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool ServerPaging
        {
            get { return false; }
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Vessel>> FetchAsync(GridQuery query, CancellationToken token)
        {
            EnumerationResult<Vessel>? result = await Context.Client.ListVesselsAsync(new ArmadaPageQuery(1, 9999), token).ConfigureAwait(false);
            List<Vessel> vessels = result?.Objects ?? new List<Vessel>();
            List<string> recent = Context.Prefs.Current.WorkspaceRecentVessels ?? new List<string>();
            List<Vessel> ordered = recent.Select(id => vessels.FirstOrDefault(v => v.Id == id)).Where(v => v != null).Select(v => v!).ToList();
            ordered.AddRange(vessels.Where(v => !recent.Contains(v.Id)));
            List<string> preview = ordered.Select(v => v.Id).Take(12).ToList();
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                List<Task> tasks = preview.Select(async id =>
                {
                    try { StatusByVessel[id] = await client.GetWorkspaceStatusAsync(id).ConfigureAwait(false); }
                    catch (Exception) { StatusByVessel[id] = null; }
                }).ToList();
                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (Exception) { }
            });
            return new GridPage<Vessel>(ordered, ordered.Count);
        }

        /// <inheritdoc />
        protected override IEnumerable<Vessel> FilterLocal(IEnumerable<Vessel> rows)
        {
            string q = Search.Value.Trim();
            if (q.Length == 0) return rows;
            return rows.Where(v => v.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || v.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || (v.WorkingDirectory ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <inheritdoc />
        protected override void OpenRow(Vessel row)
        {
            Context.Navigate("/workspace/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(Vessel row)
        {
            return row.Name;
        }

        #endregion

        #region Private-Methods

        private bool IsRecent(string id)
        {
            return (Context.Prefs.Current.WorkspaceRecentVessels ?? new List<string>()).Contains(id);
        }

        private string StateText(Vessel v)
        {
            if (StatusByVessel.TryGetValue(v.Id, out WorkspaceStatusResult? s) && s != null && !s.HasWorkingDirectory) return Tr("No working directory");
            return Tr("Workspace ready");
        }

        private string StatusText(Vessel v)
        {
            if (!StatusByVessel.TryGetValue(v.Id, out WorkspaceStatusResult? s) || s == null) return "";
            string sync = s.CommitsAhead.HasValue ? s.CommitsAhead.Value + " " + Tr("ahead") : Tr("No git sync data");
            if (s.CommitsBehind.HasValue) sync += " / " + s.CommitsBehind.Value + " " + Tr("behind");
            return (String.IsNullOrEmpty(s.BranchName) ? Tr("No branch info") : s.BranchName) + "  " + s.ActiveMissionCount + " " + Tr("active mission(s)") + "  " + sync;
        }

        #endregion
    }
}
