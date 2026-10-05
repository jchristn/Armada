namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// Voyage detail (W3.9, <c>/voyages/:id</c>), the dashboard's VoyageDetail page: actions (Run Check, Draft Release,
    /// Cancel Voyage while open or in progress, Retry Failed with a count, View JSON, Delete once finished), the
    /// status, details, configuration, captain assignments with fallback tier, timestamps, progress, and playbook
    /// snapshots or selections, and the missions table (Diff, Log, Detail). Reloads on <c>voyage.changed</c> and
    /// <c>mission.changed</c>. Not thread-safe.
    /// </summary>
    public class VoyageScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// Voyage id.
        /// </summary>
        public string VoyageId { get; }

        /// <summary>
        /// Loaded voyage, or null.
        /// </summary>
        public Voyage? Voyage { get; private set; } = null;

        /// <summary>
        /// Missions of the voyage.
        /// </summary>
        public List<Mission> Missions { get; private set; } = new List<Mission>();

        /// <summary>
        /// Overview panel.
        /// </summary>
        public OpsDocumentView Overview { get; } = new OpsDocumentView();

        /// <summary>
        /// Missions table.
        /// </summary>
        public ArmadaGrid<Mission> MissionGrid { get; }

        /// <summary>
        /// Mission actions.
        /// </summary>
        public MissionOps Ops { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public VoyageScreen(RouteMatch route, TuiContext context)
            : base(route, context, "VoyageScreen", "Voyage")
        {
            VoyageId = route.Param("id") ?? "";
            Ops = new MissionOps(this);
            Reference.Ensure("vessels", "captains");
            Reference.Changed += (s, n) => Overview.Invalidate();

            MissionGrid = new ArmadaGrid<Mission>(m => m.Id);
            MissionGrid.MultiSelect = false;
            MissionGrid.ShowPagingBar = false;
            MissionGrid.PageSize = 10000;
            MissionGrid.EmptyText = "No missions in this voyage.";
            MissionGrid.ModalHost = context.Modals;
            MissionGrid.AddColumn(new GridColumn<Mission>("title", "Mission", m => m.Title + "  " + m.Id) { Weight = 4 });
            GridColumn<Mission> status = new GridColumn<Mission>("status", "Status", m => StatusBadge.Label(m.Status.ToString())) { Width = 16 };
            status.Style = (m, t) => StatusBadge.Style(m.Status.ToString(), t);
            MissionGrid.AddColumn(status);
            MissionGrid.AddColumn(new GridColumn<Mission>("vessel", "Vessel", m => Reference.VesselName(m.VesselId)) { Weight = 2 });
            MissionGrid.AddColumn(new GridColumn<Mission>("captain", "Captain", m => Reference.CaptainName(m.CaptainId)) { Weight = 2 });
            MissionGrid.AddColumn(new GridColumn<Mission>("branch", "Branch", m => m.BranchName ?? "-") { Weight = 3 });
            MissionGrid.Activated += (s, m) => Context.Navigate("/missions/" + Uri.EscapeDataString(m.Id));

            Action("run-check", "Run Check", RunCheck, "k", () => Voyage != null, true);
            Action("draft-release", "Draft Release", DraftRelease, "R", () => Voyage != null, true);
            Action("cancel", "Cancel Voyage", Cancel, "x", () => Voyage != null && IsActive(Voyage.Status), true, true);
            OpsScreenAction retry = Action("retry-failed", "Retry Failed", RetryFailed, "F", () => FailedCount > 0, true);
            retry.DynamicLabel = () => Tr("Retry Failed") + " (" + FailedCount + ")";
            Action("json", "View JSON", () => ShowJson(Tr("Voyage: {{title}}", LocalizationArgs.Of("title", Voyage!.Title)), Voyage), "j", () => Voyage != null, true);
            Action("delete", "Delete", Delete, "del", () => Voyage != null && !IsActive(Voyage.Status), true, true);
            Action("mission-diff", "Diff", () => Ops.ViewDiff(MissionGrid.Current!.Id, Tr("Diff: {{title}}", LocalizationArgs.Of("title", MissionGrid.Current!.Title))), "d", OnMission);
            Action("mission-log", "Log", () => { Mission m = MissionGrid.Current!; Ops.ViewLog(m.Id, Tr("Log: {{title}}", LocalizationArgs.Of("title", m.Title))); }, "l", OnMission);
            Action("mission-detail", "Detail", () => Context.Navigate("/missions/" + Uri.EscapeDataString(MissionGrid.Current!.Id)), "o", OnMission);
            Action("copy-id", "Copy ID", () => Copy(VoyageId, "Voyage ID"), "y");

            Overview.Builder = BuildOverview;
            AddPanel("overview", "Overview", Overview);
            AddPanel("missions", "Missions", MissionGrid);

            SubscribeCoalesced("voyage.changed", Load);
            SubscribeCoalesced("mission.changed", Load);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Missions with status Complete.
        /// </summary>
        public int CompletedCount
        {
            get { return Missions.Count(m => m.Status == MissionStatusEnum.Complete); }
        }

        /// <summary>
        /// Missions with status Failed.
        /// </summary>
        public int FailedCount
        {
            get { return Missions.Count(m => m.Status == MissionStatusEnum.Failed); }
        }

        /// <inheritdoc />
        public override void Load()
        {
            bool initial = !Loaded;
            Call((c, t) => c.GetVoyageDetailAsync(VoyageId, t), d =>
            {
                if (d == null || d.Voyage == null)
                {
                    if (initial) LoadError = Tr("Voyage not found.");
                    return;
                }

                Voyage = d.Voyage;
                Missions = d.Missions ?? new List<Mission>();
                MissionGrid.SetLocalRows(Missions);
                Loaded = true;
                LoadError = null;
                Heading = String.IsNullOrEmpty(Voyage.Title) ? Voyage.Id : Voyage.Title;
                Status = Voyage.Status.ToString();
                SubtitleText = Tr("Voyages") + " / " + Heading;
                Overview.Invalidate();
            }, null, ex =>
            {
                if (initial) LoadError = Tr("Failed to load voyage: {{message}}", LocalizationArgs.Of("message", ex.Message));
            });
        }

        #endregion

        #region Private-Methods

        private static bool IsActive(VoyageStatusEnum status)
        {
            return status == VoyageStatusEnum.Open || status == VoyageStatusEnum.InProgress;
        }

        private bool OnMission()
        {
            return ReferenceEquals(CurrentPanel, MissionGrid) && MissionGrid.Current != null;
        }

        private string Name()
        {
            return Voyage == null ? VoyageId : (String.IsNullOrEmpty(Voyage.Title) ? Voyage.Id : Voyage.Title);
        }

        private void RunCheck()
        {
            if (Voyage == null) return;
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            q["tab"] = "checks";
            q["prefill"] = "1";
            q["vesselId"] = Missions.FirstOrDefault()?.VesselId ?? "";
            q["voyageId"] = Voyage.Id;
            string? branch = Missions.FirstOrDefault()?.BranchName;
            if (!String.IsNullOrEmpty(branch)) q["branchName"] = branch!;
            q["label"] = Voyage.Title ?? "";
            Context.Navigate("/delivery" + RouteMatch.BuildQuery(q));
        }

        private void DraftRelease()
        {
            if (Voyage == null) return;
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            q["prefill"] = "1";
            string? vessel = Missions.FirstOrDefault()?.VesselId;
            if (!String.IsNullOrEmpty(vessel)) q["vesselId"] = vessel!;
            q["voyageIds"] = Voyage.Id;
            q["missionIds"] = String.Join(",", Missions.Select(m => m.Id));
            q["title"] = String.IsNullOrEmpty(Voyage.Title) ? "Voyage Release" : Voyage.Title + " Release";
            Context.Navigate("/releases/new" + RouteMatch.BuildQuery(q));
        }

        private void Cancel()
        {
            if (Voyage == null) return;
            string name = Name();
            Confirm("Cancel Voyage", Tr("Cancel this voyage? All pending missions will be cancelled."), () =>
            {
                Run((c, t) => c.CancelVoyageAsync(VoyageId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Voyage \"{{title}}\" cancelled.", LocalizationArgs.Of("title", name)));
                    Load();
                }, null, ex => ShowMessage(Tr("Cancel failed: {{message}}", LocalizationArgs.Of("message", ex.Message))));
            }, "Cancel Voyage");
        }

        private void Delete()
        {
            if (Voyage == null) return;
            string name = Name();
            Confirm("Delete Voyage", Tr("Permanently delete this voyage and all its missions? This cannot be undone."), () =>
            {
                Run((c, t) => c.PurgeVoyageAsync(VoyageId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Voyage \"{{title}}\" deleted.", LocalizationArgs.Of("title", name)));
                    Context.Navigate("/missions?tab=voyages");
                }, null, ex => ShowMessage(Tr("Delete failed: {{message}}", LocalizationArgs.Of("message", ex.Message))));
            }, "Delete");
        }

        private void RetryFailed()
        {
            List<Mission> failed = Missions.Where(m => m.Status == MissionStatusEnum.Failed).ToList();
            if (failed.Count == 0) return;
            Confirm("Retry Failed Missions", Tr("Retry {{count}} failed mission(s)? New missions will be created with the same parameters.", LocalizationArgs.Of("count", failed.Count)), () =>
            {
                Run(async (c, t) =>
                {
                    foreach (Mission m in failed)
                    {
                        Mission retry = new Mission(m.Title, m.Description);
                        retry.VesselId = m.VesselId;
                        retry.VoyageId = m.VoyageId;
                        retry.Priority = m.Priority;
                        await c.CreateMissionAsync(retry, t).ConfigureAwait(false);
                    }
                }, () =>
                {
                    Toast(NotificationSeverityEnum.Success, Tr("Retried {{count}} failed mission(s).", LocalizationArgs.Of("count", failed.Count)));
                    Load();
                }, null, ex => ShowMessage(Tr("Retry failed: {{message}}", LocalizationArgs.Of("message", ex.Message))));
            }, "Retry");
        }

        private OpsDocument BuildOverview(OpsDocument doc)
        {
            Voyage? v = Voyage;
            if (v == null) return doc;
            DateTime now = Context.Clock.UtcNow;
            doc.Section("Status");
            doc.Add(StyledText.From(StatusBadge.Label(v.Status.ToString()), StatusBadge.Style(v.Status.ToString(), doc.Theme)));
            doc.Section("Details");
            doc.Field("ID", v.Id);
            doc.Field("Description", v.Description);
            doc.Section("Configuration");
            doc.YesNo("Auto-Push", v.AutoPush);
            doc.YesNo("Auto-Create PRs", v.AutoCreatePullRequests);
            doc.YesNo("Auto-Merge PRs", v.AutoMergePullRequests);
            doc.Field("Landing Mode", v.LandingMode?.ToString());

            List<CaptainAssignmentOverride> overrides = new List<CaptainAssignmentOverride>();
            if (!String.IsNullOrEmpty(v.CaptainOverridesJson))
            {
                try { overrides = ArmadaJson.Deserialize<List<CaptainAssignmentOverride>>(v.CaptainOverridesJson) ?? new List<CaptainAssignmentOverride>(); }
                catch (Exception) { overrides = new List<CaptainAssignmentOverride>(); }
            }

            if (overrides.Count > 0)
            {
                doc.Section("Captain Assignments");
                foreach (CaptainAssignmentOverride o in overrides)
                {
                    string captain = String.IsNullOrEmpty(o.CaptainId) ? Tr("Auto") : Reference.CaptainName(o.CaptainId);
                    string tier = o.FallbackTier.HasValue ? "  " + Tr("fallback: {{tier}}", LocalizationArgs.Of("tier", Tr(o.FallbackTier.Value.ToString()))) : "";
                    doc.Field(o.Persona, captain + tier);
                }
            }

            doc.Section("Timestamps");
            doc.Time("Created", v.CreatedUtc, now);
            if (v.CompletedUtc.HasValue) doc.Time("Completed", v.CompletedUtc, now);

            if (Missions.Count > 0)
            {
                doc.Section("Progress");
                int pct = (int)Math.Round(CompletedCount * 100.0 / Missions.Count);
                int barWidth = 40;
                int filled = pct * barWidth / 100;
                doc.Add(StyledText.From("[" + new string('#', filled), doc.Theme.Accent).Append(StyledText.From(new string('-', barWidth - filled) + "] " + pct + "%", doc.Theme.Muted)));
                doc.Text(Tr("{{completed}}/{{total}} complete, {{failed}} failed", LocalizationArgs.Of("completed", CompletedCount, "total", Missions.Count, "failed", FailedCount)), doc.Theme.Muted);
            }

            List<MissionPlaybookSnapshot> snapshots = Missions.FirstOrDefault()?.PlaybookSnapshots ?? new List<MissionPlaybookSnapshot>();
            List<SelectedPlaybook> selections = v.SelectedPlaybooks ?? new List<SelectedPlaybook>();
            if (snapshots.Count > 0 || selections.Count > 0)
            {
                doc.Section("Playbooks");
                doc.Note(snapshots.Count > 0
                    ? "These snapshots show the actual playbook content and delivery mode that were applied to the voyage missions."
                    : "This voyage has playbook selections recorded, but mission snapshots are not available yet.");
                if (snapshots.Count > 0)
                {
                    foreach (MissionPlaybookSnapshot s in snapshots)
                    {
                        doc.Text(s.FileName + "  [" + PlaybookDeliveryText.Format(s.DeliveryMode) + "]  " + (s.WorktreeRelativePath ?? s.ResolvedPath ?? "-"));
                        doc.Text("    " + (String.IsNullOrEmpty(s.Description) ? Tr("No description") : s.Description), doc.Theme.Muted);
                    }
                }
                else
                {
                    foreach (SelectedPlaybook s in selections)
                    {
                        doc.Text(s.PlaybookId + "  [" + PlaybookDeliveryText.Format(s.DeliveryMode) + "]");
                        doc.Text("    " + Tr("Playbook details are available after mission snapshots are created."), doc.Theme.Muted);
                    }
                }
            }

            doc.Section("Missions", " (" + Missions.Count + ")");
            doc.Note(Missions.Count == 0 ? "No missions in this voyage." : "] " + Tr("Missions") + ": d " + Tr("Diff") + "  l " + Tr("Log") + "  Enter " + Tr("Detail"));
            return doc;
        }

        #endregion
    }
}
