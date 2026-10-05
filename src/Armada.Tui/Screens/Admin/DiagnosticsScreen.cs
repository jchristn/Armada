namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Client.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Settings, Diagnostics tab (dashboard <c>Doctor.tsx</c>): runs the server's health checks on open and on Run
    /// Checks, shows the verdict (Healthy, Warnings, Unhealthy), passed/warning/failed counts, and each check's
    /// status and message. Not thread-safe.
    /// </summary>
    public class DiagnosticsScreen : GridScreen<DoctorCheck>
    {
        #region Public-Members

        /// <summary>
        /// Summary cards.
        /// </summary>
        public KpiBar Summary { get; } = new KpiBar();

        /// <summary>
        /// True while checks run.
        /// </summary>
        public bool Running { get; private set; } = false;

        /// <summary>
        /// Results of the last run.
        /// </summary>
        public List<DoctorCheck> Results { get; private set; } = new List<DoctorCheck>();

        #endregion

        #region Private-Members

        private readonly Armada.Tui.Widgets.Button _Run;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public DiagnosticsScreen(RouteMatch route, TuiContext context)
            : base(route, context, "Diagnostics", "System health diagnostics and checks.", "diagnostics", c => c.Name)
        {
            _Run = Header.AddButton("Run Checks", RunChecks, "r");
            AddFixed(Header, w => Header.HeightFor(w));
            AddFixed(Summary, w => Results.Count > 0 && !Running ? Summary.PreferredHeight : 0);
            Grid.MultiSelect = false;
            Grid.EmptyText = "No results yet. Click \"Run Checks\" to start diagnostics.";
            Grid.AddColumn(new GridColumn<DoctorCheck>("check", "Check", c => c.Name) { Width = 28 });
            Grid.AddColumn(new GridColumn<DoctorCheck>("status", "Status", c => StatusBadge.Label(c.Status)) { Width = 10, Style = (c, t) => StatusBadge.Style(c.Status, t) });
            Grid.AddColumn(new GridColumn<DoctorCheck>("message", "Message", c => c.Message) { Weight = 4, Style = (c, t) => t.Muted });
            AddFill(Grid);
            Scope.Focus(Grid);
            RunChecks();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Overall verdict for a set of results: Unhealthy with any failure, Healthy when all pass, else Warnings.
        /// </summary>
        /// <param name="results">Results.</param>
        /// <returns>English verdict, or empty with no results.</returns>
        public static string Verdict(IReadOnlyCollection<DoctorCheck> results)
        {
            if (results == null || results.Count == 0) return "";
            if (results.Any(r => r.Status == DoctorCheckStatusEnum.Fail)) return "Unhealthy";
            if (results.Any(r => r.Status == DoctorCheckStatusEnum.Warn)) return "Warnings";
            return "Healthy";
        }

        /// <summary>
        /// Run the checks.
        /// </summary>
        public void RunChecks()
        {
            if (Running) return;
            Running = true;
            Results = new List<DoctorCheck>();
            Grid.SetLocalRows(Results);
            Grid.EmptyText = "Running health checks...";
            _Run.Label = "Running...";
            _Run.Enabled = false;
            ScreenOps.Quiet(Context, async () =>
            {
                try
                {
                    return await Context.Client.GetDoctorAsync().ConfigureAwait(false) ?? new List<DoctorCheck>();
                }
                catch (Armada.Client.ArmadaApiException ex)
                {
                    DoctorCheck failure = new DoctorCheck();
                    failure.Name = Context.Loc.T("Error");
                    failure.Status = DoctorCheckStatusEnum.Fail;
                    failure.Message = Context.Loc.T("Failed to run health checks: {{message}}", LocalizationArgs.Of("message", ex.Message));
                    return new List<DoctorCheck> { failure };
                }
            }, results =>
            {
                Running = false;
                Results = results;
                _Run.Label = "Run Checks";
                _Run.Enabled = true;
                Grid.EmptyText = "No results yet. Click \"Run Checks\" to start diagnostics.";
                Grid.SetLocalRows(results);
                UpdateSummary();
            });
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            RunChecks();
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void OnActivate(DoctorCheck row)
        {
            ScreenOps.ShowText(Context, row.Name, row.Name + "\n" + StatusBadge.Label(row.Status) + "\n\n" + row.Message);
        }

        /// <inheritdoc />
        protected override IEnumerable<Armada.Tui.Input.ArmadaCommand> ExtraCommands()
        {
            return new List<Armada.Tui.Input.ArmadaCommand>
            {
                Command(ScreenKey + ".run", "Run Checks", RunChecks, () => !Running, "r"),
            };
        }

        #endregion

        #region Private-Methods

        private void UpdateSummary()
        {
            int pass = Results.Count(r => r.Status == DoctorCheckStatusEnum.Pass);
            int warn = Results.Count(r => r.Status == DoctorCheckStatusEnum.Warn);
            int fail = Results.Count(r => r.Status == DoctorCheckStatusEnum.Fail);
            Summary.SetCards(new List<KpiCard>
            {
                new KpiCard("Passed", Context.Loc.FormatNumber(pass), t => t.Success),
                new KpiCard("Warnings", Context.Loc.FormatNumber(warn), t => t.Warning),
                new KpiCard("Failed", Context.Loc.FormatNumber(fail), t => t.Error),
            });
            string verdict = Verdict(Results);
            Header.Status = verdict.Length > 0 ? "[" + Context.Loc.T(verdict) + "]" : "";
            Header.StatusStyle = verdict == "Unhealthy" ? (Func<Armada.Tui.Theming.ArmadaTheme, TUIKit.CellStyle>)(t => t.Error) : verdict == "Healthy" ? t => t.Success : t => t.Warning;
        }

        #endregion
    }
}
