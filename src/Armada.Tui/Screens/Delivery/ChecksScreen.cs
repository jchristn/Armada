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
    /// Delivery, Checks tab (dashboard <c>CheckRuns.tsx</c>): overview (total, passed, failed, running), filters
    /// (vessel, status, source Armada/External, check type) applied on the server, the check runs grid with parsed
    /// results and the comparison with the previous run, row actions (Open, Draft Release, View JSON), and the Run
    /// Check modal (<c>n</c>), which also opens when another screen hands off a prefilled request
    /// (<see cref="PrefillSlots.RunCheck"/>).
    /// </summary>
    public class ChecksScreen : EntityListScreen<CheckRun>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Check Run"; }
        }

        #endregion

        #region Private-Members

        private IReadOnlyDictionary<string, string> _Vessels = new Dictionary<string, string>();
        private IReadOnlyDictionary<string, CheckRunComparison> _Comparisons = new Dictionary<string, CheckRunComparison>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public ChecksScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open the Run Check modal.
        /// </summary>
        /// <param name="prefill">Prefill, or null.</param>
        public void OpenRunCheck(CheckRunRequest? prefill)
        {
            RunCheckForms.Open(Context, prefill, run =>
            {
                Reload();
                Context.Navigate("/checks/" + run.Id);
            });
        }

        /// <summary>
        /// Navigate to a prefilled release draft for a run (Draft Release).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="run">Run.</param>
        public static void DraftRelease(TuiContext context, CheckRun run)
        {
            ReleaseUpsertRequest prefill = new ReleaseUpsertRequest();
            prefill.VesselId = run.VesselId;
            prefill.VoyageIds = !String.IsNullOrEmpty(run.VoyageId) ? new List<string> { run.VoyageId! } : new List<string>();
            prefill.MissionIds = !String.IsNullOrEmpty(run.MissionId) ? new List<string> { run.MissionId! } : new List<string>();
            prefill.CheckRunIds = new List<string> { run.Id };
            prefill.Title = !String.IsNullOrEmpty(run.Label) ? run.Label + " Release" : run.Type + " Release";
            NavigationPrefill.Set(context, PrefillSlots.CreateRelease, prefill, "/releases/new");
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(CheckRun row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(CheckRun row)
        {
            return !String.IsNullOrEmpty(row.Label) ? row.Label! : row.Type.ToString();
        }

        /// <inheritdoc />
        protected override string? DetailPath(CheckRun row)
        {
            return "/checks/" + row.Id;
        }

        /// <inheritdoc />
        protected override bool HasCreate
        {
            get { return true; }
        }

        /// <inheritdoc />
        protected override bool CanCreate
        {
            get { return true; }
        }

        /// <inheritdoc />
        protected override string CreateLabel
        {
            get { return "Run Check"; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<CheckRun> grid)
        {
            grid.AddColumn(new GridColumn<CheckRun>("check", "Check", r => NameOf(r)) { Weight = 3, MinWidth = 14, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<CheckRun>("type", "Type", r => r.Type.ToString()) { Width = 18, Sortable = true });
            grid.AddColumn(new GridColumn<CheckRun>("vessel", "Vessel", r => EntityLookups.Name(_Vessels, r.VesselId)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<CheckRun>("status", "Status", r => StatusBadge.Label(r.Status.ToString())) { Width = 11, Sortable = true, Style = (r, t) => StatusBadge.Style(r.Status.ToString(), t) });
            grid.AddColumn(new GridColumn<CheckRun>("source", "Source", r => r.Source == CheckRunSourceEnum.External && !String.IsNullOrEmpty(r.ProviderName) ? r.Source + " / " + r.ProviderName : r.Source.ToString()) { Width = 16, Sortable = true });
            grid.AddColumn(new GridColumn<CheckRun>("environment", "Environment", r => EntityUi.Dash(r.EnvironmentName)) { Weight = 1, Sortable = true });
            grid.AddColumn(new GridColumn<CheckRun>("duration", "Duration", r => r.DurationMs.HasValue ? Math.Round((double)r.DurationMs.Value).ToString(CultureInfo.InvariantCulture) + " ms" : "-") { Width = 10, Sortable = true, Align = TUIKit.Widgets.CellAlignment.Right });
            grid.AddColumn(new GridColumn<CheckRun>("created", "Created", r => EntityUi.When(Context, r.CreatedUtc)) { Width = 12, Sortable = true });
            grid.AddColumn(new GridColumn<CheckRun>("results", "Results", r => CheckRunComparer.ParsingSummary(r)) { Weight = 2, DefaultVisible = false });
            grid.AddColumn(new GridColumn<CheckRun>("comparison", "Comparison", r => Comparison(r))
            {
                Weight = 4,
                MinWidth = 24,
                Style = (r, t) => _Comparisons.TryGetValue(r.Id, out CheckRunComparison? c) ? (c.HasRegression ? t.Error : c.HasImprovement ? t.Success : (TUIKit.CellStyle?)null) : null
            });
            grid.AddColumn(new GridColumn<CheckRun>("id", "ID", r => r.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No check runs match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSelect("vesselId", "All vessels", new List<SelectOption<string>>(), 22);
            filters.AddSelect("status", "All statuses", new List<SelectOption<string>>
            {
                new SelectOption<string>("Passed", T("Passed")),
                new SelectOption<string>("Failed", T("Failed")),
                new SelectOption<string>("Running", T("Running")),
                new SelectOption<string>("Pending", T("Pending")),
                new SelectOption<string>("Canceled", T("Canceled"))
            }, 18);
            filters.AddSelect("source", "All sources", new List<SelectOption<string>>
            {
                new SelectOption<string>("Armada", T("Armada")),
                new SelectOption<string>("External", T("External"))
            }, 16);
            filters.AddSelect("type", "All check types", EntityForm.EnumOptions<CheckRunTypeEnum>(), 24);
        }

        /// <inheritdoc />
        protected override void OnBuilt()
        {
            CheckRunRequest? prefill = NavigationPrefill.Take<CheckRunRequest>(Context, PrefillSlots.RunCheck);
            if (prefill != null) OpenRunCheck(prefill);
        }

        /// <inheritdoc />
        protected override async Task<GridPage<CheckRun>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(query.PageNumber, query.PageSize);
            q.With("vesselId", EntityUi.Blank(Filter("vesselId")));
            q.With("status", EntityUi.Blank(Filter("status")));
            q.With("source", EntityUi.Blank(Filter("source")));
            q.With("type", EntityUi.Blank(Filter("type")));
            EnumerationResult<CheckRun>? result = await Context.Client.ListCheckRunsAsync(q, token).ConfigureAwait(false);
            return PageOf(result);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(CheckRun row, string sortKey)
        {
            switch (sortKey)
            {
                case "created": return row.CreatedUtc;
                case "duration": return row.DurationMs ?? -1;
                case "status": return (int)row.Status;
                default: return null;
            }
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            Task<List<CheckRun>> all = ReadAllRunsAsync(token);
            Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(Context.Client, token);
            await Task.WhenAll(all, vessels).ConfigureAwait(false);
            _Vessels = vessels.Result.ToDictionary(v => v.Id, v => v.Name);
            _Comparisons = CheckRunComparer.BuildMap(all.Result);
            List<SelectOption<string>> vesselOptions = EntityLookups.Options(vessels.Result, v => v.Id, v => v.Name);
            Context.Dispatcher.Post(() => Filters.SetOptions("vesselId", vesselOptions));
            List<CheckRun> runs = all.Result;
            return new List<KpiItem>
            {
                new KpiItem("Total Runs", EntityUi.Number(Context, runs.Count)),
                new KpiItem("Passed", EntityUi.Number(Context, runs.Count(r => r.Status == CheckRunStatusEnum.Passed)), t => t.Success),
                new KpiItem("Failed", EntityUi.Number(Context, runs.Count(r => r.Status == CheckRunStatusEnum.Failed)), t => t.Error),
                new KpiItem("Running", EntityUi.Number(Context, runs.Count(r => r.Status == CheckRunStatusEnum.Running)), t => t.Info)
            };
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            OpenRunCheck(null);
        }

        /// <inheritdoc />
        protected override List<ActionMenuItem> RowActions(CheckRun row)
        {
            List<ActionMenuItem> items = base.RowActions(row);
            items.Insert(1, new ActionMenuItem("Draft Release", () => DraftRelease(Context, row)));
            return items;
        }

        #endregion

        #region Private-Methods

        private string Comparison(CheckRun run)
        {
            if (!_Comparisons.TryGetValue(run.Id, out CheckRunComparison? c)) return "";
            return "vs " + T(CheckRunComparer.ScopeLabel(c.Scope)) + ": " + CheckRunComparer.Summary(c);
        }

        private async Task<List<CheckRun>> ReadAllRunsAsync(CancellationToken token)
        {
            try
            {
                return await ArmadaPaging.ReadAllAsync<CheckRun>((p, ct) => Context.Client.ListCheckRunsAsync(new ArmadaPageQuery(p, EntityLookups.PageSize), ct), 20, token).ConfigureAwait(false);
            }
            catch (ArmadaApiException)
            {
                return new List<CheckRun>();
            }
        }

        #endregion
    }
}
