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
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Release detail (dashboard <c>ReleaseDetail.tsx</c>, routes <c>/releases/:id</c> and <c>/releases/new</c>, which
    /// takes the <see cref="PrefillSlots.CreateRelease"/> prefill including backlog items): actions Deploy and Run Check
    /// (prefilled hand-offs), View JSON, Refresh Derived Fields, Save, Delete, and Open Backlog Item (create mode with a
    /// backlog prefill); panels Overview (identifiers, timestamps, the release fields), Release (the editor for tenant
    /// admins), Linked Work (voyages, missions, and checks, each opening its record), Backlog Items (with View History
    /// and Open Backlog Item), Deployment Evidence, Artifacts (source, path, size, last write), and GitHub Pull
    /// Requests (state, review status, checks, reviews, reviewers, updated; Enter opens the pull request in a browser).
    /// </summary>
    public class ReleaseScreen : EntityDetailScreen<Release>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Release"; }
        }

        /// <inheritdoc />
        public override bool IsCreateMode
        {
            get { return EntityId.Length == 0 || String.Equals(EntityId, "new", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// Overview panel.
        /// </summary>
        public LinkDetailView Overview { get; } = new LinkDetailView();

        /// <summary>
        /// Linked work panel.
        /// </summary>
        public LinkDetailView LinkedWork { get; } = new LinkDetailView();

        /// <summary>
        /// Linked backlog items.
        /// </summary>
        public ArmadaGrid<Objective> BacklogItems { get; } = new ArmadaGrid<Objective>(o => o.Id);

        /// <summary>
        /// Linked deployments.
        /// </summary>
        public ArmadaGrid<Deployment> Deployments { get; } = new ArmadaGrid<Deployment>(d => d.Id);

        /// <summary>
        /// Artifacts.
        /// </summary>
        public ArmadaGrid<ReleaseArtifact> Artifacts { get; } = new ArmadaGrid<ReleaseArtifact>(a => (a.SourceId ?? "source") + "-" + a.Path);

        /// <summary>
        /// GitHub pull requests.
        /// </summary>
        public ArmadaGrid<GitHubPullRequestDetail> PullRequests { get; } = new ArmadaGrid<GitHubPullRequestDetail>(p => p.Repository + "#" + p.Number.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// Editor fields (null for users who cannot manage releases).
        /// </summary>
        public ReleaseForms? Editor { get; private set; } = null;

        /// <summary>
        /// Backlog items a create prefill links to.
        /// </summary>
        public List<string> PrefillObjectiveIds { get; private set; } = new List<string>();

        #endregion

        #region Private-Members

        private List<Vessel> _Vessels = new List<Vessel>();
        private List<WorkflowProfile> _Profiles = new List<WorkflowProfile>();
        private Dictionary<string, string> _Voyages = new Dictionary<string, string>();
        private Dictionary<string, string> _Checks = new Dictionary<string, string>();
        private List<Deployment> _Deployments = new List<Deployment>();
        private List<Objective> _Objectives = new List<Objective>();
        private List<GitHubPullRequestDetail> _PullRequests = new List<GitHubPullRequestDetail>();
        private bool _Saving = false;
        private bool _Refreshing = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public ReleaseScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Save the editor (create or update).
        /// </summary>
        public void Save()
        {
            if (Editor == null || _Saving || !Context.Session.IsTenantAdmin) return;
            if (!Editor.Form.View.ValidateAll()) return;
            ReleaseUpsertRequest payload = Editor.BuildPayload(PrefillObjectiveIds);
            bool create = IsCreateMode;
            _Saving = true;
            EntityUi.Run<Release?>(Context, ct => create
                ? Context.Client.CreateReleaseAsync(payload, ct)
                : Context.Client.UpdateReleaseAsync(EntityId, payload, ct), saved =>
            {
                _Saving = false;
                if (saved == null) return;
                if (create)
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Release \"{{title}}\" created.", "title", saved.Title));
                    Context.Navigate("/releases/" + saved.Id);
                    return;
                }

                EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Release \"{{title}}\" saved.", "title", saved.Title));
                Entity = saved;
                Populate(saved);
            }, "Save failed.", ex => _Saving = false);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string CreateTitle
        {
            get { return "Create Release"; }
        }

        /// <inheritdoc />
        protected override Task<Release?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetReleaseAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override async Task FetchRelatedAsync(Release entity, CancellationToken token)
        {
            Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(Context.Client, token);
            Task<List<WorkflowProfile>> profiles = EntityLookups.WorkflowProfilesAsync(Context.Client, token);
            Task<List<Voyage>> voyages = SafeAll<Voyage>((p, ct) => Context.Client.ListVoyagesAsync(new ArmadaPageQuery(p, EntityLookups.PageSize), ct), token);
            Task<List<CheckRun>> checks = SafeAll<CheckRun>((p, ct) => Context.Client.ListCheckRunsAsync(new ArmadaPageQuery(p, EntityLookups.PageSize), ct), token);
            Task<List<Deployment>> deployments = SafeAll<Deployment>((p, ct) => Context.Client.ListDeploymentsAsync(new DeploymentQuery { ReleaseId = entity.Id, PageNumber = p, PageSize = EntityLookups.PageSize }, ct), token);
            Task<List<Objective>> objectives = SafeAll<Objective>((p, ct) => Context.Client.ListObjectivesAsync(new ObjectiveQuery { ReleaseId = entity.Id, PageNumber = p, PageSize = EntityLookups.PageSize }, ct), token);
            Task<List<GitHubPullRequestDetail>> prs = SafePullRequests(entity.Id, token);
            await Task.WhenAll(vessels, profiles, voyages, checks, deployments, objectives, prs).ConfigureAwait(false);
            _Vessels = vessels.Result;
            _Profiles = profiles.Result;
            _Voyages = voyages.Result.ToDictionary(v => v.Id, v => String.IsNullOrEmpty(v.Title) ? v.Id : v.Title);
            _Checks = checks.Result.ToDictionary(c => c.Id, c => String.IsNullOrEmpty(c.Label) ? c.Type.ToString() : c.Label!);
            _Deployments = deployments.Result.Where(d => d.ReleaseId == entity.Id).ToList();
            _Objectives = objectives.Result.Where(o => o.ReleaseIds.Contains(entity.Id)).ToList();
            _PullRequests = prs.Result;
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            Func<bool> manage = () => Context.Session.IsTenantAdmin;
            Func<bool> loaded = () => Entity != null && !IsCreateMode;
            AddAction("deploy", "Deploy", OpenDeploy, loaded);
            AddAction("run-check", "Run Check", OpenRunCheck, loaded);
            AddJsonAction();
            AddAction("refresh-derived", "Refresh Derived Fields", RefreshDerived, () => loaded() && manage());
            AddAction("save", "Save Changes", Save, () => manage() && (IsCreateMode || Entity != null), "ctrl+s");
            AddAction("open-backlog", "Open Backlog Item", () => { if (PrefillObjectiveIds.Count > 0) Context.Navigate("/backlog/" + PrefillObjectiveIds[0]); }, () => IsCreateMode && PrefillObjectiveIds.Count > 0);
            AddAction("delete", "Delete", RequestDelete, () => loaded() && manage(), "delete");
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            Overview.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            LinkedWork.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            if (!IsCreateMode) AddPanel("overview", "Overview", Overview);
            if (Context.Session.IsTenantAdmin)
            {
                Editor = new ReleaseForms(Context, 8);
                Editor.Form.View.SaveButton.Label = IsCreateMode ? "Create Release" : "Save Changes";
                Editor.Form.View.DiscardButton.Label = "Discard";
                Editor.Form.View.SaveRequested += (s, e) => Save();
                Editor.Form.View.DiscardRequested += (s, e) =>
                {
                    if (Entity != null)
                    {
                        Editor.Fill(Entity);
                        Editor.Form.MarkClean();
                    }
                };
                AddPanel("edit", "Release", Editor.Form.View);
            }

            if (IsCreateMode) return;
            AddPanel("linked", "Linked Work", LinkedWork);

            Prepare(BacklogItems, "No backlog items currently reference this release.");
            BacklogItems.AddColumn(new GridColumn<Objective>("title", "Title", o => o.Title) { Weight = 3, Sortable = true });
            BacklogItems.AddColumn(new GridColumn<Objective>("status", "Status", o => StatusBadge.Label(o.Status.ToString())) { Width = 14, Sortable = true, Style = (o, t) => StatusBadge.Style(o.Status.ToString(), t) });
            BacklogItems.AddColumn(new GridColumn<Objective>("description", "Description", o => EntityUi.Dash(o.Description)) { Weight = 3 });
            BacklogItems.AddColumn(new GridColumn<Objective>("id", "ID", o => o.Id) { Width = 26 });
            BacklogItems.Activated += (s, o) => Context.Navigate("/backlog/" + o.Id);
            BacklogItems.MenuRequested += (s, o) => ActionMenu.Show(Context.Modals, o.Title, new List<ActionMenuItem>
            {
                new ActionMenuItem("View History", () => Context.Navigate("/activity?source=history&objectiveId=" + Uri.EscapeDataString(o.Id))),
                new ActionMenuItem("Open Backlog Item", () => Context.Navigate("/backlog/" + o.Id), "Enter")
            }, Context.Loc, Context.Theme.Current);
            AddPanel("backlog", "Linked Backlog Items", BacklogItems);

            Prepare(Deployments, "No deployments are linked to this release yet.");
            Deployments.AddColumn(new GridColumn<Deployment>("title", "Deployment", d => d.Title) { Weight = 3, Sortable = true });
            Deployments.AddColumn(new GridColumn<Deployment>("status", "Status", d => StatusBadge.Label(d.Status.ToString())) { Width = 20, Sortable = true, Style = (d, t) => StatusBadge.Style(d.Status.ToString(), t) });
            Deployments.AddColumn(new GridColumn<Deployment>("verification", "Verification", d => StatusBadge.Label(d.VerificationStatus.ToString())) { Width = 14, Style = (d, t) => StatusBadge.Style(d.VerificationStatus.ToString(), t) });
            Deployments.AddColumn(new GridColumn<Deployment>("environment", "Environment", d => String.IsNullOrEmpty(d.EnvironmentName) ? T("No environment") : d.EnvironmentName!) { Weight = 2 });
            Deployments.AddColumn(new GridColumn<Deployment>("checks", "Checks", d => d.CheckRunIds.Count.ToString(CultureInfo.InvariantCulture)) { Width = 7 });
            Deployments.AddColumn(new GridColumn<Deployment>("requests", "Requests", d => (d.RequestHistorySummary?.TotalCount ?? 0).ToString(CultureInfo.InvariantCulture)) { Width = 9 });
            Deployments.AddColumn(new GridColumn<Deployment>("monitoring", "Latest monitoring summary", d => EntityUi.Dash(d.LatestMonitoringSummary)) { Weight = 3, DefaultVisible = false });
            Deployments.Activated += (s, d) => Context.Navigate("/deployments/" + d.Id);
            AddPanel("deployments", "Deployment Evidence", Deployments);

            Prepare(Artifacts, "No artifacts are currently linked to this release. Refresh the release after relevant check runs complete to rebuild derived artifact metadata.");
            Artifacts.AddColumn(new GridColumn<ReleaseArtifact>("source", "Source", a => a.SourceType + (String.IsNullOrEmpty(a.SourceId) ? "" : " " + a.SourceId)) { Weight = 2, Sortable = true });
            Artifacts.AddColumn(new GridColumn<ReleaseArtifact>("path", "Path", a => a.Path) { Weight = 4, Sortable = true });
            Artifacts.AddColumn(new GridColumn<ReleaseArtifact>("size", "Size", a => a.SizeBytes.ToString("N0", Context.Loc.Culture) + " " + T("bytes")) { Width = 16, Align = TUIKit.Widgets.CellAlignment.Right });
            Artifacts.AddColumn(new GridColumn<ReleaseArtifact>("lastWrite", "Last Write", a => EntityUi.Date(Context, a.LastWriteUtc)) { Width = 20 });
            Artifacts.Activated += (s, a) => { if (!String.IsNullOrEmpty(a.SourceId)) Context.Navigate("/checks/" + a.SourceId); };
            AddPanel("artifacts", "Artifacts", Artifacts);

            Prepare(PullRequests, "No linked GitHub pull requests were found for this release yet.");
            PullRequests.AddColumn(new GridColumn<GitHubPullRequestDetail>("title", "Title", p => p.Title) { Weight = 3, Sortable = true });
            PullRequests.AddColumn(new GridColumn<GitHubPullRequestDetail>("repo", "Repository", p => p.Repository + " #" + p.Number.ToString(CultureInfo.InvariantCulture)) { Weight = 2, Sortable = true });
            PullRequests.AddColumn(new GridColumn<GitHubPullRequestDetail>("state", "State", p => StatusBadge.Label(p.State)) { Width = 12, Style = (p, t) => StatusBadge.Style(p.State, t) });
            PullRequests.AddColumn(new GridColumn<GitHubPullRequestDetail>("review", "Review", p => StatusBadge.Label(p.ReviewStatus)) { Width = 18, Style = (p, t) => StatusBadge.Style(p.ReviewStatus, t) });
            PullRequests.AddColumn(new GridColumn<GitHubPullRequestDetail>("checks", "Checks", p => p.Checks.Count.ToString(CultureInfo.InvariantCulture)) { Width = 7 });
            PullRequests.AddColumn(new GridColumn<GitHubPullRequestDetail>("reviews", "Reviews", p => p.Reviews.Count.ToString(CultureInfo.InvariantCulture)) { Width = 8 });
            PullRequests.AddColumn(new GridColumn<GitHubPullRequestDetail>("reviewers", "Reviewers", p => p.RequestedReviewers.Count > 0 ? String.Join(", ", p.RequestedReviewers) : "-") { Weight = 2 });
            PullRequests.AddColumn(new GridColumn<GitHubPullRequestDetail>("updated", "Updated", p => EntityUi.When(Context, p.UpdatedUtc)) { Width = 12 });
            PullRequests.Activated += (s, p) => Context.External.OpenUrl(p.Url);
            AddPanel("pulls", "GitHub Pull Requests", PullRequests);
        }

        /// <inheritdoc />
        protected override void OnCreateMode()
        {
            if (Editor == null) return;
            ReleaseUpsertRequest? prefill = NavigationPrefill.Take<ReleaseUpsertRequest>(Context, PrefillSlots.CreateRelease);
            if (prefill != null)
            {
                Editor.Fill(prefill);
                PrefillObjectiveIds = prefill.ObjectiveIds ?? new List<string>();
            }

            Editor.Form.MarkClean();
            EntityUi.Run(Context, async ct =>
            {
                Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(Context.Client, ct);
                Task<List<WorkflowProfile>> profiles = EntityLookups.WorkflowProfilesAsync(Context.Client, ct);
                await Task.WhenAll(vessels, profiles).ConfigureAwait(false);
                DeploymentReferenceData data = new DeploymentReferenceData();
                data.Vessels = vessels.Result;
                data.Profiles = profiles.Result;
                return data;
            }, data =>
            {
                string? vessel = Editor.Vessel.Value;
                string? profile = Editor.Profile.Value;
                Editor.SetReferenceData(data.Vessels, data.Profiles);
                Editor.Vessel.SetValue(vessel ?? "");
                Editor.Profile.SetValue(profile ?? "");
                Editor.Form.MarkClean();
            }, "Failed to load release reference data.");
        }

        /// <inheritdoc />
        protected override string HeaderTitle(Release entity)
        {
            return entity.Title;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(Release entity)
        {
            return new[] { entity.Status.ToString() };
        }

        /// <inheritdoc />
        protected override void Populate(Release r)
        {
            Dictionary<string, string> vessels = _Vessels.ToDictionary(v => v.Id, v => v.Name);
            Dictionary<string, string> profiles = _Profiles.ToDictionary(p => p.Id, p => p.Name);
            Overview.Reset();
            if (!Context.Session.IsTenantAdmin) Overview.Row("Access", T("You can view releases, but only tenant administrators can create or change them."), t => t.Warning);
            Overview.Section("Overview");
            Overview.Row("ID", r.Id, t => t.Code);
            Overview.Row("Created", EntityUi.Date(Context, r.CreatedUtc));
            Overview.Row("Last Updated", EntityUi.When(Context, r.LastUpdateUtc));
            Overview.Row("Published", EntityUi.Date(Context, r.PublishedUtc));
            Overview.Section("Release");
            Overview.Row("Status", EntityUi.Badge(Context, r.Status.ToString()), t => StatusBadge.Style(r.Status.ToString(), t));
            Overview.Link("Vessel", EntityLookups.Name(vessels, r.VesselId), !String.IsNullOrEmpty(r.VesselId) ? () => Context.Navigate("/vessels/" + r.VesselId) : (Action?)null);
            Overview.Link("Workflow Profile", !String.IsNullOrEmpty(r.WorkflowProfileId) ? EntityLookups.Name(profiles, r.WorkflowProfileId) : T("Resolved default workflow profile"),
                !String.IsNullOrEmpty(r.WorkflowProfileId) ? () => Context.Navigate("/workflow-profiles/" + r.WorkflowProfileId) : (Action?)null);
            Overview.Row("Version", String.IsNullOrEmpty(r.Version) ? T("Unversioned") : r.Version);
            Overview.Row("Tag Name", EntityUi.Dash(r.TagName));
            Overview.Row("Summary", EntityUi.Dash(r.Summary));
            Overview.Row("Notes", EntityUi.Dash(r.Notes));

            LinkedWork.Reset();
            LinkedWork.Section("Linked Voyages");
            if (r.VoyageIds.Count == 0) LinkedWork.Row("Voyage", T("None"));
            foreach (string id in r.VoyageIds) { string v = id; LinkedWork.Link(EntityLookups.Name(_Voyages, v), v, () => Context.Navigate("/voyages/" + v)); }
            LinkedWork.Section("Linked Missions");
            if (r.MissionIds.Count == 0) LinkedWork.Row("Mission", T("None"));
            foreach (string id in r.MissionIds) { string m = id; LinkedWork.Link("Mission", m, () => Context.Navigate("/missions/" + m)); }
            LinkedWork.Section("Linked Checks");
            if (r.CheckRunIds.Count == 0) LinkedWork.Row("Check", T("None"));
            foreach (string id in r.CheckRunIds) { string c = id; LinkedWork.Link(EntityLookups.Name(_Checks, c), c, () => Context.Navigate("/checks/" + c)); }

            if (!IsCreateMode)
            {
                BacklogItems.SetLocalRows(_Objectives);
                Deployments.SetLocalRows(_Deployments);
                Artifacts.SetLocalRows(r.Artifacts);
                PullRequests.SetLocalRows(_PullRequests);
            }

            if (Editor != null)
            {
                Editor.SetReferenceData(_Vessels, _Profiles);
                Editor.Fill(r);
                Editor.Form.MarkClean();
            }
        }

        #endregion

        #region Private-Methods

        private void Prepare<TRow>(ArmadaGrid<TRow> grid, string empty)
        {
            grid.Dispatcher = Context.Dispatcher;
            grid.ModalHost = Context.Modals;
            grid.MultiSelect = false;
            grid.EmptyText = empty;
        }

        private static async Task<List<TRow>> SafeAll<TRow>(Func<int, CancellationToken, Task<EnumerationResult<TRow>?>> page, CancellationToken token)
        {
            try
            {
                return await ArmadaPaging.ReadAllAsync(page, 20, token).ConfigureAwait(false);
            }
            catch (ArmadaApiException)
            {
                return new List<TRow>();
            }
        }

        private async Task<List<GitHubPullRequestDetail>> SafePullRequests(string id, CancellationToken token)
        {
            try
            {
                return await Context.Client.GetReleaseGitHubPullRequestsAsync(id, token).ConfigureAwait(false) ?? new List<GitHubPullRequestDetail>();
            }
            catch (ArmadaApiException)
            {
                return new List<GitHubPullRequestDetail>();
            }
        }

        private void RefreshDerived()
        {
            Release? r = Entity;
            if (r == null || _Refreshing) return;
            _Refreshing = true;
            EntityUi.Toast(Context, NotificationSeverityEnum.Info, T("Refreshing..."));
            EntityUi.Run<Release?>(Context, ct => Context.Client.RefreshReleaseAsync(r.Id, ct), refreshed =>
            {
                _Refreshing = false;
                if (refreshed == null) return;
                EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Release \"{{title}}\" refreshed from linked work.", "title", refreshed.Title));
                Reload();
            }, "Refresh failed.", ex => _Refreshing = false);
        }

        private void RequestDelete()
        {
            Release? r = Entity;
            if (r == null) return;
            Context.Confirm("Delete Release", EntityUi.T(Context, "Delete \"{{title}}\"? This removes only the release record.", "title", r.Title), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeleteReleaseAsync(r.Id, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, EntityUi.T(Context, "Release \"{{title}}\" deleted.", "title", r.Title));
                    Context.Navigate("/delivery?tab=releases");
                }, "Delete failed.");
            }, "Delete");
        }

        private void OpenDeploy()
        {
            Release? r = Entity;
            if (r == null) return;
            DeploymentUpsertRequest prefill = new DeploymentUpsertRequest();
            prefill.VesselId = EntityUi.Blank(r.VesselId);
            prefill.WorkflowProfileId = EntityUi.Blank(r.WorkflowProfileId);
            prefill.ReleaseId = r.Id;
            prefill.VoyageId = r.VoyageIds.FirstOrDefault();
            prefill.MissionId = r.MissionIds.FirstOrDefault();
            prefill.Title = r.Title + " Deploy";
            prefill.SourceRef = EntityUi.Blank(r.TagName) ?? EntityUi.Blank(r.Version);
            prefill.Summary = EntityUi.Blank(r.Summary);
            NavigationPrefill.Set(Context, PrefillSlots.CreateDeployment, prefill, "/deployments/new");
        }

        private void OpenRunCheck()
        {
            Release? r = Entity;
            if (r == null) return;
            CheckRunRequest prefill = new CheckRunRequest();
            prefill.VesselId = r.VesselId ?? "";
            prefill.WorkflowProfileId = EntityUi.Blank(r.WorkflowProfileId);
            prefill.VoyageId = r.VoyageIds.FirstOrDefault();
            prefill.MissionId = r.MissionIds.FirstOrDefault();
            prefill.Label = r.Title;
            NavigationPrefill.Set(Context, PrefillSlots.RunCheck, prefill, "/delivery?tab=checks");
        }

        #endregion
    }
}
