namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// A fleet's page (W4.5, <c>/fleets/:id</c>), the dashboard's FleetDetail: ID, name, description, default pipeline,
    /// active, created, and last updated; the fleet's vessels (name and id, repository URL, branch; <c>Enter</c> opens a
    /// vessel, <c>y</c> copies its URL); and Edit, Duplicate, View JSON, and Delete. Not thread-safe.
    /// </summary>
    public class FleetScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// Fleet id from the route.
        /// </summary>
        public string FleetId { get; }

        /// <summary>
        /// The fleet once loaded.
        /// </summary>
        public Fleet? Fleet { get; private set; } = null;

        /// <summary>
        /// Vessels in the fleet.
        /// </summary>
        public List<Vessel> Vessels { get; private set; } = new List<Vessel>();

        /// <summary>
        /// Pipelines (for names and the form).
        /// </summary>
        public List<Pipeline> Pipelines { get; private set; } = new List<Pipeline>();

        /// <summary>
        /// Overview panel.
        /// </summary>
        public OpsDocumentView Overview { get; } = new OpsDocumentView();

        /// <summary>
        /// Vessels panel.
        /// </summary>
        public ArmadaGrid<Vessel> VesselGrid { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public FleetScreen(RouteMatch route, TuiContext context)
            : base(route, context, "FleetScreen", "Fleet")
        {
            FleetId = route.Param("id") ?? "";
            VesselGrid = new ArmadaGrid<Vessel>(v => v.Id);
            VesselGrid.MultiSelect = false;
            VesselGrid.ShowPagingBar = false;
            VesselGrid.PageSize = 10000;
            VesselGrid.EmptyText = "No vessels in this fleet.";
            VesselGrid.ModalHost = context.Modals;
            VesselGrid.AddColumn(new GridColumn<Vessel>("name", "Name", v => v.Name + "  " + v.Id) { Weight = 3 });
            VesselGrid.AddColumn(new GridColumn<Vessel>("repoUrl", "Repo URL", v => String.IsNullOrEmpty(v.RepoUrl) ? "-" : v.RepoUrl!) { Weight = 4 });
            VesselGrid.AddColumn(new GridColumn<Vessel>("branch", "Branch", v => String.IsNullOrEmpty(v.DefaultBranch) ? "main" : v.DefaultBranch) { Weight = 1 });
            VesselGrid.Activated += (s, v) => Context.Navigate("/vessels/" + Uri.EscapeDataString(v.Id));

            Action("edit", "Edit", Edit, "e", () => Fleet != null, true);
            Action("duplicate", "Duplicate", Duplicate, "u", () => Fleet != null, true);
            Action("json", "View JSON", () => ShowJson(Tr("Fleet: {{name}}", LocalizationArgs.Of("name", Fleet!.Name)), Fleet), "j", () => Fleet != null, true);
            Action("delete", "Delete", Delete, "del", () => Fleet != null, true, true);
            Action("copy-id", "Copy ID", () => Copy(FleetId, "Fleet ID"), "y", () => !ReferenceEquals(CurrentPanel, VesselGrid));
            Action("copy-url", "Copy URL", () => Copy(VesselGrid.Current?.RepoUrl, "URL"), "y", () => ReferenceEquals(CurrentPanel, VesselGrid) && !String.IsNullOrEmpty(VesselGrid.Current?.RepoUrl));

            Overview.Builder = BuildOverview;
            AddPanel("overview", "Overview", Overview);
            AddPanel("vessels", "Vessels", VesselGrid);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Load()
        {
            bool initial = !Loaded;
            Call(async (c, t) =>
            {
                Task<EnumerationResult<Fleet>?> fleets = c.ListFleetsAsync(new ArmadaPageQuery(1, 9999), t);
                Task<EnumerationResult<Vessel>?> vessels = c.ListVesselsAsync(new ArmadaPageQuery(1, 9999), t);
                Task<EnumerationResult<Pipeline>?> pipelines = c.ListPipelinesAsync(new ArmadaPageQuery(1, 9999), t);
                await Task.WhenAll(fleets, vessels, pipelines).ConfigureAwait(false);
                FleetLoad result = new FleetLoad();
                result.Fleet = (fleets.Result?.Objects ?? new List<Fleet>()).FirstOrDefault(f => f.Id == FleetId);
                result.Vessels = (vessels.Result?.Objects ?? new List<Vessel>()).Where(v => v.FleetId == FleetId).ToList();
                result.Pipelines = pipelines.Result?.Objects ?? new List<Pipeline>();
                return result;
            }, r =>
            {
                if (r.Fleet == null)
                {
                    LoadError = Tr("Fleet not found.");
                    return;
                }

                Fleet = r.Fleet;
                Vessels = r.Vessels;
                Pipelines = r.Pipelines;
                VesselGrid.SetLocalRows(Vessels);
                Loaded = true;
                LoadError = null;
                Heading = Fleet.Name;
                SubtitleText = Tr("Fleets") + " > " + Fleet.Name;
                Overview.Invalidate();
            }, null, ex =>
            {
                if (initial) LoadError = Tr("Failed to load fleet.");
            });
        }

        #endregion

        #region Private-Methods

        private void Edit()
        {
            if (Fleet == null) return;
            FleetForm.Open(this, Fleet, Pipelines, f => Load());
        }

        private void Duplicate()
        {
            if (Fleet == null) return;
            Call((c, t) => c.CreateFleetAsync(FleetForm.DuplicatePayload(Fleet), t), created =>
            {
                if (created == null) return;
                Toast(NotificationSeverityEnum.Success, Tr("Fleet \"{{name}}\" duplicated.", LocalizationArgs.Of("name", created.Name)));
                Context.Navigate("/fleets/" + Uri.EscapeDataString(created.Id));
            }, null, ex => ShowMessage(ex is ArmadaApiException api ? api.Message : Tr("Duplicate failed.")));
        }

        private void Delete()
        {
            if (Fleet == null) return;
            string name = Fleet.Name;
            Confirm("Delete Fleet", Tr("Delete fleet \"{{name}}\"? This cannot be undone.", LocalizationArgs.Of("name", name)), () =>
            {
                Run((c, t) => c.DeleteFleetAsync(FleetId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Fleet \"{{name}}\" deleted.", LocalizationArgs.Of("name", name)));
                    Context.Navigate("/vessels?tab=fleets");
                }, null, ex => ShowMessage(Tr("Delete failed.")));
            }, "Delete");
        }

        private OpsDocument BuildOverview(OpsDocument doc)
        {
            Fleet? f = Fleet;
            if (f == null) return doc;
            DateTime now = Context.Clock.UtcNow;
            Pipeline? pipeline = Pipelines.FirstOrDefault(p => p.Id == f.DefaultPipelineId);
            doc.Section("Details");
            doc.Field("ID", f.Id);
            doc.Field("Name", f.Name);
            doc.Field("Description", String.IsNullOrEmpty(f.Description) ? "-" : f.Description);
            doc.Field("Default Pipeline", pipeline?.Name ?? (String.IsNullOrEmpty(f.DefaultPipelineId) ? Tr("None (WorkerOnly)") : f.DefaultPipelineId));
            doc.YesNo("Active", f.Active);
            doc.Time("Created", f.CreatedUtc, now);
            doc.Time("Last Updated", f.LastUpdateUtc, now);
            doc.Section("Vessels", " (" + Vessels.Count + ")");
            if (Vessels.Count == 0) doc.Note("No vessels in this fleet.");
            else
            {
                foreach (Vessel v in Vessels) doc.Text(v.Name + "  " + (String.IsNullOrEmpty(v.RepoUrl) ? "-" : v.RepoUrl) + "  [" + (String.IsNullOrEmpty(v.DefaultBranch) ? "main" : v.DefaultBranch) + "]");
                doc.Note("] " + Tr("Vessels") + ": Enter " + Tr("Open") + "  y " + Tr("Copy URL"));
            }

            return doc;
        }

        #endregion
    }
}
