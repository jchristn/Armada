namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The standard OPERATIONS list: a heading and subtitle, a toolbar of screen and bulk actions, the filter row,
    /// optional KPI, banner, or chart lines above the table, and an <see cref="ArmadaGrid{T}"/> with server or local
    /// paging, sorting within the loaded rows (like the dashboard's tables), multi-select, a row action menu (<c>.</c>),
    /// and single-key row actions. Every action is also a palette and Actions-menu command and is listed in the help
    /// overlay. Background refreshes keep the cursor and marks. Derived screens add columns, filters, and actions in
    /// their constructor and then call <see cref="Start"/>. Not thread-safe.
    /// </summary>
    /// <typeparam name="T">Row type.</typeparam>
    public abstract class OpsListScreen<T> : OpsScreen where T : class
    {
        #region Public-Members

        /// <summary>
        /// The table.
        /// </summary>
        public ArmadaGrid<T> Grid { get; }

        /// <summary>
        /// The filter row.
        /// </summary>
        public OpsFilterBar Filters { get; } = new OpsFilterBar();

        /// <summary>
        /// The toolbar.
        /// </summary>
        public ButtonRow Toolbar { get; } = new ButtonRow();

        /// <summary>
        /// Row actions in menu order.
        /// </summary>
        public List<OpsAction<T>> RowActions { get; } = new List<OpsAction<T>>();

        /// <summary>
        /// Screen-level and bulk actions in toolbar order.
        /// </summary>
        public List<OpsScreenAction> ScreenActions { get; } = new List<OpsScreenAction>();

        /// <summary>
        /// Sort values by column sort key (columns with an entry are sortable).
        /// </summary>
        public Dictionary<string, Func<T, IComparable?>> SortValues { get; } = new Dictionary<string, Func<T, IComparable?>>(StringComparer.Ordinal);

        /// <summary>
        /// Rows from the last load before local filtering (server page or all rows).
        /// </summary>
        public IReadOnlyList<T> LoadedRows
        {
            get { return _Loaded; }
        }

        /// <summary>
        /// Total records reported by the server on the last load (server paging), or the local row count.
        /// </summary>
        public long ServerTotal { get; private set; } = 0;

        /// <summary>
        /// True once the first load finished.
        /// </summary>
        public bool HasLoaded { get; private set; } = false;

        /// <summary>
        /// English subtitle under the heading, or null.
        /// </summary>
        public virtual string? Subtitle
        {
            get { return null; }
        }

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> TypingHints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                foreach (OpsScreenAction a in ScreenActions.Where(a => a.Key != null && a.Available && KeyGesture.WorksWhileTyping(a.Key)).Take(3))
                    hints.Add(new KeyValuePair<string, string>(KeyLabel(a.Key!), a.Label));
                return hints;
            }
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                hints.Add(new KeyValuePair<string, string>("Enter", "Open"));
                hints.Add(new KeyValuePair<string, string>(".", "Actions"));
                if (!Filters.IsEmpty) hints.Add(new KeyValuePair<string, string>("/", "Filter"));
                foreach (OpsScreenAction a in ScreenActions.Where(a => a.Key != null && a.Available).Take(3))
                    hints.Add(new KeyValuePair<string, string>(KeyLabel(a.Key!), a.Label));
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private List<T> _Loaded = new List<T>();
        private volatile bool _Stale = true;
        private List<T>? _Cache = null;
        private Timer? _Debounce = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        /// <param name="idSelector">Row id.</param>
        /// <param name="screenName">Screen name (preferences and command ids).</param>
        /// <param name="title">English title.</param>
        protected OpsListScreen(RouteMatch route, TuiContext context, Func<T, string> idSelector, string screenName, string title)
            : base(route, context, screenName, title)
        {
            Grid = new ArmadaGrid<T>(idSelector);
            Grid.Dispatcher = context.Dispatcher;
            Grid.ModalHost = context.Modals;
            Grid.Loader = LoadAsync;
            Grid.Activated += (s, row) => OpenRow(row);
            Grid.MenuRequested += (s, row) => ShowRowMenu(row);
            Grid.QueryChanged += (s, q) => SaveTablePrefs();
            Grid.ColumnsChanged += (s, cols) => SaveTablePrefs();
            Filters.Exited += (s, e) => Scope.Focus(Grid);
            AddChild(Filters);
            AddChild(Toolbar);
            AddChild(Grid);
            Scope.Focus(Grid);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reload from the server (F5 and auto-refresh).
        /// </summary>
        public virtual void Refresh()
        {
            _Stale = true;
            Grid.Reload();
        }

        /// <summary>
        /// Re-apply local filters and sort to the loaded rows (server mode refetches the page).
        /// </summary>
        public void ApplyFilters()
        {
            if (ServerPaging)
            {
                Grid.Reload();
                return;
            }

            if (Grid.PageNumber != 1) Grid.GoToPage(1);
            else Grid.Reload();
        }

        /// <summary>
        /// A server-side filter changed: back to page 1 and reload.
        /// </summary>
        public void ServerFilterChanged()
        {
            _Stale = true;
            if (Grid.PageNumber != 1) Grid.GoToPage(1);
            else Grid.Reload();
        }

        /// <summary>
        /// Open the row action menu for a row.
        /// </summary>
        /// <param name="row">Row.</param>
        public void ShowRowMenu(T row)
        {
            if (row == null) return;
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            foreach (OpsAction<T> a in RowActions.Where(a => a.AppliesTo(row)))
            {
                OpsAction<T> action = a;
                ActionMenuItem item = new ActionMenuItem((a.Danger ? "! " : "") + Tr(a.Label), () => action.Run(row), a.Key != null ? KeyLabel(a.Key) : "");
                item.Destructive = a.Danger;
                items.Add(item);
            }

            if (items.Count == 0) return;
            ShowMenu(RowTitle(row), items);
        }

        /// <summary>
        /// Run a row action by id on the current row.
        /// </summary>
        /// <param name="id">Action id.</param>
        /// <returns>True when it ran.</returns>
        public bool RunRowAction(string id)
        {
            T? row = Grid.Current;
            OpsAction<T>? action = RowActions.FirstOrDefault(a => a.Id == id);
            if (row == null || action == null || !action.AppliesTo(row)) return false;
            action.Run(row);
            return true;
        }

        /// <summary>
        /// Run a screen action by id when available.
        /// </summary>
        /// <param name="id">Action id.</param>
        /// <returns>True when it ran.</returns>
        public bool RunScreenAction(string id)
        {
            OpsScreenAction? action = ScreenActions.FirstOrDefault(a => a.Id == id);
            if (action == null || !action.Available) return false;
            action.Run();
            return true;
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return Refresh;
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            foreach (OpsScreenAction a in ScreenActions)
            {
                OpsScreenAction action = a;
                ArmadaCommand c = a.Key != null
                    ? new ArmadaCommand(ScreenKey + "." + a.Id, a.Label, CommandMenuEnum.Actions, () => { if (action.Available) action.Run(); }, a.Key)
                    : new ArmadaCommand(ScreenKey + "." + a.Id, a.Label, CommandMenuEnum.Actions, () => { if (action.Available) action.Run(); });
                c.Group = Title;
                c.Dispatch = false;
                c.IsEnabled = () => action.Available;
                list.Add(c);
            }

            ArmadaCommand menu = new ArmadaCommand(ScreenKey + ".row-menu", "Row actions", CommandMenuEnum.Actions, () => { T? r = Grid.Current; if (r != null) ShowRowMenu(r); }, ".");
            menu.Group = Title;
            menu.Dispatch = false;
            menu.IsEnabled = () => Grid.Current != null;
            list.Add(menu);
            foreach (OpsAction<T> a in RowActions)
            {
                OpsAction<T> action = a;
                string title = Tr("Selected row") + ": " + Tr(a.Label);
                ArmadaCommand c = a.Key != null
                    ? new ArmadaCommand(ScreenKey + ".row." + a.Id, title, CommandMenuEnum.Actions, () => RunRowAction(action.Id), a.Key)
                    : new ArmadaCommand(ScreenKey + ".row." + a.Id, title, CommandMenuEnum.Actions, () => RunRowAction(action.Id));
                c.Group = Title;
                c.Dispatch = false;
                c.IsEnabled = () => Grid.Current != null && action.AppliesTo(Grid.Current!);
                list.Add(c);
            }

            if (!Filters.IsEmpty)
            {
                ArmadaCommand filter = new ArmadaCommand(ScreenKey + ".filter", "Filter", CommandMenuEnum.Actions, () => Scope.Focus(Filters), "/");
                filter.Group = Title;
                filter.Dispatch = false;
                list.Add(filter);
            }

            return list;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            IWidget? focused = Scope.Focused;
            bool onGrid = ReferenceEquals(focused, Grid) || ReferenceEquals(focused, Toolbar);
            if (onGrid)
            {
                if (key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None && key.Rune == '/' && !Filters.IsEmpty)
                {
                    Scope.Focus(Filters);
                    Filters.Scope.FocusFirst();
                    return true;
                }

                T? row = Grid.Current;
                if (row != null)
                {
                    foreach (OpsAction<T> a in RowActions)
                    {
                        if (a.Key != null && Matches(a.Key, key) && a.AppliesTo(row))
                        {
                            a.Run(row);
                            return true;
                        }
                    }
                }

                foreach (OpsScreenAction a in ScreenActions)
                {
                    if (a.Key != null && Matches(a.Key, key) && a.Available)
                    {
                        a.Run();
                        return true;
                    }
                }
            }

            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Escape && !onGrid)
            {
                Scope.Focus(Grid);
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            _Debounce?.Dispose();
            _Debounce = null;
            base.OnDeactivated();
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 4) return;
            // Each focus region (toolbar, filters, grid) gets a box line above and below it (see RegionStack).
            RegionStack stack = new RegionStack(width, height);
            int y = stack.Content(1);
            string heading = Tr(Title);
            int x = SurfaceText.Draw(surface, 0, y, heading, Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
            if (Subtitle != null) SurfaceText.Draw(surface, x + 2, y, Tr(Subtitle), Theme.Muted, width - x - 2);

            SyncToolbar();
            string refresh = Context.Refresh.StatusText;
            string right = refresh.Length > 0 ? "[" + Tr(refresh) + "]  F5 " + Tr("Refresh") : "F5 " + Tr("Refresh");
            int rw = TextCells.Width(right);
            if (Toolbar.Buttons.Any(b => b.Visible))
            {
                Rect bar = stack.Place(Toolbar, 1);
                y = bar.Y;
                Scope.RenderChild(surface, Toolbar, new Rect(0, y, Math.Max(1, width - rw - 2), 1));
                Scope.Place(Toolbar, bar);
            }
            else
            {
                y = stack.Content(1);
            }

            SurfaceText.Draw(surface, Math.Max(0, width - rw), y, right, Theme.Muted, rw);

            if (!Filters.IsEmpty)
            {
                int fh = Filters.PreferredHeight(width);
                Scope.RenderChild(surface, Filters, stack.Place(Filters, fh));
            }

            int above = Math.Min(AboveHeight(width), Math.Max(0, stack.Remaining - 5));
            if (above > 0)
            {
                int top = stack.Content(above);
                RenderAbove(new SurfaceView(surface, new Rect(0, top, width, above)), width);
            }

            Rect grid = stack.Fill(Grid);
            if (!grid.IsEmpty) Scope.RenderChild(surface, Grid, grid);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// True when the server pages (the loader gets one page); false to load all rows once and page locally.
        /// </summary>
        protected virtual bool ServerPaging
        {
            get { return true; }
        }

        /// <summary>
        /// Fetch rows: one page (server paging) or all rows (local paging). Runs off the UI loop.
        /// </summary>
        /// <param name="query">Page, size, and sort.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows and the total.</returns>
        protected abstract Task<GridPage<T>> FetchAsync(GridQuery query, CancellationToken token);

        /// <summary>
        /// Local filters (the dashboard's column filters). Runs off the UI loop; read widget values only.
        /// </summary>
        /// <param name="rows">Rows.</param>
        /// <returns>Matching rows.</returns>
        protected virtual IEnumerable<T> FilterLocal(IEnumerable<T> rows)
        {
            return rows;
        }

        /// <summary>
        /// Called on the UI loop after rows arrive (before they are shown), for KPIs and lookups.
        /// </summary>
        /// <param name="rows">Rows before local filtering.</param>
        protected virtual void OnLoaded(IReadOnlyList<T> rows)
        {
        }

        /// <summary>
        /// Open a row (Enter or double-click).
        /// </summary>
        /// <param name="row">Row.</param>
        protected virtual void OpenRow(T row)
        {
        }

        /// <summary>
        /// Title of a row for menus and confirmations.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>Title.</returns>
        protected abstract string RowTitle(T row);

        /// <summary>
        /// Rows above the table (KPIs, banners, charts) at a width.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Rows.</returns>
        protected virtual int AboveHeight(int width)
        {
            return 0;
        }

        /// <summary>
        /// Draw the area above the table.
        /// </summary>
        /// <param name="surface">Surface sized to <see cref="AboveHeight"/>.</param>
        /// <param name="width">Width.</param>
        protected virtual void RenderAbove(ISurface surface, int width)
        {
        }

        /// <summary>
        /// Add a column; sortable when <paramref name="sort"/> is given.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="title">English title.</param>
        /// <param name="value">Cell text.</param>
        /// <param name="weight">Proportional weight, or 0 with a fixed width.</param>
        /// <param name="width">Fixed width, or null.</param>
        /// <param name="sort">Sort value, or null.</param>
        /// <param name="style">Cell style, or null.</param>
        /// <returns>The column.</returns>
        protected GridColumn<T> Column(string key, string title, Func<T, string> value, int weight = 1, int? width = null, Func<T, IComparable?>? sort = null, Func<T, Armada.Tui.Theming.ArmadaTheme, CellStyle?>? style = null)
        {
            GridColumn<T> col = new GridColumn<T>(key, title, value);
            if (width.HasValue) col.Width = width.Value;
            else col.Weight = Math.Max(1, weight);
            col.MinWidth = Math.Min(col.Width ?? 6, 6);
            if (sort != null)
            {
                col.Sortable = true;
                SortValues[key] = sort;
            }

            col.Style = style;
            return Grid.AddColumn(col);
        }

        /// <summary>
        /// Add a text filter that applies locally (debounced 250 ms).
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="width">Field width.</param>
        /// <param name="placeholder">English placeholder.</param>
        /// <param name="server">True when the filter goes to the server (refetch).</param>
        /// <returns>The input.</returns>
        protected TextInput TextFilter(string label, int width = 18, string placeholder = "Search...", bool server = false)
        {
            TextInput input = new TextInput();
            input.Placeholder = placeholder;
            input.ValueChanged += (s, e) => Debounced(server);
            return Filters.Add(label, input, width);
        }

        /// <summary>
        /// Add a select filter.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="options">Options.</param>
        /// <param name="width">Field width.</param>
        /// <param name="server">True when the filter goes to the server.</param>
        /// <returns>The select.</returns>
        protected SelectField<string> SelectFilter(string label, List<SelectOption<string>> options, int width = 18, bool server = true)
        {
            SelectField<string> select = new SelectField<string>();
            select.Options = options;
            select.PickerTitle = label;
            select.ModalHost = Context.Modals;
            if (options.Count > 0) select.SetValue(options[0].Value);
            select.ValueChanged += (s, e) =>
            {
                if (server) ServerFilterChanged();
                else ApplyFilters();
            };
            return Filters.Add(label, select, width);
        }

        /// <summary>
        /// Add the admin-only user scope filter (hidden for regular users, as on the dashboard).
        /// </summary>
        /// <returns>The select, or null for regular users.</returns>
        protected SelectField<string>? UserScopeFilter()
        {
            if (!IsTenantAdmin) return null;
            SelectField<string> select = SelectFilter("User", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All users")) }, 22, true);
            Reference.Changed += (s, name) =>
            {
                if (name != "users") return;
                string? current = select.Value;
                select.Options = Reference.UserScopeOptions();
                select.SetValue(current ?? "");
            };
            Reference.Ensure("users");
            return select;
        }

        /// <summary>
        /// Restore table preferences and start loading. Call at the end of the derived constructor.
        /// </summary>
        /// <param name="defaultSortKey">Default sort key, or null.</param>
        /// <param name="defaultDescending">Default sort direction.</param>
        /// <param name="defaultPageSize">Default page size.</param>
        protected void Start(string? defaultSortKey = null, bool defaultDescending = false, int defaultPageSize = 25)
        {
            foreach (OpsScreenAction a in ScreenActions.Where(a => a.Toolbar))
            {
                OpsScreenAction action = a;
                Button b = new Button(a.Label, () => { if (action.Available) action.Run(); });
                b.Hint = a.Key != null ? KeyLabel(a.Key) : null;
                Toolbar.Add(b);
            }

            ApplyTablePrefs(defaultSortKey, defaultDescending, defaultPageSize);
            Grid.Reload();
        }

        /// <summary>
        /// Human label of a key string.
        /// </summary>
        /// <param name="key">Key string.</param>
        /// <returns>Label.</returns>
        protected static string KeyLabel(string key)
        {
            try { return KeyStroke.Parse(key).ToLabel(); }
            catch (FormatException) { return key; }
        }

        #endregion

        #region Private-Methods

        private static bool Matches(string keyText, KeyEvent key)
        {
            try { return KeyStroke.Parse(keyText).Matches(key); }
            catch (FormatException) { return false; }
        }

        private void SyncToolbar()
        {
            List<OpsScreenAction> toolbar = ScreenActions.Where(a => a.Toolbar).ToList();
            IReadOnlyList<Button> buttons = Toolbar.Buttons;
            for (int i = 0; i < buttons.Count && i < toolbar.Count; i++)
            {
                buttons[i].Visible = toolbar[i].Available;
                buttons[i].Label = toolbar[i].DynamicLabel != null ? toolbar[i].DynamicLabel!() : toolbar[i].Label;
            }
        }

        private void Debounced(bool server)
        {
            _Debounce?.Dispose();
            _Debounce = new Timer(_ => Context.Dispatcher.Post(() =>
            {
                if (!IsLive) return;
                if (server) ServerFilterChanged();
                else ApplyFilters();
            }), null, 250, Timeout.Infinite);
        }

        private async Task<GridPage<T>> LoadAsync(GridQuery query, CancellationToken token)
        {
            List<T> source;
            long total;
            if (ServerPaging)
            {
                GridPage<T> page = await FetchAsync(query, token).ConfigureAwait(false);
                source = page.Rows ?? new List<T>();
                total = page.TotalRecords;
                _Stale = false;
            }
            else
            {
                List<T>? cache = _Cache;
                if (cache == null || _Stale)
                {
                    GridQuery all = new GridQuery();
                    all.PageNumber = 1;
                    all.PageSize = 10000;
                    GridPage<T> page = await FetchAsync(all, token).ConfigureAwait(false);
                    cache = page.Rows ?? new List<T>();
                    _Cache = cache;
                    _Stale = false;
                }

                source = cache;
                total = cache.Count;
            }

            List<T> snapshot = source.ToList();
            Context.Dispatcher.Post(() =>
            {
                if (!IsLive) return;
                _Loaded = snapshot;
                ServerTotal = total;
                HasLoaded = true;
                OnLoaded(snapshot);
            });

            List<T> filtered = FilterLocal(snapshot).ToList();
            if (query.SortKey != null && SortValues.TryGetValue(query.SortKey, out Func<T, IComparable?>? sel))
            {
                Comparer<IComparable?> cmp = Comparer<IComparable?>.Create((a, b) =>
                {
                    if (a == null && b == null) return 0;
                    if (a == null) return -1;
                    if (b == null) return 1;
                    if (a is string sa && b is string sb) return String.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                    return a.CompareTo(b);
                });
                filtered = query.SortDescending ? filtered.OrderByDescending(sel, cmp).ToList() : filtered.OrderBy(sel, cmp).ToList();
            }

            if (ServerPaging) return new GridPage<T>(filtered, Math.Max(total, filtered.Count));
            int size = Math.Max(1, query.PageSize);
            int pages = Math.Max(1, (filtered.Count + size - 1) / size);
            int pageNumber = Math.Clamp(query.PageNumber, 1, pages);
            return new GridPage<T>(filtered.Skip((pageNumber - 1) * size).Take(size).ToList(), filtered.Count);
        }

        private void ApplyTablePrefs(string? sortKey, bool descending, int pageSize)
        {
            if (Context.Prefs.Current.Tables.TryGetValue(ScreenKey, out TablePreferences? prefs) && prefs != null)
            {
                Grid.Restore(prefs.SortColumn ?? sortKey, prefs.SortColumn != null ? prefs.SortDescending : descending, prefs.PageSize ?? pageSize);
                if (prefs.VisibleColumns != null && prefs.VisibleColumns.Count > 0) Grid.VisibleColumnKeys = prefs.VisibleColumns.ToList();
            }
            else
            {
                Grid.Restore(sortKey, descending, pageSize);
            }
        }

        private void SaveTablePrefs()
        {
            TablePreferences prefs = new TablePreferences();
            prefs.PageSize = Grid.PageSize;
            prefs.SortColumn = Grid.SortKey;
            prefs.SortDescending = Grid.SortDescending;
            prefs.VisibleColumns = Grid.VisibleColumnKeys?.ToList();
            Context.Prefs.Current.Tables[ScreenKey] = prefs;
            Context.Prefs.Save();
        }

        #endregion
    }
}
