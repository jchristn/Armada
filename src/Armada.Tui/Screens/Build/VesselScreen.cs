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
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// A vessel's page (W4.4, <c>/vessels/:id</c>), the dashboard's VesselDetail: Manage Objectives, Manage Fleet,
    /// Onboarding, Run Check, Open Workspace, and Health (the health inspector with Re-evaluate) as buttons, plus Edit,
    /// Duplicate, View JSON, and Delete; the Readiness and Landing Preview panels, every field (branch prefixes and
    /// policies, auto-approve, the auto-land and Definition-of-Done gates, the GitHub token state), the context
    /// blocks (project context, style guide, protected branches, dock boundary, model context), and the vessel's
    /// missions. <c>?edit=1</c> (after Duplicate) opens the edit form once. Not thread-safe.
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

        #endregion

        #region Private-Members

        private bool _LoadingReadiness = false;
        private bool _LoadingPreview = false;
        private bool _EditHandled = false;
        private VesselHealthDialog? _HealthDialog = null;

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

            Action("dispatch", "Dispatch", () => Context.Navigate(VesselsScreen.DispatchRoute(Vessel!)), "d", () => Vessel != null, true);
            Action("objectives", "Manage Objectives", () => Context.Navigate(VesselsScreen.ObjectivesRoute(Vessel!)), "O", () => Vessel != null, true);
            Action("fleet", "Manage Fleet", () => Context.Navigate("/fleets/" + Uri.EscapeDataString(Vessel!.FleetId!)), "f", () => !String.IsNullOrEmpty(Vessel?.FleetId), true);
            Action("onboarding", "Onboarding", () => Context.Navigate("/vessels/" + Uri.EscapeDataString(VesselId) + "/onboarding"), "g", () => Vessel != null, true);
            Action("run-check", "Run Check", RunCheck, "k", () => Vessel != null, true);
            Action("workspace", "Open Workspace", () => Context.Navigate("/workspace/" + Uri.EscapeDataString(VesselId)), "w", () => Vessel != null, true);
            Action("health", "Health", ShowHealth, "h", () => Vessel != null, true);
            Action("edit", "Edit", Edit, "e", () => Vessel != null);
            Action("duplicate", "Duplicate", Duplicate, "u", () => Vessel != null);
            Action("json", "View JSON", () => ShowJson(Tr("Vessel: {{name}}", LocalizationArgs.Of("name", Vessel!.Name)), Vessel), "j", () => Vessel != null);
            Action("delete", "Delete", Delete, "del", () => Vessel != null, false, true);
            Action("copy-id", "Copy ID", () => Copy(VesselId, "Vessel ID"), "y");

            Overview.Builder = BuildOverview;
            ContextView.Builder = BuildContext;
            AddPanel("overview", "Overview", Overview);
            AddPanel("context", "Context", ContextView);
            AddPanel("missions", "Missions", MissionGrid);
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
                Task<EnumerationResult<Pipeline>?> pipelines = c.ListPipelinesAsync(new ArmadaPageQuery(1, 9999), t);
                await Task.WhenAll(vessels, fleets, mlist, pipelines).ConfigureAwait(false);
                VesselLoad result = new VesselLoad();
                result.Vessel = (vessels.Result?.Objects ?? new List<Vessel>()).FirstOrDefault(v => v.Id == VesselId);
                result.Fleets = fleets.Result?.Objects ?? new List<Fleet>();
                result.Missions = mlist.Result?.Objects ?? new List<MissionSummary>();
                result.Pipelines = pipelines.Result?.Objects ?? new List<Pipeline>();
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
                Run((c, t) => c.DeleteVesselAsync(VesselId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Vessel \"{{name}}\" deleted.", LocalizationArgs.Of("name", name)));
                    Context.Navigate("/vessels");
                }, null, ex => ShowMessage(Tr("Delete failed.")));
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
