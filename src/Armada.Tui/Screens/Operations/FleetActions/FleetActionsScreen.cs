namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// Fleet Actions: Actions tab (W3.7, <c>/fleet-actions</c>), the dashboard's FleetActionsTable: saved actions with
    /// name (hidden marker, id, description), kind, source (built-in or custom), timeout, concurrency, created
    /// (sortable on the server), and updated; Run action... (vessel picker, run dialog, run), + Action, and the row
    /// menu (Run, Edit, Duplicate, View JSON, Delete; deleting a built-in hides it). Writes are tenant-admin only;
    /// others open View JSON. <c>?run=new</c> starts the run flow. Not thread-safe.
    /// </summary>
    public class FleetActionsScreen : OpsListScreen<FleetAction>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Reusable commands and mission prompts you can run across many vessels."; }
        }

        /// <summary>
        /// Default timeout for new actions (from settings, default 300).
        /// </summary>
        public int DefaultTimeout { get; private set; } = 300;

        /// <summary>
        /// The last run flow started (tests).
        /// </summary>
        public FleetActionRunFlow? LastFlow { get; private set; } = null;

        #endregion

        #region Private-Members

        private bool _RunOnMount;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public FleetActionsScreen(RouteMatch route, TuiContext context)
            : base(route, context, a => a.Id, "FleetActionsScreen", "Fleet Actions")
        {
            Grid.MultiSelect = false;
            Grid.EmptyText = "No fleet actions yet";
            Column("name", "Name", a => a.Name + (a.Active ? "" : "  [" + Tr("Hidden") + "]") + (String.IsNullOrEmpty(a.Description) ? "" : "  - " + a.Description), 4);
            Column("id", "ID", a => a.Id, 0, 24).DefaultVisible = false;
            Column("kind", "Kind", a => (a.Kind == FleetActionKindEnum.Command ? "! " : "i ") + Tr(a.Kind.ToString()), 0, 11, null, (a, t) => a.Kind == FleetActionKindEnum.Command ? t.Warning : t.Info);
            Column("source", "Source", a => a.IsBuiltIn ? "# " + Tr("Built-in") : Tr("Custom"), 0, 11);
            Column("timeout", "Timeout", a => a.Kind == FleetActionKindEnum.Command ? Tr("{{value}} s", LocalizationArgs.Of("value", Context.Loc.FormatNumber(a.TimeoutSeconds))) : "-", 0, 9).Align = TUIKit.Widgets.CellAlignment.Right;
            Column("concurrency", "Concurrency", a => Context.Loc.FormatNumber(a.DefaultConcurrency), 0, 11).Align = TUIKit.Widgets.CellAlignment.Right;
            Column("createdUtc", "Created", a => Context.Loc.FormatRelative(a.CreatedUtc, Context.Clock.UtcNow), 0, 15, a => a.CreatedUtc);
            Column("lastUpdateUtc", "Updated", a => Context.Loc.FormatRelative(a.LastUpdateUtc, Context.Clock.UtcNow), 0, 15);

            ScreenActions.Add(new OpsScreenAction("run", "Run action...", () => StartRun(null), "R", () => IsTenantAdmin));
            ScreenActions.Add(new OpsScreenAction("new", "+ Action", () => FleetActionForm.Open(this, null, false, DefaultTimeout, Saved), "n", () => IsTenantAdmin));

            RowActions.Add(new OpsAction<FleetAction>("run", "Run", a => StartRun(a.Id), "r", a => IsTenantAdmin));
            RowActions.Add(new OpsAction<FleetAction>("edit", "Edit", a => FleetActionForm.Open(this, a, true, DefaultTimeout, Saved), "e", a => IsTenantAdmin));
            RowActions.Add(new OpsAction<FleetAction>("duplicate", "Duplicate", a => FleetActionForm.Open(this, a, false, DefaultTimeout, Saved), "u", a => IsTenantAdmin));
            RowActions.Add(new OpsAction<FleetAction>("json", "View JSON", a => ShowJson(Tr("Fleet action: {{name}}", LocalizationArgs.Of("name", a.Name)), a), "j"));
            RowActions.Add(new OpsAction<FleetAction>("copy-id", "Copy ID", a => Copy(a.Id, "Fleet action ID"), "y"));
            OpsAction<FleetAction> delete = new OpsAction<FleetAction>("delete", "Delete", Delete, "del", a => IsTenantAdmin);
            delete.Danger = true;
            RowActions.Add(delete);

            _RunOnMount = OpsHandoff.Get(route, "run") == "new";
            Call((c, t) => c.GetSettingsAsync(t), s =>
            {
                int? timeout = s?.FleetActions?.DefaultTimeoutSeconds;
                if (timeout.HasValue && timeout.Value > 0) DefaultTimeout = timeout.Value;
            }, null, ex => { });
            Start("createdUtc", true);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void OnActivated()
        {
            base.OnActivated();
            if (_RunOnMount && IsTenantAdmin)
            {
                _RunOnMount = false;
                StartRun(null);
            }
        }

        /// <summary>
        /// Start the run flow (vessel picker, then the run dialog).
        /// </summary>
        /// <param name="actionId">Saved action to preselect, or null.</param>
        /// <returns>The flow.</returns>
        public FleetActionRunFlow StartRun(string? actionId)
        {
            LastFlow = FleetActionRunFlow.Start(this, null, actionId, null);
            return LastFlow;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<FleetAction>> FetchAsync(GridQuery query, CancellationToken token)
        {
            FleetActionEnumerateQuery q = new FleetActionEnumerateQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            q.Order = query.SortKey == "createdUtc" && !query.SortDescending ? "CreatedAscending" : "CreatedDescending";
            EnumerationResult<FleetAction>? result = await Context.Client.EnumerateFleetActionsAsync(q, token).ConfigureAwait(false);
            List<FleetAction> rows = result?.Objects ?? new List<FleetAction>();
            return new GridPage<FleetAction>(rows, result?.TotalRecords ?? rows.Count);
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<FleetAction> rows)
        {
            Grid.EmptyText = ServerTotal == 0 ? "No fleet actions yet" : "No actions on this page.";
        }

        /// <inheritdoc />
        protected override int AboveHeight(int width)
        {
            return HasLoaded && ServerTotal == 0 ? 4 : 0;
        }

        /// <inheritdoc />
        protected override void RenderAbove(ISurface surface, int width)
        {
            int y = 0;
            foreach (string text in new[]
            {
                "Armada seeds five built-in actions into each tenant the first time actions are listed: Fast-forward default branch, Prune merged branches, Build, Update outdated dependencies, and Add a test project.",
                "Deleted built-ins are hidden for good and never re-seeded. Create your own action to get started.",
            })
            {
                foreach (string line in TextCells.Wrap(Tr(text), width))
                {
                    if (y >= surface.Size.Height) return;
                    SurfaceText.Draw(surface, 0, y++, line, Theme.Muted, width);
                }
            }
        }

        /// <inheritdoc />
        protected override void OpenRow(FleetAction row)
        {
            if (IsTenantAdmin) FleetActionForm.Open(this, row, true, DefaultTimeout, Saved);
            else ShowJson(Tr("Fleet action: {{name}}", LocalizationArgs.Of("name", row.Name)), row);
        }

        /// <inheritdoc />
        protected override string RowTitle(FleetAction row)
        {
            return row.Name;
        }

        #endregion

        #region Private-Methods

        private void Saved(FleetAction saved)
        {
            Toast(NotificationSeverityEnum.Success, Tr("Fleet action \"{{name}}\" saved.", LocalizationArgs.Of("name", saved.Name)));
            Refresh();
        }

        private void Delete(FleetAction a)
        {
            string title = a.IsBuiltIn ? "Hide built-in action" : "Delete fleet action";
            string message = a.IsBuiltIn
                ? Tr("\"{{name}}\" is a built-in action. Deleting it hides it permanently (a soft delete) and Armada will not seed it again. Past runs keep their snapshot.", LocalizationArgs.Of("name", a.Name))
                : Tr("Delete \"{{name}}\"? Past runs keep their snapshot of the definition. This cannot be undone.", LocalizationArgs.Of("name", a.Name));
            Confirm(title, message, () =>
            {
                Run((c, t) => c.DeleteFleetActionAsync(a.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, a.IsBuiltIn
                        ? Tr("Built-in action \"{{name}}\" hidden.", LocalizationArgs.Of("name", a.Name))
                        : Tr("Fleet action \"{{name}}\" deleted.", LocalizationArgs.Of("name", a.Name)));
                    Refresh();
                }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Delete failed.") : ex.Message));
            }, a.IsBuiltIn ? "Hide" : "Delete");
        }

        #endregion
    }
}
