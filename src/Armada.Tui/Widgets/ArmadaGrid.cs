namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The table every Armada list uses (TUIKit gap U3: DataTable has equal-width columns, ordinal sort, no paging or
    /// multi-select). Features: fixed and proportional columns with minimum widths and CJK-safe text, server-side sort
    /// with <c>^</c>/<c>v</c> indicators, multi-select with a checkbox column (Space, Shift+Up/Down, Ctrl+A, Esc), per-cell
    /// styles, a column chooser with pinned columns (<c>c</c>), a paging bar ("Showing 1-25 of 248. Page 1 of 10.") with
    /// page sizes 10/25/50/100/250, empty, loading, and error states, and row virtualization (only visible rows are
    /// drawn). Data comes from an async <see cref="Loader"/> (server paging and sort) or from
    /// <see cref="SetLocalRows"/> (in-memory paging and sort). The cursor and marks follow row ids across reloads so
    /// background refreshes never move the user's place. Not thread-safe; loader results are posted to the UI loop.
    /// </summary>
    /// <typeparam name="T">Row type.</typeparam>
    public class ArmadaGrid<T> : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Columns in definition order. Never null.
        /// </summary>
        public List<GridColumn<T>> Columns { get; } = new List<GridColumn<T>>();

        /// <summary>
        /// Row id selector (selection and cursor persistence). Required for multi-select.
        /// </summary>
        public Func<T, string> IdSelector { get; set; }

        /// <summary>
        /// Show the checkbox column and allow marking rows. Default true.
        /// </summary>
        public bool MultiSelect { get; set; } = true;

        /// <summary>
        /// Allowed page sizes. Default 10, 25, 50, 100, 250.
        /// </summary>
        public IReadOnlyList<int> PageSizes { get; set; } = new List<int> { 10, 25, 50, 100, 250 };

        /// <summary>
        /// Page size. Default 25; clamped to 1..10000.
        /// </summary>
        public int PageSize
        {
            get { return _PageSize; }
            set { _PageSize = Math.Clamp(value, 1, 10000); }
        }

        /// <summary>
        /// Current 1-based page.
        /// </summary>
        public int PageNumber { get; private set; } = 1;

        /// <summary>
        /// Total records across pages.
        /// </summary>
        public long TotalRecords { get; private set; } = 0;

        /// <summary>
        /// Rows of the current page. Never null.
        /// </summary>
        public IReadOnlyList<T> Rows
        {
            get { return _Rows; }
        }

        /// <summary>
        /// Load state.
        /// </summary>
        public GridStateEnum State { get; private set; } = GridStateEnum.Ready;

        /// <summary>
        /// Last load error, or null.
        /// </summary>
        public string? ErrorMessage { get; private set; } = null;

        /// <summary>
        /// English text shown when there are no rows.
        /// </summary>
        public string EmptyText { get; set; } = "No records found.";

        /// <summary>
        /// Current sort key, or null.
        /// </summary>
        public string? SortKey { get; private set; } = null;

        /// <summary>
        /// Current sort direction.
        /// </summary>
        public bool SortDescending { get; private set; } = false;

        /// <summary>
        /// Visible column keys in display order (null uses each column's <see cref="GridColumn{T}.DefaultVisible"/>).
        /// </summary>
        public List<string>? VisibleColumnKeys { get; set; } = null;

        /// <summary>
        /// Ids of marked rows (multi-select). Never null.
        /// </summary>
        public HashSet<string> Marked { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Cursor index within <see cref="Rows"/> (-1 when empty).
        /// </summary>
        public int CursorIndex
        {
            get { return _Cursor; }
        }

        /// <summary>
        /// Row under the cursor, or default.
        /// </summary>
        public T? Current
        {
            get { return _Cursor >= 0 && _Cursor < _Rows.Count ? _Rows[_Cursor] : default; }
        }

        /// <summary>
        /// Index of the focused column among visible columns (header highlight; <c>s</c> sorts it).
        /// </summary>
        public int FocusedColumn
        {
            get { return _FocusedColumn; }
        }

        /// <summary>
        /// Async page loader for server paging and sort, or null for local rows.
        /// </summary>
        public Func<GridQuery, CancellationToken, Task<GridPage<T>>>? Loader { get; set; } = null;

        /// <summary>
        /// Dispatcher used to deliver loader results on the UI loop. Required with <see cref="Loader"/>.
        /// </summary>
        public IUiDispatcher? Dispatcher { get; set; } = null;

        /// <summary>
        /// Modal host for the column chooser.
        /// </summary>
        public IModalHost? ModalHost { get; set; } = null;

        /// <summary>
        /// Show the paging bar. Default true.
        /// </summary>
        public bool ShowPagingBar { get; set; } = true;

        /// <summary>
        /// Raised when the cursor row changes.
        /// </summary>
        public event EventHandler<T?>? CursorChanged;

        /// <summary>
        /// Raised when marks change.
        /// </summary>
        public event EventHandler<IReadOnlyCollection<string>>? SelectionChanged;

        /// <summary>
        /// Raised on Enter or double-click.
        /// </summary>
        public event EventHandler<T>? Activated;

        /// <summary>
        /// Raised on <c>.</c> or <c>Shift+F10</c> (row menu).
        /// </summary>
        public event EventHandler<T>? MenuRequested;

        /// <summary>
        /// Raised when sort, page, or page size change (persist preferences here).
        /// </summary>
        public event EventHandler<GridQuery>? QueryChanged;

        /// <summary>
        /// Raised when the visible columns change.
        /// </summary>
        public event EventHandler<List<string>>? ColumnsChanged;

        #endregion

        #region Private-Members

        private List<T> _Rows = new List<T>();
        private List<T>? _Local = null;
        private int _PageSize = 25;
        private int _Cursor = -1;
        private int _Scroll = 0;
        private int _FocusedColumn = 0;
        private int _Anchor = -1;
        private int _BodyHeight = 1;
        private int _HeaderY = 0;
        private int _LoadGeneration = 0;
        private CancellationTokenSource? _LoadCts = null;
        private List<int> _ColumnStarts = new List<int>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="idSelector">Row id selector.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="idSelector"/> is null.</exception>
        public ArmadaGrid(Func<T, string> idSelector)
        {
            IdSelector = idSelector ?? throw new ArgumentNullException(nameof(idSelector));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a column.
        /// </summary>
        /// <param name="column">Column.</param>
        /// <returns>The column.</returns>
        public GridColumn<T> AddColumn(GridColumn<T> column)
        {
            if (column == null) throw new ArgumentNullException(nameof(column));
            Columns.Add(column);
            return column;
        }

        /// <summary>
        /// Columns currently shown, in display order.
        /// </summary>
        /// <returns>Columns.</returns>
        public List<GridColumn<T>> VisibleColumns()
        {
            if (VisibleColumnKeys == null || VisibleColumnKeys.Count == 0)
                return Columns.Where(c => c.DefaultVisible || c.Pinned).ToList();
            List<GridColumn<T>> result = new List<GridColumn<T>>();
            foreach (GridColumn<T> pinned in Columns.Where(c => c.Pinned))
            {
                if (!VisibleColumnKeys.Contains(pinned.Key)) result.Add(pinned);
            }

            foreach (string key in VisibleColumnKeys)
            {
                GridColumn<T>? column = Columns.FirstOrDefault(c => c.Key == key);
                if (column != null && !result.Contains(column)) result.Add(column);
            }

            return result;
        }

        /// <summary>
        /// Use in-memory rows (local paging and sort).
        /// </summary>
        /// <param name="rows">Rows.</param>
        public void SetLocalRows(IEnumerable<T> rows)
        {
            _Local = (rows ?? Enumerable.Empty<T>()).ToList();
            State = GridStateEnum.Ready;
            ErrorMessage = null;
            ApplyLocal();
        }

        /// <summary>
        /// Show a page directly (callers that load data themselves).
        /// </summary>
        /// <param name="page">Page.</param>
        /// <param name="pageNumber">Page number.</param>
        public void SetPage(GridPage<T> page, int pageNumber)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            string? cursorId = CurrentId();
            _Rows = page.Rows ?? new List<T>();
            TotalRecords = Math.Max(page.TotalRecords, _Rows.Count);
            PageNumber = Math.Max(1, pageNumber);
            State = GridStateEnum.Ready;
            ErrorMessage = null;
            RestoreCursor(cursorId);
        }

        /// <summary>
        /// Show the loading state (rows stay visible while refreshing).
        /// </summary>
        public void SetLoading()
        {
            State = GridStateEnum.Loading;
        }

        /// <summary>
        /// Show the error state.
        /// </summary>
        /// <param name="message">Message.</param>
        public void SetError(string message)
        {
            State = GridStateEnum.Error;
            ErrorMessage = message ?? "";
        }

        /// <summary>
        /// Reload the current page (from the loader, or re-sort local rows).
        /// </summary>
        public void Reload()
        {
            if (_Local != null && Loader == null)
            {
                ApplyLocal();
                return;
            }

            if (Loader == null) return;
            _LoadCts?.Cancel();
            CancellationTokenSource cts = new CancellationTokenSource();
            _LoadCts = cts;
            int generation = ++_LoadGeneration;
            State = GridStateEnum.Loading;
            GridQuery query = CurrentQuery();
            Func<GridQuery, CancellationToken, Task<GridPage<T>>> loader = Loader;
            Task.Run(async () =>
            {
                try
                {
                    GridPage<T> page = await loader(query, cts.Token).ConfigureAwait(false);
                    Post(() =>
                    {
                        if (generation == _LoadGeneration) SetPage(page, query.PageNumber);
                    });
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a newer load.
                }
                catch (ArmadaApiException ex)
                {
                    Post(() =>
                    {
                        if (generation == _LoadGeneration) SetError(ex.Message);
                    });
                }
                catch (Exception ex)
                {
                    Post(() =>
                    {
                        if (generation == _LoadGeneration) SetError(ex.Message);
                    });
                }
            });
        }

        /// <summary>
        /// The query for the current page and sort.
        /// </summary>
        /// <returns>Query.</returns>
        public GridQuery CurrentQuery()
        {
            GridQuery q = new GridQuery();
            q.PageNumber = PageNumber;
            q.PageSize = PageSize;
            q.SortKey = SortKey;
            q.SortDescending = SortDescending;
            return q;
        }

        /// <summary>
        /// Sort by a column key (toggles direction when already sorted by it).
        /// </summary>
        /// <param name="key">Column key.</param>
        /// <param name="descending">Explicit direction, or null to toggle.</param>
        public void SortBy(string key, bool? descending = null)
        {
            GridColumn<T>? column = Columns.FirstOrDefault(c => c.Key == key);
            if (column == null || !column.Sortable) return;
            string sortKey = column.EffectiveSortKey;
            if (descending.HasValue) SortDescending = descending.Value;
            else SortDescending = String.Equals(SortKey, sortKey, StringComparison.Ordinal) && !SortDescending;
            SortKey = sortKey;
            PageNumber = 1;
            RaiseQueryChanged();
            Reload();
        }

        /// <summary>
        /// Restore sort and page size from preferences without loading.
        /// </summary>
        /// <param name="sortKey">Sort key.</param>
        /// <param name="descending">Descending.</param>
        /// <param name="pageSize">Page size, or null.</param>
        public void Restore(string? sortKey, bool descending, int? pageSize)
        {
            SortKey = sortKey;
            SortDescending = descending;
            if (pageSize.HasValue) PageSize = pageSize.Value;
        }

        /// <summary>
        /// Go to a page.
        /// </summary>
        /// <param name="page">1-based page.</param>
        public void GoToPage(int page)
        {
            PageWindow window = new PageWindow(page, PageSize, TotalRecords);
            if (window.PageNumber == PageNumber) return;
            PageNumber = window.PageNumber;
            _Cursor = -1;
            RaiseQueryChanged();
            Reload();
        }

        /// <summary>
        /// Advance to the next allowed page size.
        /// </summary>
        public void CyclePageSize()
        {
            List<int> sizes = PageSizes.ToList();
            int idx = sizes.IndexOf(PageSize);
            PageSize = sizes[(idx + 1) % sizes.Count];
            PageNumber = 1;
            RaiseQueryChanged();
            Reload();
        }

        /// <summary>
        /// Open the column chooser (pinned columns stay visible).
        /// </summary>
        /// <returns>The modal, or null without a host.</returns>
        public MultiPickerModal<string>? OpenColumnChooser()
        {
            if (ModalHost == null) return null;
            List<SelectOption<string>> options = Columns.Select(c =>
            {
                SelectOption<string> option = new SelectOption<string>(c.Key, T(c.Title), c.Pinned ? T("pinned") : "");
                option.Pinned = c.Pinned;
                return option;
            }).ToList();
            MultiPickerModal<string> picker = new MultiPickerModal<string>("Columns", options, VisibleColumns().Select(c => c.Key), Localizer, Theme);
            ModalHost.Show(picker, result =>
            {
                if (result is List<string> keys) SetVisibleColumns(keys);
            });
            return picker;
        }

        /// <summary>
        /// Set the visible columns (pinned columns are always kept) and raise <see cref="ColumnsChanged"/>.
        /// </summary>
        /// <param name="keys">Column keys in order.</param>
        public void SetVisibleColumns(IEnumerable<string> keys)
        {
            List<string> list = (keys ?? Enumerable.Empty<string>()).ToList();
            foreach (GridColumn<T> pinned in Columns.Where(c => c.Pinned))
            {
                if (!list.Contains(pinned.Key)) list.Insert(0, pinned.Key);
            }

            VisibleColumnKeys = list;
            _FocusedColumn = Math.Min(_FocusedColumn, Math.Max(0, VisibleColumns().Count - 1));
            EventHandler<List<string>>? handler = ColumnsChanged;
            if (handler != null) handler(this, list);
        }

        /// <summary>
        /// Mark or unmark the cursor row.
        /// </summary>
        public void ToggleMark()
        {
            string? id = CurrentId();
            if (id == null || !MultiSelect) return;
            if (!Marked.Remove(id)) Marked.Add(id);
            RaiseSelection();
        }

        /// <summary>
        /// Mark every row on the page, or clear them when all are marked.
        /// </summary>
        public void ToggleMarkPage()
        {
            if (!MultiSelect) return;
            List<string> ids = _Rows.Select(IdSelector).ToList();
            bool all = ids.Count > 0 && ids.All(Marked.Contains);
            foreach (string id in ids)
            {
                if (all) Marked.Remove(id);
                else Marked.Add(id);
            }

            RaiseSelection();
        }

        /// <summary>
        /// Clear all marks.
        /// </summary>
        /// <returns>True when anything was marked.</returns>
        public bool ClearMarks()
        {
            if (Marked.Count == 0) return false;
            Marked.Clear();
            RaiseSelection();
            return true;
        }

        /// <summary>
        /// Rows on the current page that are marked.
        /// </summary>
        /// <returns>Rows.</returns>
        public List<T> MarkedRows()
        {
            return _Rows.Where(r => Marked.Contains(IdSelector(r))).ToList();
        }

        /// <summary>
        /// Move the cursor to a row index.
        /// </summary>
        /// <param name="index">Index.</param>
        public void MoveCursor(int index)
        {
            if (_Rows.Count == 0)
            {
                SetCursor(-1);
                return;
            }

            SetCursor(Math.Clamp(index, 0, _Rows.Count - 1));
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool shift = (key.Modifiers & KeyModifiers.Shift) != 0;
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            switch (key.Code)
            {
                case KeyCode.Up:
                    return MoveBy(-1, shift);
                case KeyCode.Down:
                    return MoveBy(1, shift);
                case KeyCode.PageUp:
                    return MoveBy(-Math.Max(1, _BodyHeight - 1), shift);
                case KeyCode.PageDown:
                    return MoveBy(Math.Max(1, _BodyHeight - 1), shift);
                case KeyCode.Home:
                    MoveCursor(0);
                    return true;
                case KeyCode.End:
                    MoveCursor(_Rows.Count - 1);
                    return true;
                case KeyCode.Left:
                    _FocusedColumn = Math.Max(0, _FocusedColumn - 1);
                    return true;
                case KeyCode.Right:
                    _FocusedColumn = Math.Min(Math.Max(0, VisibleColumns().Count - 1), _FocusedColumn + 1);
                    return true;
                case KeyCode.Enter:
                    T? current = Current;
                    if (current != null && Activated != null) Activated(this, current);
                    return current != null;
                case KeyCode.Escape:
                    return ClearMarks();
                case KeyCode.F10:
                    if (!shift) return false;
                    return RequestMenu();
                case KeyCode.Character:
                    if (ctrl && Char.ToLowerInvariant((char)key.Rune) == 'a')
                    {
                        if (!MultiSelect) return false;
                        ToggleMarkPage();
                        return true;
                    }

                    if ((key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0) return false;
                    switch (key.Rune)
                    {
                        case ' ':
                            if (!MultiSelect) return false;
                            ToggleMark();
                            return true;
                        case '.':
                            return RequestMenu();
                        case 's':
                            SortFocused(null);
                            return true;
                        case 'S':
                            SortFocused(true);
                            return true;
                        case 'c':
                            OpenColumnChooser();
                            return ModalHost != null;
                        case '<':
                            GoToPage(PageNumber - 1);
                            return true;
                        case '>':
                            GoToPage(PageNumber + 1);
                            return true;
                        case 'z':
                            CyclePageSize();
                            return true;
                        default:
                            return false;
                    }

                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Wheel)
            {
                if (mouse.Button == MouseButton.WheelUp) return MoveBy(-3, false);
                if (mouse.Button == MouseButton.WheelDown) return MoveBy(3, false);
                return false;
            }

            if (mouse.Kind != MouseEventKind.Press || mouse.Button != MouseButton.Left) return false;
            if (mouse.Y == _HeaderY)
            {
                for (int i = _ColumnStarts.Count - 1; i >= 0; i--)
                {
                    if (mouse.X >= _ColumnStarts[i])
                    {
                        _FocusedColumn = i;
                        List<GridColumn<T>> cols = VisibleColumns();
                        if (i < cols.Count) SortBy(cols[i].Key);
                        return true;
                    }
                }

                return true;
            }

            int row = _Scroll + (mouse.Y - _HeaderY - 1);
            if (row < 0 || row >= _Rows.Count) return false;
            if (MultiSelect && mouse.X < 4)
            {
                MoveCursor(row);
                ToggleMark();
                return true;
            }

            MoveCursor(row);
            if (mouse.ClickCount >= 2)
            {
                T? current = Current;
                if (current != null && Activated != null) Activated(this, current);
            }

            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 4 || height < 2) return;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.GridRow);
            int pagingRows = ShowPagingBar ? 1 : 0;
            _HeaderY = 0;
            _BodyHeight = Math.Max(1, height - 1 - pagingRows);

            List<GridColumn<T>> cols = VisibleColumns();
            int checkWidth = MultiSelect ? 4 : 0;
            List<int> widths = LayoutColumns(cols, width - checkWidth);
            _ColumnStarts = new List<int>();
            int x = checkWidth;
            CellStyle header = Theme.GridHeader;
            SurfaceText.FillRow(surface, 0, 0, width, header);
            if (MultiSelect)
            {
                bool allMarked = _Rows.Count > 0 && _Rows.All(r => Marked.Contains(IdSelector(r)));
                SurfaceText.Draw(surface, 0, 0, allMarked ? "[x]" : "[ ]", header, 3);
            }

            for (int i = 0; i < cols.Count; i++)
            {
                if (widths[i] <= 0)
                {
                    _ColumnStarts.Add(x);
                    continue;
                }

                GridColumn<T> col = cols[i];
                string title = T(col.Title);
                if (SortKey != null && String.Equals(SortKey, col.EffectiveSortKey, StringComparison.Ordinal)) title += SortDescending ? " v" : " ^";
                CellStyle style = IsFocused && i == _FocusedColumn ? header.WithForeground(Theme.Accent.Foreground) : header;
                _ColumnStarts.Add(x);
                string cell = col.Align == CellAlignment.Right ? TextCells.PadLeft(title, widths[i]) : TextCells.PadRight(title, widths[i]);
                SurfaceText.Draw(surface, x, 0, cell, style, widths[i]);
                x += widths[i] + 1;
            }

            if (_Rows.Count == 0)
            {
                string message = State == GridStateEnum.Loading ? T("Loading...")
                    : State == GridStateEnum.Error ? "! " + T("Failed to load") + ": " + (ErrorMessage ?? "") + "  (F5 " + T("Retry") + ")"
                    : T(EmptyText);
                CellStyle style = State == GridStateEnum.Error ? Theme.Error : Theme.Muted;
                int my = Math.Min(height - 1 - pagingRows, 1 + _BodyHeight / 2);
                SurfaceText.Draw(surface, Math.Max(0, (width - TextCells.Width(message)) / 2), my, message, style, width);
            }
            else
            {
                if (_Cursor < 0) _Cursor = 0;
                if (_Cursor < _Scroll) _Scroll = _Cursor;
                if (_Cursor >= _Scroll + _BodyHeight) _Scroll = _Cursor - _BodyHeight + 1;
                _Scroll = Math.Clamp(_Scroll, 0, Math.Max(0, _Rows.Count - _BodyHeight));
                for (int r = 0; r < _BodyHeight; r++)
                {
                    int idx = _Scroll + r;
                    if (idx >= _Rows.Count) break;
                    T row = _Rows[idx];
                    int y = 1 + r;
                    string id = IdSelector(row);
                    bool marked = Marked.Contains(id);
                    bool cursor = idx == _Cursor;
                    CellStyle rowStyle = cursor ? (IsFocused ? Theme.GridCursor : Theme.SelectionInactive) : (idx % 2 == 1 ? Theme.GridRowAlt : Theme.GridRow);
                    SurfaceText.FillRow(surface, 0, y, width, rowStyle);
                    if (MultiSelect) SurfaceText.Draw(surface, 0, y, marked ? "[x]" : "[ ]", marked && !cursor ? rowStyle.WithForeground(Theme.GridMarked.Foreground) : rowStyle, 3);
                    int cx = checkWidth;
                    for (int i = 0; i < cols.Count; i++)
                    {
                        if (widths[i] <= 0) continue;
                        GridColumn<T> col = cols[i];
                        string text = SafeValue(col, row);
                        CellStyle cellStyle = rowStyle;
                        if (!cursor && col.Style != null)
                        {
                            CellStyle? custom = col.Style(row, Theme);
                            if (custom.HasValue) cellStyle = custom.Value.WithBackground(rowStyle.Background);
                        }

                        string cell = col.Align == CellAlignment.Right ? TextCells.PadLeft(text, widths[i]) : TextCells.PadRight(text, widths[i]);
                        SurfaceText.Draw(surface, cx, y, cell, cellStyle, widths[i]);
                        cx += widths[i] + 1;
                    }
                }
            }

            if (ShowPagingBar) RenderPagingBar(surface, height - 1, width);
        }

        /// <summary>
        /// Paging bar text for the current state ("Showing 1-25 of 248. Page 1 of 10.").
        /// </summary>
        /// <returns>Text.</returns>
        public string PagingText()
        {
            PageWindow window = new PageWindow(PageNumber, PageSize, TotalRecords);
            Dictionary<string, object?> args = new Dictionary<string, object?>(StringComparer.Ordinal);
            args["start"] = Localizer.FormatNumber(window.First);
            args["end"] = Localizer.FormatNumber(window.Last);
            args["total"] = Localizer.FormatNumber(window.TotalRecords);
            args["page"] = Localizer.FormatNumber(window.PageNumber);
            args["pages"] = Localizer.FormatNumber(window.TotalPages);
            return Localizer.T("Showing {{start}}-{{end}} of {{total}}. Page {{page}} of {{pages}}.", args);
        }

        #endregion

        #region Private-Methods

        private void RenderPagingBar(ISurface surface, int y, int width)
        {
            SurfaceText.FillRow(surface, 0, y, width, Theme.StatusBar);
            string left = PagingText();
            if (Marked.Count > 0) left += "  " + Localizer.T("{count, plural, one {# selected} other {# selected}}", LocalizationArgs.Of("count", Marked.Count));
            if (State == GridStateEnum.Loading && _Rows.Count > 0) left += "  " + T("Refreshing...");
            int used = SurfaceText.Draw(surface, 0, y, left, Theme.StatusBar, width);
            string right = T("Page size") + " " + PageSize + "  < >  z  c";
            int rw = TextCells.Width(right);
            if (used + rw + 2 <= width) SurfaceText.Draw(surface, width - rw, y, right, Theme.StatusBar.WithForeground(Theme.Muted.Foreground), rw);
        }

        private List<int> LayoutColumns(List<GridColumn<T>> cols, int available)
        {
            List<int> widths = cols.Select(c => 0).ToList();
            List<int> included = Enumerable.Range(0, cols.Count).ToList();
            while (true)
            {
                int gaps = Math.Max(0, included.Count - 1);
                int need = included.Sum(i => cols[i].Width ?? cols[i].MinWidth) + gaps;
                if (need <= available || included.Count <= 1) break;
                int drop = included.LastOrDefault(i => !cols[i].Pinned && i != _FocusedColumn);
                if (!included.Contains(drop) || (cols[drop].Pinned)) drop = included.Last();
                included.Remove(drop);
            }

            int fixedTotal = included.Where(i => cols[i].Width.HasValue).Sum(i => cols[i].Width!.Value);
            int gapTotal = Math.Max(0, included.Count - 1);
            int remaining = Math.Max(0, available - fixedTotal - gapTotal);
            List<int> flex = included.Where(i => !cols[i].Width.HasValue).ToList();
            int weightTotal = flex.Sum(i => cols[i].Weight);
            foreach (int i in included)
            {
                if (cols[i].Width.HasValue) widths[i] = Math.Min(cols[i].Width!.Value, available);
            }

            int assigned = 0;
            for (int k = 0; k < flex.Count; k++)
            {
                int i = flex[k];
                int share = weightTotal > 0 ? remaining * cols[i].Weight / weightTotal : 0;
                if (k == flex.Count - 1) share = remaining - assigned;
                widths[i] = Math.Max(cols[i].MinWidth, share);
                assigned += widths[i];
            }

            return widths;
        }

        private string SafeValue(GridColumn<T> column, T row)
        {
            try
            {
                return (column.Value(row) ?? "").Replace('\n', ' ').Replace('\r', ' ');
            }
            catch (Exception)
            {
                return "";
            }
        }

        private void ApplyLocal()
        {
            if (_Local == null) return;
            IEnumerable<T> rows = _Local;
            if (SortKey != null)
            {
                GridColumn<T>? column = Columns.FirstOrDefault(c => c.EffectiveSortKey == SortKey);
                if (column != null)
                {
                    IComparer<string> comparer = new NaturalTextComparer();
                    rows = SortDescending
                        ? rows.OrderByDescending(r => SafeValue(column, r), comparer)
                        : rows.OrderBy(r => SafeValue(column, r), comparer);
                }
            }

            List<T> all = rows.ToList();
            PageWindow window = new PageWindow(PageNumber, PageSize, all.Count);
            GridPage<T> page = new GridPage<T>(all.Skip((window.PageNumber - 1) * PageSize).Take(PageSize).ToList(), all.Count);
            SetPage(page, window.PageNumber);
        }

        private bool MoveBy(int delta, bool extend)
        {
            if (_Rows.Count == 0) return true;
            int from = _Cursor < 0 ? 0 : _Cursor;
            int to = Math.Clamp(from + delta, 0, _Rows.Count - 1);
            if (extend && MultiSelect)
            {
                if (_Anchor < 0) _Anchor = from;
                int lo = Math.Min(_Anchor, to);
                int hi = Math.Max(_Anchor, to);
                for (int i = lo; i <= hi; i++) Marked.Add(IdSelector(_Rows[i]));
                RaiseSelection();
            }
            else
            {
                _Anchor = -1;
            }

            SetCursor(to);
            return true;
        }

        private bool RequestMenu()
        {
            T? current = Current;
            if (current == null || MenuRequested == null) return false;
            MenuRequested(this, current);
            return true;
        }

        private void SortFocused(bool? reverse)
        {
            List<GridColumn<T>> cols = VisibleColumns();
            if (cols.Count == 0) return;
            GridColumn<T> col = cols[Math.Clamp(_FocusedColumn, 0, cols.Count - 1)];
            if (!col.Sortable) return;
            if (reverse == true) SortBy(col.Key, String.Equals(SortKey, col.EffectiveSortKey, StringComparison.Ordinal) ? !SortDescending : true);
            else SortBy(col.Key);
        }

        private void SetCursor(int index)
        {
            if (index == _Cursor) return;
            _Cursor = index;
            EventHandler<T?>? handler = CursorChanged;
            if (handler != null) handler(this, Current);
        }

        private string? CurrentId()
        {
            T? current = Current;
            return current != null ? IdSelector(current) : null;
        }

        private void RestoreCursor(string? id)
        {
            int idx = id == null ? -1 : _Rows.FindIndex(r => IdSelector(r) == id);
            if (idx >= 0) _Cursor = idx;
            else _Cursor = _Rows.Count == 0 ? -1 : Math.Clamp(_Cursor < 0 ? 0 : _Cursor, 0, _Rows.Count - 1);
            EventHandler<T?>? handler = CursorChanged;
            if (handler != null) handler(this, Current);
        }

        private void RaiseSelection()
        {
            EventHandler<IReadOnlyCollection<string>>? handler = SelectionChanged;
            if (handler != null) handler(this, Marked);
        }

        private void RaiseQueryChanged()
        {
            EventHandler<GridQuery>? handler = QueryChanged;
            if (handler != null) handler(this, CurrentQuery());
        }

        private void Post(Action action)
        {
            if (Dispatcher != null) Dispatcher.Post(action);
            else action();
        }

        #endregion
    }
}
