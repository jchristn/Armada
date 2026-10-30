namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// A vessel's page (W4.4, <c>/vessels/:id</c>), the dashboard's VesselDetail: Manage Objectives, Manage Fleet,
    /// Onboarding, Run Check, Open Workspace, Health (the health inspector with Re-evaluate), History, and Cleanup as buttons, plus Edit,
    /// Duplicate, View JSON, Delete, Copy ID, and Copy Harbor ID (when a Harbor has the checkout); the Readiness and
    /// Landing Preview panels, every field (branch prefixes and
    /// policies, auto-approve, the auto-land and Definition-of-Done gates, the GitHub token state), the context
    /// blocks (project context, style guide, protected branches, dock boundary, model context), and the vessel's
    /// missions, plus the black box prevention panels: Reservations (the soft directory reservations active missions
    /// hold, server paged; Enter opens the mission) and Black Box Doctor (the configuration recommendations that keep
    /// recurring on the vessel, server paged; <c>A</c> applies an actionable one through the dry run and confirm flow,
    /// Enter opens its example entry). <c>L</c> toggles Serialize Overlapping Missions. <c>?edit=1</c> (after
    /// Duplicate) opens the edit form once. Not thread-safe.
    /// </summary>
    public class VesselScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// Vessel id from the route.
        /// </summary>
        public string VesselId { get; }

        /// <summary>
        /// The vessel once loaded.
        /// </summary>
        public Vessel? Vessel { get; private set; } = null;

        /// <summary>
        /// Fleets (names and the form).
        /// </summary>
        public List<Fleet> Fleets { get; private set; } = new List<Fleet>();

        /// <summary>
        /// Pipelines (names and the form).
        /// </summary>
        public List<Pipeline> Pipelines { get; private set; } = new List<Pipeline>();

        /// <summary>
        /// The vessel's missions.
        /// </summary>
        public List<MissionSummary> Missions { get; private set; } = new List<MissionSummary>();

        /// <summary>
        /// Readiness, or null.
        /// </summary>
        public VesselReadinessResult? Readiness { get; private set; } = null;

        /// <summary>
        /// Landing preview, or null.
        /// </summary>
        public LandingPreviewResult? LandingPreview { get; private set; } = null;

        /// <summary>
        /// Overview panel (readiness, landing preview, fields).
        /// </summary>
        public OpsDocumentView Overview { get; } = new OpsDocumentView();

        /// <summary>
        /// Context panel (project context, style guide, policies, dock boundary, model context).
        /// </summary>
        public OpsDocumentView ContextView { get; } = new OpsDocumentView();

        /// <summary>
        /// Missions panel.
        /// </summary>
        public ArmadaGrid<MissionSummary> MissionGrid { get; }

        /// <summary>
        /// Health evaluation tracker (the Health button's Re-evaluate).
        /// </summary>
        public HealthEvaluation Evaluation { get; }

        /// <summary>
        /// Soft directory reservations panel (server paged).
        /// </summary>
        public ArmadaGrid<VesselReservation> Reservations { get; }

        /// <summary>
        /// Black box doctor panel (server paged).
        /// </summary>
        public ArmadaGrid<BlackBoxDoctorFinding> Doctor { get; }

        /// <summary>
        /// The last reservations page (threshold and scan details), or null.
        /// </summary>
        public VesselReservationPage? ReservationPage { get; private set; } = null;

        /// <summary>
        /// The last doctor page (window and scan details), or null.
        /// </summary>
        public BlackBoxDoctorPage? DoctorPage { get; private set; } = null;

        #endregion

        #region Private-Members

        private bool _LoadingReadiness = false;
        private bool _LoadingPreview = false;
        private bool _EditHandled = false;
        private VesselHealthDialog? _HealthDialog = null;
        private bool _ReservationsLoaded = false;
        private bool _DoctorLoaded = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public VesselScreen(RouteMatch route, TuiContext context)
            : base(route, context, "VesselScreen", "Vessel")
        {
            VesselId = route.Param("id") ?? "";
            Evaluation = new HealthEvaluation(this);
            Evaluation.Finished += (s, job) =>
            {
                Toast(job.Status == JobStatusEnum.Succeeded ? NotificationSeverityEnum.Success : NotificationSeverityEnum.Warning,
                    job.Status == JobStatusEnum.Succeeded ? Tr("Evaluation finished.") : Tr("Evaluation ended with status {{status}}.", LocalizationArgs.Of("status", Tr(job.Status.ToString()))));
                _HealthDialog?.Load();
            };
            Track(Evaluation);
            MissionGrid = new ArmadaGrid<MissionSummary>(m => m.Id);
            MissionGrid.MultiSelect = false;
            MissionGrid.ShowPagingBar = false;
            MissionGrid.PageSize = 10000;
            MissionGrid.EmptyText = "No missions yet";
            MissionGrid.ModalHost = context.Modals;
            MissionGrid.AddColumn(new GridColumn<MissionSummary>("title", "Mission", m => m.Title + "  " + m.Id) { Weight = 4 });
            GridColumn<MissionSummary> status = new GridColumn<MissionSummary>("status", "Status", m => StatusBadge.Label(m.Status)) { Width = 16 };
            status.Style = (m, t) => StatusBadge.Style(m.Status, t);
            MissionGrid.AddColumn(status);
            MissionGrid.AddColumn(new GridColumn<MissionSummary>("captain", "Captain", m => String.IsNullOrEmpty(m.CaptainId) ? "-" : m.CaptainId!) { Weight = 2 });
            MissionGrid.AddColumn(new GridColumn<MissionSummary>("branch", "Branch", m => String.IsNullOrEmpty(m.BranchName) ? "-" : m.BranchName!) { Weight = 3 });
            MissionGrid.Activated += (s, m) => Context.Navigate("/missions/" + Uri.EscapeDataString(m.Id));

            Reservations = new ArmadaGrid<VesselReservation>(r => r.Directory + "|" + r.MissionId);
            Reservations.MultiSelect = false;
            Reservations.ModalHost = context.Modals;
            Reservations.Dispatcher = context.Dispatcher;
            Reservations.PageSize = 25;
            Reservations.Loader = LoadReservationsAsync;
            Reservations.EmptyText = "No directory reservations: no active mission works in a conflict hotspot.";
            Reservations.AddColumn(new GridColumn<VesselReservation>("directory", "Directory", r => r.Directory) { Weight = 3 });
            Reservations.AddColumn(new GridColumn<VesselReservation>("mission", "Mission", r => (String.IsNullOrEmpty(r.MissionTitle) ? "" : r.MissionTitle + "  ") + r.MissionId) { Weight = 4 });
            Reservations.AddColumn(new GridColumn<VesselReservation>("status", "Status", r => r.MissionStatus ?? "-") { Width = 14 });
            Reservations.AddColumn(new GridColumn<VesselReservation>("hotspot", "Failures", r => r.HotspotEntryCount.ToString(System.Globalization.CultureInfo.InvariantCulture)) { Width = 9 });
            Reservations.AddColumn(new GridColumn<VesselReservation>("files", "Files", r => r.FileCount.ToString(System.Globalization.CultureInfo.InvariantCulture)) { Width = 7 });
            Reservations.AddColumn(new GridColumn<VesselReservation>("since", "Since", r => Context.Loc.FormatRelative(r.SinceUtc, Context.Clock.UtcNow)) { Width = 14 });
            Reservations.Activated += (s, r) => Context.Navigate("/missions/" + Uri.EscapeDataString(r.MissionId));

            Doctor = new ArmadaGrid<BlackBoxDoctorFinding>(f => f.Key);
            Doctor.MultiSelect = false;
            Doctor.ModalHost = context.Modals;
            Doctor.Dispatcher = context.Dispatcher;
            Doctor.PageSize = 25;
            Doctor.Loader = LoadDoctorAsync;
            Doctor.EmptyText = "No recurring black box recommendations for this vessel in the last 30 days.";
            Doctor.AddColumn(new GridColumn<BlackBoxDoctorFinding>("count", "Times", f => f.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x") { Width = 7 });
            Doctor.AddColumn(new GridColumn<BlackBoxDoctorFinding>("rule", "Rule", f => f.RuleId ?? "-") { Width = 14 });
            Doctor.AddColumn(new GridColumn<BlackBoxDoctorFinding>("setting", "Setting", f => f.Setting + " (" + f.Scope + ")") { Weight = 3 });
            Doctor.AddColumn(new GridColumn<BlackBoxDoctorFinding>("change", "Change", f => (f.CurrentValue ?? "-") + " -> " + (f.SuggestedValue ?? "-")) { Weight = 3 });
            Doctor.AddColumn(new GridColumn<BlackBoxDoctorFinding>("apply", "Apply", f => Tr(f.Applicable && !String.IsNullOrEmpty(f.RecommendationId) ? "Yes" : "No")) { Width = 6 });
            Doctor.AddColumn(new GridColumn<BlackBoxDoctorFinding>("last", "Last seen", f => Context.Loc.FormatRelative(f.LastSeenUtc, Context.Clock.UtcNow)) { Width = 14 });
            GridColumn<BlackBoxDoctorFinding> rationale = new GridColumn<BlackBoxDoctorFinding>("rationale", "Rationale", f => f.Rationale ?? "") { Weight = 4 };
            rationale.DefaultVisible = false;
            Doctor.AddColumn(rationale);
            Doctor.Activated += (s, f) => OpenExample(f);

            Action("dispatch", "Dispatch", () => Context.Navigate(VesselsScreen.DispatchRoute(Vessel!)), "d", () => Vessel != null, true);
            Action("objectives", "Manage Objectives", () => Context.Navigate(VesselsScreen.ObjectivesRoute(Vessel!)), "O", () => Vessel != null, true);
            Action("fleet", "Manage Fleet", () => Context.Navigate("/fleets/" + Uri.EscapeDataString(Vessel!.FleetId!)), "f", () => !String.IsNullOrEmpty(Vessel?.FleetId), true);
            Action("onboarding", "Onboarding", () => Context.Navigate("/vessels/" + Uri.EscapeDataString(VesselId) + "/onboarding"), "g", () => Vessel != null, true);
            Action("run-check", "Run Check", RunCheck, "k", () => Vessel != null, true);
            Action("workspace", "Open Workspace", () => Context.Navigate("/workspace/" + Uri.EscapeDataString(VesselId)), "w", () => Vessel != null, true);
            Action("health", "Health", ShowHealth, "h", () => Vessel != null, true);
            Action("history", "History", () => Context.Navigate(VesselsScreen.HistoryRoute(VesselId)), "H", () => Vessel != null, true);
            Action("cleanup", "Cleanup", ShowCleanup, "c", () => Vessel != null, true);
            Action("edit", "Edit", Edit, "e", () => Vessel != null);
            Action("duplicate", "Duplicate", Duplicate, "u", () => Vessel != null);
            Action("json", "View JSON", () => ShowJson(Tr("Vessel: {{name}}", LocalizationArgs.Of("name", Vessel!.Name)), Vessel), "j", () => Vessel != null);
            Action("delete", "Delete", Delete, "del", () => Vessel != null, false, true);
            Action("copy-id", "Copy ID", () => Copy(VesselId, "Vessel ID"), "y");
            Action("copy-harbor-id", "Copy Harbor ID", () => Copy(Readiness?.HarborId, "Harbor ID"), "Y", () => !String.IsNullOrEmpty(Readiness?.HarborId));
            OpsScreenAction serialize = Action("serialize-overlapping", "Serialize Overlapping Missions", ToggleSerialize, "L", () => Vessel != null);
            serialize.Panel = "overview";
            serialize.DynamicLabel = () => Tr(Vessel != null && Vessel.SerializeOverlappingMissions ? "Stop serializing overlapping missions" : "Serialize overlapping missions");
            PanelAction("doctor", "doctor-apply", "Apply recommendation", () => ApplyFinding(Doctor.Current!), "A", () => Doctor.Current != null && Doctor.Current.Applicable && !String.IsNullOrEmpty(Doctor.Current.RecommendationId));
            PanelAction("doctor", "doctor-details", "Recommendation details", () => ShowFinding(Doctor.Current!), "i", () => Doctor.Current != null);
            PanelHint("doctor", "Enter", "Open example entry");
            PanelHint("reservations", "Enter", "Open mission");
            PanelHint("reservations", "< >", "Page");

            Overview.Builder = BuildOverview;
            ContextView.Builder = BuildContext;
            AddPanel("overview", "Overview", Overview);
            AddPanel("context", "Context", ContextView);
            AddPanel("missions", "Missions", MissionGrid);
            AddPanel("reservations", "Reservations", Reservations);
            AddPanel("doctor", "Black Box Doctor", Doctor);
            Tabs.SelectedChanged += (s, e) =>
            {
                if (CurrentPanelKey == "reservations" && !_ReservationsLoaded)
                {
                    _ReservationsLoaded = true;
                    Reservations.Reload();
                }
                else if (CurrentPanelKey == "doctor" && !_DoctorLoaded)
                {
                    _DoctorLoaded = true;
                    Doctor.Reload();
                }
            };
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
                ArmadaPageQuery missions = new ArmadaPageQuery(1, 1000);
                missions.With("vesselId", VesselId);
                Task<EnumerationResult<Vessel>?> vessels = c.ListVesselsAsync(new ArmadaPageQuery(1, 9999), t);
                Task<EnumerationResult<Fleet>?> fleets = c.ListFleetsAsync(new ArmadaPageQuery(1, 9999), t);
                Task<EnumerationResult<MissionSummary>?> mlist = c.ListMissionSummariesAsync(missions, t);
                Task<List<Pipeline>> pipelines = ArmadaPaging.ReadAllAsync<Pipeline>((p, ct) => c.ListPipelinesAsync(new ArmadaPageQuery(p, 1000), ct), ArmadaPaging.DefaultMaxPages, t);
                await Task.WhenAll(vessels, fleets, mlist, pipelines).ConfigureAwait(false);
                VesselLoad result = new VesselLoad();
                result.Vessel = (vessels.Result?.Objects ?? new List<Vessel>()).FirstOrDefault(v => v.Id == VesselId);
                result.Fleets = fleets.Result?.Objects ?? new List<Fleet>();
                result.Missions = mlist.Result?.Objects ?? new List<MissionSummary>();
                result.Pipelines = pipelines.Result;
                return result;
            }, r =>
            {
                if (r.Vessel == null)
                {
                    LoadError = Tr("Vessel not found.");
                    return;
                }

                Vessel = r.Vessel;
                Fleets = r.Fleets;
                Missions = r.Missions;
                Pipelines = r.Pipelines;
                MissionGrid.SetLocalRows(Missions);
                Loaded = true;
                LoadError = null;
                Heading = Vessel.Name;
                SubtitleText = Tr("Vessels") + " > " + Vessel.Name;
                Overview.Invalidate();
                ContextView.Invalidate();
                LoadPanels();
                if (_ReservationsLoaded) Reservations.Reload();
                if (_DoctorLoaded) Doctor.Reload();
                if (!_EditHandled && OpsHandoff.Get(Route, "edit") == "1")
                {
                    _EditHandled = true;
                    Context.Router.ReplaceQuietly("/vessels/" + Uri.EscapeDataString(VesselId));
                    Edit();
                }
            }, null, ex =>
            {
                if (initial) LoadError = Tr("Failed to load vessel.");
            });
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Apply a doctor recommendation through its example entry: a dry run shows the action, confirming applies it
        /// with the server's confirm token.
        /// </summary>
        /// <param name="finding">Finding.</param>
        public void ApplyFinding(BlackBoxDoctorFinding finding)
        {
            if (finding == null || String.IsNullOrEmpty(finding.RecommendationId) || String.IsNullOrEmpty(finding.ExampleEntryId)) return;
            if (!finding.Applicable)
            {
                Toast(NotificationSeverityEnum.Warning, Tr("This recommendation is advice only; change the setting by hand."));
                return;
            }

            string entryId = finding.ExampleEntryId;
            string recommendationId = finding.RecommendationId!;
            BlackBoxRecommendationApplyRequest dry = new BlackBoxRecommendationApplyRequest();
            dry.DryRun = true;
            dry.AuthorKind = BlackBoxNoteAuthorKindEnum.Operator;
            Call((c, t) => c.ApplyBlackBoxRecommendationAsync(entryId, recommendationId, dry, t), r =>
            {
                if (r == null) return;
                string plan = Tr("Dry run: nothing has changed yet.") + "\n\n" + FindingText(finding)
                    + (r.Action != null && !String.IsNullOrEmpty(r.Action.Tool) ? "\n\n" + Tr("Runs") + ": " + r.Action.Tool : "")
                    + (String.IsNullOrEmpty(r.Message) ? "" : "\n\n" + r.Message);
                if (String.IsNullOrEmpty(r.ConfirmToken))
                {
                    ShowText(Tr("Recommendation dry run"), plan + "\n\n" + Tr("The server did not issue a confirm token, so this recommendation cannot be applied from here."));
                    return;
                }

                string token = r.ConfirmToken!;
                Confirm("Apply Recommendation", plan, () =>
                {
                    BlackBoxRecommendationApplyRequest run = new BlackBoxRecommendationApplyRequest();
                    run.DryRun = false;
                    run.ConfirmToken = token;
                    run.AuthorKind = BlackBoxNoteAuthorKindEnum.Operator;
                    Call((c, t) => c.ApplyBlackBoxRecommendationAsync(entryId, recommendationId, run, t), done =>
                    {
                        bool ok = done != null && done.Succeeded;
                        Toast(ok ? NotificationSeverityEnum.Success : NotificationSeverityEnum.Error, ok
                            ? Tr("Recommendation applied: {{setting}}.", LocalizationArgs.Of("setting", finding.Setting))
                            : Tr("The recommendation was not applied: {{message}}", LocalizationArgs.Of("message", done?.Message ?? "")));
                        Load();
                    }, "Apply failed");
                }, "Apply");
            }, "Recommendation dry run failed");
        }

        private void ShowFinding(BlackBoxDoctorFinding finding)
        {
            if (finding == null) return;
            ShowText(Tr("Recommendation: {{setting}}", LocalizationArgs.Of("setting", finding.Setting)), FindingText(finding)
                + "\n\n" + (finding.Applicable && !String.IsNullOrEmpty(finding.RecommendationId) ? Tr("A applies it (dry run first).") : Tr("Advice only: change the setting by hand.")));
        }

        private string FindingText(BlackBoxDoctorFinding f)
        {
            List<string> lines = new List<string>();
            lines.Add((f.RuleId ?? "-") + " " + Tr("recommended {{setting}} {{value}} {{count}} times", LocalizationArgs.Of("setting", f.Setting, "value", f.SuggestedValue ?? "-", "count", f.Count)));
            lines.Add(Tr("Scope") + ": " + f.Scope + "   " + Tr("Current") + ": " + (f.CurrentValue ?? "-") + "   " + Tr("Suggested") + ": " + (f.SuggestedValue ?? "-"));
            lines.Add(Tr("Seen") + ": " + Context.Loc.FormatDateTime(f.FirstSeenUtc) + " - " + Context.Loc.FormatDateTime(f.LastSeenUtc));
            lines.Add(Tr("Example entry") + ": " + f.ExampleEntryId + (String.IsNullOrEmpty(f.ExampleMissionId) ? "" : "  (" + f.ExampleMissionId + ")"));
            if (!String.IsNullOrEmpty(f.Rationale)) lines.Add(f.Rationale!);
            return String.Join("\n", lines);
        }

        private void OpenExample(BlackBoxDoctorFinding f)
        {
            if (f != null && !String.IsNullOrEmpty(f.ExampleEntryId)) Context.Navigate("/blackbox/" + Uri.EscapeDataString(f.ExampleEntryId));
        }

        private void ToggleSerialize()
        {
            if (Vessel == null) return;
            bool on = !Vessel.SerializeOverlappingMissions;
            VesselUpsertRequest body = VesselUpsertRequest.From(Vessel);
            body.SerializeOverlappingMissions = on;
            string name = Vessel.Name;
            Call((c, t) => c.UpdateVesselAsync(VesselId, body, t), r =>
            {
                Toast(NotificationSeverityEnum.Success, on
                    ? Tr("Vessel \"{{name}}\" now chains overlapping missions.", LocalizationArgs.Of("name", name))
                    : Tr("Vessel \"{{name}}\" no longer chains overlapping missions.", LocalizationArgs.Of("name", name)));
                Load();
            }, "Save failed");
        }

        private async Task<GridPage<VesselReservation>> LoadReservationsAsync(GridQuery query, System.Threading.CancellationToken token)
        {
            EnumerationQuery q = new EnumerationQuery();
            q.PageNumber = Math.Max(1, query.PageNumber);
            q.PageSize = Math.Max(1, query.PageSize);
            VesselReservationPage? page = await Context.Client.ListVesselReservationsAsync(VesselId, q, token).ConfigureAwait(false);
            ReservationPage = page;
            List<VesselReservation> rows = page?.Objects ?? new List<VesselReservation>();
            return new GridPage<VesselReservation>(rows, page?.TotalRecords ?? rows.Count);
        }

        private async Task<GridPage<BlackBoxDoctorFinding>> LoadDoctorAsync(GridQuery query, System.Threading.CancellationToken token)
        {
            BlackBoxDoctorRequest q = new BlackBoxDoctorRequest();
            q.PageNumber = Math.Max(1, query.PageNumber);
            q.PageSize = Math.Max(1, query.PageSize);
            BlackBoxDoctorPage? page = await Context.Client.GetVesselBlackBoxDoctorAsync(VesselId, q, token).ConfigureAwait(false);
            DoctorPage = page;
            List<BlackBoxDoctorFinding> rows = page?.Objects ?? new List<BlackBoxDoctorFinding>();
            return new GridPage<BlackBoxDoctorFinding>(rows, page?.TotalRecords ?? rows.Count);
        }

        private void LoadPanels()
        {
            if (Vessel == null) return;
            string branch = String.IsNullOrEmpty(Vessel.DefaultBranch) ? "" : Vessel.DefaultBranch;
            _LoadingReadiness = true;
            Call((c, t) => c.GetVesselReadinessAsync(VesselId, null, t), r =>
            {
                _LoadingReadiness = false;
                Readiness = r;
                Overview.Invalidate();
            }, null, ex =>
            {
                _LoadingReadiness = false;
                Readiness = null;
                Overview.Invalidate();
            });
            _LoadingPreview = true;
            Call((c, t) => c.GetVesselLandingPreviewAsync(VesselId, branch.Length > 0 ? branch : null, t), r =>
            {
                _LoadingPreview = false;
                LandingPreview = r;
                Overview.Invalidate();
            }, null, ex =>
            {
                _LoadingPreview = false;
                LandingPreview = null;
                Overview.Invalidate();
            });
        }

        private void RunCheck()
        {
            if (Vessel == null) return;
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            q["tab"] = "checks";
            q["prefill"] = "1";
            q["vesselId"] = Vessel.Id;
            q["branchName"] = Vessel.DefaultBranch ?? "";
            Context.Navigate("/delivery" + RouteMatch.BuildQuery(q));
        }

        private void ShowHealth()
        {
            if (Vessel == null) return;
            VesselHealthDialog dialog = new VesselHealthDialog(this, VesselId, Vessel.Name, Vessel.DefaultBranch, IsTenantAdmin, Evaluation);
            _HealthDialog = dialog;
            Context.Modals.Show(dialog, r => { if (ReferenceEquals(_HealthDialog, dialog)) _HealthDialog = null; });
        }

        private void ShowCleanup()
        {
            if (Vessel == null) return;
            Context.Modals.Show(new VesselCleanupDialog(this, VesselId, Vessel.Name), r => Load());
        }

        private void Edit()
        {
            if (Vessel == null) return;
            VesselForm.Open(this, Vessel, Fleets, Pipelines, v => Load());
        }

        private void Duplicate()
        {
            if (Vessel == null) return;
            Call((c, t) => c.CreateVesselAsync(VesselForm.DuplicatePayload(Vessel), t), created =>
            {
                if (created == null) return;
                Toast(NotificationSeverityEnum.Success, Tr("Vessel \"{{name}}\" duplicated.", LocalizationArgs.Of("name", created.Name)));
                Context.Navigate("/vessels/" + Uri.EscapeDataString(created.Id) + "?edit=1");
            }, null, ex => ShowMessage(ex is ArmadaApiException api ? api.Message : Tr("Duplicate failed.")));
        }

        private void Delete()
        {
            if (Vessel == null) return;
            string name = Vessel.Name;
            Confirm("Delete Vessel", Tr("Delete vessel \"{{name}}\"? This cannot be undone.", LocalizationArgs.Of("name", name)), () =>
            {
                DeleteProgress.RunVoid(Context, "Deleting vessel", t => Context.Client.DeleteVesselAsync(VesselId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Vessel \"{{name}}\" deleted.", LocalizationArgs.Of("name", name)));
                    Context.Navigate("/vessels");
                }, "Delete failed.");
            }, "Delete");
        }

        private OpsDocument BuildOverview(OpsDocument doc)
        {
            Vessel? v = Vessel;
            if (v == null) return doc;
            DateTime now = Context.Clock.UtcNow;
            OpsReadiness.Build(doc, "Readiness", Readiness, _LoadingReadiness, "Readiness data is not available for this vessel yet.", false);
            if (Readiness != null && Readiness.SetupChecklistSatisfiedCount < Readiness.SetupChecklistTotalCount) doc.Note("g " + Tr("Open Onboarding"));
            doc.Blank();
            RenderLandingPreview(doc);
            doc.Section("Details");
            doc.Field("ID", v.Id);
            doc.Field("Name", v.Name);
            Fleet? fleet = Fleets.FirstOrDefault(f => f.Id == v.FleetId);
            doc.Field("Fleet", String.IsNullOrEmpty(v.FleetId) ? "-" : (fleet?.Name ?? v.FleetId) + "   (f)");
            doc.Field("Repo URL", String.IsNullOrEmpty(v.RepoUrl) ? "-" : v.RepoUrl);
            doc.Field("Default Branch", String.IsNullOrEmpty(v.DefaultBranch) ? "main" : v.DefaultBranch);
            doc.Field("Local Path", String.IsNullOrEmpty(v.LocalPath) ? "-" : v.LocalPath);
            doc.Field("Working Directory", String.IsNullOrEmpty(v.WorkingDirectory) ? "-" : v.WorkingDirectory);
            doc.Field("Landing Mode", v.LandingMode?.ToString() ?? "-");
            doc.Field("Branch Cleanup Policy", v.BranchCleanupPolicy?.ToString() ?? "-");
            doc.Field("Release Branch Prefix", String.IsNullOrEmpty(v.ReleaseBranchPrefix) ? "release/" : v.ReleaseBranchPrefix);
            doc.Field("Hotfix Branch Prefix", String.IsNullOrEmpty(v.HotfixBranchPrefix) ? "hotfix/" : v.HotfixBranchPrefix);
            doc.YesNo("Require Passing Checks To Land", v.RequirePassingChecksToLand);
            doc.YesNo("Require PR For Protected Branches", v.RequirePullRequestForProtectedBranches);
            doc.YesNo("Require Merge Queue For Release Branches", v.RequireMergeQueueForReleaseBranches);
            doc.YesNo("Allow Concurrent Missions", v.AllowConcurrentMissions);
            doc.Field("Serialize Overlapping Missions", Tr(v.SerializeOverlappingMissions ? "Yes" : "No") + "   (L)");
            doc.Field("Agent Auto-Approve", Tr(v.AutoApprove == true ? "On for this vessel" : v.AutoApprove == false ? "Off for this vessel" : "Use captain setting"));
            doc.Field("Auto-Land Gate", Tr(v.AutoLandEnabled ? "Enabled" : "Disabled"));
            if (v.AutoLandEnabled)
            {
                doc.Field("Auto-Land Max Files", v.AutoLandMaxFiles.ToString(System.Globalization.CultureInfo.InvariantCulture));
                doc.Field("Auto-Land Max Lines", v.AutoLandMaxLines.ToString(System.Globalization.CultureInfo.InvariantCulture));
                doc.Field("Auto-Land Allowed Paths", v.AutoLandPathAllowGlobs != null && v.AutoLandPathAllowGlobs.Count > 0 ? String.Join(", ", v.AutoLandPathAllowGlobs) : "-");
                doc.Field("Auto-Land Denied Paths", v.AutoLandPathDenyGlobs != null && v.AutoLandPathDenyGlobs.Count > 0 ? String.Join(", ", v.AutoLandPathDenyGlobs) : "-");
            }

            doc.Field("Definition-of-Done Gate", Tr(v.DefinitionOfDoneEnabled ? "Enabled" : "Disabled"));
            if (v.DefinitionOfDoneEnabled)
            {
                doc.Field("DoD Build Command", String.IsNullOrEmpty(v.DefinitionOfDoneBuildCommand) ? "-" : v.DefinitionOfDoneBuildCommand);
                doc.Field("DoD Test Command", String.IsNullOrEmpty(v.DefinitionOfDoneTestCommand) ? "-" : v.DefinitionOfDoneTestCommand);
                doc.Field("DoD Timeout (s)", v.DefinitionOfDoneTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            doc.Field("GitHub Token Override", v.HasGitHubTokenOverride ? "Configured" : "Inherited / None");
            Pipeline? pipeline = Pipelines.FirstOrDefault(p => p.Id == v.DefaultPipelineId);
            doc.Field("Default Pipeline", pipeline?.Name ?? (String.IsNullOrEmpty(v.DefaultPipelineId) ? Tr("None (WorkerOnly)") : v.DefaultPipelineId));
            doc.YesNo("Active", v.Active);
            doc.Field("Created", Context.Loc.FormatRelative(v.CreatedUtc, now) + " (" + Context.Loc.FormatDateTime(v.CreatedUtc) + ")");
            doc.Field("Last Updated", Context.Loc.FormatRelative(v.LastUpdateUtc, now) + " (" + Context.Loc.FormatDateTime(v.LastUpdateUtc) + ")");
            doc.Section("Missions", " (" + Missions.Count + ")");
            doc.Note(Missions.Count == 0 ? "No missions yet" : "] " + Tr("Missions") + ": Enter " + Tr("Open"));
            return doc;
        }

        private void RenderLandingPreview(OpsDocument doc)
        {
            LandingPreviewResult? p = LandingPreview;
            bool ready = p != null && p.IsReadyToLand;
            string meta = p != null && !String.IsNullOrEmpty(p.SourceBranch) ? p.SourceBranch + " -> " + p.TargetBranch : (p != null && !String.IsNullOrEmpty(p.TargetBranch) ? p.TargetBranch! : Tr("No branch selected"));
            doc.Section("Landing Preview");
            doc.Add(StyledText.From(meta, doc.Theme.Muted).Append(StyledText.From("   [" + Tr(ready ? "Ready To Land" : "Needs Review") + "]", ready ? doc.Theme.Success : doc.Theme.Warning)));
            if (_LoadingPreview)
            {
                doc.Note("Calculating landing preview...");
                return;
            }

            if (p == null)
            {
                doc.Note("Landing preview is not available for this vessel yet.");
                return;
            }

            List<string> row1 = new List<string>
            {
                Tr("Branch category") + ": " + p.BranchCategory,
                Tr("Landing mode") + ": " + (p.LandingMode?.ToString() ?? Tr("Inherited")),
                Tr("Cleanup") + ": " + (p.BranchCleanupPolicy?.ToString() ?? Tr("Inherited")),
            };
            if (!String.IsNullOrEmpty(p.ExpectedLandingAction)) row1.Add(Tr("Action") + ": " + p.ExpectedLandingAction);
            row1.Add(Tr(p.RequirePassingChecksToLand ? "Passing checks required" : "Passing checks optional"));
            doc.Text(String.Join("  |  ", row1));
            List<string> row2 = new List<string> { Tr(p.TargetBranchProtected ? "Protected target branch" : "Target branch not protected") };
            if (!String.IsNullOrEmpty(p.ProtectedBranchMatch)) row2.Add(Tr("Policy") + ": " + p.ProtectedBranchMatch);
            if (p.RequirePullRequestForProtectedBranches) row2.Add(Tr("PR required for protected branches"));
            if (p.RequireMergeQueueForReleaseBranches) row2.Add(Tr("Merge queue required for release branches"));
            doc.Text(String.Join("  |  ", row2));
            if (!String.IsNullOrEmpty(p.LatestCheckSummary)) doc.Field("Latest check", p.LatestCheckSummary);
            if (p.Issues != null && p.Issues.Count > 0)
            {
                foreach (LandingPreviewIssue issue in p.Issues)
                {
                    CellStyle style = issue.Severity == ReadinessSeverityEnum.Error ? doc.Theme.Error : issue.Severity == ReadinessSeverityEnum.Warning ? doc.Theme.Warning : doc.Theme.Info;
                    doc.Add(StyledText.From("[" + issue.Severity + "] ", style).Append(StyledText.From(issue.Title ?? "", doc.Theme.Text)));
                    doc.Text("    " + (issue.Message ?? ""), doc.Theme.Muted);
                }
            }
            else
            {
                doc.Text(Tr("No landing blockers are currently predicted for this vessel."), doc.Theme.Success);
            }
        }

        private OpsDocument BuildContext(OpsDocument doc)
        {
            Vessel? v = Vessel;
            if (v == null) return doc;
            bool any = false;
            if (!String.IsNullOrEmpty(v.ProjectContext))
            {
                any = true;
                doc.Section("Project Context");
                Block(doc, v.ProjectContext!);
            }

            if (!String.IsNullOrEmpty(v.StyleGuide))
            {
                any = true;
                doc.Section("Style Guide");
                Block(doc, v.StyleGuide!);
            }

            if (v.ProtectedBranchPatterns != null && v.ProtectedBranchPatterns.Count > 0)
            {
                any = true;
                doc.Section("Protected Branch Patterns");
                Block(doc, String.Join("\n", v.ProtectedBranchPatterns));
            }

            bool boundary = v.SecretScanEnabled || (v.ProtectedPathPatterns != null && v.ProtectedPathPatterns.Count > 0) || (v.PrivateIdentifierDenylist != null && v.PrivateIdentifierDenylist.Count > 0);
            if (boundary)
            {
                any = true;
                doc.Section("Dock Boundary");
                doc.Field("Secret Scan", Tr(v.SecretScanEnabled ? "Enabled" : "Disabled"));
                if (v.ProtectedPathPatterns != null && v.ProtectedPathPatterns.Count > 0) Block(doc, Tr("Protected paths") + ":\n" + String.Join("\n", v.ProtectedPathPatterns));
                if (v.PrivateIdentifierDenylist != null && v.PrivateIdentifierDenylist.Count > 0) Block(doc, Tr("Private identifiers") + ":\n" + String.Join("\n", v.PrivateIdentifierDenylist));
            }

            if (v.EnableModelContext && !String.IsNullOrEmpty(v.ModelContext))
            {
                any = true;
                doc.Section("Model Context");
                Block(doc, v.ModelContext!);
            }

            if (!any) doc.Note("No context recorded for this vessel. Edit (e) adds project context, a style guide, and model context.");
            return doc;
        }

        private static void Block(OpsDocument doc, string text)
        {
            foreach (string line in text.Split('\n')) doc.Text(line.TrimEnd('\r'), doc.Theme.Code);
        }

        #endregion
    }
}
