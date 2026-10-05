namespace Armada.Tui.Screens.Entities
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Base for the delivery and configuration list screens (the dashboard's list pages): an overview strip (KPIs), a
    /// filter bar, and an <see cref="ArmadaGrid{T}"/> with server paging and filters, plus the standard keys and
    /// commands: <c>Enter</c> open, <c>.</c> row actions, <c>n</c> new, <c>e</c> edit, <c>Del</c> delete (marked rows
    /// when any are marked), <c>j</c> View JSON, <c>y</c> copy ID, <c>/</c> filter, <c>F5</c> refresh. Sorting uses the
    /// server where it can; otherwise the screen reads every matching record (server-filtered) and sorts and pages
    /// them locally. Filters can be deep linked through the route query (<c>?status=Failed</c>). Subscribes to
    /// <see cref="LiveEvents"/> while active. Subclasses describe columns, filters, the loader, and actions; the
    /// screen is built lazily on first use. Use on the UI loop thread.
    /// </summary>
    /// <typeparam name="T">Row type.</typeparam>
    public abstract class EntityListScreen<T> : ScreenBase where T : class
    {
        #region Public-Members

        /// <summary>
        /// The grid.
        /// </summary>
        public ArmadaGrid<T> Grid { get; }

        /// <summary>
        /// The filter bar.
        /// </summary>
        public FilterBar Filters { get; }

        /// <summary>
        /// The overview strip.
        /// </summary>
        public KpiStrip Kpis { get; } = new KpiStrip();

        /// <summary>
        /// Read-only or explanatory line under the filters (English), or null.
        /// </summary>
        public string? Notice { get; protected set; } = null;

        /// <summary>
        /// Number of completed grid loads (tests).
        /// </summary>
        public int LoadCount
        {
            get { return _LoadCount; }
        }

        /// <summary>
        /// Preference and command scope key: the screen's type name, so tabs of one hub keep separate table
        /// preferences and command ids.
        /// </summary>
        public override string ScreenKey
        {
            get { return GetType().Name; }
        }

        /// <summary>
        /// English singular entity name ("Deployment").
        /// </summary>
        public abstract string EntityLabel { get; }

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                hints.Add(new KeyValuePair<string, string>("Enter", "Open"));
                hints.Add(new KeyValuePair<string, string>(".", "Actions"));
                hints.Add(new KeyValuePair<string, string>("/", "Filter"));
                if (HasCreate && CanCreate) hints.Add(new KeyValuePair<string, string>("n", "New"));
                hints.Add(new KeyValuePair<string, string>("j", "JSON"));
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private bool _Built = false;
        private int _LoadCount = 0;
        private int _KpiGeneration = 0;
        private Dictionary<string, string> _FilterSnapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<IDisposable> _Subscriptions = new List<IDisposable>();
        private List<ArmadaCommand>? _Commands = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        protected EntityListScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            Grid = new ArmadaGrid<T>(r => IdOf(r));
            Grid.Dispatcher = context.Dispatcher;
            Grid.ModalHost = context.Modals;
            Filters = new FilterBar(context.Modals);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build (once) and load the grid and overview.
        /// </summary>
        public void Reload()
        {
            EnsureBuilt();
            _FilterSnapshot = Filters.Values();
            Grid.Reload();
            ReloadKpis();
        }

        /// <summary>
        /// Current value of a filter, safe to read from the loader thread.
        /// </summary>
        /// <param name="key">Filter key.</param>
        /// <returns>Value; empty when unset.</returns>
        public string Filter(string key)
        {
            return _FilterSnapshot.TryGetValue(key, out string? v) ? v : "";
        }

        /// <summary>
        /// Open the record (Enter).
        /// </summary>
        /// <param name="row">Row.</param>
        public virtual void Open(T row)
        {
            string? path = DetailPath(row);
            if (path != null) Context.Navigate(path);
            else if (HasEdit && CanEdit(row)) OpenEdit(row);
        }

        /// <summary>
        /// Show the row action menu for a row.
        /// </summary>
        /// <param name="row">Row.</param>
        public void ShowRowMenu(T row)
        {
            List<ActionMenuItem> items = RowActions(row);
            ActionMenu.Show(Context.Modals, NameOf(row), items, Context.Loc, Context.Theme.Current);
        }

        /// <summary>
        /// Ask to delete a row (or every marked row).
        /// </summary>
        /// <param name="row">Row under the cursor.</param>
        public void RequestDelete(T row)
        {
            if (!HasDelete) return;
            List<T> targets = Grid.Marked.Count > 0 ? Grid.MarkedRows().Where(r => CanDelete(r)).ToList() : new List<T> { row };
            if (targets.Count == 0 || targets.Any(t => !CanDelete(t))) return;
            string title = DeleteTitle;
            string message = targets.Count == 1
                ? DeleteMessage(targets[0])
                : EntityUi.T(Context, "Delete {{count}} selected records? This cannot be undone.", "count", targets.Count);
            Context.Confirm(title, message, () =>
            {
                EntityUi.Run(Context, async ct =>
                {
                    foreach (T target in targets) await DeleteAsync(target, ct).ConfigureAwait(false);
                }, () =>
                {
                    Grid.ClearMarks();
                    string text = targets.Count == 1
                        ? DeletedText(targets[0])
                        : EntityUi.T(Context, "Deleted {{count}} records.", "count", targets.Count);
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, text);
                    Reload();
                }, "Delete failed.");
            }, "Delete", DeleteRequiredText);
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            EnsureBuilt();
            if (_Commands != null) return _Commands;
            string p = CommandPrefix;
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            if (HasCreate)
            {
                list.Add(Cmd(p + ".new", CreateLabel, OpenCreate, () => CanCreate, "n"));
            }

            ArmadaCommand open = Cmd(p + ".open", "Open", () => WithCurrent(Open), () => Grid.Current != null, "enter");
            open.Dispatch = false;
            list.Add(open);
            ArmadaCommand menu = Cmd(p + ".menu", "Row actions...", () => WithCurrent(ShowRowMenu), () => Grid.Current != null, ".");
            menu.Dispatch = false;
            list.Add(menu);
            if (HasEdit) list.Add(Cmd(p + ".edit", "Edit", () => WithCurrent(r => { if (CanEdit(r)) OpenEdit(r); }), () => Grid.Current != null && CanEdit(Grid.Current), "e"));
            list.Add(Cmd(p + ".json", "View JSON", () => WithCurrent(r => EntityUi.ShowJson(Context, NameOf(r), r)), () => Grid.Current != null, "j"));
            list.Add(Cmd(p + ".copy-id", "Copy ID", () => WithCurrent(r => Context.Clipboard.Copy(IdOf(r), "ID")), () => Grid.Current != null, "y"));
            if (HasDelete) list.Add(Cmd(p + ".delete", "Delete", () => WithCurrent(RequestDelete), () => Grid.Current != null && CanDelete(Grid.Current), "delete"));
            list.Add(Cmd(p + ".filter", "Filter", FocusFilters, null, "/"));
            list.Add(Cmd(p + ".clear-filters", "Clear filters", () => Filters.Clear(), null));
            list.AddRange(ExtraCommands());
            _Commands = list;
            return list;
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return Reload;
        }

        /// <inheritdoc />
        public override void OnActivated()
        {
            EnsureBuilt();
            foreach (string evt in LiveEvents) _Subscriptions.Add(Context.Events.SubscribeCoalesced(evt, Reload));
            Reload();
        }

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            foreach (IDisposable sub in _Subscriptions) sub.Dispose();
            _Subscriptions.Clear();
        }

        /// <summary>
        /// Move focus to the filter bar.
        /// </summary>
        public void FocusFilters()
        {
            EnsureBuilt();
            if (Scope.Focus(Filters)) Filters.FocusFirst();
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            EnsureBuilt();
            return Scope.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            EnsureBuilt();
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 4) return;
            // The filter row and the grid are focus regions, each with a box line above and below (see RegionStack).
            RegionStack stack = new RegionStack(width, height);
            int kpiLines = Math.Min(3, Kpis.LinesFor(width));
            if (kpiLines > 0)
            {
                int top = stack.Content(kpiLines);
                Kpis.Render(new SurfaceView(surface, new Rect(0, top, width, kpiLines)));
            }

            int filterLines = Math.Min(3, Filters.LinesFor(width));
            if (filterLines > 0) Scope.RenderChild(surface, Filters, stack.Place(Filters, filterLines));

            if (!String.IsNullOrEmpty(Notice))
            {
                int top = stack.Content(1);
                SurfaceText.Draw(surface, 0, top, T(Notice!), Theme.Muted, width);
            }

            Rect grid = stack.Fill(Grid);
            if (grid.Height >= 2) Scope.RenderChild(surface, Grid, grid);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Stable id of a row.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>Id.</returns>
        protected abstract string IdOf(T row);

        /// <summary>
        /// Display name of a row (menu titles, confirmations, JSON titles).
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>Name.</returns>
        protected abstract string NameOf(T row);

        /// <summary>
        /// Add the grid columns.
        /// </summary>
        /// <param name="grid">Grid.</param>
        protected abstract void BuildColumns(ArmadaGrid<T> grid);

        /// <summary>
        /// Add filters (keys double as deep-link query names).
        /// </summary>
        /// <param name="filters">Filter bar.</param>
        protected virtual void BuildFilters(FilterBar filters)
        {
        }

        /// <summary>
        /// Load one page with the current filters (<see cref="Filter"/>). Runs off the UI loop.
        /// </summary>
        /// <param name="query">Page and sort.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        protected abstract Task<GridPage<T>> FetchPageAsync(GridQuery query, CancellationToken token);

        /// <summary>
        /// True when the server sorts by a key (the loader passes the sort through); otherwise sorting reads every
        /// matching record and sorts locally.
        /// </summary>
        /// <param name="sortKey">Sort key.</param>
        /// <returns>True for server sorts.</returns>
        protected virtual bool ServerSorts(string sortKey)
        {
            return false;
        }

        /// <summary>
        /// Value to sort a row by for a key (dates and numbers sort by value); null sorts by the column text.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="sortKey">Sort key.</param>
        /// <returns>Comparable or null.</returns>
        protected virtual IComparable? SortValue(T row, string sortKey)
        {
            return null;
        }

        /// <summary>
        /// Overview figures, or null for none. Runs off the UI loop.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items.</returns>
        protected virtual Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            return Task.FromResult<List<KpiItem>?>(null);
        }

        /// <summary>
        /// Path of the detail screen for a row, or null when the row has none.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>Path or null.</returns>
        protected virtual string? DetailPath(T row)
        {
            return null;
        }

        /// <summary>
        /// Whether the user may create records (tenant admins by default).
        /// </summary>
        protected virtual bool CanCreate
        {
            get { return Context.Session.IsTenantAdmin; }
        }

        /// <summary>
        /// Whether the user may edit a row.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>True when editable.</returns>
        protected virtual bool CanEdit(T row)
        {
            return CanCreate;
        }

        /// <summary>
        /// Whether the user may delete a row.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>True when deletable.</returns>
        protected virtual bool CanDelete(T row)
        {
            return CanEdit(row);
        }

        /// <summary>
        /// True when the screen offers create (override <see cref="OpenCreate"/>).
        /// </summary>
        protected virtual bool HasCreate
        {
            get { return false; }
        }

        /// <summary>
        /// True when the screen offers edit (override <see cref="OpenEdit"/>).
        /// </summary>
        protected virtual bool HasEdit
        {
            get { return false; }
        }

        /// <summary>
        /// True when the screen offers delete (override <see cref="DeleteAsync"/>).
        /// </summary>
        protected virtual bool HasDelete
        {
            get { return false; }
        }

        /// <summary>
        /// English label of the create command.
        /// </summary>
        protected virtual string CreateLabel
        {
            get { return "New " + EntityLabel; }
        }

        /// <summary>
        /// Open the create form.
        /// </summary>
        protected virtual void OpenCreate()
        {
        }

        /// <summary>
        /// Open the edit form for a row.
        /// </summary>
        /// <param name="row">Row.</param>
        protected virtual void OpenEdit(T row)
        {
        }

        /// <summary>
        /// Delete one row. Runs off the UI loop.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        protected virtual Task DeleteAsync(T row, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// English title of the delete confirmation.
        /// </summary>
        protected virtual string DeleteTitle
        {
            get { return "Delete " + EntityLabel; }
        }

        /// <summary>
        /// Translated delete confirmation message for a row.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>Message.</returns>
        protected virtual string DeleteMessage(T row)
        {
            return EntityUi.T(Context, "Delete \"{{name}}\"? This cannot be undone.", "name", NameOf(row));
        }

        /// <summary>
        /// Translated toast after a row is deleted.
        /// </summary>
        /// <param name="row">Deleted row.</param>
        /// <returns>Text.</returns>
        protected virtual string DeletedText(T row)
        {
            return EntityUi.T(Context, "\"{{name}}\" deleted.", "name", NameOf(row));
        }

        /// <summary>
        /// Word to type before a delete (typed-delete confirmation), or null.
        /// </summary>
        protected virtual string? DeleteRequiredText
        {
            get { return null; }
        }

        /// <summary>
        /// Socket event types that refresh this list while it is open.
        /// </summary>
        protected virtual IEnumerable<string> LiveEvents
        {
            get { return Array.Empty<string>(); }
        }

        /// <summary>
        /// Prefix for command ids (defaults to the screen name).
        /// </summary>
        protected virtual string CommandPrefix
        {
            get { return ScreenKey; }
        }

        /// <summary>
        /// Row menu items. The default is Open, Edit, View JSON, Copy ID, and Delete as the screen supports them.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>Items.</returns>
        protected virtual List<ActionMenuItem> RowActions(T row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            if (DetailPath(row) != null) items.Add(new ActionMenuItem("Open", () => Open(row), "Enter"));
            if (HasEdit && CanEdit(row)) items.Add(new ActionMenuItem("Edit", () => OpenEdit(row), "e"));
            items.Add(new ActionMenuItem("View JSON", () => EntityUi.ShowJson(Context, NameOf(row), row), "j") { Key = ActionMenuItem.JsonKey });
            items.Add(new ActionMenuItem("Copy ID", () => Context.Clipboard.Copy(IdOf(row), "ID"), "y"));
            if (HasDelete && CanDelete(row))
            {
                ActionMenuItem delete = new ActionMenuItem("Delete", () => RequestDelete(row), "Del");
                delete.Destructive = true;
                items.Add(delete);
            }

            return items;
        }

        /// <summary>
        /// Screen-specific commands (screen actions such as Run Health Sweep).
        /// </summary>
        /// <returns>Commands.</returns>
        protected virtual IEnumerable<ArmadaCommand> ExtraCommands()
        {
            return Array.Empty<ArmadaCommand>();
        }

        /// <summary>
        /// Called once after columns and filters are built (load reference data, apply prefill).
        /// </summary>
        protected virtual void OnBuilt()
        {
        }

        /// <summary>
        /// Build a screen command.
        /// </summary>
        /// <param name="id">Command id.</param>
        /// <param name="title">English title.</param>
        /// <param name="handler">Handler.</param>
        /// <param name="enabled">Enablement, or null.</param>
        /// <param name="gesture">Key, or null.</param>
        /// <returns>Command.</returns>
        protected ArmadaCommand Cmd(string id, string title, Action handler, Func<bool>? enabled, string? gesture = null)
        {
            ArmadaCommand c = gesture != null
                ? new ArmadaCommand(id, title, CommandMenuEnum.Actions, handler, gesture)
                : new ArmadaCommand(id, title, CommandMenuEnum.Actions, handler);
            c.IsEnabled = enabled;
            c.Group = Title;
            return c;
        }

        /// <summary>
        /// Read every record matching the current filters by paging through <see cref="FetchPageAsync"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Records.</returns>
        protected virtual async Task<List<T>> FetchAllAsync(CancellationToken token)
        {
            List<T> all = new List<T>();
            for (int page = 1; page <= 100; page++)
            {
                GridQuery q = new GridQuery();
                q.PageNumber = page;
                q.PageSize = 500;
                GridPage<T> result = await FetchPageAsync(q, token).ConfigureAwait(false);
                all.AddRange(result.Rows);
                if (result.Rows.Count == 0 || all.Count >= result.TotalRecords || result.Rows.Count < q.PageSize) break;
            }

            return all;
        }

        /// <summary>
        /// A grid page from a server enumeration result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <returns>Page.</returns>
        protected static GridPage<T> PageOf(EnumerationResult<T>? result)
        {
            if (result == null) return new GridPage<T>();
            return new GridPage<T>(result.Objects ?? new List<T>(), result.TotalRecords);
        }

        /// <summary>
        /// A grid page cut from an in-memory list (endpoints without server paging).
        /// </summary>
        /// <param name="rows">All rows.</param>
        /// <param name="query">Query.</param>
        /// <returns>Page.</returns>
        protected static GridPage<T> Slice(List<T> rows, GridQuery query)
        {
            List<T> list = rows ?? new List<T>();
            PageWindow window = new PageWindow(query.PageNumber, query.PageSize, list.Count);
            return new GridPage<T>(list.Skip((window.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToList(), list.Count);
        }

        /// <summary>
        /// Case-insensitive "contains" across several values (the dashboard's search box).
        /// </summary>
        /// <param name="search">Search text (blank matches everything).</param>
        /// <param name="values">Values.</param>
        /// <returns>True on a match.</returns>
        protected static bool Matches(string search, params string?[] values)
        {
            if (String.IsNullOrWhiteSpace(search)) return true;
            string s = search.Trim();
            return values.Any(v => v != null && v.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Build (once): columns, filters, deep-link filter values, preferences, and events.
        /// </summary>
        protected void EnsureBuilt()
        {
            if (_Built) return;
            _Built = true;
            Grid.Localizer = Localizer;
            Grid.ApplyTheme(Theme);
            BuildColumns(Grid);
            BuildFilters(Filters);
            foreach (string key in Filters.Keys)
            {
                if (Route.Query.TryGetValue(key, out string? value)) Filters.SetValue(key, value);
            }

            TablePreferences prefs = Context.Prefs.Table(ScreenKey);
            Grid.Restore(prefs.SortColumn, prefs.SortDescending, prefs.PageSize);
            if (prefs.VisibleColumns != null) Grid.SetVisibleColumns(prefs.VisibleColumns);
            Grid.QueryChanged += (s, q) =>
            {
                TablePreferences tp = Context.Prefs.Table(ScreenKey);
                tp.SortColumn = q.SortKey;
                tp.SortDescending = q.SortDescending;
                tp.PageSize = q.PageSize;
                Context.Prefs.Save();
            };
            Grid.ColumnsChanged += (s, cols) =>
            {
                Context.Prefs.Table(ScreenKey).VisibleColumns = cols;
                Context.Prefs.Save();
            };
            Grid.Loader = LoadAsync;
            Grid.Activated += (s, row) => Open(row);
            Grid.MenuRequested += (s, row) => ShowRowMenu(row);
            Grid.EmptyText = "No records found.";
            Filters.Changed += (s, key) =>
            {
                _FilterSnapshot = Filters.Values();
                if (Grid.PageNumber != 1) Grid.GoToPage(1);
                else Grid.Reload();
            };
            Filters.Escaped += (s, e) => Scope.Focus(Grid);
            AddChild(Filters);
            AddChild(Grid);
            AddChild(Kpis);
            _FilterSnapshot = Filters.Values();
            Scope.Focus(Grid);
            OnBuilt();
        }

        #endregion

        #region Private-Methods

        private async Task<GridPage<T>> LoadAsync(GridQuery query, CancellationToken token)
        {
            GridPage<T> page;
            if (query.SortKey == null || ServerSorts(query.SortKey))
            {
                page = await FetchPageAsync(query, token).ConfigureAwait(false);
            }
            else
            {
                List<T> all = await FetchAllAsync(token).ConfigureAwait(false);
                List<T> sorted = SortLocal(all, query.SortKey, query.SortDescending);
                page = Slice(sorted, query);
            }

            Interlocked.Increment(ref _LoadCount);
            return page;
        }

        private List<T> SortLocal(List<T> rows, string sortKey, bool descending)
        {
            GridColumn<T>? column = Grid.Columns.FirstOrDefault(c => c.EffectiveSortKey == sortKey);
            NaturalTextComparer natural = new NaturalTextComparer();
            Comparison<T> compare = (a, b) =>
            {
                IComparable? va = SortValue(a, sortKey);
                IComparable? vb = SortValue(b, sortKey);
                int result;
                if (va != null || vb != null)
                {
                    if (va == null) result = -1;
                    else if (vb == null) result = 1;
                    else result = va.GetType() == vb.GetType() ? va.CompareTo(vb) : natural.Compare(va.ToString(), vb.ToString());
                }
                else
                {
                    string sa = column != null ? column.Value(a) ?? "" : "";
                    string sb = column != null ? column.Value(b) ?? "" : "";
                    result = natural.Compare(sa, sb);
                }

                return descending ? -result : result;
            };
            List<T> copy = new List<T>(rows);
            copy.Sort(compare);
            return copy;
        }

        private void ReloadKpis()
        {
            int generation = ++_KpiGeneration;
            Task.Run(async () =>
            {
                try
                {
                    List<KpiItem>? items = await FetchKpisAsync(CancellationToken.None).ConfigureAwait(false);
                    Context.Dispatcher.Post(() =>
                    {
                        if (generation == _KpiGeneration && items != null) Kpis.SetItems(items);
                    });
                }
                catch (Exception)
                {
                    // The grid reports load errors; the overview keeps its last values.
                }
            });
        }

        private void WithCurrent(Action<T> action)
        {
            T? current = Grid.Current;
            if (current != null) action(current);
        }

        #endregion
    }
}
