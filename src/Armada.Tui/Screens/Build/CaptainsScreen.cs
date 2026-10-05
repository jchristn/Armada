namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Captains (W4.7, the Captains tab of <c>/captains</c>), the dashboard's Captains page: every captain (admin user
    /// scope on the server), name, runtime, and state filters, sortable name, runtime, state, and created; the tier
    /// badge and quarantine tag; Delete Selected, Stop All (confirmed), and + Captain; and the row menu (View Detail,
    /// Start Planning for idle captains that support it, Edit, Duplicate, View Tools, View JSON, View Notifications,
    /// Stop, Recall, Restart, Delete). Refreshes on <c>captain.changed</c> and auto-refresh. Not thread-safe.
    /// </summary>
    public class CaptainsScreen : OpsListScreen<Captain>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "AI agent harness processes that execute missions. Monitor state, current mission, and captain lifecycle."; }
        }

        /// <summary>
        /// Name filter.
        /// </summary>
        public TextInput NameFilter { get; }

        /// <summary>
        /// Runtime filter.
        /// </summary>
        public TextInput RuntimeFilter { get; }

        /// <summary>
        /// State filter.
        /// </summary>
        public TextInput StateFilter { get; }

        /// <summary>
        /// User scope filter (admins only).
        /// </summary>
        public SelectField<string>? UserScope { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public CaptainsScreen(RouteMatch route, TuiContext context)
            : base(route, context, c => c.Id, "CaptainsScreen", "Captains")
        {
            Grid.EmptyText = "No captains configured.";
            UserScope = UserScopeFilter();
            NameFilter = TextFilter("Name", 16, "Filter...");
            RuntimeFilter = TextFilter("Runtime", 12, "Filter...");
            StateFilter = TextFilter("State", 12, "Filter...");

            Column("name", "Name", c => c.Name + (c.Tier.HasValue ? "  [" + Tr(c.Tier.Value.ToString()) + "]" : ""), 3, null, c => c.Name.ToLowerInvariant()).Pinned = true;
            Column("id", "ID", c => c.Id, 0, 26);
            Column("runtime", "Runtime", c => c.Runtime.ToString(), 0, 12, c => c.Runtime.ToString().ToLowerInvariant());
            Column("state", "State", StateText, 0, 28, c => c.State.ToString().ToLowerInvariant(), (c, t) => StatusBadge.Style(c.State, t));
            Column("mission", "Current Mission", c => String.IsNullOrEmpty(c.CurrentMissionId) ? "-" : BuildText.Short(c.CurrentMissionId) + "...", 0, 16);
            Column("heartbeat", "Heartbeat", c => c.LastHeartbeatUtc.HasValue ? Context.Loc.FormatRelative(c.LastHeartbeatUtc.Value, Context.Clock.UtcNow) : "-", 0, 14);
            Column("createdUtc", "Created", c => Context.Loc.FormatRelative(c.CreatedUtc, Context.Clock.UtcNow), 0, 14, c => c.CreatedUtc);

            OpsScreenAction bulk = new OpsScreenAction("delete-selected", "Delete Selected", BulkDelete, "D", () => Grid.Marked.Count > 0);
            bulk.DynamicLabel = () => Tr("Delete Selected") + " (" + Grid.Marked.Count + ")";
            bulk.Danger = true;
            ScreenActions.Add(bulk);
            OpsScreenAction stopAll = new OpsScreenAction("stop-all", "Stop All", StopAll, "X");
            stopAll.Danger = true;
            ScreenActions.Add(stopAll);
            ScreenActions.Add(new OpsScreenAction("new", "+ Captain", () => CaptainForm.Open(this, null, false, c => Refresh()), "n"));

            RowActions.Add(new OpsAction<Captain>("view", "View Detail", c => OpenRow(c), "o"));
            RowActions.Add(new OpsAction<Captain>("planning", "Start Planning", c => Context.Navigate(OpsHandoff.Planning(null, c.Id, null, null, null, null, null, null)), "P", CanStartPlanning));
            RowActions.Add(new OpsAction<Captain>("edit", "Edit", c => CaptainForm.Open(this, c, false, x => Refresh()), "e"));
            RowActions.Add(new OpsAction<Captain>("duplicate", "Duplicate", Duplicate, "u"));
            RowActions.Add(new OpsAction<Captain>("tools", "View Tools", c => CaptainTools.Show(this, c.Id, c.Name), "t"));
            RowActions.Add(new OpsAction<Captain>("json", "View JSON", c => ShowJson(Tr("Captain") + ": " + c.Name, c), "j"));
            RowActions.Add(new OpsAction<Captain>("notifications", "View Notifications", c => Context.Navigate("/inbox"), "i"));
            RowActions.Add(new OpsAction<Captain>("mission", "Open Mission", c => Context.Navigate("/missions/" + Uri.EscapeDataString(c.CurrentMissionId!)), "m", c => !String.IsNullOrEmpty(c.CurrentMissionId)));
            RowActions.Add(new OpsAction<Captain>("copy-id", "Copy ID", c => Copy(c.Id, "Captain ID"), "y"));
            RowActions.Add(new OpsAction<Captain>("stop", "Stop", Stop, "x"));
            RowActions.Add(new OpsAction<Captain>("recall", "Recall", Recall, "R"));
            RowActions.Add(new OpsAction<Captain>("restart", "Restart", Restart, "r"));
            OpsAction<Captain> delete = new OpsAction<Captain>("delete", "Delete", Delete, "del");
            delete.Danger = true;
            RowActions.Add(delete);

            SubscribeCoalesced("captain.changed", Refresh);
            Start("name", false);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The dashboard's <c>canCaptainStartPlanning</c>: supports planning sessions and is idle.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <returns>True when Start Planning applies.</returns>
        public static bool CanStartPlanning(Captain captain)
        {
            return captain != null && captain.SupportsPlanningSessions && captain.State == CaptainStateEnum.Idle;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool ServerPaging
        {
            get { return false; }
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Captain>> FetchAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(1, 9999);
            q.With("userId", UserScope?.Value);
            EnumerationResult<Captain>? result = await Context.Client.ListCaptainsAsync(q, token).ConfigureAwait(false);
            List<Captain> rows = result?.Objects ?? new List<Captain>();
            return new GridPage<Captain>(rows, rows.Count);
        }

        /// <inheritdoc />
        protected override IEnumerable<Captain> FilterLocal(IEnumerable<Captain> rows)
        {
            string name = NameFilter.Value.Trim();
            string runtime = RuntimeFilter.Value.Trim();
            string state = StateFilter.Value.Trim();
            return rows.Where(c =>
                (name.Length == 0 || c.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (runtime.Length == 0 || c.Runtime.ToString().IndexOf(runtime, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (state.Length == 0 || c.State.ToString().IndexOf(state, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<Captain> rows)
        {
            Grid.EmptyText = rows.Count == 0 ? "No captains configured." : "No captains match the current filters.";
        }

        /// <inheritdoc />
        protected override void OpenRow(Captain row)
        {
            Context.Navigate("/captains/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(Captain row)
        {
            return row.Name;
        }

        #endregion

        #region Private-Methods

        private string StateText(Captain c)
        {
            string text = StatusBadge.Label(c.State);
            if (c.State == CaptainStateEnum.Quarantined)
            {
                text += c.QuarantineUntilUtc.HasValue
                    ? "  [" + Tr("until {{time}}", LocalizationArgs.Of("time", Context.Loc.FormatRelative(c.QuarantineUntilUtc.Value, Context.Clock.UtcNow))) + "]"
                    : "  [" + Tr("quarantined") + "]";
            }

            return text;
        }

        private void Duplicate(Captain captain)
        {
            Call((c, t) => c.CreateCaptainAsync(CaptainForm.DuplicatePayload(captain), t), created =>
            {
                if (created == null) return;
                Toast(NotificationSeverityEnum.Success, Tr("Captain \"{{name}}\" duplicated.", LocalizationArgs.Of("name", created.Name)));
                Context.Navigate("/captains/" + Uri.EscapeDataString(created.Id));
            }, null, ex => ShowMessage(ex is ArmadaApiException api ? api.Message : Tr("Duplicate failed.")));
        }

        private void Stop(Captain captain)
        {
            Confirm("Stop Captain", Tr("Stop captain \"{{name}}\"? The captain process will be terminated.", LocalizationArgs.Of("name", captain.Name)), () =>
            {
                Run((c, t) => c.StopCaptainAsync(captain.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Captain \"{{name}}\" stopped.", LocalizationArgs.Of("name", captain.Name)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Stop failed.")));
            }, "Stop");
        }

        private void Recall(Captain captain)
        {
            Confirm("Recall Captain", Tr("Recall captain \"{{name}}\"? The captain will be recalled from its current mission.", LocalizationArgs.Of("name", captain.Name)), () =>
            {
                Run((c, t) => c.RecallCaptainAsync(captain.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Captain \"{{name}}\" recalled.", LocalizationArgs.Of("name", captain.Name)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Recall failed.")));
            }, "Recall");
        }

        private void Restart(Captain captain)
        {
            Confirm("Restart Captain", Tr("Restart captain \"{{name}}\"? The captain will be deleted and recreated with the same saved configuration.", LocalizationArgs.Of("name", captain.Name)), () =>
            {
                Run((c, t) => c.RestartCaptainAsync(captain.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Success, Tr("Captain \"{{name}}\" restarted.", LocalizationArgs.Of("name", captain.Name)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Restart failed.")));
            }, "Restart");
        }

        private void Delete(Captain captain)
        {
            Confirm("Delete Captain", Tr("Delete captain \"{{name}}\"? This cannot be undone.", LocalizationArgs.Of("name", captain.Name)), () =>
            {
                Run((c, t) => c.DeleteCaptainAsync(captain.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Captain \"{{name}}\" deleted.", LocalizationArgs.Of("name", captain.Name)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Delete failed.")));
            }, "Delete");
        }

        private void StopAll()
        {
            Confirm("Stop All Captains", Tr("Stop ALL captains? All captain processes will be terminated. This cannot be undone."), () =>
            {
                Run((c, t) => c.StopAllCaptainsAsync(t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("All captains stopped."));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Stop all failed.")));
            }, "Stop All");
        }

        private void BulkDelete()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Confirm("Delete Selected Captains", Tr("Delete {{count}} selected captain(s)? This cannot be undone.", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                Grid.ClearMarks();
                Call(async (c, t) =>
                {
                    int failed = 0;
                    foreach (string id in ids)
                    {
                        try { await c.DeleteCaptainAsync(id, t).ConfigureAwait(false); }
                        catch (ArmadaApiException) { failed++; }
                    }

                    return failed;
                }, failed =>
                {
                    int deleted = ids.Count - failed;
                    if (deleted > 0)
                    {
                        Toast(failed > 0 ? NotificationSeverityEnum.Warning : NotificationSeverityEnum.Success, failed > 0
                            ? Tr("Deleted {{deleted}} captains. {{failed}} failed.", LocalizationArgs.Of("deleted", deleted, "failed", failed))
                            : Tr("Deleted {{deleted}} captains.", LocalizationArgs.Of("deleted", deleted)));
                    }

                    if (failed > 0) ShowMessage(Tr("Deleted {{deleted}} captains, {{failed}} failed.", LocalizationArgs.Of("deleted", deleted, "failed", failed)));
                    Refresh();
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
