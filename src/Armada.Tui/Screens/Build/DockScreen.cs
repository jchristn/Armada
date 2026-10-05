namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using Armada.Client;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// A dock's page (W4.8, <c>/docks/:id</c>), the dashboard's DockDetail: ID, tenant, status, vessel and captain
    /// links, branch, worktree path, created and updated times, and the Starting Point (start commit, target and
    /// working branch, recent commits on relevant paths, subject terms already in the tree); View JSON and Delete
    /// (cleans up the worktree). Not thread-safe.
    /// </summary>
    public class DockScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// Dock id from the route.
        /// </summary>
        public string DockId { get; }

        /// <summary>
        /// The dock once loaded.
        /// </summary>
        public Dock? Dock { get; private set; } = null;

        /// <summary>
        /// Overview panel.
        /// </summary>
        public OpsDocumentView Overview { get; } = new OpsDocumentView();

        /// <summary>
        /// Links (vessel and captain).
        /// </summary>
        public OpsLinkList Links { get; } = new OpsLinkList();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public DockScreen(RouteMatch route, TuiContext context)
            : base(route, context, "DockScreen", "Dock")
        {
            DockId = route.Param("id") ?? "";
            Reference.Ensure("vessels", "captains");
            Reference.Changed += (s, n) =>
            {
                Overview.Invalidate();
                BuildLinks();
            };

            Action("json", "View JSON", () => ShowJson(Tr("Dock: {{id}}", LocalizationArgs.Of("id", DockId)), Dock), "j", () => Dock != null, true);
            Action("delete", "Delete", Delete, "del", () => Dock != null, true, true);
            Action("vessel", "Open Vessel", () => Context.Navigate("/vessels/" + Uri.EscapeDataString(Dock!.VesselId)), "v", () => Dock != null && !String.IsNullOrEmpty(Dock.VesselId));
            Action("captain", "Open Captain", () => Context.Navigate("/captains/" + Uri.EscapeDataString(Dock!.CaptainId!)), "C", () => Dock != null && !String.IsNullOrEmpty(Dock.CaptainId));
            Action("copy-id", "Copy ID", () => Copy(DockId, "Dock ID"), "y");
            Action("copy-branch", "Copy branch", () => Copy(Dock?.BranchName, "Branch"), "Y", () => !String.IsNullOrEmpty(Dock?.BranchName));
            Action("back", "Back to Docks", () => Context.Navigate("/captains?tab=docks"), "b");

            Overview.Builder = BuildOverview;
            AddPanel("overview", "Dock Details", Overview);
            AddPanel("links", "Links", Links);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Load()
        {
            bool initial = !Loaded;
            Call((c, t) => c.GetDockAsync(DockId, t), d =>
            {
                if (d == null)
                {
                    if (initial) LoadError = Tr("Failed to load dock.");
                    return;
                }

                Dock = d;
                Loaded = true;
                LoadError = null;
                Heading = Tr("Dock Details");
                Status = d.Active ? "Active" : "Inactive";
                SubtitleText = Tr("Docks") + " / " + d.Id;
                Overview.Invalidate();
                BuildLinks();
            }, null, ex =>
            {
                if (initial) LoadError = Tr("Failed to load dock.");
            });
        }

        /// <summary>
        /// The git anchors recorded when the dock was provisioned, or null.
        /// </summary>
        /// <returns>Anchors or null.</returns>
        public GitAnchorsSnapshot? Anchors()
        {
            if (Dock == null || String.IsNullOrEmpty(Dock.GitAnchorsJson)) return null;
            try { return ArmadaJson.Deserialize<GitAnchorsSnapshot>(Dock.GitAnchorsJson!); }
            catch (Exception) { return null; }
        }

        #endregion

        #region Private-Methods

        private void BuildLinks()
        {
            List<OpsLinkItem> items = new List<OpsLinkItem>();
            Dock? d = Dock;
            if (d != null)
            {
                if (!String.IsNullOrEmpty(d.VesselId))
                    items.Add(new OpsLinkItem(Tr("Vessel") + ": " + VesselName(d.VesselId), () => Context.Navigate("/vessels/" + Uri.EscapeDataString(d.VesselId)), null, d.VesselId));
                if (!String.IsNullOrEmpty(d.CaptainId))
                    items.Add(new OpsLinkItem(Tr("Captain") + ": " + CaptainName(d.CaptainId), () => Context.Navigate("/captains/" + Uri.EscapeDataString(d.CaptainId!)), null, d.CaptainId!));
            }

            Links.Items = items;
        }

        private string VesselName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            Vessel? v = Reference.Vessels.Find(x => x.Id == id);
            return v?.Name ?? id!;
        }

        private string CaptainName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            Captain? c = Reference.Captains.Find(x => x.Id == id);
            return c?.Name ?? id!;
        }

        private void Delete()
        {
            Confirm("Delete Dock", Tr("Delete dock {{name}}? This will clean up the git worktree and cannot be undone.", LocalizationArgs.Of("name", DockId)), () =>
            {
                Run((c, t) => c.DeleteDockAsync(DockId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Dock {{id}} deleted.", LocalizationArgs.Of("id", DockId)));
                    Context.Navigate("/captains?tab=docks");
                }, null, ex => ShowMessage(Tr("Delete failed.")));
            }, "Delete");
        }

        private OpsDocument BuildOverview(OpsDocument doc)
        {
            Dock? d = Dock;
            if (d == null) return doc;
            DateTime now = Context.Clock.UtcNow;
            doc.Section("Details");
            doc.Field("ID", d.Id);
            doc.Field("Tenant ID", String.IsNullOrEmpty(d.TenantId) ? "-" : d.TenantId);
            doc.Field("Status", Tr(d.Active ? "Active" : "Inactive"));
            doc.Field("Vessel", VesselName(d.VesselId));
            doc.Field("Captain", CaptainName(d.CaptainId));
            doc.Field("Branch Name", String.IsNullOrEmpty(d.BranchName) ? "-" : d.BranchName);
            doc.Field("Worktree Path", String.IsNullOrEmpty(d.WorktreePath) ? "-" : d.WorktreePath);
            doc.Field("Created", Context.Loc.FormatRelative(d.CreatedUtc, now) + " (" + Context.Loc.FormatDateTime(d.CreatedUtc) + ")");
            doc.Field("Last Updated", Context.Loc.FormatRelative(d.LastUpdateUtc, now) + " (" + Context.Loc.FormatDateTime(d.LastUpdateUtc) + ")");

            GitAnchorsSnapshot? anchors = Anchors();
            if (anchors != null)
            {
                doc.Section("Starting Point");
                doc.Field("Start Commit", String.IsNullOrEmpty(anchors.StartCommit) ? "-" : anchors.StartCommit);
                doc.Field("Target Branch", String.IsNullOrEmpty(anchors.TargetBranch) ? "-" : anchors.TargetBranch);
                doc.Field("Working Branch", String.IsNullOrEmpty(anchors.WorkingBranch) ? "-" : anchors.WorkingBranch);
                if (anchors.RecentPathCommits != null && anchors.RecentPathCommits.Count > 0)
                {
                    doc.Text(Tr("Recent Commits On Relevant Paths"), doc.Theme.Muted);
                    foreach (string c in anchors.RecentPathCommits) doc.Text("  - " + c, doc.Theme.Code);
                }

                if (anchors.SubjectTermsPresent != null && anchors.SubjectTermsPresent.Count > 0)
                {
                    doc.Text(Tr("Subject Terms Already In Tree"), doc.Theme.Muted);
                    doc.Text("  " + String.Join("  ", anchors.SubjectTermsPresent.ConvertAll(t => "[" + t + "]")));
                }
            }

            return doc;
        }

        #endregion
    }
}
