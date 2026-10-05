namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Merge entry detail (W3.10, <c>/merge-queue/:id</c>), the dashboard's MergeQueueDetail page: Diff and Log for
    /// the mission, the action menu (View JSON, Process, Cancel, Mission Diff, Mission Log, Delete), the Landing
    /// Preview for the entry's branch, every field, the test command, and the test output in a log viewer. Reloads on
    /// <c>mission.changed</c> and auto-refresh. Not thread-safe.
    /// </summary>
    public class MergeEntryScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// Entry id.
        /// </summary>
        public string EntryId { get; }

        /// <summary>
        /// Loaded entry, or null.
        /// </summary>
        public MergeEntry? Entry { get; private set; } = null;

        /// <summary>
        /// Landing preview, or null.
        /// </summary>
        public LandingPreviewResult? LandingPreview { get; private set; } = null;

        /// <summary>
        /// Overview panel.
        /// </summary>
        public OpsDocumentView Overview { get; } = new OpsDocumentView();

        /// <summary>
        /// Test output panel.
        /// </summary>
        public LogViewer TestOutput { get; } = new LogViewer();

        /// <summary>
        /// Mission actions.
        /// </summary>
        public MissionOps Ops { get; }

        #endregion

        #region Private-Members

        private bool _LoadingPreview = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public MergeEntryScreen(RouteMatch route, TuiContext context)
            : base(route, context, "MergeEntryScreen", "Merge Entry")
        {
            EntryId = route.Param("id") ?? "";
            Ops = new MissionOps(this);
            Reference.Ensure("vessels");
            Reference.Changed += (s, n) => Overview.Invalidate();
            TestOutput.Follow = false;

            Action("diff", "Diff", () => Ops.ViewDiff(Entry!.MissionId!, Tr("Diff: Mission {{id}}...", LocalizationArgs.Of("id", Short(Entry!.MissionId!)))), "d", HasMission, true);
            Action("log", "Log", () => Ops.ViewLog(Entry!.MissionId!, Tr("Log: Mission {{id}}...", LocalizationArgs.Of("id", Short(Entry!.MissionId!)))), "l", HasMission, true);
            Action("json", "View JSON", () => ShowJson(Tr("Merge Entry: {{id}}", LocalizationArgs.Of("id", EntryId)), Entry), "j", () => Entry != null);
            Action("process", "Process", Process, "p", () => Entry != null);
            Action("cancel", "Cancel", Cancel, "x", () => Entry != null);
            Action("mission-diff", "Mission Diff", () => RunAction("diff"), null, HasMission);
            Action("mission-log", "Mission Log", () => RunAction("log"), null, HasMission);
            Action("mission", "Open Mission", () => Context.Navigate("/missions/" + Uri.EscapeDataString(Entry!.MissionId!)), "m", HasMission);
            Action("vessel", "Open Vessel", () => Context.Navigate("/vessels/" + Uri.EscapeDataString(Entry!.VesselId!)), "v", () => Entry != null && !String.IsNullOrEmpty(Entry.VesselId));
            Action("copy-id", "Copy ID", () => Copy(EntryId, "Merge entry ID"), "y");
            Action("copy-branch", "Copy branch", () => Copy(Entry?.BranchName, "Branch"), "Y", () => Entry != null);
            Action("delete", "Delete", Delete, "del", () => Entry != null, false, true);

            Overview.Builder = BuildOverview;
            AddPanel("overview", "Overview", Overview);
            AddPanel("test-output", "Test Output", TestOutput);

            SubscribeCoalesced("mission.changed", Load);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Load()
        {
            bool initial = !Loaded;
            Call((c, t) => c.GetMergeEntryAsync(EntryId, t), e =>
            {
                if (e == null)
                {
                    if (initial) LoadError = Tr("Merge entry not found.");
                    return;
                }

                Entry = e;
                Loaded = true;
                LoadError = null;
                Heading = Tr("Merge Entry");
                Status = e.Status.ToString();
                SubtitleText = Tr("Merge Queue") + " > " + e.Id;
                TestOutput.SetText(String.IsNullOrEmpty(e.TestOutput) ? "" : e.TestOutput);
                Overview.Invalidate();
                LoadPreview();
            }, null, ex =>
            {
                if (initial) LoadError = Tr("Failed to load merge entry.");
            });
        }

        #endregion

        #region Private-Methods

        private static string Short(string id)
        {
            return id.Length > 8 ? id.Substring(0, 8) : id;
        }

        private bool HasMission()
        {
            return Entry != null && !String.IsNullOrEmpty(Entry.MissionId);
        }

        private void LoadPreview()
        {
            MergeEntry? e = Entry;
            if (e == null || String.IsNullOrEmpty(e.VesselId))
            {
                LandingPreview = null;
                return;
            }

            _LoadingPreview = LandingPreview == null;
            Call((c, t) => c.GetVesselLandingPreviewAsync(e.VesselId!, e.BranchName, t), p =>
            {
                LandingPreview = p;
                _LoadingPreview = false;
                Overview.Invalidate();
            }, null, ex =>
            {
                LandingPreview = null;
                _LoadingPreview = false;
                Overview.Invalidate();
            });
        }

        private void Process()
        {
            if (Entry == null) return;
            Confirm("Process Entry", Tr("Process merge entry for branch \"{{branchName}}\"?", LocalizationArgs.Of("branchName", Entry.BranchName)), () =>
            {
                Run((c, t) => c.ProcessMergeEntryAsync(EntryId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Success, Tr("Merge entry {{id}} processing started.", LocalizationArgs.Of("id", EntryId)));
                    Load();
                }, null, ex => ShowMessage(Tr("Process failed.")));
            }, "Process");
        }

        private void Cancel()
        {
            if (Entry == null) return;
            Confirm("Cancel Entry", Tr("Cancel merge entry for branch \"{{branchName}}\"?", LocalizationArgs.Of("branchName", Entry.BranchName)), () =>
            {
                Run((c, t) => c.CancelMergeEntryAsync(EntryId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Merge entry {{id}} cancelled.", LocalizationArgs.Of("id", EntryId)));
                    Load();
                }, null, ex => ShowMessage(Tr("Cancel failed.")));
            }, "Cancel Entry");
        }

        private void Delete()
        {
            Confirm("Delete Entry", Tr("Delete merge entry {{id}}? This cannot be undone.", LocalizationArgs.Of("id", EntryId)), () =>
            {
                Run((c, t) => c.DeleteMergeEntryAsync(EntryId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Merge entry {{id}} deleted.", LocalizationArgs.Of("id", EntryId)));
                    Context.Navigate("/missions?tab=merge-queue");
                }, null, ex => ShowMessage(Tr("Delete failed.")));
            }, "Delete");
        }

        private OpsDocument BuildOverview(OpsDocument doc)
        {
            MergeEntry? e = Entry;
            if (e == null) return doc;
            DateTime now = Context.Clock.UtcNow;
            OpsLandingPreview.Render(doc, LandingPreview, _LoadingPreview, e.BranchName + " -> " + e.TargetBranch, false);
            doc.Section("Merge Entry");
            doc.Field("ID", e.Id);
            doc.Field("Status", StatusBadge.Label(e.Status.ToString()), StatusBadge.Style(e.Status.ToString(), doc.Theme));
            doc.Field("Branch", e.BranchName);
            doc.Field("Target Branch", e.TargetBranch);
            doc.Field("Priority", e.Priority.ToString(CultureInfo.InvariantCulture));
            doc.Field("Vessel", String.IsNullOrEmpty(e.VesselId) ? "-" : Reference.VesselName(e.VesselId));
            doc.Field("Mission", e.MissionId);
            doc.Field("Batch ID", e.BatchId);
            doc.Field("Test Exit Code", e.TestExitCode?.ToString(CultureInfo.InvariantCulture));
            doc.Field("Tenant ID", e.TenantId);
            doc.Field("Test Command", e.TestCommand);
            doc.Time("Created", e.CreatedUtc, now);
            doc.Time("Test Started", e.TestStartedUtc, now);
            doc.Time("Completed", e.CompletedUtc, now);
            doc.Time("Last Updated", e.LastUpdateUtc, now);
            if (!String.IsNullOrEmpty(e.TestCommand))
            {
                doc.Section("Test Command");
                doc.Text(e.TestCommand, doc.Theme.Code);
            }

            if (!String.IsNullOrEmpty(e.TestOutput))
            {
                doc.Section("Test Output");
                doc.Note("] " + Tr("Test Output"));
            }

            return doc;
        }

        #endregion
    }
}
