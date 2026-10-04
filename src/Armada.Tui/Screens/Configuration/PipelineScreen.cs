namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Pipeline detail (dashboard <c>PipelineDetail.tsx</c>, route <c>/pipelines/:name</c>): actions Run (the Run
    /// Pipeline modal: vessel, title, objective; Launch Voyage opens the new voyage), View JSON, Edit stages, Duplicate,
    /// and Delete (not for built-in pipelines); panels Overview (ID, name, description, built-in, active, created, last
    /// updated) and Flow (the stage chain and the stages table: order, persona, optional, review, on deny,
    /// description).
    /// </summary>
    public class PipelineScreen : EntityDetailScreen<Pipeline>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Pipeline"; }
        }

        /// <summary>
        /// Overview panel.
        /// </summary>
        public LinkDetailView Overview { get; } = new LinkDetailView();

        /// <summary>
        /// Flow chain text.
        /// </summary>
        public TextBlock Flow { get; } = new TextBlock();

        /// <summary>
        /// Stages table.
        /// </summary>
        public ArmadaGrid<PipelineStage> Stages { get; } = new ArmadaGrid<PipelineStage>(s => s.Id);

        #endregion

        #region Private-Members

        private List<Vessel> _Vessels = new List<Vessel>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public PipelineScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<Pipeline?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetPipelineAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override async Task FetchRelatedAsync(Pipeline entity, CancellationToken token)
        {
            _Vessels = await EntityLookups.VesselsAsync(Context.Client, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            AddAction("run", "Run", () => { if (Entity != null) PipelineForms.Run(Context, Entity, _Vessels); }, () => Entity != null && CanManage() && Entity.Stages.Count > 0, "r");
            AddJsonAction();
            AddAction("edit", "Edit stages", () => { if (Entity != null) PipelineForms.Open(Context, Entity, true, n => Reload()); }, () => Entity != null && CanManage(), "e");
            AddAction("duplicate", "Duplicate", () => { if (Entity != null) PipelineForms.Duplicate(Context, Entity); }, () => Entity != null);
            AddAction("delete", "Delete", RequestDelete, () => Entity != null && !Entity.IsBuiltIn && CanManage(), "delete");
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            Overview.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            AddPanel("overview", "Overview", Overview);
            Stages.Dispatcher = Context.Dispatcher;
            Stages.ModalHost = Context.Modals;
            Stages.MultiSelect = false;
            Stages.EmptyText = "No stages defined.";
            Stages.AddColumn(new GridColumn<PipelineStage>("order", "Order", s => s.Order.ToString(CultureInfo.InvariantCulture)) { Width = 6 });
            Stages.AddColumn(new GridColumn<PipelineStage>("persona", "Persona Name", s => s.PersonaName) { Weight = 2 });
            Stages.AddColumn(new GridColumn<PipelineStage>("optional", "Optional", s => s.IsOptional ? T("Yes") : T("No")) { Width = 9 });
            Stages.AddColumn(new GridColumn<PipelineStage>("review", "Review", s => s.RequiresReview ? T("Required") : T("None")) { Width = 10, Style = (s, t) => s.RequiresReview ? t.Warning : t.Muted });
            Stages.AddColumn(new GridColumn<PipelineStage>("deny", "On Deny", s => s.RequiresReview ? PipelineForms.DenyLabel(Context, s.ReviewDenyAction) : "-") { Width = 14 });
            Stages.AddColumn(new GridColumn<PipelineStage>("description", "Description", s => EntityUi.Dash(s.Description)) { Weight = 3 });
            Stages.Activated += (s, stage) => Context.Navigate("/personas/" + Uri.EscapeDataString(stage.PersonaName));
            Flow.Translate = false;
            StackPanel flow = new StackPanel();
            flow.Add(Flow, 2, "Flow");
            flow.Add(Stages, null, "Stages");
            AddPanel("flow", "Flow", flow);
        }

        /// <inheritdoc />
        protected override string HeaderTitle(Pipeline entity)
        {
            return entity.Name;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(Pipeline entity)
        {
            List<string> statuses = new List<string> { entity.Active ? "Active" : "Inactive" };
            if (entity.IsBuiltIn) statuses.Add("Built-in");
            return statuses;
        }

        /// <inheritdoc />
        protected override void Populate(Pipeline p)
        {
            Overview.Reset();
            Overview.Row("ID", p.Id, t => t.Code);
            Overview.Row("Name", p.Name);
            Overview.Row("Description", EntityUi.Dash(p.Description));
            Overview.Row("Built-in", p.IsBuiltIn ? T("Yes") : T("No"));
            Overview.Row("Active", p.Active ? T("Yes") : T("No"));
            Overview.Row("Visibility", T(ScopeRules.Label(p.Scope)));
            Overview.Row("Stages", p.Stages.Count.ToString(CultureInfo.InvariantCulture));
            Overview.Row("Created", EntityUi.Date(Context, p.CreatedUtc));
            Overview.Row("Last Updated", EntityUi.Date(Context, p.LastUpdateUtc));
            List<PipelineStage> ordered = p.Stages.OrderBy(s => s.Order).ToList();
            Flow.Text = ordered.Count == 0 ? T("No stages defined.") : String.Join("  ->  ", ordered.Select((s, i) =>
                T("Stage") + " " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + s.PersonaName
                + (s.IsOptional ? " [" + T("Optional") + "]" : "")
                + (s.RequiresReview ? " [" + T("Review") + "]" : "")));
            Stages.SetLocalRows(ordered);
        }

        #endregion

        #region Private-Methods

        private bool CanManage()
        {
            Pipeline? p = Entity;
            return p != null && ScopeRules.CanEdit(Context.Session, p.Scope, p.TenantId, p.UserId);
        }

        private void RequestDelete()
        {
            Pipeline? p = Entity;
            if (p == null) return;
            if (p.IsBuiltIn)
            {
                EntityUi.Toast(Context, NotificationSeverityEnum.Error, T("Built-in pipelines cannot be deleted."));
                return;
            }

            Context.Confirm("Delete Pipeline", EntityUi.T(Context, "Delete pipeline \"{{name}}\"? This cannot be undone.", "name", p.Name), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeletePipelineAsync(p.Name, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, EntityUi.T(Context, "Pipeline \"{{name}}\" deleted.", "name", p.Name));
                    Context.Navigate("/configuration?tab=pipelines");
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
