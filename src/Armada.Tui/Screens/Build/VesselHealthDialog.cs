namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;
    using Button = Armada.Tui.Widgets.Button;
    using FocusScope = Armada.Tui.Widgets.FocusScope;

    /// <summary>
    /// The dashboard's vessel health inspector (VesselHealthDetailModal): the vessel name with its Overall badge, id,
    /// and evaluation time; Open vessel (<c>o</c>), Re-evaluate (<c>e</c>, tenant admins), and Manage branches
    /// (<c>b</c>); and the Summary, Findings (each criterion as a sentence; <c>Enter</c> overrides it), Dependencies
    /// (<c>Enter</c> opens the advisory), Overrides (list with <c>e</c> edit and <c>Del</c> remove, and the add or edit
    /// form saved with <c>Ctrl+S</c>), and Raw JSON sections, switched with <c>[</c> and <c>]</c> or <c>1</c> to
    /// <c>5</c>. Not thread-safe.
    /// </summary>
    public class VesselHealthDialog : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Section keys in order.
        /// </summary>
        public static readonly string[] SectionKeys = new string[] { "summary", "findings", "dependencies", "overrides", "json" };

        /// <summary>
        /// Vessel id.
        /// </summary>
        public string VesselId { get; }

        /// <summary>
        /// The loaded detail, or null.
        /// </summary>
        public VesselHealthDetail? Detail { get; private set; } = null;

        /// <summary>
        /// Current section key.
        /// </summary>
        public string Section { get; private set; } = "summary";

        /// <summary>
        /// Load error, or null.
        /// </summary>
        public string? LoadError { get; private set; } = null;

        /// <summary>
        /// Override form error, or null.
        /// </summary>
        public string? FormError { get; private set; } = null;

        /// <summary>
        /// Summary section.
        /// </summary>
        public OpsDocumentView SummaryView { get; } = new OpsDocumentView();

        /// <summary>
        /// Findings section.
        /// </summary>
        public ArmadaGrid<VesselHealthFinding> Findings { get; } = new ArmadaGrid<VesselHealthFinding>(f => f.Criterion.ToString());

        /// <summary>
        /// Dependencies section.
        /// </summary>
        public ArmadaGrid<VesselDependency> Dependencies { get; } = new ArmadaGrid<VesselDependency>(d => d.Id);

        /// <summary>
        /// Overrides list.
        /// </summary>
        public ArmadaGrid<VesselHealthOverride> Overrides { get; } = new ArmadaGrid<VesselHealthOverride>(o => o.Criterion.ToString());

        /// <summary>
        /// Override form: criterion.
        /// </summary>
        public SelectField<string> FormCriterion { get; } = new SelectField<string>();

        /// <summary>
        /// Override form: status.
        /// </summary>
        public SelectField<string> FormStatus { get; } = new SelectField<string>();

        /// <summary>
        /// Override form: note.
        /// </summary>
        public TextInput FormNote { get; } = new TextInput();

        /// <summary>
        /// Override form: save.
        /// </summary>
        public Button SaveButton { get; }

        /// <summary>
        /// Raw JSON section.
        /// </summary>
        public JsonViewer Json { get; } = new JsonViewer("");

        #endregion

        #region Private-Members

        private readonly OpsScreen _Screen;
        private readonly string _VesselName;
        private readonly string? _DefaultBranch;
        private readonly bool _CanAdmin;
        private readonly HealthEvaluation? _Evaluation;
        private readonly Action? _Changed;
        private readonly Action<string, string>? _OpenBranches;
        private readonly FocusScope _Scope = new FocusScope();
        private bool _Loading = false;
        private bool _Saving = false;
        private int _ScreenWidth = 100;
        private int _ScreenHeight = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and load.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="vesselName">Name shown while loading.</param>
        /// <param name="defaultBranch">Default branch (the divergence base), or null.</param>
        /// <param name="canAdmin">Tenant admin: Re-evaluate and override editing.</param>
        /// <param name="evaluation">Evaluation tracker for Re-evaluate, or null to hide it.</param>
        /// <param name="initialSection">Section to open.</param>
        /// <param name="initialCriterion">Criterion preselected in the override form, or null.</param>
        /// <param name="changed">Called after an override change, or null.</param>
        /// <param name="openBranches">Opens branch management, or null to hide it.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public VesselHealthDialog(OpsScreen screen, string vesselId, string? vesselName, string? defaultBranch, bool canAdmin, HealthEvaluation? evaluation, string initialSection = "summary", VesselHealthCriterionEnum? initialCriterion = null, Action? changed = null, Action<string, string>? openBranches = null)
            : base(vesselName ?? vesselId ?? "", screen?.Context.Loc, screen?.Context.Theme.Current)
        {
            _Screen = screen ?? throw new ArgumentNullException(nameof(screen));
            VesselId = vesselId ?? throw new ArgumentNullException(nameof(vesselId));
            _VesselName = vesselName ?? vesselId;
            _DefaultBranch = defaultBranch;
            _CanAdmin = canAdmin;
            _Evaluation = evaluation;
            _Changed = changed;
            _OpenBranches = openBranches;
            IModalHost modals = screen.Context.Modals;

            SummaryView.Builder = BuildSummary;
            SetupGrid(Findings, "No findings yet. Findings appear after the first evaluation.");
            Findings.AddColumn(new GridColumn<VesselHealthFinding>("criterion", "Criterion", f => T(HealthText.CriterionLabel(f.Criterion))) { Width = 24 });
            GridColumn<VesselHealthFinding> fstatus = new GridColumn<VesselHealthFinding>("status", "Status", FindingStatus) { Width = 34 };
            fstatus.Style = (f, t) => StatusBadge.Style(f.Status, t);
            Findings.AddColumn(fstatus);
            Findings.AddColumn(new GridColumn<VesselHealthFinding>("details", "Details", FindingDetails) { Weight = 5 });
            Findings.Activated += (s, f) => StartOverride(f.Criterion);

            SetupGrid(Dependencies, "No outdated or vulnerable dependencies were found.");
            Dependencies.AddColumn(new GridColumn<VesselDependency>("ecosystem", "Ecosystem", d => d.Ecosystem) { Width = 10 });
            Dependencies.AddColumn(new GridColumn<VesselDependency>("project", "Project", d => String.IsNullOrEmpty(d.ProjectPath) ? "-" : d.ProjectPath!) { Weight = 3 });
            Dependencies.AddColumn(new GridColumn<VesselDependency>("package", "Package", d => d.PackageName) { Weight = 3 });
            Dependencies.AddColumn(new GridColumn<VesselDependency>("version", "Version", d => (String.IsNullOrEmpty(d.CurrentVersion) ? "-" : d.CurrentVersion!) + (String.IsNullOrEmpty(d.LatestVersion) ? "" : " -> " + d.LatestVersion)) { Weight = 2 });
            Dependencies.AddColumn(new GridColumn<VesselDependency>("drift", "Drift", d => d.Drift == DependencyDriftEnum.None ? "-" : T(HealthText.DriftLabel(d.Drift))) { Width = 7 });
            GridColumn<VesselDependency> vuln = new GridColumn<VesselDependency>("vulnerability", "Vulnerability", d => d.IsVulnerable ? T(HealthText.SeverityLabel(d.Severity)) : "-") { Width = 13 };
            vuln.Style = (d, t) => d.IsVulnerable && d.Severity >= VulnerabilitySeverityEnum.High ? t.Error : d.IsVulnerable ? t.Warning : t.Muted;
            Dependencies.AddColumn(vuln);
            Dependencies.AddColumn(new GridColumn<VesselDependency>("advisory", "Advisory", d => String.IsNullOrEmpty(d.AdvisoryUrl) ? "-" : T("Advisory")) { Width = 9 });
            Dependencies.Activated += (s, d) =>
            {
                if (!String.IsNullOrEmpty(d.AdvisoryUrl)) _Screen.Context.External.OpenUrl(d.AdvisoryUrl!);
            };

            SetupGrid(Overrides, "No overrides. An override sets a manual status for one criterion or for Overall.");
            Overrides.AddColumn(new GridColumn<VesselHealthOverride>("criterion", "Criterion", o => T(HealthText.CriterionLabel(o.Criterion))) { Width = 24 });
            GridColumn<VesselHealthOverride> ostatus = new GridColumn<VesselHealthOverride>("status", "Status", o => HealthText.Badge(o.Status, Localizer, true)) { Width = 18 };
            ostatus.Style = (o, t) => StatusBadge.Style(o.Status, t);
            Overrides.AddColumn(ostatus);
            Overrides.AddColumn(new GridColumn<VesselHealthOverride>("updated", "Updated", o => _Screen.Context.Loc.FormatRelative(o.LastUpdateUtc, _Screen.Context.Clock.UtcNow)) { Width = 14 });
            Overrides.AddColumn(new GridColumn<VesselHealthOverride>("note", "Note", o => o.Note ?? "") { Weight = 4 });
            Overrides.Activated += (s, o) => StartOverride(o.Criterion);

            FormCriterion.ModalHost = modals;
            FormCriterion.PickerTitle = "Criterion";
            FormCriterion.Options = HealthText.OverrideCriteria.Select(c => new SelectOption<string>(c.ToString(), T(HealthText.CriterionLabel(c)))).ToList();
            FormCriterion.SetValue((initialCriterion ?? VesselHealthCriterionEnum.Overall).ToString());
            FormStatus.ModalHost = modals;
            FormStatus.PickerTitle = "Status";
            FormStatus.Options = HealthText.Statuses.Select(s => new SelectOption<string>(s.ToString(), T(HealthText.StatusLabel(s)))).ToList();
            FormStatus.SetValue("Pass");
            FormNote.Placeholder = "Why this status is set by hand (optional)";
            FormNote.MaxLength = 4000;
            SaveButton = new Button("Save override", SubmitOverride);
            foreach (ArmadaWidget w in new ArmadaWidget[] { SummaryView, Findings, Dependencies, Overrides, FormCriterion, FormStatus, FormNote, SaveButton, Json })
            {
                w.Localizer = Localizer;
                w.ApplyTheme(Theme);
            }

            MinContentWidth = 60;
            MaxContentWidth = 180;
            FooterHint = " [ ] " + T("Sections") + "  o " + T("Open vessel") + (_CanAdmin && _Evaluation != null ? "  e " + T("Re-evaluate") : "") + (_OpenBranches != null ? "  b " + T("Manage branches") : "") + "  Esc " + T("Close") + " ";
            _Scope.Wrap = true;
            _Scope.SetActive(true);
            ShowSection(SectionKeys.Contains(initialSection) ? initialSection : "summary");
            if (initialCriterion.HasValue) StartOverride(initialCriterion.Value);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reload the detail.
        /// </summary>
        public void Load()
        {
            _Loading = true;
            LoadError = null;
            _Screen.Call((c, t) => c.GetVesselHealthAsync(VesselId, t), d =>
            {
                _Loading = false;
                Apply(d);
            }, null, ex =>
            {
                _Loading = false;
                LoadError = ex.Message;
            });
        }

        /// <summary>
        /// Switch to a section.
        /// </summary>
        /// <param name="key">Section key.</param>
        public void ShowSection(string key)
        {
            if (!SectionKeys.Contains(key)) return;
            Section = key;
            _Scope.Clear();
            foreach (IWidget w in SectionWidgets()) _Scope.Add(w);
            _Scope.FocusFirst();
        }

        /// <summary>
        /// Open the override form for a criterion (tenant admins), filled from its existing override.
        /// </summary>
        /// <param name="criterion">Criterion.</param>
        public void StartOverride(VesselHealthCriterionEnum criterion)
        {
            if (!_CanAdmin) return;
            VesselHealthOverride? existing = Detail?.Overrides?.FirstOrDefault(o => o.Criterion == criterion);
            FormCriterion.SetValue(criterion.ToString());
            FormStatus.SetValue((existing?.Status ?? VesselHealthStatusEnum.Pass).ToString());
            FormNote.Value = existing?.Note ?? "";
            FormError = null;
            ShowSection("overrides");
            _Scope.Focus(FormStatus);
        }

        /// <summary>
        /// Save the override form.
        /// </summary>
        public void SubmitOverride()
        {
            if (!_CanAdmin || _Saving) return;
            if (!Enum.TryParse<VesselHealthStatusEnum>(FormStatus.Value ?? "", out VesselHealthStatusEnum status))
            {
                FormError = T("Choose a status.");
                return;
            }

            if (FormNote.Value.Length > 4000)
            {
                FormError = Localizer.T("The note can be at most {{count}} characters.", LocalizationArgs.Of("count", Localizer.FormatNumber(4000)));
                return;
            }

            VesselHealthCriterionEnum criterion = Enum.TryParse<VesselHealthCriterionEnum>(FormCriterion.Value ?? "", out VesselHealthCriterionEnum c0) ? c0 : VesselHealthCriterionEnum.Overall;
            string? note = String.IsNullOrWhiteSpace(FormNote.Value) ? null : FormNote.Value.Trim();
            _Saving = true;
            FormError = null;
            _Screen.Call((c, t) => c.SetVesselHealthOverrideAsync(VesselId, criterion, status, note, t), d =>
            {
                _Saving = false;
                Apply(d);
                FormNote.Value = "";
                _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("Override saved for {{criterion}}.", LocalizationArgs.Of("criterion", T(HealthText.CriterionLabel(criterion)))));
                _Changed?.Invoke();
            }, null, ex =>
            {
                _Saving = false;
                FormError = ex.Message;
            });
        }

        /// <summary>
        /// Remove an override after the dashboard's confirmation.
        /// </summary>
        /// <param name="o">Override.</param>
        public void RemoveOverride(VesselHealthOverride o)
        {
            if (!_CanAdmin || o == null) return;
            string label = T(HealthText.CriterionLabel(o.Criterion));
            _Screen.Confirm("Remove override", _Screen.Tr("Remove the {{criterion}} override? The evaluated status applies again.", LocalizationArgs.Of("criterion", label)), () =>
            {
                _Screen.Call((c, t) => c.DeleteVesselHealthOverrideAsync(VesselId, o.Criterion, t), d =>
                {
                    Apply(d);
                    _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("Override removed for {{criterion}}.", LocalizationArgs.Of("criterion", label)));
                    _Changed?.Invoke();
                }, null, ex => _Screen.Toast(NotificationSeverityEnum.Error, _Screen.Tr("Could not remove the override: {{message}}", LocalizationArgs.Of("message", ex.Message))));
            }, "Remove");
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            IWidget? leaf = _Scope.FocusedLeaf() ?? _Scope.Focused;
            bool typing = leaf is TextInput || (leaf is ScrollTextView sv && sv.Searching);
            if (!typing && HandleDismiss(key, null)) return true;
            if (typing && key.Code == KeyCode.Escape)
            {
                _Scope.FocusFirst();
                return true;
            }

            if (key.Code == KeyCode.Tab)
            {
                _Scope.Move((key.Modifiers & KeyModifiers.Shift) == 0);
                return true;
            }

            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (ctrl && key.Code == KeyCode.Character && Char.ToLowerInvariant((char)key.Rune) == 's' && Section == "overrides")
            {
                SubmitOverride();
                return true;
            }

            if (!typing && key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None)
            {
                int idx = Array.IndexOf(SectionKeys, Section);
                switch (key.Rune)
                {
                    case '[':
                        ShowSection(SectionKeys[(idx + SectionKeys.Length - 1) % SectionKeys.Length]);
                        return true;
                    case ']':
                        ShowSection(SectionKeys[(idx + 1) % SectionKeys.Length]);
                        return true;
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                        ShowSection(SectionKeys[key.Rune - '1']);
                        return true;
                    case 'o':
                        RequestClose(null);
                        _Screen.DropClosedModals();
                        _Screen.Context.Navigate("/vessels/" + Uri.EscapeDataString(VesselId));
                        return true;
                    case 'e':
                        if (Section == "overrides" && ReferenceEquals(_Scope.Focused, Overrides) && Overrides.Current != null)
                        {
                            StartOverride(Overrides.Current.Criterion);
                            return true;
                        }

                        if (_CanAdmin && _Evaluation != null && !_Evaluation.Running) _Evaluation.Start(new List<string> { VesselId });
                        return true;
                    case 'b':
                        if (_OpenBranches == null) break;
                        RequestClose(null);
                        _Screen.DropClosedModals();
                        _OpenBranches(VesselId, Detail?.Health?.VesselName ?? _VesselName);
                        return true;
                    case 'y':
                        if (Section == "json")
                        {
                            _Screen.Copy(Json.PlainText, "JSON");
                            return true;
                        }

                        _Screen.Copy(VesselId, "Vessel ID");
                        return true;
                    case 'f':
                        if (Section == "summary")
                        {
                            ShowSection("findings");
                            return true;
                        }

                        break;
                }
            }

            if (!typing && key.Code == KeyCode.Delete && Section == "overrides" && ReferenceEquals(_Scope.Focused, Overrides) && Overrides.Current != null)
            {
                RemoveOverride(Overrides.Current);
                return true;
            }

            _Scope.HandleKey(key);
            return true;
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            if (ReferenceEquals(_Scope.Focused, FormNote)) FormNote.Insert(text);
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            _ScreenWidth = surface.Size.Width;
            _ScreenHeight = surface.Size.Height;
            SaveButton.Label = _Saving ? T("Saving...") : T("Save override");
            base.Render(surface);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, Math.Max(60, (int)(_ScreenWidth * 0.92)));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Max(12, (int)(_ScreenHeight * 0.88) - 4);
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int height = content.Size.Height;
            int y = 0;
            VesselHealth? h = Detail?.Health;
            VesselHealthOverride? overall = Detail?.Overrides?.FirstOrDefault(o => o.Criterion == VesselHealthCriterionEnum.Overall);
            string name = !String.IsNullOrEmpty(h?.VesselName) ? h!.VesselName! : _VesselName;
            int x = SurfaceText.Draw(content, 0, y, name, On(Theme.Accent), width);
            if (h != null) SurfaceText.Draw(content, x + 2, y, HealthText.Badge(h.OverallStatus, Localizer, overall != null), On(StatusBadge.Style(h.OverallStatus, Theme)), Math.Max(0, width - x - 2));
            y++;
            string evaluated = h?.EvaluatedUtc != null
                ? Localizer.T("Evaluated {{when}}", LocalizationArgs.Of("when", _Screen.Context.Loc.FormatRelative(h.EvaluatedUtc.Value, _Screen.Context.Clock.UtcNow)))
                : T("Never evaluated");
            string running = _Evaluation != null && _Evaluation.Running ? "   " + T("Evaluating...") : "";
            SurfaceText.Draw(content, 0, y++, VesselId + "   " + evaluated + running, Dim(), width);
            int tx = 0;
            for (int i = 0; i < SectionKeys.Length; i++)
            {
                string label = (i + 1) + " " + T(SectionLabel(SectionKeys[i]));
                if (SectionKeys[i] == "dependencies" && (Detail?.Dependencies?.Count ?? 0) > 0) label += " (" + Detail!.Dependencies.Count + ")";
                if (SectionKeys[i] == "overrides" && (Detail?.Overrides?.Count ?? 0) > 0) label += " (" + Detail!.Overrides.Count + ")";
                string text = SectionKeys[i] == Section ? "[" + label + "]" : " " + label + " ";
                if (tx >= width) break;
                tx += SurfaceText.Draw(content, tx, y, text, SectionKeys[i] == Section ? On(Theme.Accent) : Dim(), width - tx) + 1;
            }

            y++;
            SurfaceText.Draw(content, 0, y++, new string('-', width), Dim(), width);
            if (LoadError != null) SurfaceText.Draw(content, 0, y++, "! " + Localizer.T("Could not load vessel health: {{message}}", LocalizationArgs.Of("message", LoadError)) + "  (F5)", On(Theme.Error), width);
            if (Detail == null)
            {
                if (_Loading) SurfaceText.Draw(content, 0, y, T("Loading health..."), Dim(), width);
                return;
            }

            int remaining = Math.Max(1, height - y);
            switch (Section)
            {
                case "summary":
                    _Scope.RenderChild(content, SummaryView, new Rect(0, y, width, remaining));
                    break;
                case "findings":
                    _Scope.RenderChild(content, Findings, new Rect(0, y, width, remaining));
                    break;
                case "dependencies":
                    _Scope.RenderChild(content, Dependencies, new Rect(0, y, width, remaining));
                    break;
                case "json":
                    _Scope.RenderChild(content, Json, new Rect(0, y, width, remaining));
                    break;
                default:
                    RenderOverrides(content, y, width, remaining);
                    break;
            }
        }

        #endregion

        #region Private-Methods

        private static string SectionLabel(string key)
        {
            switch (key)
            {
                case "findings": return "Findings";
                case "dependencies": return "Dependencies";
                case "overrides": return "Overrides";
                case "json": return "Raw JSON";
                default: return "Summary";
            }
        }

        private void SetupGrid<TRow>(ArmadaGrid<TRow> grid, string empty) where TRow : class
        {
            grid.MultiSelect = false;
            grid.ShowPagingBar = false;
            grid.PageSize = 10000;
            grid.EmptyText = empty;
            grid.ModalHost = _Screen.Context.Modals;
        }

        private IEnumerable<IWidget> SectionWidgets()
        {
            switch (Section)
            {
                case "findings": return new IWidget[] { Findings };
                case "dependencies": return new IWidget[] { Dependencies };
                case "json": return new IWidget[] { Json };
                case "overrides":
                    return _CanAdmin ? new IWidget[] { Overrides, FormCriterion, FormStatus, FormNote, SaveButton } : new IWidget[] { Overrides };
                default: return new IWidget[] { SummaryView };
            }
        }

        private void Apply(VesselHealthDetail? d)
        {
            if (d == null) return;
            d.Findings = d.Findings ?? new List<VesselHealthFinding>();
            d.Dependencies = d.Dependencies ?? new List<VesselDependency>();
            d.Overrides = d.Overrides ?? new List<VesselHealthOverride>();
            Detail = d;
            List<VesselHealthFinding> ordered = d.Findings.OrderBy(f => { int i = Array.IndexOf(HealthText.Criteria, f.Criterion); return i < 0 ? 99 : i; }).ToList();
            Findings.SetLocalRows(ordered);
            Dependencies.SetLocalRows(d.Dependencies);
            Overrides.SetLocalRows(d.Overrides);
            Json.Json = ArmadaJson.Serialize(d);
            SummaryView.Invalidate();
        }

        private string FindingStatus(VesselHealthFinding f)
        {
            string text = HealthText.Badge(f.Status, Localizer);
            VesselHealthOverride? o = Detail?.Overrides?.FirstOrDefault(x => x.Criterion == f.Criterion);
            if (o != null) text += "  " + T("Overridden to") + " " + HealthText.Badge(o.Status, Localizer, true);
            return text;
        }

        private string FindingDetails(VesselHealthFinding f)
        {
            string text = HealthText.Describe(f, Localizer);
            VesselHealthOverride? o = Detail?.Overrides?.FirstOrDefault(x => x.Criterion == f.Criterion);
            if (o != null && !String.IsNullOrEmpty(o.Note)) text += "  " + Localizer.T("Note: {{note}}", LocalizationArgs.Of("note", o.Note));
            return text;
        }

        private void RenderOverrides(ISurface content, int y, int width, int remaining)
        {
            int formRows = _CanAdmin ? 6 : 1;
            int listHeight = Math.Max(2, remaining - formRows - 1);
            _Scope.RenderChild(content, Overrides, new Rect(0, y, width, listHeight));
            y += listHeight + 1;
            int height = content.Size.Height;
            if (!_CanAdmin)
            {
                if (y < height) SurfaceText.Draw(content, 0, y, T("Only tenant administrators can change overrides."), Dim(), width);
                return;
            }

            bool editing = Enum.TryParse<VesselHealthCriterionEnum>(FormCriterion.Value ?? "", out VesselHealthCriterionEnum crit) && (Detail?.Overrides?.Any(o => o.Criterion == crit) ?? false);
            if (y < height) SurfaceText.Draw(content, 0, y++, T(editing ? "Edit override" : "Add override") + "   (" + T("e edit, Del remove, Ctrl+S save") + ")", On(Theme.Accent), width);
            if (FormError != null && y < height) SurfaceText.Draw(content, 0, y++, "! " + FormError, On(Theme.Error), width);
            int labelWidth = 12;
            int fieldWidth = Math.Max(10, width - labelWidth);
            if (y < height)
            {
                SurfaceText.Draw(content, 0, y, T("Criterion"), Dim(), labelWidth);
                _Scope.RenderChild(content, FormCriterion, new Rect(labelWidth, y, Math.Min(30, fieldWidth), 1));
                y++;
            }

            if (y < height)
            {
                SurfaceText.Draw(content, 0, y, T("Status"), Dim(), labelWidth);
                _Scope.RenderChild(content, FormStatus, new Rect(labelWidth, y, Math.Min(30, fieldWidth), 1));
                y++;
            }

            if (y < height)
            {
                SurfaceText.Draw(content, 0, y, T("Note"), Dim(), labelWidth);
                _Scope.RenderChild(content, FormNote, new Rect(labelWidth, y, fieldWidth, 1));
                y++;
            }

            if (y < height) _Scope.RenderChild(content, SaveButton, new Rect(labelWidth, y, Math.Min(24, fieldWidth), 1));
        }

        private OpsDocument BuildSummary(OpsDocument doc)
        {
            VesselHealthDetail? d = Detail;
            VesselHealth? h = d?.Health;
            if (h == null) return doc;
            ITextLocalizer loc = Localizer;
            DateTime now = _Screen.Context.Clock.UtcNow;
            bool evaluated = !String.IsNullOrEmpty(h.Id);
            if (!evaluated)
            {
                doc.Text(T("This vessel has not been evaluated yet."), doc.Theme.Warning);
                if (_CanAdmin && _Evaluation != null) doc.Note("e " + T("Evaluate now"));
                doc.Blank();
            }

            if (!String.IsNullOrEmpty(h.ErrorCode)) doc.Text("! " + HealthText.Describe(h.ErrorCode, null, null, loc), doc.Theme.Warning);
            string baseBranch = String.IsNullOrEmpty(_DefaultBranch) ? T("the default branch") : _DefaultBranch!;
            doc.Field("Fleet", h.FleetName ?? h.FleetId);
            doc.Field("Current branch", h.CurrentBranch);
            doc.Field(loc.T("Versus {{base}}", LocalizationArgs.Of("base", baseBranch)), HealthText.AheadBehind(h.AheadOfDefault, h.BehindDefault, loc));
            doc.Field("Versus upstream", HealthText.AheadBehind(h.AheadOfUpstream, h.BehindUpstream, loc));
            doc.Field("Dirty", h.IsDirty.HasValue ? T(h.IsDirty.Value ? "Yes" : "No") : "-");
            doc.Field("Untracked files", HealthText.Count(loc, h.UntrackedCount));
            doc.Field("Branches", h.BranchCount.HasValue
                ? loc.T("{{count}} ({{stale}} stale, {{armada}} armada/*)", LocalizationArgs.Of("count", loc.FormatNumber(h.BranchCount.Value), "stale", loc.FormatNumber(h.StaleBranchCount ?? 0), "armada", loc.FormatNumber(h.ArmadaBranchCount ?? 0)))
                : "-");
            doc.Field("Primary language", h.PrimaryLanguage);
            doc.Field("Projects", HealthText.Count(loc, h.ProjectCount));
            doc.Field("Outdated packages", h.OutdatedCount.HasValue
                ? loc.T("{{count}} ({{major}} major)", LocalizationArgs.Of("count", loc.FormatNumber(h.OutdatedCount.Value), "major", loc.FormatNumber(h.OutdatedMajorCount ?? 0)))
                : "-");
            doc.Field("Vulnerable packages", h.VulnerableCount.HasValue
                ? loc.T("{{count}} (max {{severity}})", LocalizationArgs.Of("count", loc.FormatNumber(h.VulnerableCount.Value), "severity", loc.T(HealthText.SeverityLabel(h.MaxVulnerabilitySeverity))))
                : "-");
            doc.Field("Last test run", String.IsNullOrEmpty(h.LastCheckRunStatus) ? "-" : T(h.LastCheckRunStatus!));
            doc.Field("CI configuration", BuildText.YesNo(h.HasCiConfig, _Screen.Context.Loc));
            doc.Field("License", BuildText.YesNo(h.HasLicense, _Screen.Context.Loc));
            doc.Field("Readme", BuildText.YesNo(h.HasReadme, _Screen.Context.Loc));
            doc.Field("Readiness errors", HealthText.Count(loc, h.ReadinessErrorCount));
            doc.Field("Recent failed missions", HealthText.Count(loc, h.RecentMissionFailureCount));
            doc.Field("Last commit", h.LastCommitUtc.HasValue ? loc.FormatRelative(h.LastCommitUtc.Value, now) : "-");
            doc.Field("Evaluated", h.EvaluatedUtc.HasValue ? loc.FormatDateTime(h.EvaluatedUtc.Value) : T("Never"));
            doc.Field("Evaluation time", h.EvaluationDurationMs.HasValue ? loc.T("{{count}} ms", LocalizationArgs.Of("count", loc.FormatNumber(h.EvaluationDurationMs.Value))) : "-");
            doc.Field("Dependencies checked", h.DependenciesEvaluatedUtc.HasValue ? loc.FormatDateTime(h.DependenciesEvaluatedUtc.Value) : "-");
            doc.Field("Evaluated path", h.EvaluatedPath);
            doc.Blank();
            doc.Note((_OpenBranches != null ? "b " + T("Manage branches") + "   " : "") + "f " + T("View findings"));
            return doc;
        }

        #endregion
    }
}
