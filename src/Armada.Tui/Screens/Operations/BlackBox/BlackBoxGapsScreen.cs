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
    /// Armada resilience gaps (<c>/blackbox/gaps</c>, <c>G</c> on the Black Box tab): failure modes in Armada's own
    /// handoff and landing that the black box triaged ArmadaDefect, grouped by fingerprint with occurrences, Armada
    /// versions, severity escalated by recurrence, whether Armada could have healed them and the top proposed Armada
    /// change. Server paged (the server returns at most 50 per page; larger grid pages are filled from several server
    /// pages) and filtered by vessel, resolution and phase. Enter opens the newest entry. Not thread-safe.
    /// </summary>
    public class BlackBoxGapsScreen : OpsListScreen<BlackBoxResilienceGap>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Failures in Armada's own handoff and landing, by fingerprint: what Armada should change to heal them."; }
        }

        /// <summary>
        /// Most gaps the server returns per page.
        /// </summary>
        public const int ServerPageSize = 50;

        /// <summary>
        /// Vessel filter (server).
        /// </summary>
        public SelectField<string> VesselFilter { get; }

        /// <summary>
        /// Resolution filter (server).
        /// </summary>
        public SelectField<string> ResolutionFilter { get; }

        /// <summary>
        /// Phase filter (server).
        /// </summary>
        public SelectField<string> PhaseFilter { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public BlackBoxGapsScreen(RouteMatch route, TuiContext context)
            : base(route, context, g => g.Fingerprint, "BlackBoxGapsScreen", "Armada Resilience Gaps")
        {
            Grid.EmptyText = "No Armada resilience gaps recorded.";
            Reference.Ensure("vessels");
            VesselFilter = SelectFilter("Vessel", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Vessels")) }, 18, true);
            Reference.Changed += (s, name) =>
            {
                if (name != "vessels") return;
                string? current = VesselFilter.Value;
                VesselFilter.Options = Reference.VesselOptions("All Vessels");
                VesselFilter.SetValue(current ?? "");
            };
            ResolutionFilter = SelectFilter("Resolution", Options(Enum.GetValues(typeof(BlackBoxResolutionEnum)).Cast<BlackBoxResolutionEnum>().Where(r => r != BlackBoxResolutionEnum.Unknown).Select(r => r.ToString()), "All Resolutions"), 16, true);
            PhaseFilter = SelectFilter("Phase", Options(new[] { "Handoff", "Landing", "MergeQueue", "Recovery", "Provision", "Completion", "Review" }, "All Phases"), 14, true);
            if (route.Query.TryGetValue("vesselId", out string? vessel) && !String.IsNullOrWhiteSpace(vessel)) VesselFilter.SetValue(vessel);

            Column("severity", "Severity", g => g.Severity.ToString(), 0, 10, g => (int)g.Severity, (g, theme) => g.Severity == BlackBoxSeverityEnum.Critical || g.Severity == BlackBoxSeverityEnum.High ? theme.Error : null);
            Column("occurrences", "Seen", g => g.Occurrences.ToString(CultureInfo.InvariantCulture) + "x", 0, 7, g => g.Occurrences);
            Column("open", "Open", g => g.OpenCount.ToString(CultureInfo.InvariantCulture), 0, 6, g => g.OpenCount);
            Column("where", "Where", g => BlackBoxText.Where(g.Phase, g.Step) + (g.Suspected ? " ?" : ""), 2);
            Column("selfHealable", "Self-heal", g => g.SelfHealable.ToString(), 0, 10);
            Column("change", "Top proposed Armada change", g => g.TopProposedChange?.Title ?? "-", 4);
            Column("versions", "Versions", g => g.ArmadaVersions.Count.ToString(CultureInfo.InvariantCulture), 0, 9, g => g.ArmadaVersions.Count);
            Column("lastSeen", "Last seen", g => Context.Loc.FormatRelative(g.LastSeenUtc, Context.Clock.UtcNow), 0, 14, g => g.LastSeenUtc);
            GridColumn<BlackBoxResilienceGap> gap = Column("gap", "Detection gap", g => g.DetectionGap ?? "", 3);
            gap.DefaultVisible = false;
            GridColumn<BlackBoxResilienceGap> fp = Column("fingerprint", "Fingerprint", g => g.Fingerprint, 0, 16);
            fp.DefaultVisible = false;

            ScreenActions.Add(new OpsScreenAction("blackbox", "All failures", () => Context.Navigate(TabLinks.BlackBoxPath), "B"));
            RowActions.Add(new OpsAction<BlackBoxResilienceGap>("view", "Open newest entry", g => OpenRow(g), "o", g => !String.IsNullOrEmpty(g.LatestEntryId)));
            RowActions.Add(new OpsAction<BlackBoxResilienceGap>("details", "Gap details", ShowDetails, "i"));
            RowActions.Add(new OpsAction<BlackBoxResilienceGap>("entries", "Entries with this fingerprint", g => Context.Navigate(TabLinks.BlackBox(g.Fingerprint)), "f"));
            RowActions.Add(new OpsAction<BlackBoxResilienceGap>("copy-change", "Copy top proposed change", CopyChange, "y", g => g.TopProposedChange != null));
            RowActions.Add(new OpsAction<BlackBoxResilienceGap>("json", "View JSON", g => ShowJson(Tr("Resilience gap: {{fingerprint}}", LocalizationArgs.Of("fingerprint", g.Fingerprint)), g), "j"));

            SubscribeCoalesced("blackbox.", Refresh);
            Start(null, false, 25);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The server query for the current filters and one server page.
        /// </summary>
        /// <param name="pageNumber">Server page number.</param>
        /// <param name="pageSize">Server page size (clamped to 1..50).</param>
        /// <returns>Request.</returns>
        public BlackBoxResilienceGapRequest BuildRequest(int pageNumber, int pageSize)
        {
            BlackBoxResilienceGapRequest q = new BlackBoxResilienceGapRequest();
            q.PageNumber = Math.Max(1, pageNumber);
            q.PageSize = Math.Clamp(pageSize, 1, ServerPageSize);
            if (!String.IsNullOrEmpty(VesselFilter.Value)) q.VesselId = VesselFilter.Value;
            if (Enum.TryParse(ResolutionFilter.Value, out BlackBoxResolutionEnum resolution)) q.Resolution = resolution;
            if (Enum.TryParse(PhaseFilter.Value, out BlackBoxPhaseEnum phase)) q.Phase = phase;
            return q;
        }

        /// <summary>
        /// The text of one gap: recurrence, scope verdicts, detection gap and the top proposed change.
        /// </summary>
        /// <param name="g">Gap.</param>
        /// <returns>Text.</returns>
        public string DetailsText(BlackBoxResilienceGap g)
        {
            if (g == null) throw new ArgumentNullException(nameof(g));
            List<string> lines = new List<string>();
            lines.Add(Tr("Where") + ": " + BlackBoxText.Where(g.Phase, g.Step) + (g.FailureKind.HasValue ? "  (" + g.FailureKind.Value + ")" : ""));
            lines.Add(Tr("Severity") + ": " + g.Severity + "  " + Tr("Seen") + " " + g.Occurrences.ToString(CultureInfo.InvariantCulture) + "x, " + g.OpenCount.ToString(CultureInfo.InvariantCulture) + " " + Tr("open"));
            if (g.ArmadaVersions.Count > 0) lines.Add(Tr("Armada versions") + ": " + String.Join(", ", g.ArmadaVersions));
            lines.Add(Tr("Self-healable") + ": " + g.SelfHealable + (g.Suspected ? "  (" + Tr("suspected Armada defect") + ")" : ""));
            if (!String.IsNullOrEmpty(g.DetectionGap)) lines.Add(Tr("Detection gap") + ": " + g.DetectionGap);
            if (g.TopProposedChange != null)
            {
                lines.Add("");
                lines.Add(Tr("Top proposed Armada change") + ": " + g.TopProposedChange.Title + "  [" + g.TopProposedChange.Severity + "]");
                lines.Add(g.TopProposedChange.Change);
                if (!String.IsNullOrEmpty(g.TopProposedChange.AcceptanceTest)) lines.Add(Tr("Acceptance test") + ": " + g.TopProposedChange.AcceptanceTest);
            }

            lines.Add("");
            lines.Add(Tr("Newest entry") + ": " + (g.LatestEntryId ?? "-") + (String.IsNullOrEmpty(g.LatestMissionTitle) ? "" : "  " + g.LatestMissionTitle));
            if (g.RuleIds.Count > 0) lines.Add(Tr("Rules") + ": " + String.Join(", ", g.RuleIds));
            return String.Join("\n", lines);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<BlackBoxResilienceGap>> FetchAsync(GridQuery query, CancellationToken token)
        {
            int size = Math.Max(1, query.PageSize);
            int number = Math.Max(1, query.PageNumber);
            if (size <= ServerPageSize)
            {
                EnumerationResult<BlackBoxResilienceGap>? one = await Context.Client.EnumerateBlackBoxResilienceGapsAsync(BuildRequest(number, size), token).ConfigureAwait(false);
                List<BlackBoxResilienceGap> rows = one?.Objects ?? new List<BlackBoxResilienceGap>();
                return new GridPage<BlackBoxResilienceGap>(rows, one?.TotalRecords ?? rows.Count);
            }

            // A grid page larger than the server cap: read the server pages of 50 that cover it.
            long start = (long)(number - 1) * size;
            int firstServerPage = (int)(start / ServerPageSize) + 1;
            int skip = (int)(start % ServerPageSize);
            List<BlackBoxResilienceGap> collected = new List<BlackBoxResilienceGap>();
            long total = 0;
            for (int p = firstServerPage; collected.Count < size + skip; p++)
            {
                EnumerationResult<BlackBoxResilienceGap>? page = await Context.Client.EnumerateBlackBoxResilienceGapsAsync(BuildRequest(p, ServerPageSize), token).ConfigureAwait(false);
                if (page == null) break;
                total = page.TotalRecords;
                collected.AddRange(page.Objects);
                if (page.Objects.Count < ServerPageSize || p >= page.TotalPages) break;
            }

            return new GridPage<BlackBoxResilienceGap>(collected.Skip(skip).Take(size).ToList(), total);
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<BlackBoxResilienceGap> rows)
        {
            bool filtered = !String.IsNullOrEmpty(VesselFilter.Value) || !String.IsNullOrEmpty(ResolutionFilter.Value) || !String.IsNullOrEmpty(PhaseFilter.Value);
            Grid.EmptyText = filtered ? "No resilience gaps match the current filters." : "No Armada resilience gaps recorded.";
        }

        /// <inheritdoc />
        protected override void OpenRow(BlackBoxResilienceGap row)
        {
            if (String.IsNullOrEmpty(row.LatestEntryId)) return;
            Context.Navigate("/blackbox/" + Uri.EscapeDataString(row.LatestEntryId!));
        }

        /// <inheritdoc />
        protected override string RowTitle(BlackBoxResilienceGap row)
        {
            return row.TopProposedChange?.Title ?? BlackBoxText.Where(row.Phase, row.Step);
        }

        #endregion

        #region Private-Methods

        private List<SelectOption<string>> Options(IEnumerable<string> values, string all)
        {
            List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Tr(all)) };
            foreach (string v in values) options.Add(new SelectOption<string>(v, v));
            return options;
        }

        private void ShowDetails(BlackBoxResilienceGap g)
        {
            ShowText(Tr("Resilience gap: {{fingerprint}}", LocalizationArgs.Of("fingerprint", g.Fingerprint)), DetailsText(g));
        }

        private void CopyChange(BlackBoxResilienceGap g)
        {
            if (g.TopProposedChange == null) return;
            Copy(BlackBoxResilienceText.DraftBrief(g.TopProposedChange), "Proposed Armada change");
        }

        #endregion
    }
}
