namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Client.Models;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// Mission detail (W3.8, <c>/missions/:id</c>), the dashboard's MissionDetail page: header actions (Resolve Review,
    /// Mark Complete, Diff, Log, Instructions, Run Check, Land or Retry Landing), the More menu (Edit, Transition
    /// Status, View JSON, Restart, Purge, Delete, and the rest), and panels for the overview (Landing Preview, GitHub
    /// Pull Request with refresh and open, every field, failure reason, review comment), the description (Markdown, copy
    /// raw), linked checks and deployments, and playbook snapshots. Reloads on <c>mission.changed</c>. Not thread-safe.
    /// </summary>
    public class MissionScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// Mission id from the route.
        /// </summary>
        public string MissionId { get; }

        /// <summary>
        /// Loaded mission, or null.
        /// </summary>
        public Mission? Mission { get; private set; } = null;

        /// <summary>
        /// Landing preview, or null.
        /// </summary>
        public LandingPreviewResult? LandingPreview { get; private set; } = null;

        /// <summary>
        /// Pull request evidence, or null.
        /// </summary>
        public GitHubPullRequestDetail? PullRequest { get; private set; } = null;

        /// <summary>
        /// Linked check runs.
        /// </summary>
        public List<CheckRun> LinkedChecks { get; private set; } = new List<CheckRun>();

        /// <summary>
        /// Linked deployments.
        /// </summary>
        public List<Deployment> LinkedDeployments { get; private set; } = new List<Deployment>();

        /// <summary>
        /// Mission actions.
        /// </summary>
        public MissionOps Ops { get; }

        /// <summary>
        /// Overview panel.
        /// </summary>
        public OpsDocumentView Overview { get; } = new OpsDocumentView();

        /// <summary>
        /// Description panel.
        /// </summary>
        public OpsDocumentView Description { get; } = new OpsDocumentView();

        /// <summary>
        /// Linked checks and deployments panel.
        /// </summary>
        public OpsDocumentView Linked { get; } = new OpsDocumentView();

        /// <summary>
        /// Playbook snapshots panel.
        /// </summary>
        public OpsDocumentView Playbooks { get; } = new OpsDocumentView();

        #endregion

        #region Private-Members

        private bool _LoadingPreview = false;
        private bool _LoadingPullRequest = false;
        private string? _PullRequestFor = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public MissionScreen(RouteMatch route, TuiContext context)
            : base(route, context, "MissionScreen", "Mission")
        {
            MissionId = route.Param("id") ?? "";
            Ops = new MissionOps(this);
            Reference.Ensure("vessels", "captains");
            Reference.Changed += (s, n) => Invalidate();

            Action("resolve-review", "Resolve Review", () => Ops.ResolveReview(MissionId, Mission!.Title, Mission.ReviewComment, Load), "R", () => CanResolveReview, true);
            Action("mark-complete", "Mark Complete", () => Ops.MarkComplete(MissionId, Mission!.Title, Load), "M", () => CanMarkComplete, true);
            Action("diff", "Diff", () => Ops.ViewDiff(MissionId, Tr("Diff: {{title}}", LocalizationArgs.Of("title", Mission?.Title ?? MissionId))), "d", () => Mission != null, true);
            Action("log", "Log", ViewLog, "l", () => Mission != null, true);
            Action("instructions", "Instructions", () => Ops.ViewInstructions(MissionId), "i", () => Mission != null, true);
            Action("run-check", "Run Check", RunCheck, "k", () => Mission != null && !String.IsNullOrEmpty(Mission.VesselId), true);
            OpsScreenAction land = Action("land", "Land", () => Ops.RetryLanding(MissionId, Mission!.Title, true, Load), "L", () => CanLand && !ManualLandingOnly, true);
            land.DynamicLabel = () => Mission != null && Mission.Status == MissionStatusEnum.LandingFailed ? "Retry Landing" : "Land";
            Action("merge-branches", "Merge in Manage Branches", () => OpenManualMerge(), "b", () => CanLand && ManualLandingOnly && !String.IsNullOrEmpty(Mission!.VesselId) && !String.IsNullOrEmpty(Mission.BranchName), true);
            Action("edit", "Edit", () => Ops.Edit(Mission!, Load), "e", () => Mission != null);
            Action("transition", "Transition Status", () => Ops.Transition(MissionId, Mission!.Title, Mission.Status.ToString(), true, Load), "t", () => Mission != null);
            Action("json", "View JSON", () => ShowJson(Tr("Mission: {{title}}", LocalizationArgs.Of("title", Mission!.Title)), Mission), "j", () => Mission != null);
            Action("restart", "Restart", () => Ops.Restart(MissionId, Mission!.Title, true, Load), "r", () => Mission != null);
            Action("pr-open", "Open GitHub", () => Context.External.OpenUrl(Mission!.PrUrl!), "o", () => Mission != null && !String.IsNullOrEmpty(Mission.PrUrl));
            Action("pr-refresh", "Refresh pull request", () => LoadPullRequest(true), "p", () => Mission != null && !String.IsNullOrEmpty(Mission.PrUrl));
            Action("copy-id", "Copy ID", () => Copy(MissionId, "Mission ID"), "y");
            Action("copy-description", "Copy raw markdown", () => Copy(Mission?.Description, "Description"), "Y", () => Mission != null && !String.IsNullOrEmpty(Mission.Description));
            Action("voyage", "Open Voyage", () => Context.Navigate("/voyages/" + Uri.EscapeDataString(Mission!.VoyageId!)), "V", () => Mission != null && !String.IsNullOrEmpty(Mission.VoyageId));
            Action("vessel", "Open Vessel", () => Context.Navigate("/vessels/" + Uri.EscapeDataString(Mission!.VesselId!)), "v", () => Mission != null && !String.IsNullOrEmpty(Mission.VesselId));
            Action("captain", "Open Captain", () => Context.Navigate("/captains/" + Uri.EscapeDataString(Mission!.CaptainId!)), "C", () => Mission != null && !String.IsNullOrEmpty(Mission.CaptainId));
            Action("parent", "Open Parent Mission", () => Context.Navigate("/missions/" + Uri.EscapeDataString(Mission!.ParentMissionId!)), null, () => Mission != null && !String.IsNullOrEmpty(Mission.ParentMissionId));
            Action("depends-on", "Open Depends On", () => Context.Navigate("/missions/" + Uri.EscapeDataString(Mission!.DependsOnMissionId!)), null, () => Mission != null && !String.IsNullOrEmpty(Mission.DependsOnMissionId));
            Action("dock", "Open Dock", () => Context.Navigate("/docks/" + Uri.EscapeDataString(Mission!.DockId!)), null, () => Mission != null && !String.IsNullOrEmpty(Mission.DockId));
            Action("purge", "Purge", () => Ops.Purge(MissionId, Mission!.Title, true, Load), "P", () => Mission != null, false, true);
            Action("delete", "Delete", () => Ops.Delete(MissionId, Mission!.Title, () => Context.Navigate("/missions")), "del", () => Mission != null, false, true);

            Overview.Builder = BuildOverview;
            Description.Builder = BuildDescription;
            Description.Empty = "No description.";
            Linked.Builder = BuildLinked;
            Playbooks.Builder = BuildPlaybooks;
            Playbooks.Empty = "No playbooks.";
            AddPanel("overview", "Overview", Overview);
            AddPanel("description", "Description", Description);
            AddPanel("linked", "Linked Checks and Deployments", Linked);
            AddPanel("playbooks", "Playbooks", Playbooks);

            SubscribeCoalesced("mission.changed", Load);
            Load();
            LoadLinked();
        }

        #endregion

        #region Public-Members-Computed

        /// <summary>
        /// Review gate waiting (Resolve Review).
        /// </summary>
        public bool CanResolveReview
        {
            get { return Mission != null && Mission.Status == MissionStatusEnum.Review && Mission.RequiresReview; }
        }

        /// <summary>
        /// In Review without a gate (Mark Complete).
        /// </summary>
        public bool CanMarkComplete
        {
            get { return Mission != null && Mission.Status == MissionStatusEnum.Review && !Mission.RequiresReview; }
        }

        /// <summary>
        /// Landable (WorkProduced, LandingFailed, or Review without a gate).
        /// </summary>
        public bool CanLand
        {
            get
            {
                return Mission != null && (Mission.Status == MissionStatusEnum.WorkProduced || Mission.Status == MissionStatusEnum.LandingFailed
                    || (Mission.Status == MissionStatusEnum.Review && !Mission.RequiresReview));
            }
        }

        /// <summary>
        /// Landing Mode None for this mission (from the landing preview): Armada will not land the branch, so the screen
        /// offers Merge in Manage Branches instead of Land.
        /// </summary>
        public bool ManualLandingOnly
        {
            get { return LandingPreview != null && LandingPreview.ManualLandingOnly; }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open the vessel's Manage Branches dialog with this mission's branch preselected to merge into the target branch.
        /// The mission reloads when the dialog closes (the server completes it once the branch is merged).
        /// </summary>
        /// <returns>The dialog, or null when the mission has no vessel or branch.</returns>
        public Armada.Tui.Screens.Build.VesselBranchesDialog? OpenManualMerge()
        {
            if (Mission == null || String.IsNullOrEmpty(Mission.VesselId) || String.IsNullOrEmpty(Mission.BranchName)) return null;
            Armada.Tui.Screens.Build.VesselBranchesDialog dialog = new Armada.Tui.Screens.Build.VesselBranchesDialog(this, Mission.VesselId!, Reference.VesselName(Mission.VesselId));
            dialog.PreselectMerge(Mission.BranchName, LandingPreview?.TargetBranch);
            Context.Modals.Show(dialog, r => Load());
            return dialog;
        }

        /// <inheritdoc />
        public override void Load()
        {
            bool initial = !Loaded;
            Call((c, t) => c.GetMissionAsync(MissionId, t), m =>
            {
                if (m == null)
                {
                    if (initial) LoadError = Tr("Mission not found.");
                    return;
                }

                Mission = m;
                Loaded = true;
                LoadError = null;
                Heading = m.Title;
                Status = m.Status.ToString();
                SubtitleText = Tr("Missions") + " > " + m.Title + "   " + m.Id;
                Invalidate();
                LoadPreview();
                if (!String.IsNullOrEmpty(m.PrUrl) && _PullRequestFor != m.Id + m.PrUrl) LoadPullRequest(false);
                if (String.IsNullOrEmpty(m.PrUrl)) PullRequest = null;
            }, null, ex =>
            {
                if (initial) LoadError = Tr("Failed to load mission: {{message}}", LocalizationArgs.Of("message", ex.Message));
            });
        }

        #endregion

        #region Private-Methods

        private void Invalidate()
        {
            Overview.Invalidate();
            Description.Invalidate();
            Linked.Invalidate();
            Playbooks.Invalidate();
        }

        private void LoadPreview()
        {
            _LoadingPreview = LandingPreview == null;
            Call((c, t) => c.GetMissionLandingPreviewAsync(MissionId, t), p =>
            {
                LandingPreview = p;
                _LoadingPreview = false;
                Invalidate();
            }, null, ex =>
            {
                LandingPreview = null;
                _LoadingPreview = false;
                Invalidate();
            });
        }

        private void LoadPullRequest(bool force)
        {
            if (Mission == null || String.IsNullOrEmpty(Mission.PrUrl)) return;
            _PullRequestFor = Mission.Id + Mission.PrUrl;
            _LoadingPullRequest = true;
            Invalidate();
            Call((c, t) => c.GetMissionGitHubPullRequestAsync(MissionId, t), pr =>
            {
                PullRequest = pr;
                _LoadingPullRequest = false;
                Invalidate();
            }, null, ex =>
            {
                PullRequest = null;
                _LoadingPullRequest = false;
                Invalidate();
            });
        }

        private void LoadLinked()
        {
            Call((c, t) =>
            {
                ArmadaPageQuery q = new ArmadaPageQuery();
                q.PageSize = 1000;
                q.With("missionId", MissionId);
                return c.ListCheckRunsAsync(q, t);
            }, r =>
            {
                LinkedChecks = r?.Objects ?? new List<CheckRun>();
                Invalidate();
            }, null, ex => { });
            Call((c, t) =>
            {
                DeploymentQuery q = new DeploymentQuery();
                q.PageSize = 1000;
                q.MissionId = MissionId;
                return c.ListDeploymentsAsync(q, t);
            }, r =>
            {
                LinkedDeployments = r?.Objects ?? new List<Deployment>();
                Invalidate();
            }, null, ex => { });
        }

        private void ViewLog()
        {
            if (Mission == null) return;
            Ops.ViewLog(MissionId, Tr("Log: {{title}}", LocalizationArgs.Of("title", Mission.Title)), () => Mission != null && MissionOps.LogCompleted(Mission.Status), Load);
        }

        private void RunCheck()
        {
            if (Mission == null || String.IsNullOrEmpty(Mission.VesselId)) return;
            Ops.RunCheck(Mission.VesselId!, Mission.Id, Mission.VoyageId, Mission.BranchName, Mission.CommitHash, Mission.Title);
        }

        private string FormatDuration(long? ms)
        {
            if (ms == null || ms < 0) return Tr("N/A");
            double totalSeconds = ms.Value / 1000.0;
            if (totalSeconds < 60) return Tr("{{seconds}}s", LocalizationArgs.Of("seconds", totalSeconds.ToString("0.0", CultureInfo.InvariantCulture)));
            long hours = (long)(totalSeconds / 3600);
            long minutes = (long)((totalSeconds % 3600) / 60);
            long seconds = (long)(totalSeconds % 60);
            if (hours > 0) return Tr("{{hours}}h {{minutes}}m", LocalizationArgs.Of("hours", hours, "minutes", minutes));
            if (minutes > 0 && seconds > 0) return Tr("{{minutes}}m {{seconds}}s", LocalizationArgs.Of("minutes", minutes, "seconds", seconds));
            return Tr("{{minutes}}m", LocalizationArgs.Of("minutes", minutes));
        }

        private string CaptainRef(string? id)
        {
            return String.IsNullOrEmpty(id) ? "-" : Reference.CaptainName(id) + "  (" + id + ")";
        }

        private OpsDocument BuildOverview(OpsDocument doc)
        {
            Mission? m = Mission;
            if (m == null) return doc;
            DateTime now = Context.Clock.UtcNow;
            string meta = LandingPreview?.SourceBranch != null
                ? LandingPreview.SourceBranch + " -> " + LandingPreview.TargetBranch
                : (m.BranchName ?? Tr("No branch selected"));
            if (m.Status == MissionStatusEnum.Pending && m.AssignmentBlocker != null) RenderBlocker(doc, m.AssignmentBlocker);
            OpsLandingPreview.Render(doc, LandingPreview, _LoadingPreview, meta, true);

            if (!String.IsNullOrEmpty(m.PrUrl))
            {
                doc.Section("GitHub Pull Request");
                doc.Note("o Open GitHub   p " + Tr(_LoadingPullRequest ? "Refreshing..." : "Refresh"));
                if (_LoadingPullRequest) doc.Note("Loading GitHub pull-request evidence...");
                else if (PullRequest == null) doc.Note("GitHub pull-request evidence is unavailable for this mission.");
                else
                {
                    GitHubPullRequestDetail pr = PullRequest;
                    doc.Field("Repository", pr.Repository);
                    doc.Field("Review Status", pr.ReviewStatus);
                    doc.Field("State", pr.State);
                    doc.Field("Mergeability", pr.MergeableState);
                    doc.Text(pr.Title, doc.Theme.Muted);
                    doc.Note("Reviews", doc.Theme.Accent);
                    if (pr.Reviews == null || pr.Reviews.Count == 0) doc.Note("No reviews");
                    else foreach (GitHubPullRequestReview r in pr.Reviews) doc.Text("  " + (r.ReviewerLogin ?? Tr("Unknown")) + "  " + r.State);
                    doc.Note("Checks", doc.Theme.Accent);
                    if (pr.Checks == null || pr.Checks.Count == 0) doc.Note("No provider checks");
                    else foreach (GitHubPullRequestCheck c in pr.Checks) doc.Text("  " + c.Name + "  " + c.Status + (String.IsNullOrEmpty(c.Conclusion) ? "" : " / " + c.Conclusion));
                }
            }

            doc.Section("Mission");
            doc.Field("ID", m.Id);
            doc.Field("Tenant ID", m.TenantId);
            doc.Field("Status", StatusBadge.Label(m.Status), StatusBadge.Style(m.Status, doc.Theme));
            doc.Field("Mode", Tr(m.Mode.ToString()));
            doc.Field("Review Gate", m.RequiresReview ? Tr(m.Status == MissionStatusEnum.Review ? "Waiting Review" : "Required") : Tr("None"));
            doc.Field("On Deny", m.RequiresReview ? Tr(m.ReviewDenyAction == ReviewDenyActionEnum.FailPipeline ? "Fail pipeline" : "Retry stage") : "-");
            doc.Field("Review Requested", m.ReviewRequestedUtc.HasValue ? Context.Loc.FormatDateTime(m.ReviewRequestedUtc.Value) : "-");
            doc.Field("Reviewed", m.ReviewedUtc.HasValue ? Context.Loc.FormatDateTime(m.ReviewedUtc.Value) : "-");
            doc.Field("Reviewed By", m.ReviewedByUserId);
            doc.Field("Priority", m.Priority.ToString(CultureInfo.InvariantCulture));
            doc.Field("Voyage", m.VoyageId);
            doc.Field("Vessel", String.IsNullOrEmpty(m.VesselId) ? "-" : Reference.VesselName(m.VesselId));
            string preferred = CaptainRef(m.RequestedCaptainId);
            if (!String.IsNullOrEmpty(m.RequestedCaptainId) && !String.IsNullOrEmpty(m.CaptainId) && m.CaptainId != m.RequestedCaptainId) preferred += "  " + Tr("(fell back to tier)");
            doc.Field("Preferred Captain", preferred);
            doc.Field("Actual Captain", CaptainRef(m.CaptainId));
            doc.Field("Parent Mission", m.ParentMissionId);
            doc.Field("Persona", String.IsNullOrEmpty(m.Persona) ? Tr("Worker") : m.Persona);
            if (!String.IsNullOrEmpty(m.DependsOnMissionId)) doc.Field("Depends On", m.DependsOnMissionId);
            if (m.Status == MissionStatusEnum.WorkProduced && m.DependsOnMissionId == null && !String.IsNullOrEmpty(m.Persona) && !PersonaCatalog.Matches(m.Persona, PersonaCatalog.Worker))
                doc.Field("Pipeline Status", Tr("Work complete -- handed off to the next pipeline stage"));
            doc.Field("Branch Name", m.BranchName);
            doc.Field("Dock", m.DockId);
            doc.Field("Process ID", m.ProcessId?.ToString(CultureInfo.InvariantCulture));
            doc.Field("PR URL", m.PrUrl);
            doc.Field("Commit Hash", m.CommitHash);
            doc.Time("Created", m.CreatedUtc, now);
            doc.Time("Started", m.StartedUtc, now);
            doc.Time("Completed", m.CompletedUtc, now);
            doc.Field("Total Runtime", FormatDuration(m.TotalRuntimeMs));
            doc.Time("Last Updated", m.LastUpdateUtc, now);
            doc.Field("Linked Checks", LinkedChecks.Count.ToString(CultureInfo.InvariantCulture));
            doc.Field("Linked Deployments", LinkedDeployments.Count.ToString(CultureInfo.InvariantCulture));

            if (!String.IsNullOrEmpty(m.FailureReason))
            {
                doc.Section("Failure Reason");
                doc.Text(m.FailureReason, doc.Theme.Error);
            }

            if (!String.IsNullOrEmpty(m.ReviewComment))
            {
                doc.Section("Review Comment");
                doc.Text(m.ReviewComment, doc.Theme.Warning);
            }

            return doc;
        }

        private void RenderBlocker(OpsDocument doc, MissionAssignmentBlocker blocker)
        {
            doc.Section("Why This Mission Is Waiting");
            doc.Text(blocker.Summary, blocker.Reason == MissionAssignmentBlockerReasonEnum.AwaitingDispatch ? doc.Theme.Text : doc.Theme.Warning);
            if (blocker.UntilUtc.HasValue) doc.Field("Expected to clear", Context.Loc.FormatDateTime(blocker.UntilUtc.Value));
            if (!String.IsNullOrEmpty(blocker.DependsOnMissionId)) doc.Field("Depends On", blocker.DependsOnMissionId);
            if (blocker.BlockingMissionIds.Count > 0) doc.Field("Held by", String.Join(", ", blocker.BlockingMissionIds));
            foreach (MissionAssignmentCaptainStatus captain in blocker.Captains)
            {
                doc.Text("  " + (captain.CaptainName ?? captain.CaptainId) + ": " + captain.Detail, doc.Theme.Muted);
            }
        }

        private OpsDocument BuildDescription(OpsDocument doc)
        {
            Mission? m = Mission;
            if (m == null || String.IsNullOrEmpty(m.Description)) return doc;
            doc.Note("Y " + Tr("Copy raw markdown"));
            doc.Blank();
            doc.Markdown(m.Description);
            return doc;
        }

        private OpsDocument BuildLinked(OpsDocument doc)
        {
            if (Mission == null) return doc;
            doc.Section("Linked Checks");
            if (LinkedChecks.Count == 0) doc.Note("No checks are linked to this mission yet.");
            else foreach (CheckRun c in LinkedChecks) doc.Text((String.IsNullOrEmpty(c.Label) ? c.Type.ToString() : c.Label) + "  " + c.Id + "  " + c.Status);
            doc.Section("Linked Deployments");
            if (LinkedDeployments.Count == 0) doc.Note("No deployments are linked to this mission yet.");
            else
            {
                foreach (Deployment d in LinkedDeployments)
                {
                    doc.Text(d.Title + "  " + d.Id);
                    doc.Text("    " + (String.IsNullOrEmpty(d.EnvironmentName) ? Tr("No environment") : d.EnvironmentName) + " - " + d.Status + " - " + d.VerificationStatus, doc.Theme.Muted);
                }
            }

            return doc;
        }

        private OpsDocument BuildPlaybooks(OpsDocument doc)
        {
            Mission? m = Mission;
            if (m == null || m.PlaybookSnapshots == null || m.PlaybookSnapshots.Count == 0) return doc;
            foreach (MissionPlaybookSnapshot s in m.PlaybookSnapshots)
            {
                doc.Section(s.FileName, "  [" + PlaybookDeliveryText.Format(s.DeliveryMode) + "]");
                doc.Text(String.IsNullOrEmpty(s.Description) ? Tr("No description") : s.Description, doc.Theme.Muted);
                doc.Field("Resolved Path", s.ResolvedPath);
                doc.Field("Worktree Path", s.WorktreeRelativePath);
                doc.Field("Source Updated", s.SourceLastUpdateUtc.HasValue ? Context.Loc.FormatDateTime(s.SourceLastUpdateUtc.Value) : "-");
                doc.Blank();
                doc.Text(s.Content, doc.Theme.Code);
            }

            return doc;
        }

        #endregion
    }
}
