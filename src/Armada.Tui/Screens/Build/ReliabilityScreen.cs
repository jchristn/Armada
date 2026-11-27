namespace Armada.Tui.Screens.Build
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
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// Reliability (the Reliability tab of <c>/captains</c>), the dashboard's Reliability tab: per model and persona
    /// completion, failure kinds, repairs, escalations and the Unclassified rate over the last 14 days, with server
    /// filtering, sorting and paging, and the latest acceptance gate report above the table. Not thread-safe.
    /// </summary>
    public class ReliabilityScreen : OpsListScreen<ReliabilitySummaryRow>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "How each model does per persona over the last 14 days, and the latest acceptance gate report."; }
        }

        /// <summary>
        /// Model filter (server side).
        /// </summary>
        public TextInput ModelFilter { get; }

        /// <summary>
        /// Persona filter (server side).
        /// </summary>
        public TextInput PersonaFilter { get; }

        /// <summary>
        /// The latest gate report, or null when none was uploaded.
        /// </summary>
        public GateReport? LatestGate { get; private set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public ReliabilityScreen(RouteMatch route, TuiContext context)
            : base(route, context, r => r.Model + "|" + r.Persona, "ReliabilityScreen", "Reliability")
        {
            Grid.EmptyText = "No stage attempts recorded in this window.";
            ModelFilter = TextFilter("Model", 18, "Filter...", true);
            PersonaFilter = TextFilter("Persona", 14, "Filter...", true);

            Column("model", "Model", r => r.Model, 3, null, r => r.Model);
            Column("persona", "Persona", r => r.Persona, 2, null, r => r.Persona);
            Column("attempts", "Attempts", r => r.Attempts.ToString(CultureInfo.InvariantCulture), 0, 9, r => r.Attempts);
            Column("completionRate", "Completion", r => Percent(r.CompletionRate), 0, 11, r => r.CompletionRate);
            Column("failures", "Failed", r => r.Failed.ToString(CultureInfo.InvariantCulture), 0, 7, r => r.Failed);
            Column("repairs", "Repairs", r => r.Repairs.ToString(CultureInfo.InvariantCulture), 0, 8, r => r.Repairs);
            Column("escalations", "Escalations", r => r.Escalations.ToString(CultureInfo.InvariantCulture), 0, 12, r => r.Escalations);
            Column("unclassifiedRate", "Unclassified", r => Percent(r.UnclassifiedRate), 0, 13, r => r.UnclassifiedRate);
            Column("failureKinds", "Failure kinds", r => FailureKinds(r), 3);

            RowActions.Add(new OpsAction<ReliabilitySummaryRow>("json", "View JSON", r => ShowJson(Tr("Reliability") + ": " + r.Model + " / " + r.Persona, r), "j"));
            ScreenActions.Add(new OpsScreenAction("gate", "View Gate Report", () => ShowJson(Tr("Gate Report"), LatestGate), "g", () => LatestGate != null));

            LoadGate();
            Start("attempts", true, 50);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Map a grid sort key to the server sort.
        /// </summary>
        /// <param name="key">Grid column key, or null.</param>
        /// <returns>The server sort.</returns>
        public static ReliabilitySortEnum SortFor(string? key)
        {
            switch (key)
            {
                case "model": return ReliabilitySortEnum.Model;
                case "persona": return ReliabilitySortEnum.Persona;
                case "completionRate": return ReliabilitySortEnum.CompletionRate;
                case "unclassifiedRate": return ReliabilitySortEnum.UnclassifiedRate;
                case "repairs": return ReliabilitySortEnum.Repairs;
                case "escalations": return ReliabilitySortEnum.Escalations;
                case "failures": return ReliabilitySortEnum.Failures;
                default: return ReliabilitySortEnum.Attempts;
            }
        }

        /// <summary>
        /// One-line summary of a gate report.
        /// </summary>
        /// <param name="report">Report, or null.</param>
        /// <param name="tr">Localizer.</param>
        /// <returns>The line.</returns>
        /// <exception cref="ArgumentNullException">When tr is null.</exception>
        public static string GateLine(GateReport? report, Func<string, string> tr)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (report == null) return tr("No acceptance gate report uploaded yet.");
            int failed = report.Measures.Count(m => m.Status == GateMeasureStatusEnum.Fail);
            return tr("Latest gate report") + " " + (report.Id ?? "-") + ": " + tr(report.Passed ? "passed" : "failed")
                + ", " + report.Measures.Count.ToString(CultureInfo.InvariantCulture) + " " + tr("measures") + ", " + failed.ToString(CultureInfo.InvariantCulture) + " " + tr("failed")
                + ", " + report.FinishedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<ReliabilitySummaryRow>> FetchAsync(GridQuery query, CancellationToken token)
        {
            ReliabilitySummaryQuery q = new ReliabilitySummaryQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            q.Sort = SortFor(query.SortKey);
            q.Descending = query.SortDescending;
            string model = ModelFilter.Value.Trim();
            string persona = PersonaFilter.Value.Trim();
            if (model.Length > 0) q.Model = model;
            if (persona.Length > 0) q.Persona = persona;
            ReliabilitySummary? summary = await Context.Client.Reliability.GetSummaryAsync(q, token).ConfigureAwait(false);
            List<ReliabilitySummaryRow> rows = summary?.Rows ?? new List<ReliabilitySummaryRow>();
            return new GridPage<ReliabilitySummaryRow>(rows, summary?.TotalRecords ?? rows.Count);
        }

        /// <inheritdoc />
        protected override int AboveHeight(int width)
        {
            return 1;
        }

        /// <inheritdoc />
        protected override void RenderAbove(ISurface surface, int width)
        {
            Armada.Tui.Text.SurfaceText.Draw(surface, 0, 0, GateLine(LatestGate, s => Tr(s)), LatestGate != null && !LatestGate.Passed ? Theme.Warning : Theme.Muted, width);
        }

        /// <inheritdoc />
        protected override string RowTitle(ReliabilitySummaryRow row)
        {
            return row.Model + " / " + row.Persona;
        }

        #endregion

        #region Private-Methods

        private void LoadGate()
        {
            Call((c, t) => c.Reliability.GetGateReportAsync("latest", t), r =>
            {
                LatestGate = r;
            }, null, ex => { });
        }

        private static string Percent(double rate)
        {
            return (rate * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static string FailureKinds(ReliabilitySummaryRow row)
        {
            if (row.FailureKinds.Count == 0) return "-";
            return String.Join(", ", row.FailureKinds.OrderByDescending(k => k.Value).Take(3).Select(k => k.Key + " " + k.Value.ToString(CultureInfo.InvariantCulture)));
        }

        #endregion
    }
}
