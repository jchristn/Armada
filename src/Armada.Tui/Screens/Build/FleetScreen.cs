namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
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
                Task<FleetDetail?> fleet = c.GetFleetAsync(FleetId, t);
                Task<List<Vessel>> vessels = LoadFleetVesselsAsync(c, t);
                Task<List<Pipeline>> pipelines = ArmadaPaging.ReadAllAsync<Pipeline>((p, ct) => c.ListPipelinesAsync(new ArmadaPageQuery(p, 1000), ct), ArmadaPaging.DefaultMaxPages, t);
                await Task.WhenAll(fleet, vessels, pipelines).ConfigureAwait(false);
                FleetLoad result = new FleetLoad();
                result.Fleet = fleet.Result?.Fleet;
                result.Vessels = vessels.Result;
                result.Pipelines = pipelines.Result;
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

        private async Task<List<Vessel>> LoadFleetVesselsAsync(ArmadaClient client, CancellationToken token)
        {
            // The fleet read carries only a vessel count; walk the fleet's vessels one bounded page at a time.
            List<Vessel> all = new List<Vessel>();
            int page = 1;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                EnumerationResult<Vessel>? result = await client.ListVesselsAsync(new ArmadaPageQuery(page, 1000).With("fleetId", FleetId), token).ConfigureAwait(false);
                if (result == null || result.Objects == null || result.Objects.Count == 0) break;
                all.AddRange(result.Objects);
                if (page >= result.TotalPages) break;
                page++;
            }

            return all;
        }

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
                DeleteProgress.RunVoid(Context, "Deleting fleet", t => Context.Client.DeleteFleetAsync(FleetId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Fleet \"{{name}}\" deleted.", LocalizationArgs.Of("name", name)));
                    Context.Navigate("/vessels?tab=fleets");
                }, "Delete failed.");
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
