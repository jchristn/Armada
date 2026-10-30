namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Black Box, the Missions hub's Black Box tab (<c>/missions?tab=blackbox</c>; <c>/blackbox</c> redirects there),
    /// mirroring the dashboard's Black Box tab: the failure entries the black box
    /// recorded, server paged and filtered by vessel, triage, resolution, phase, and fingerprint (the route query
    /// <c>missionId</c>, <c>voyageId</c>, <c>vesselId</c>, and <c>fingerprint</c> pre-filter it). <c>g</c> opens the
    /// fingerprints view (most frequent failure signatures; choosing one filters the list), <c>R</c> the recovery
    /// statistics view (each recovery path's tracked runs and success rate, for the vessel filter), Enter opens an entry, and
    /// the row menu pins, copies, opens the mission, and deletes. Refreshes on <c>blackbox.</c> events. Not thread-safe.
    /// </summary>
    public class BlackBoxScreen : OpsListScreen<BlackBoxEntrySummary>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Failures the black box recorded: where, how, and why each one happened, with recovery paths."; }
        }

        /// <summary>
        /// Vessel filter (server).
        /// </summary>
        public SelectField<string> VesselFilter { get; }

        /// <summary>
        /// Triage filter (server).
        /// </summary>
        public SelectField<string> TriageFilter { get; }

        /// <summary>
        /// Resolution filter (server).
        /// </summary>
        public SelectField<string> ResolutionFilter { get; }

        /// <summary>
        /// Phase filter (server).
        /// </summary>
        public SelectField<string> PhaseFilter { get; }

        /// <summary>
        /// Fingerprint filter (server).
        /// </summary>
        public TextInput FingerprintFilter { get; }

        /// <summary>
        /// Fingerprints shown by the last fingerprints view.
        /// </summary>
        public List<BlackBoxFingerprintSummary> Fingerprints { get; private set; } = new List<BlackBoxFingerprintSummary>();

        /// <summary>
        /// Most fingerprints the fingerprints view reads.
        /// </summary>
        public const int FingerprintPageSize = 50;

        /// <summary>
        /// Most recovery statistics the recovery stats view reads.
        /// </summary>
        public const int RecoveryStatsPageSize = 100;

        /// <summary>
        /// Recovery statistics shown by the last recovery stats view.
        /// </summary>
        public List<BlackBoxRecoveryStat> RecoveryStats { get; private set; } = new List<BlackBoxRecoveryStat>();

        #endregion

        #region Private-Members

        private readonly string? _MissionId;
        private readonly string? _VoyageId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public BlackBoxScreen(RouteMatch route, TuiContext context)
            : base(route, context, e => e.Id, "BlackBoxScreen", "Black Box")
        {
            _MissionId = Query(route, "missionId");
            _VoyageId = Query(route, "voyageId");
            Grid.EmptyText = "No failures recorded.";
            Reference.Ensure("vessels");
            VesselFilter = SelectFilter("Vessel", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Vessels")) }, 18, true);
            Reference.Changed += (s, name) =>
            {
                if (name != "vessels") return;
                string? current = VesselFilter.Value;
                VesselFilter.Options = Reference.VesselOptions("All Vessels");
                VesselFilter.SetValue(current ?? "");
            };
            TriageFilter = SelectFilter("Triage", EnumOptions<BlackBoxTriageEnum>("All Triage"), 16, true);
            ResolutionFilter = SelectFilter("Resolution", EnumOptions<BlackBoxResolutionEnum>("All Resolutions"), 16, true);
            PhaseFilter = SelectFilter("Phase", EnumOptions<BlackBoxPhaseEnum>("All Phases"), 14, true);
            FingerprintFilter = TextFilter("Fingerprint", 18, "Fingerprint...", true);
            string? vessel = Query(route, "vesselId");
            if (vessel != null) VesselFilter.SetValue(vessel);
            string? fingerprint = Query(route, "fingerprint");
            if (fingerprint != null) FingerprintFilter.Value = fingerprint;

            Column("createdUtc", "Captured", e => Context.Loc.FormatRelative(e.CreatedUtc, Context.Clock.UtcNow), 0, 14, e => e.CreatedUtc);
            Column("subject", "Subject", e => (String.IsNullOrEmpty(e.MissionTitle) ? (e.MissionId ?? e.VoyageId ?? e.Id) : e.MissionTitle!), 3);
            Column("vessel", "Vessel", e => String.IsNullOrEmpty(e.VesselName) ? Reference.VesselName(e.VesselId) : e.VesselName!, 2);
            Column("where", "Where", e => BlackBoxText.Where(e.Phase, e.Step), 2);
            Column("kind", "Kind", e => e.FailureKind?.ToString() ?? "-", 0, 16);
            Column("triage", "Triage", e => e.Triage.ToString(), 0, 13);
            Column("resolution", "Resolution", e => e.Resolution.ToString() + (e.Pinned ? " *" : ""), 0, 13);
            GridColumn<BlackBoxEntrySummary> reason = Column("reason", "Reason", e => e.ReasonExcerpt ?? "", 3);
            reason.DefaultVisible = false;
            GridColumn<BlackBoxEntrySummary> id = Column("id", "ID", e => e.Id, 0, 26);
            id.DefaultVisible = false;

            ScreenActions.Add(new OpsScreenAction("fingerprints", "Fingerprints", ShowFingerprints, "F"));
            ScreenActions.Add(new OpsScreenAction("recovery-stats", "Recovery stats", ShowRecoveryStats, "R"));
            ScreenActions.Add(new OpsScreenAction("clear-fingerprint", "Clear fingerprint", () => SetFingerprint(""), "X", () => FingerprintFilter.Value.Length > 0));

            RowActions.Add(new OpsAction<BlackBoxEntrySummary>("view", "View Entry", e => OpenRow(e), "o"));
            RowActions.Add(new OpsAction<BlackBoxEntrySummary>("mission", "Open Mission", e => Context.Navigate("/missions/" + Uri.EscapeDataString(e.MissionId!)), "m", e => !String.IsNullOrEmpty(e.MissionId)));
            RowActions.Add(new OpsAction<BlackBoxEntrySummary>("voyage", "Open Voyage", e => Context.Navigate("/voyages/" + Uri.EscapeDataString(e.VoyageId!)), "V", e => !String.IsNullOrEmpty(e.VoyageId)));
            RowActions.Add(new OpsAction<BlackBoxEntrySummary>("same-fingerprint", "Same fingerprint", e => SetFingerprint(e.Fingerprint ?? ""), "f", e => !String.IsNullOrEmpty(e.Fingerprint)));
            RowActions.Add(new OpsAction<BlackBoxEntrySummary>("pin", "Pin or unpin", TogglePin, "p"));
            RowActions.Add(new OpsAction<BlackBoxEntrySummary>("json", "View JSON", e => ShowJson(Tr("Black Box: {{id}}", LocalizationArgs.Of("id", e.Id)), e), "j"));
            RowActions.Add(new OpsAction<BlackBoxEntrySummary>("copy-id", "Copy ID", e => Copy(e.Id, "Black box entry ID"), "y"));
            OpsAction<BlackBoxEntrySummary> delete = new OpsAction<BlackBoxEntrySummary>("delete", "Delete", Delete, "del");
            delete.Danger = true;
            RowActions.Add(delete);

            SubscribeCoalesced("blackbox.", Refresh);
            Start("createdUtc", true);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The server query for the current filters and page.
        /// </summary>
        /// <param name="pageNumber">Page number.</param>
        /// <param name="pageSize">Page size.</param>
        /// <returns>Request.</returns>
        public BlackBoxEnumerateRequest BuildRequest(int pageNumber, int pageSize)
        {
            BlackBoxEnumerateRequest q = new BlackBoxEnumerateRequest();
            q.PageNumber = Math.Max(1, pageNumber);
            q.PageSize = Math.Max(1, pageSize);
            q.Order = EnumerationOrderEnum.CreatedDescending;
            if (!String.IsNullOrEmpty(VesselFilter.Value)) q.VesselId = VesselFilter.Value;
            if (_MissionId != null) q.MissionId = _MissionId;
            if (_VoyageId != null) q.VoyageId = _VoyageId;
            if (Enum.TryParse(TriageFilter.Value, out BlackBoxTriageEnum triage)) q.Triage = triage;
            if (Enum.TryParse(ResolutionFilter.Value, out BlackBoxResolutionEnum resolution)) q.Resolution = resolution;
            if (Enum.TryParse(PhaseFilter.Value, out BlackBoxPhaseEnum phase)) q.Phase = phase;
            string fingerprint = FingerprintFilter.Value.Trim();
            if (fingerprint.Length > 0) q.Fingerprint = fingerprint;
            return q;
        }

        /// <summary>
        /// The fingerprints view: the most frequent failure signatures (for the vessel filter); choosing one filters the
        /// list to it.
        /// </summary>
        public void ShowFingerprints()
        {
            BlackBoxFingerprintEnumerateRequest request = new BlackBoxFingerprintEnumerateRequest();
            request.PageNumber = 1;
            request.PageSize = FingerprintPageSize;
            if (!String.IsNullOrEmpty(VesselFilter.Value)) request.VesselId = VesselFilter.Value;
            Call((c, t) => c.EnumerateBlackBoxFingerprintsAsync(request, t), r =>
            {
                Fingerprints = r?.Objects ?? new List<BlackBoxFingerprintSummary>();
                if (Fingerprints.Count == 0)
                {
                    Toast(NotificationSeverityEnum.Info, Tr("No fingerprints recorded yet."));
                    return;
                }

                List<ActionMenuItem> items = new List<ActionMenuItem>();
                foreach (BlackBoxFingerprintSummary f in Fingerprints)
                {
                    BlackBoxFingerprintSummary fp = f;
                    string label = fp.Count.ToString(CultureInfo.InvariantCulture) + "x  " + BlackBoxText.Where(fp.Phase, fp.Step) + "  " + (fp.FailureKind?.ToString() ?? "-")
                        + "  " + Tr("{{open}} open", LocalizationArgs.Of("open", fp.OpenCount)) + "  " + Short(fp.Fingerprint);
                    items.Add(new ActionMenuItem(label, () => SetFingerprint(fp.Fingerprint), ""));
                }

                ShowMenu("Fingerprints", items);
            }, "Fingerprints could not be loaded");
        }

        /// <summary>
        /// The recovery statistics view: each recovery path's confirmed runs followed to the mission's next terminal
        /// status (succeeded, failed, pending, expired) and its success rate, for the vessel filter.
        /// </summary>
        public void ShowRecoveryStats()
        {
            BlackBoxRecoveryStatsRequest request = new BlackBoxRecoveryStatsRequest();
            request.PageNumber = 1;
            request.PageSize = RecoveryStatsPageSize;
            if (!String.IsNullOrEmpty(VesselFilter.Value)) request.VesselId = VesselFilter.Value;
            Call((c, t) => c.GetBlackBoxRecoveryStatsAsync(request, t), r =>
            {
                RecoveryStats = r?.Objects ?? new List<BlackBoxRecoveryStat>();
                if (RecoveryStats.Count == 0)
                {
                    Toast(NotificationSeverityEnum.Info, Tr("No recovery runs have been tracked yet."));
                    return;
                }

                List<string> lines = new List<string>();
                lines.Add(Tr("Confirmed recovery runs, followed to the mission's next terminal status.") + (String.IsNullOrEmpty(VesselFilter.Value) ? "" : "  " + Tr("Vessel") + ": " + Reference.VesselName(VesselFilter.Value)));
                long total = r?.TotalRecords ?? RecoveryStats.Count;
                if (total > RecoveryStats.Count) lines.Add(Tr("Showing {{shown}} of {{total}} paths.", LocalizationArgs.Of("shown", RecoveryStats.Count, "total", total)));
                lines.Add("");
                lines.Add(Tr("Rule  Path  Success  Runs  Ok  Failed  Pending  Expired"));
                foreach (BlackBoxRecoveryStat stat in RecoveryStats) lines.Add(BlackBoxInsightText.RecoveryStat(stat));
                ShowText(Tr("Recovery Stats"), String.Join("\n", lines));
            }, "Recovery stats could not be loaded");
        }

        /// <summary>
        /// Filter the list to one fingerprint (empty clears the filter).
        /// </summary>
        /// <param name="fingerprint">Fingerprint.</param>
        public void SetFingerprint(string fingerprint)
        {
            FingerprintFilter.Value = fingerprint ?? "";
            ServerFilterChanged();
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<BlackBoxEntrySummary>> FetchAsync(GridQuery query, CancellationToken token)
        {
            BlackBoxEnumerateRequest q = BuildRequest(query.PageNumber, query.PageSize);
            EnumerationResult<BlackBoxEntrySummary>? result = await Context.Client.EnumerateBlackBoxAsync(q, token).ConfigureAwait(false);
            List<BlackBoxEntrySummary> rows = result?.Objects ?? new List<BlackBoxEntrySummary>();
            return new GridPage<BlackBoxEntrySummary>(rows, result?.TotalRecords ?? rows.Count);
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<BlackBoxEntrySummary> rows)
        {
            bool filtered = !String.IsNullOrEmpty(VesselFilter.Value) || !String.IsNullOrEmpty(TriageFilter.Value) || !String.IsNullOrEmpty(ResolutionFilter.Value)
                || !String.IsNullOrEmpty(PhaseFilter.Value) || FingerprintFilter.Value.Trim().Length > 0 || _MissionId != null || _VoyageId != null;
            Grid.EmptyText = filtered ? "No failures match the current filters." : "No failures recorded.";
        }

        /// <inheritdoc />
        protected override void OpenRow(BlackBoxEntrySummary row)
        {
            Context.Navigate("/blackbox/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(BlackBoxEntrySummary row)
        {
            return String.IsNullOrEmpty(row.MissionTitle) ? row.Id : row.MissionTitle!;
        }

        #endregion

        #region Private-Methods

        private static string? Query(RouteMatch route, string name)
        {
            return route.Query.TryGetValue(name, out string? v) && !String.IsNullOrWhiteSpace(v) ? v : null;
        }

        private List<SelectOption<string>> EnumOptions<TEnum>(string all) where TEnum : struct, Enum
        {
            List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Tr(all)) };
            foreach (TEnum value in Enum.GetValues(typeof(TEnum)))
            {
                string name = value.ToString();
                if (name == "Unknown" && typeof(TEnum) != typeof(BlackBoxTriageEnum)) continue;
                options.Add(new SelectOption<string>(name, name));
            }

            return options;
        }

        private static string Short(string fingerprint)
        {
            string f = fingerprint ?? "";
            return f.Length > 12 ? f.Substring(0, 12) : f;
        }

        private void TogglePin(BlackBoxEntrySummary e)
        {
            bool pin = !e.Pinned;
            Call((c, t) => pin ? c.PinBlackBoxAsync(e.Id, t) : c.UnpinBlackBoxAsync(e.Id, t), r =>
            {
                Toast(NotificationSeverityEnum.Success, pin ? Tr("Black box entry pinned.") : Tr("Black box entry unpinned."));
                Refresh();
            }, pin ? "Pin failed" : "Unpin failed");
        }

        private void Delete(BlackBoxEntrySummary e)
        {
            Confirm("Delete Black Box Entry", Tr("Delete black box entry {{id}} and its evidence? This cannot be undone.", LocalizationArgs.Of("id", e.Id)), () =>
            {
                Call((c, t) => c.DeleteBlackBoxAsync(e.Id, t), r =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Black box entry {{id}} deleted.", LocalizationArgs.Of("id", e.Id)));
                    Refresh();
                }, "Delete failed");
            }, "Delete");
        }

        #endregion
    }
}
