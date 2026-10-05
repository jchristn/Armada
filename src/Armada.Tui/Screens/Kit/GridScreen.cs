namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// A list screen: a <see cref="ScreenHeader"/> over an <see cref="ArmadaGrid{T}"/>, with optional KPI bar and
    /// filter strip in between. Wires the grid to the dispatcher, modal host, and saved table preferences (columns,
    /// sort, page size), and contributes the standard list commands: row actions (<c>.</c>), View JSON (<c>j</c>),
    /// Copy ID (<c>y</c>), New (<c>n</c>), and Delete (<c>Del</c>) where the screen supports them. Not thread-safe.
    /// </summary>
    /// <typeparam name="T">Row type.</typeparam>
    public abstract class GridScreen<T> : StackScreen where T : class
    {
        #region Public-Members

        /// <summary>
        /// Page header.
        /// </summary>
        public ScreenHeader Header { get; }

        /// <summary>
        /// The grid.
        /// </summary>
        public ArmadaGrid<T> Grid { get; }

        /// <summary>
        /// Table preference id.
        /// </summary>
        public string TableId { get; }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                hints.Add(new KeyValuePair<string, string>("Enter", "Open"));
                hints.Add(new KeyValuePair<string, string>(".", "Actions"));
                if (Grid.MultiSelect) hints.Add(new KeyValuePair<string, string>("Space", "Select"));
                hints.Add(new KeyValuePair<string, string>("j", "JSON"));
                if (CanCreate()) hints.Add(new KeyValuePair<string, string>("n", "New"));
                if (CanDeleteRows()) hints.Add(new KeyValuePair<string, string>("Del", "Delete"));
                return hints;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        /// <param name="title">English title.</param>
        /// <param name="subtitle">English subtitle.</param>
        /// <param name="tableId">Table preference id.</param>
        /// <param name="idSelector">Row id selector.</param>
        protected GridScreen(RouteMatch route, TuiContext context, string title, string subtitle, string tableId, Func<T, string> idSelector)
            : base(route, context)
        {
            TableId = tableId ?? throw new ArgumentNullException(nameof(tableId));
            Header = new ScreenHeader(title, subtitle);
            Grid = new ArmadaGrid<T>(idSelector);
            Grid.Dispatcher = context.Dispatcher;
            Grid.ModalHost = context.Modals;
            Grid.Activated += (s, row) => OnActivate(row);
            Grid.MenuRequested += (s, row) => ShowRowMenu(row);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return Refresh;
        }

        /// <summary>
        /// Reload the data (F5 and auto-refresh).
        /// </summary>
        public virtual void Refresh()
        {
            Grid.Reload();
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> commands = new List<ArmadaCommand>();
            string prefix = ScreenKey + ".";
            commands.Add(Command(prefix + "row-actions", "Row actions", () => { T? row = Grid.Current; if (row != null) ShowRowMenu(row); }, () => Grid.Current != null, "shift+f10"));
            commands.Add(Command(prefix + "json", "View JSON", () => { T? row = Grid.Current; if (row != null) ScreenOps.ShowJson(Context, JsonTitle(row), JsonFor(row)); }, () => Grid.Current != null, "j"));
            commands.Add(Command(prefix + "copy-id", "Copy ID", () => { T? row = Grid.Current; if (row != null) Context.Clipboard.Copy(Grid.IdSelector(row), "ID"); }, () => Grid.Current != null, "y"));
            commands.Add(Command(prefix + "columns", "Columns...", () => Grid.OpenColumnChooser(), null));
            if (SupportsCreate()) commands.Add(Command(prefix + "new", "New", OnCreate, CanCreate, "n"));
            if (SupportsDelete()) commands.Add(Command(prefix + "delete", "Delete", OnDeleteKey, CanDeleteRows, "Delete"));
            commands.AddRange(ExtraCommands());
            return commands;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build a screen command.
        /// </summary>
        /// <param name="id">Command id.</param>
        /// <param name="title">English title.</param>
        /// <param name="handler">Handler.</param>
        /// <param name="enabled">Enablement, or null.</param>
        /// <param name="gestures">Gestures.</param>
        /// <returns>Command.</returns>
        protected static ArmadaCommand Command(string id, string title, Action handler, Func<bool>? enabled, params string[] gestures)
        {
            ArmadaCommand command = new ArmadaCommand(id, title, CommandMenuEnum.Actions, handler, gestures ?? new string[0]);
            command.IsEnabled = enabled;
            return command;
        }

        /// <summary>
        /// Restore saved columns, sort, and page size, and save changes from now on. Call after adding columns.
        /// </summary>
        /// <param name="defaultSortKey">Default sort key, or null.</param>
        /// <param name="defaultDescending">Default sort direction.</param>
        /// <param name="defaultPageSize">Default page size.</param>
        protected void BindPreferences(string? defaultSortKey, bool defaultDescending, int defaultPageSize)
        {
            TablePreferences prefs = Context.Prefs.Table(TableId);
            Grid.Restore(prefs.SortColumn ?? defaultSortKey, prefs.SortColumn != null ? prefs.SortDescending : defaultDescending, prefs.PageSize ?? defaultPageSize);
            if (prefs.VisibleColumns != null && prefs.VisibleColumns.Count > 0) Grid.VisibleColumnKeys = new List<string>(prefs.VisibleColumns);
            Grid.QueryChanged += (s, q) =>
            {
                TablePreferences p = Context.Prefs.Table(TableId);
                p.SortColumn = q.SortKey;
                p.SortDescending = q.SortDescending;
                p.PageSize = q.PageSize;
                Context.Prefs.Save();
            };
            Grid.ColumnsChanged += (s, keys) =>
            {
                Context.Prefs.Table(TableId).VisibleColumns = new List<string>(keys);
                Context.Prefs.Save();
            };
        }

        /// <summary>
        /// Enter on a row (default: the row menu).
        /// </summary>
        /// <param name="row">Row.</param>
        protected virtual void OnActivate(T row)
        {
            ShowRowMenu(row);
        }

        /// <summary>
        /// Row menu items.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>Items.</returns>
        protected virtual IEnumerable<ActionMenuItem> RowMenu(T row)
        {
            return new List<ActionMenuItem>
            {
                new ActionMenuItem("View JSON", () => ScreenOps.ShowJson(Context, JsonTitle(row), JsonFor(row)), "j"),
            };
        }

        /// <summary>
        /// Show the row menu.
        /// </summary>
        /// <param name="row">Row.</param>
        protected void ShowRowMenu(T row)
        {
            List<ActionMenuItem> items = RowMenu(row).ToList();
            if (items.Count == 0) return;
            ActionMenu.Show(Context.Modals, "Actions", items, Context.Loc, Context.Theme.Current);
        }

        /// <summary>
        /// Value shown by View JSON.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>Value.</returns>
        protected virtual object? JsonFor(T row)
        {
            return row;
        }

        /// <summary>
        /// Title for View JSON.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>English title (ids appended).</returns>
        protected virtual string JsonTitle(T row)
        {
            return Context.Loc.T(Header.Title) + ": " + Grid.IdSelector(row);
        }

        /// <summary>
        /// True when the screen has a New action.
        /// </summary>
        /// <returns>True when supported.</returns>
        protected virtual bool SupportsCreate()
        {
            return false;
        }

        /// <summary>
        /// True when New is allowed now (role gating).
        /// </summary>
        /// <returns>True when allowed.</returns>
        protected virtual bool CanCreate()
        {
            return SupportsCreate();
        }

        /// <summary>
        /// New.
        /// </summary>
        protected virtual void OnCreate()
        {
        }

        /// <summary>
        /// True when the screen has a Delete action.
        /// </summary>
        /// <returns>True when supported.</returns>
        protected virtual bool SupportsDelete()
        {
            return false;
        }

        /// <summary>
        /// True when Delete is allowed now (role gating and a row or selection).
        /// </summary>
        /// <returns>True when allowed.</returns>
        protected virtual bool CanDeleteRows()
        {
            return SupportsDelete() && (Grid.Marked.Count > 0 || Grid.Current != null);
        }

        /// <summary>
        /// <c>Del</c>: delete the marked rows, or the cursor row when nothing is marked.
        /// </summary>
        protected virtual void OnDeleteKey()
        {
            if (Grid.Marked.Count > 0)
            {
                OnDeleteSelected();
                return;
            }

            T? row = Grid.Current;
            if (row != null) OnDeleteRow(row);
        }

        /// <summary>
        /// Delete one row (confirm first).
        /// </summary>
        /// <param name="row">Row.</param>
        protected virtual void OnDeleteRow(T row)
        {
        }

        /// <summary>
        /// Delete the marked rows (confirm first).
        /// </summary>
        protected virtual void OnDeleteSelected()
        {
        }

        /// <summary>
        /// Extra screen commands.
        /// </summary>
        /// <returns>Commands.</returns>
        protected virtual IEnumerable<ArmadaCommand> ExtraCommands()
        {
            return new List<ArmadaCommand>();
        }

        #endregion
    }
}
