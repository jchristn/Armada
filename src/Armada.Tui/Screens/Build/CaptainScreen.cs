namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// A captain's page (W4.7, <c>/captains/:id</c>), the dashboard's CaptainDetail: ID, name, tenant, runtime, the Mux
    /// endpoint settings, system instructions, allowed and preferred personas, model, state, quarantine with Lift
    /// Quarantine, current mission and dock links, process id, recovery attempts, heartbeat, created and updated; the
    /// current mission card; the captain log (log viewer with the server's Readable formatting, 500 lines first); and
    /// recent missions. Actions: Edit, Duplicate, View Tools, View Log, View JSON, Recall and Stop (while working,
    /// stalled, or planning), and Remove. Reloads on <c>captain.changed</c>. Not thread-safe.
    /// </summary>
    public class CaptainScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// Captain id from the route.
        /// </summary>
        public string CaptainId { get; }

        /// <summary>
        /// The captain once loaded.
        /// </summary>
        public Captain? Captain { get; private set; } = null;

        /// <summary>
        /// The current mission, when the captain has one.
        /// </summary>
        public Mission? CurrentMission { get; private set; } = null;

        /// <summary>
        /// Recent missions of this captain.
        /// </summary>
        public List<MissionSummary> Missions { get; private set; } = new List<MissionSummary>();

        /// <summary>
        /// Overview panel.
        /// </summary>
        public OpsDocumentView Overview { get; } = new OpsDocumentView();

        /// <summary>
        /// Recent missions panel.
        /// </summary>
        public ArmadaGrid<MissionSummary> MissionGrid { get; }

        /// <summary>
        /// Server readable formatting for the captain log (the dashboard's Readable checkbox, on by default).
        /// </summary>
        public bool LogReadable { get; private set; } = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public CaptainScreen(RouteMatch route, TuiContext context)
            : base(route, context, "CaptainScreen", "Captain")
        {
            CaptainId = route.Param("id") ?? "";
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
            MissionGrid.AddColumn(new GridColumn<MissionSummary>("branch", "Branch", m => String.IsNullOrEmpty(m.BranchName) ? "-" : m.BranchName!) { Weight = 3 });
            MissionGrid.AddColumn(new GridColumn<MissionSummary>("date", "Date", m => Context.Loc.FormatRelative(m.CompletedUtc ?? m.CreatedUtc, Context.Clock.UtcNow)) { Width = 14 });
            MissionGrid.Activated += (s, m) => Context.Navigate("/missions/" + Uri.EscapeDataString(m.Id));

            Action("edit", "Edit", () => CaptainForm.Open(this, Captain!, true, c => Load()), "e", () => Captain != null, true);
            Action("duplicate", "Duplicate", Duplicate, "u", () => Captain != null);
            Action("tools", "View Tools", () => CaptainTools.Show(this, CaptainId, Captain!.Name), "t", () => Captain != null, true);
            Action("log", "View Log", () => ViewLog(), "l", () => Captain != null, true);
            Action("json", "View JSON", () => ShowJson(Tr("Captain: {{name}}", LocalizationArgs.Of("name", Captain!.Name)), Captain), "j", () => Captain != null, true);
            Action("unquarantine", "Lift Quarantine", Unquarantine, "q", () => Captain != null && Captain.State == CaptainStateEnum.Quarantined, true);
            Action("recall", "Recall Captain", Recall, "R", () => Captain != null && (Captain.State == CaptainStateEnum.Working || Captain.State == CaptainStateEnum.Stalled));
            Action("stop", "Stop Captain", Stop, "x", () => Captain != null && (Captain.State == CaptainStateEnum.Working || Captain.State == CaptainStateEnum.Stalled || Captain.State == CaptainStateEnum.Planning), false, true);
            Action("remove", "Remove", Remove, "del", () => Captain != null, false, true);
            Action("mission", "View Mission", () => Context.Navigate("/missions/" + Uri.EscapeDataString(Captain!.CurrentMissionId!)), "m", () => !String.IsNullOrEmpty(Captain?.CurrentMissionId));
            Action("dock", "Open Dock", () => Context.Navigate("/docks/" + Uri.EscapeDataString(Captain!.CurrentDockId!)), "k", () => !String.IsNullOrEmpty(Captain?.CurrentDockId));
            Action("copy-id", "Copy ID", () => Copy(CaptainId, "Captain ID"), "y");

            Overview.Builder = BuildOverview;
            AddPanel("overview", "Overview", Overview);
            AddPanel("missions", "Recent Missions", MissionGrid);
            SubscribeCoalesced("captain.changed", Load);
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
                CaptainLoad result = new CaptainLoad();
                result.Captain = await c.GetCaptainAsync(CaptainId, t).ConfigureAwait(false);
                if (result.Captain != null && !String.IsNullOrEmpty(result.Captain.CurrentMissionId))
                {
                    try { result.CurrentMission = await c.GetMissionAsync(result.Captain.CurrentMissionId!, t).ConfigureAwait(false); }
                    catch (ArmadaApiException) { result.CurrentMission = null; }
                }

                try
                {
                    ArmadaPageQuery q = new ArmadaPageQuery(1, 100);
                    q.With("captainId", CaptainId);
                    result.Missions = (await c.ListMissionSummariesAsync(q, t).ConfigureAwait(false))?.Objects ?? new List<MissionSummary>();
                }
                catch (ArmadaApiException)
                {
                    result.Missions = new List<MissionSummary>();
                }

                return result;
            }, r =>
            {
                if (r.Captain == null)
                {
                    if (initial) LoadError = Tr("Captain not found.");
                    return;
                }

                Captain = r.Captain;
                CurrentMission = r.CurrentMission;
                Missions = r.Missions;
                MissionGrid.SetLocalRows(Missions);
                Loaded = true;
                LoadError = null;
                Heading = Captain.Name;
                Status = Captain.State.ToString();
                SubtitleText = Tr("Captains") + " > " + Captain.Name;
                Overview.Invalidate();
            }, null, ex =>
            {
                if (initial) LoadError = Tr("Failed to load captain.");
            });
        }

        /// <summary>
        /// Open the captain log viewer.
        /// </summary>
        /// <returns>The viewer.</returns>
        public OpsLogModal ViewLog()
        {
            OpsLogModal modal = new OpsLogModal(
                Tr("Captain Log") + ": " + (Captain?.Name ?? CaptainId),
                (lines, token) => Context.Client.GetCaptainLogAsync(CaptainId, lines, LogReadable, token),
                () => false,
                Context.Dispatcher,
                text => Context.Clipboard.Copy(text, "Log"),
                Context.Loc,
                Context.Theme.Current,
                null,
                500);
            modal.ServerReadable = LogReadable;
            modal.ToggleServerReadable = () =>
            {
                LogReadable = !LogReadable;
                return LogReadable;
            };
            Context.Modals.Show(modal, r => modal.Dispose());
            return modal;
        }

        #endregion

        #region Private-Methods

        private void Duplicate()
        {
            if (Captain == null) return;
            Call((c, t) => c.CreateCaptainAsync(CaptainForm.DuplicatePayload(Captain), t), created =>
            {
                if (created == null) return;
                Toast(NotificationSeverityEnum.Success, Tr("Captain \"{{name}}\" duplicated.", LocalizationArgs.Of("name", created.Name)));
                Context.Navigate("/captains/" + Uri.EscapeDataString(created.Id));
            }, null, ex => ShowMessage(ex is ArmadaApiException api ? api.Message : Tr("Duplicate failed.")));
        }

        private void Unquarantine()
        {
            if (Captain == null) return;
            string name = Captain.Name;
            Call((c, t) => c.UnquarantineCaptainAsync(CaptainId, t), r =>
            {
                Toast(NotificationSeverityEnum.Success, Tr("Quarantine lifted for \"{{name}}\".", LocalizationArgs.Of("name", name)));
                Load();
            }, null, ex => ShowMessage(Tr("Failed to lift quarantine.")));
        }

        private void Stop()
        {
            if (Captain == null) return;
            string name = Captain.Name;
            Confirm("Stop Captain", Tr("Stop captain \"{{name}}\"? This will halt the current mission.", LocalizationArgs.Of("name", name)), () =>
            {
                Run((c, t) => c.StopCaptainAsync(CaptainId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Captain \"{{name}}\" stopped.", LocalizationArgs.Of("name", name)));
                    Load();
                }, null, ex => ShowMessage(Tr("Failed to stop captain.")));
            }, "Stop");
        }

        private void Recall()
        {
            if (Captain == null) return;
            string name = Captain.Name;
            Confirm("Recall Captain", Tr("Recall captain \"{{name}}\"? The captain will finish current work and return to idle.", LocalizationArgs.Of("name", name)), () =>
            {
                Run((c, t) => c.RecallCaptainAsync(CaptainId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Captain \"{{name}}\" recalled.", LocalizationArgs.Of("name", name)));
                    Load();
                }, null, ex => ShowMessage(Tr("Failed to recall captain.")));
            }, "Recall");
        }

        private void Remove()
        {
            if (Captain == null) return;
            string name = Captain.Name;
            Confirm("Remove Captain", Tr("Remove captain \"{{name}}\"? This cannot be undone.", LocalizationArgs.Of("name", name)), () =>
            {
                Run((c, t) => c.DeleteCaptainAsync(CaptainId, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Captain \"{{name}}\" removed.", LocalizationArgs.Of("name", name)));
                    Context.Navigate("/captains");
                }, null, ex => ShowMessage(Tr("Remove failed.")));
            }, "Remove");
        }

        private OpsDocument BuildOverview(OpsDocument doc)
        {
            Captain? c = Captain;
            if (c == null) return doc;
            DateTime now = Context.Clock.UtcNow;
            doc.Section("Details");
            doc.Field("ID", c.Id);
            doc.Field("Name", c.Name);
            doc.Field("Tenant ID", String.IsNullOrEmpty(c.TenantId) ? "-" : c.TenantId);
            doc.Field("Runtime", c.Runtime.ToString());
            if (c.Runtime == AgentRuntimeEnum.Mux)
            {
                MuxCaptainOptions? mux = null;
                try { mux = CaptainRuntimeOptions.GetMuxOptions(c); }
                catch (Exception) { mux = null; }
                doc.Field("Mux Endpoint", !String.IsNullOrEmpty(mux?.Endpoint) ? mux!.Endpoint : Tr("Not configured"));
                doc.Field("Mux Config Directory", !String.IsNullOrEmpty(mux?.ConfigDirectory) ? mux!.ConfigDirectory : Tr("Mux default"));
                doc.Field("Mux Adapter", !String.IsNullOrEmpty(mux?.AdapterType) ? mux!.AdapterType : Tr("Endpoint default"));
                doc.Field("Mux Base URL", !String.IsNullOrEmpty(mux?.BaseUrl) ? mux!.BaseUrl : Tr("Endpoint default"));
            }

            doc.Field("Allowed Personas", String.IsNullOrEmpty(c.AllowedPersonas) ? Tr("Any (no restriction)") : c.AllowedPersonas);
            doc.Field("Model", String.IsNullOrEmpty(c.Model) ? Tr("Runtime default") : c.Model);
            doc.Field("Preferred Persona", String.IsNullOrEmpty(c.PreferredPersona) ? Tr("None") : c.PreferredPersona);
            doc.Field("Reasoning effort", c.ReasoningEffort?.ToString() ?? Tr("Runtime default"));
            doc.Field("Capability tier", c.Tier.HasValue ? Tr(c.Tier.Value.ToString()) : Tr("Auto (classify from model)"));
            doc.Field("State", StatusBadge.Label(c.State), StatusBadge.Style(c.State, doc.Theme));
            if (c.State == CaptainStateEnum.Quarantined)
            {
                string q = String.IsNullOrEmpty(c.QuarantineReason) ? Tr("quarantined") : c.QuarantineReason!;
                if (c.QuarantineUntilUtc.HasValue) q += " (" + Tr("until") + " " + Context.Loc.FormatDateTime(c.QuarantineUntilUtc.Value) + ")";
                doc.Field("Quarantine", q + "   q " + Tr("Lift Quarantine"), doc.Theme.Warning);
            }

            doc.Field("Current Mission", String.IsNullOrEmpty(c.CurrentMissionId) ? "-" : c.CurrentMissionId + "   (m)");
            doc.Field("Current Dock", String.IsNullOrEmpty(c.CurrentDockId) ? "-" : c.CurrentDockId + "   (k)");
            doc.Field("Process ID", c.ProcessId.HasValue && c.ProcessId.Value != 0 ? c.ProcessId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-");
            doc.Field("Recovery Attempts", c.RecoveryAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
            doc.Field("Last Heartbeat", c.LastHeartbeatUtc.HasValue ? Context.Loc.FormatRelative(c.LastHeartbeatUtc.Value, now) + " (" + Context.Loc.FormatDateTime(c.LastHeartbeatUtc.Value) + ")" : "-");
            doc.Field("Created", Context.Loc.FormatRelative(c.CreatedUtc, now) + " (" + Context.Loc.FormatDateTime(c.CreatedUtc) + ")");
            doc.Field("Last Updated", Context.Loc.FormatRelative(c.LastUpdateUtc, now) + " (" + Context.Loc.FormatDateTime(c.LastUpdateUtc) + ")");

            if (!String.IsNullOrEmpty(c.SystemInstructions))
            {
                doc.Section("System Instructions");
                foreach (string line in c.SystemInstructions!.Split('\n')) doc.Text(line.TrimEnd('\r'), doc.Theme.Code);
            }

            Mission? m = CurrentMission;
            if (m != null)
            {
                doc.Section("Current Mission");
                doc.Add(StyledText.From(m.Title + "  ", doc.Theme.Text).Append(StyledText.From(StatusBadge.Label(m.Status), StatusBadge.Style(m.Status, doc.Theme))));
                if (!String.IsNullOrEmpty(m.Description)) doc.Text(m.Description, doc.Theme.Muted);
                doc.Text(Tr("Branch") + ": " + (String.IsNullOrEmpty(m.BranchName) ? "-" : m.BranchName) + "   " + Tr("Priority") + ": " + m.Priority);
                doc.Note("m " + Tr("View Mission"));
            }

            doc.Section("Recent Missions", " (" + Missions.Count + ")");
            doc.Note(Missions.Count == 0 ? "No missions yet" : "] " + Tr("Recent Missions") + ": Enter " + Tr("Open"));
            return doc;
        }

        #endregion
    }
}
