namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;
    using FocusScope = Armada.Tui.Widgets.FocusScope;

    /// <summary>
    /// The dashboard's VesselPickerModal: choose the target vessels for a fleet action run. Filters by name or
    /// working directory and by fleet; <c>Space</c> toggles a vessel, <c>Ctrl+A</c> selects every visible vessel (or
    /// clears them), and <c>Ctrl+S</c> or <c>Enter</c> on the list continues with the selection (1 to 500 vessels).
    /// <c>Tab</c> moves between the filters and the list; <c>Esc</c> cancels. Closes with the chosen ids
    /// (<see cref="List{T}"/> of string). Not thread-safe.
    /// </summary>
    public class VesselPickerDialog : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Name or path filter.
        /// </summary>
        public TextInput Search { get; } = new TextInput();

        /// <summary>
        /// Fleet filter.
        /// </summary>
        public SelectField<string> Fleet { get; } = new SelectField<string>();

        /// <summary>
        /// Vessel list.
        /// </summary>
        public ArmadaGrid<Vessel> Grid { get; } = new ArmadaGrid<Vessel>(v => v.Id);

        /// <summary>
        /// Selected vessel ids in selection order.
        /// </summary>
        public List<string> Selected { get; } = new List<string>();

        /// <summary>
        /// Error (translated), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// True while vessels load.
        /// </summary>
        public bool Loading { get; set; } = true;

        #endregion

        #region Private-Members

        private readonly FocusScope _Scope = new FocusScope();
        private List<Vessel> _Vessels = new List<Vessel>();
        private int _ScreenWidth = 100;
        private int _ScreenHeight = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="modals">Modal host for the fleet picker.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        public VesselPickerDialog(string title, IModalHost modals, ITextLocalizer? localizer, ArmadaTheme? theme)
            : base(title, localizer, theme)
        {
            Search.Placeholder = "Vessel name or path contains...";
            Search.ValueChanged += (s, e) => ApplyFilter();
            Search.Submitted += (s, e) => _Scope.Focus(Grid);
            Fleet.ModalHost = modals;
            Fleet.PickerTitle = "Filter vessels by fleet";
            Fleet.Options = new List<SelectOption<string>> { new SelectOption<string>("", T("All Fleets")) };
            Fleet.SetValue("");
            Fleet.ValueChanged += (s, e) => ApplyFilter();
            Grid.PageSize = 500;
            Grid.PageSizes = new List<int> { 500 };
            Grid.ShowPagingBar = false;
            Grid.EmptyText = "No vessels match the current filters.";
            Grid.AddColumn(new GridColumn<Vessel>("name", "Name", v => v.Name) { Weight = 2 });
            Grid.AddColumn(new GridColumn<Vessel>("path", "Working Directory", v => v.WorkingDirectory ?? "-") { Weight = 3 });
            Grid.SelectionChanged += (s, ids) => SyncSelection();
            Grid.Activated += (s, v) => Continue();
            foreach (ArmadaWidget w in new ArmadaWidget[] { Search, Fleet, Grid })
            {
                w.Localizer = Localizer;
                w.ApplyTheme(Theme);
            }

            _Scope.Wrap = true;
            _Scope.Add(Search);
            _Scope.Add(Fleet);
            _Scope.Add(Grid);
            _Scope.Focus(Grid);
            _Scope.SetActive(true);
            FooterHint = " Space " + T("Toggle") + "  Ctrl+A " + T("All") + "  Tab " + T("Filters") + "  Ctrl+S " + T("Continue") + "  Esc " + T("Cancel") + " ";
            MinContentWidth = 50;
            MaxContentWidth = 140;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Supply the vessels and fleets.
        /// </summary>
        /// <param name="vessels">Vessels.</param>
        /// <param name="fleets">Fleets.</param>
        public void SetData(IEnumerable<Vessel> vessels, IEnumerable<Fleet> fleets)
        {
            _Vessels = (vessels ?? Enumerable.Empty<Vessel>()).ToList();
            List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", T("All Fleets")) };
            options.AddRange((fleets ?? Enumerable.Empty<Fleet>()).Select(f => new SelectOption<string>(f.Id, f.Name)));
            Fleet.Options = options;
            Fleet.SetValue("");
            Loading = false;
            ApplyFilter();
        }

        /// <summary>
        /// Vessels shown under the current filters.
        /// </summary>
        /// <returns>Vessels.</returns>
        public List<Vessel> Visible()
        {
            string term = Search.Value.Trim();
            string fleet = Fleet.Value ?? "";
            return _Vessels.Where(v => (fleet.Length == 0 || v.FleetId == fleet)
                && (term.Length == 0 || v.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || (v.WorkingDirectory ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        }

        /// <summary>
        /// Continue with the selection when it is valid.
        /// </summary>
        /// <returns>True when the dialog closed.</returns>
        public bool Continue()
        {
            SyncSelection();
            if (Selected.Count == 0 || Selected.Count > FleetActionLabels.MaxRunVessels) return false;
            RequestClose(Selected.ToList());
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (ctrl && key.Code == KeyCode.Character && Char.ToLowerInvariant((char)key.Rune) == 's')
            {
                Continue();
                return true;
            }

            if (ctrl && key.Code == KeyCode.Character && Char.ToLowerInvariant((char)key.Rune) == 'a')
            {
                ToggleAllVisible();
                return true;
            }

            if (key.Code == KeyCode.Tab)
            {
                _Scope.Move((key.Modifiers & KeyModifiers.Shift) == 0);
                return true;
            }

            _Scope.HandleKey(key);
            return true;
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            if (ReferenceEquals(_Scope.Focused, Search)) Search.Insert(text);
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            _ScreenWidth = surface.Size.Width;
            _ScreenHeight = surface.Size.Height;
            base.Render(surface);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, Math.Max(50, (int)(_ScreenWidth * 0.75)));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Max(8, (int)(_ScreenHeight * 0.8) - 4);
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int height = content.Size.Height;
            int y = 0;
            string count = Localizer.T("{count, plural, one {# vessel selected} other {# vessels selected}}", LocalizationArgs.Of("count", Selected.Count));
            SurfaceText.Draw(content, 0, y++, count, Dim(), width);
            int half = Math.Max(20, width / 2);
            _Scope.RenderChild(content, Search, new Rect(0, y, half - 1, 1));
            _Scope.RenderChild(content, Fleet, new Rect(half + 1, y, Math.Max(10, width - half - 1), 1));
            y += 2;
            if (Selected.Count > FleetActionLabels.MaxRunVessels) SurfaceText.Draw(content, 0, y++, "! " + T("A run can target at most 500 vessels."), On(Theme.Error), width);
            if (Error != null) SurfaceText.Draw(content, 0, y++, "! " + Error, On(Theme.Error), width);
            if (Loading)
            {
                SurfaceText.Draw(content, 0, y, T("Loading..."), Dim(), width);
                return;
            }

            if (_Vessels.Count == 0)
            {
                SurfaceText.Draw(content, 0, y, T("No vessels yet. Import repositories from the Vessels page first."), Dim(), width);
                return;
            }

            _Scope.RenderChild(content, Grid, new Rect(0, y, width, Math.Max(1, height - y)));
        }

        #endregion

        #region Private-Methods

        private void ApplyFilter()
        {
            List<string> keep = Selected.ToList();
            Grid.SetLocalRows(Visible());
            Grid.Marked.Clear();
            foreach (string id in keep) Grid.Marked.Add(id);
        }

        private void SyncSelection()
        {
            HashSet<string> visible = new HashSet<string>(Grid.Rows.Select(v => v.Id), StringComparer.Ordinal);
            foreach (string id in visible)
            {
                bool marked = Grid.Marked.Contains(id);
                if (marked && !Selected.Contains(id)) Selected.Add(id);
                if (!marked) Selected.Remove(id);
            }
        }

        private void ToggleAllVisible()
        {
            List<Vessel> visible = Grid.Rows.ToList();
            bool all = visible.Count > 0 && visible.All(v => Selected.Contains(v.Id));
            foreach (Vessel v in visible)
            {
                if (all) Grid.Marked.Remove(v.Id);
                else Grid.Marked.Add(v.Id);
            }

            SyncSelection();
        }

        #endregion
    }
}
