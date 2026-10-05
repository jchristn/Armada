namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Delivery, Runbooks tab (dashboard <c>Runbooks.tsx</c>): overview (total runbooks, active, executions, running),
    /// filters (search and active state, applied on the server), the runbooks grid (runbook, file, binding, steps and
    /// parameters, executions, visibility, last updated), row actions (Open, Duplicate, View JSON, Running: N, Delete
    /// for editors per scope), and Create Runbook. A prefilled execution handed off by a deployment or incident is
    /// announced and carried to the runbook the user opens.
    /// </summary>
    public class RunbooksScreen : EntityListScreen<Runbook>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Runbook"; }
        }

        /// <summary>
        /// Execution hand-off carried to the next runbook opened, or null.
        /// </summary>
        public RunbookExecutionStartRequest? CarriedExecution { get; private set; } = null;

        #endregion

        #region Private-Members

        private IReadOnlyDictionary<string, string> _Profiles = new Dictionary<string, string>();
        private IReadOnlyDictionary<string, string> _Environments = new Dictionary<string, string>();
        private IReadOnlyDictionary<string, int> _ExecutionTotals = new Dictionary<string, int>();
        private IReadOnlyDictionary<string, int> _ExecutionRunning = new Dictionary<string, int>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public RunbooksScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Open(Runbook row)
        {
            if (CarriedExecution != null) NavigationPrefill.Set(Context, PrefillSlots.RunbookExecution, CarriedExecution);
            Context.Navigate("/runbooks/" + row.Id);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(Runbook row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(Runbook row)
        {
            return row.Title;
        }

        /// <inheritdoc />
        protected override string? DetailPath(Runbook row)
        {
            return "/runbooks/" + row.Id;
        }

        /// <inheritdoc />
        protected override bool CanCreate
        {
            get { return true; }
        }

        /// <inheritdoc />
        protected override bool CanEdit(Runbook row)
        {
            return ScopeRules.CanEdit(Context.Session, row.Scope, row.TenantId, row.UserId);
        }

        /// <inheritdoc />
        protected override bool HasCreate
        {
            get { return true; }
        }

        /// <inheritdoc />
        protected override bool HasDelete
        {
            get { return true; }
        }

        /// <inheritdoc />
        protected override string CreateLabel
        {
            get { return "Create Runbook"; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<Runbook> grid)
        {
            grid.AddColumn(new GridColumn<Runbook>("title", "Runbook", r => r.Title) { Weight = 3, MinWidth = 14, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<Runbook>("fileName", "File Name", r => r.FileName + " (" + T(r.Active ? "Active" : "Inactive") + ")") { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Runbook>("binding", "Binding", Binding) { Weight = 4, Sortable = true });
            grid.AddColumn(new GridColumn<Runbook>("steps", "Steps", r => r.Steps.Count.ToString(CultureInfo.InvariantCulture) + " " + T("steps") + ", " + r.Parameters.Count.ToString(CultureInfo.InvariantCulture) + " " + T("parameters")) { Width = 22, Sortable = true });
            grid.AddColumn(new GridColumn<Runbook>("executions", "Executions", r => Count(_ExecutionTotals, r.Id) + " " + T("total") + ", " + Count(_ExecutionRunning, r.Id) + " " + T("running")) { Width = 20, Sortable = true });
            grid.AddColumn(new GridColumn<Runbook>("visibility", "Visibility", r => T(ScopeRules.Label(r.Scope))) { Width = 12, Sortable = true });
            grid.AddColumn(new GridColumn<Runbook>("updated", "Last Updated", r => EntityUi.When(Context, r.LastUpdateUtc)) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<Runbook>("description", "Description", r => EntityUi.Dash(r.Description)) { Weight = 3, DefaultVisible = false });
            grid.AddColumn(new GridColumn<Runbook>("id", "ID", r => r.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No runbooks match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("search", "Search by title, file name, description, environment, or ID...");
            filters.AddSelect("state", "All states", new List<SelectOption<string>>
            {
                new SelectOption<string>("active", T("Active")),
                new SelectOption<string>("inactive", T("Inactive"))
            }, 16);
        }

        /// <inheritdoc />
        protected override void OnBuilt()
        {
            CarriedExecution = NavigationPrefill.Take<RunbookExecutionStartRequest>(Context, PrefillSlots.RunbookExecution);
            if (CarriedExecution != null) Notice = "An incident or deployment handed off a prefilled runbook execution context. Open a runbook to start the execution with those defaults.";
            else if (!Context.Session.IsTenantAdmin) Notice = "Ask a tenant administrator to create and manage runbooks.";
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Runbook>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            RunbookQuery q = new RunbookQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            q.Search = EntityUi.Blank(Filter("search"));
            string state = Filter("state");
            if (state == "active") q.Active = true;
            else if (state == "inactive") q.Active = false;
            EnumerationResult<Runbook>? result = await Context.Client.ListRunbooksAsync(q, token).ConfigureAwait(false);
            return PageOf(result);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(Runbook row, string sortKey)
        {
            switch (sortKey)
            {
                case "updated": return row.LastUpdateUtc;
                case "steps": return row.Steps.Count;
                case "executions": return Count(_ExecutionTotals, row.Id);
                default: return null;
            }
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            Task<List<Runbook>> runbooks = AllRunbooksAsync(token);
            Task<List<RunbookExecution>> executions = AllExecutionsAsync(token);
            Task<List<WorkflowProfile>> profiles = EntityLookups.WorkflowProfilesAsync(Context.Client, token);
            Task<List<DeploymentEnvironment>> environments = EntityLookups.EnvironmentsAsync(Context.Client, token);
            await Task.WhenAll(runbooks, executions, profiles, environments).ConfigureAwait(false);
            _Profiles = profiles.Result.ToDictionary(p => p.Id, p => p.Name);
            _Environments = environments.Result.ToDictionary(e => e.Id, e => e.Name);
            _ExecutionTotals = executions.Result.GroupBy(e => e.RunbookId).ToDictionary(g => g.Key, g => g.Count());
            _ExecutionRunning = executions.Result.Where(e => e.Status == RunbookExecutionStatusEnum.Running).GroupBy(e => e.RunbookId).ToDictionary(g => g.Key, g => g.Count());
            List<Runbook> all = runbooks.Result;
            List<RunbookExecution> runs = executions.Result;
            return new List<KpiItem>
            {
                new KpiItem("Total Runbooks", EntityUi.Number(Context, all.Count)),
                new KpiItem("Active", EntityUi.Number(Context, all.Count(r => r.Active)), t => t.Success),
                new KpiItem("Executions", EntityUi.Number(Context, runs.Count)),
                new KpiItem("Running", EntityUi.Number(Context, runs.Count(e => e.Status == RunbookExecutionStatusEnum.Running)), t => t.Info)
            };
        }

        /// <inheritdoc />
        protected override List<ActionMenuItem> RowActions(Runbook row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            items.Add(new ActionMenuItem("Open", () => Open(row), "Enter"));
            items.Add(new ActionMenuItem("Duplicate", () => RunbookForms.Duplicate(Context, row, created =>
            {
                if (CarriedExecution != null) NavigationPrefill.Set(Context, PrefillSlots.RunbookExecution, CarriedExecution);
                Context.Navigate("/runbooks/" + created.Id);
            })));
            items.Add(new ActionMenuItem("View JSON", () => EntityUi.ShowJson(Context, row.Title, row), "j"));
            items.Add(new ActionMenuItem("Copy ID", () => Context.Clipboard.Copy(row.Id, "ID"), "y"));
            int running = Count(_ExecutionRunning, row.Id);
            if (running > 0) items.Add(new ActionMenuItem(T("Running") + ": " + running.ToString(CultureInfo.InvariantCulture), () => Open(row)));
            if (CanDelete(row))
            {
                ActionMenuItem delete = new ActionMenuItem("Delete", () => RequestDelete(row), "Del");
                delete.Destructive = true;
                items.Add(delete);
            }

            return items;
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            RunbookForms.OpenCreate(Context, created => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(Runbook row, CancellationToken token)
        {
            return Context.Client.DeleteRunbookAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(Runbook row)
        {
            return EntityUi.T(Context, "Delete \"{{title}}\"? This removes the runbook definition but does not touch deployments, incidents, or completed check runs.", "title", row.Title);
        }

        /// <inheritdoc />
        protected override string DeletedText(Runbook row)
        {
            return EntityUi.T(Context, "Runbook \"{{title}}\" deleted.", "title", row.Title);
        }

        #endregion

        #region Private-Methods

        private string Binding(Runbook r)
        {
            string profile = !String.IsNullOrEmpty(r.WorkflowProfileId) ? EntityLookups.Name(_Profiles, r.WorkflowProfileId) : T("No workflow profile");
            string env = !String.IsNullOrEmpty(r.EnvironmentId)
                ? EntityLookups.Name(_Environments, r.EnvironmentId, r.EnvironmentName ?? "-")
                : (String.IsNullOrEmpty(r.EnvironmentName) ? T("No environment") : r.EnvironmentName!);
            string check = r.DefaultCheckType.HasValue ? r.DefaultCheckType.Value.ToString() : T("No default check");
            return profile + " / " + env + " / " + check;
        }

        private static int Count(IReadOnlyDictionary<string, int> map, string id)
        {
            return map.TryGetValue(id, out int n) ? n : 0;
        }

        private async Task<List<Runbook>> AllRunbooksAsync(CancellationToken token)
        {
            try
            {
                return await ArmadaPaging.ReadAllAsync<Runbook>((p, ct) => Context.Client.ListRunbooksAsync(new RunbookQuery { PageNumber = p, PageSize = EntityLookups.PageSize }, ct), 20, token).ConfigureAwait(false);
            }
            catch (ArmadaApiException)
            {
                return new List<Runbook>();
            }
        }

        private async Task<List<RunbookExecution>> AllExecutionsAsync(CancellationToken token)
        {
            try
            {
                return await ArmadaPaging.ReadAllAsync<RunbookExecution>((p, ct) => Context.Client.ListRunbookExecutionsAsync(new RunbookExecutionQuery { PageNumber = p, PageSize = EntityLookups.PageSize }, ct), 20, token).ConfigureAwait(false);
            }
            catch (ArmadaApiException)
            {
                return new List<RunbookExecution>();
            }
        }

        #endregion
    }
}
