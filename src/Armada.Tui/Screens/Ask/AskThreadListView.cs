namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The conversation list (W2.1, the dashboard's <c>AskThreadList</c>): New conversation, server-side search (300 ms
    /// debounce), Show archived, pinned threads first, unread badges (99+), a "Working" marker while tracked work is
    /// active, "Replying..." while a turn runs, an Archived tag, relative times, paging ("Load more", also automatic at
    /// the end), retry on error, inline rename, and a row menu (Rename, Pin/Unpin, Summarize, Archive/Unarchive,
    /// Delete). Keys: <c>Up</c>/<c>Down</c>/<c>Home</c>/<c>End</c>, <c>Enter</c> open, <c>n</c> new, <c>/</c> search,
    /// <c>A</c> show archived, <c>p</c> pin, <c>e</c> rename, <c>s</c> summarize, <c>Del</c> delete, <c>.</c> row menu,
    /// <c>R</c> retry. Not thread-safe.
    /// </summary>
    public class AskThreadListView : ArmadaWidget, IPasteTarget, ITextEntry
    {
        #region Public-Members

        /// <summary>
        /// True while the search field or an inline rename has focus: printable keys type into it (TUIKit's
        /// <see cref="ITextEntry"/>).
        /// </summary>
        public bool AcceptsText
        {
            get { return SearchFocused || RenamingId != null; }
        }

        /// <summary>
        /// Search field.
        /// </summary>
        public TextInput SearchInput { get; } = new TextInput();

        /// <summary>
        /// Inline rename field.
        /// </summary>
        public TextInput RenameInput { get; } = new TextInput();

        /// <summary>
        /// Cursor row (threads, then the Load more row).
        /// </summary>
        public int Cursor { get; private set; } = 0;

        /// <summary>
        /// The search field has focus.
        /// </summary>
        public bool SearchFocused { get; private set; } = false;

        /// <summary>
        /// Thread being renamed, or null.
        /// </summary>
        public string? RenamingId { get; private set; } = null;

        /// <summary>
        /// Raised when the list asks to be closed (narrow overlay, after opening a thread).
        /// </summary>
        public event EventHandler? CloseRequested;

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;
        private readonly AskController _Ask;
        private int _Top = 0;
        private int _RowsVisible = 5;
        private string? _CursorThreadId = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <param name="ask">Ask session.</param>
        public AskThreadListView(TuiContext context, AskController ask)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Ask = ask ?? throw new ArgumentNullException(nameof(ask));
            SearchInput.Placeholder = "Search conversations";
            SearchInput.Value = ask.Search;
            SearchInput.ValueChanged += (s, e) => _Ask.SetSearch(e.NewValue ?? "");
            RenameInput.MaxLength = 200;
            _CursorThreadId = ask.Conversation.ThreadId;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The thread under the cursor, or null (Load more row or empty list).
        /// </summary>
        /// <returns>Thread or null.</returns>
        public AskThread? Current()
        {
            List<AskThread> threads = _Ask.Threads;
            return Cursor >= 0 && Cursor < threads.Count ? threads[Cursor] : null;
        }

        /// <summary>
        /// Start renaming the thread under the cursor.
        /// </summary>
        /// <returns>True when renaming.</returns>
        public bool BeginRename()
        {
            AskThread? t = Current();
            if (t == null) return false;
            RenamingId = t.Id;
            RenameInput.Value = t.Title ?? "";
            RenameInput.ApplyTheme(Theme);
            RenameInput.Localizer = Localizer;
            RenameInput.OnFocusChanged(true);
            return true;
        }

        /// <summary>
        /// Open the row menu for the thread under the cursor.
        /// </summary>
        /// <returns>True when shown.</returns>
        public bool ShowRowMenu()
        {
            AskThread? t = Current();
            if (t == null) return false;
            List<ActionMenuItem> items = new List<ActionMenuItem>
            {
                new ActionMenuItem("Rename", () => BeginRename(), "e"),
                new ActionMenuItem(t.Pinned ? "Unpin" : "Pin", () => _Ask.TogglePin(t), "p"),
                new ActionMenuItem("Summarize", () => _Ask.Summarize(t), "s"),
                new ActionMenuItem(t.Archived ? "Unarchive" : "Archive", () => _Ask.ToggleArchive(t)),
                new ActionMenuItem("Delete", () => _Ask.Delete(t), "Del") { Destructive = true }
            };
            string title = _Context.Loc.T("Actions for {{title}}", Services.LocalizationArgs.Of("title", String.IsNullOrEmpty(t.Title) ? T("New conversation") : t.Title));
            ActionMenu.Show(_Context.Modals, title, items, _Context.Loc, Theme);
            return true;
        }

        /// <inheritdoc />
        public bool HandlePaste(string text)
        {
            if (RenamingId != null) return RenameInput.HandlePaste(text);
            if (!SearchFocused) FocusSearch();
            return SearchInput.HandlePaste(text);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (RenamingId != null) return HandleRenameKey(key);
            if (SearchFocused) return HandleSearchKey(key);
            List<AskThread> threads = _Ask.Threads;
            int rows = threads.Count + (_Ask.ListHasMore ? 1 : 0);
            bool plain = (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0;
            switch (key.Code)
            {
                case KeyCode.Up:
                    if (!plain) return false;
                    if (Cursor == 0)
                    {
                        FocusSearch();
                        return true;
                    }

                    MoveTo(Cursor - 1);
                    return true;
                case KeyCode.Down:
                    if (!plain) return false;
                    MoveTo(Math.Min(Math.Max(0, rows - 1), Cursor + 1));
                    return true;
                case KeyCode.PageUp:
                    MoveTo(Math.Max(0, Cursor - _RowsVisible));
                    return true;
                case KeyCode.PageDown:
                    MoveTo(Math.Min(Math.Max(0, rows - 1), Cursor + _RowsVisible));
                    return true;
                case KeyCode.Home:
                    MoveTo(0);
                    return true;
                case KeyCode.End:
                    MoveTo(Math.Max(0, rows - 1));
                    return true;
                case KeyCode.Enter:
                    if (!plain) return false;
                    return OpenCurrent();
                case KeyCode.Delete:
                    {
                        AskThread? t = Current();
                        if (t == null) return false;
                        _Ask.Delete(t);
                        return true;
                    }

                case KeyCode.Character:
                    if (!plain) return false;
                    return HandleChar((char)key.Rune);
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Wheel)
            {
                MoveTo(Math.Max(0, Cursor + (mouse.Button == MouseButton.WheelUp ? -1 : 1)));
                return true;
            }

            if (mouse.Kind != MouseEventKind.Press) return false;
            if (mouse.Y == 1)
            {
                FocusSearch();
                return true;
            }

            if (mouse.Y == 2)
            {
                _Ask.SetIncludeArchived(!_Ask.IncludeArchived);
                return true;
            }

            int row = (mouse.Y - 4) / 2 + _Top;
            if (mouse.Y >= 4 && row >= 0 && row < _Ask.Threads.Count + (_Ask.ListHasMore ? 1 : 0))
            {
                MoveTo(row);
                OpenCurrent();
            }

            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 8 || height < 4) return;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            SyncCursor();
            string head = T("Conversations").ToUpperInvariant();
            SurfaceText.Draw(surface, 0, 0, head, IsFocused ? Theme.Accent.WithAttribute(CellAttributes.Bold, true) : Theme.Muted, width);
            string newHint = "n " + T("New");
            SurfaceText.Draw(surface, Math.Max(TextCells.Width(head) + 1, width - TextCells.Width(newHint)), 0, newHint, Theme.Muted, width);
            SurfaceText.Draw(surface, 0, 1, "/", Theme.Muted, 1);
            SearchInput.ApplyTheme(Theme);
            SearchInput.Localizer = Localizer;
            SearchInput.Render(new SurfaceView(surface, new Rect(2, 1, width - 2, 1)));
            SurfaceText.Draw(surface, 0, 2, (_Ask.IncludeArchived ? "[x] " : "[ ] ") + T("Show archived") + " (A)", Theme.Muted, width);

            List<AskThread> threads = _Ask.Threads;
            int y = 4;
            if (_Ask.ListError != null)
            {
                foreach (string line in TextCells.Wrap("! " + T(_Ask.ListError) + "  (R " + T("Retry") + ")", width))
                {
                    if (y >= height) break;
                    SurfaceText.Draw(surface, 0, y++, line, Theme.Error, width);
                }

                return;
            }

            if (threads.Count == 0 && !_Ask.ListLoading)
            {
                string empty = _Ask.Search.Trim().Length > 0 ? T("No conversations match your search.") : T("No conversations yet. Start one to ask a question or kick off work.");
                foreach (string line in TextCells.Wrap(empty, width))
                {
                    if (y >= height) break;
                    SurfaceText.Draw(surface, 0, y++, line, Theme.Muted, width);
                }

                return;
            }

            int rows = threads.Count + (_Ask.ListHasMore ? 1 : 0);
            _RowsVisible = Math.Max(1, (height - 4 - (_Ask.ListLoading ? 1 : 0)) / 2);
            if (Cursor < _Top) _Top = Cursor;
            if (Cursor >= _Top + _RowsVisible) _Top = Cursor - _RowsVisible + 1;
            _Top = Math.Clamp(_Top, 0, Math.Max(0, rows - _RowsVisible));
            for (int i = _Top; i < rows && y + 1 < height + 1; i++)
            {
                bool cursor = i == Cursor;
                if (i >= threads.Count)
                {
                    CellStyle more = cursor && IsFocused ? Theme.Selection : Theme.Link;
                    if (cursor) SurfaceText.FillRow(surface, 0, y, width, more);
                    SurfaceText.Draw(surface, 0, y, "  [" + T("Load more") + "]", more, width);
                    y += 2;
                    continue;
                }

                RenderRow(surface, threads[i], cursor, y, width);
                y += 2;
                if (y >= height) break;
            }

            if (_Ask.ListLoading && height > 0) SurfaceText.Draw(surface, 0, height - 1, T("Loading conversations..."), Theme.Muted, width);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void OnFocusChangedCore(bool focused)
        {
            if (!focused)
            {
                SearchFocused = false;
                SearchInput.OnFocusChanged(false);
            }
        }

        #endregion

        #region Private-Methods

        private void RenderRow(ISurface surface, AskThread thread, bool cursor, int y, int width)
        {
            bool open = thread.Id == _Ask.Conversation.ThreadId;
            int unread = open ? 0 : thread.UnreadCount;
            CellStyle row = cursor && IsFocused ? Theme.Selection : cursor ? Theme.SelectionInactive : open ? Theme.Text.WithAttribute(CellAttributes.Bold, true) : Theme.Text;
            SurfaceText.FillRow(surface, 0, y, width, row);
            if (y + 1 < surface.Size.Height) SurfaceText.FillRow(surface, 0, y + 1, width, cursor ? row : Theme.Text);
            string marker = (open ? ">" : " ") + (thread.Pinned ? "^ " : "  ");
            if (RenamingId == thread.Id)
            {
                SurfaceText.Draw(surface, 0, y, marker, row, 3);
                RenameInput.Render(new SurfaceView(surface, new Rect(3, y, width - 3, 1)));
            }
            else
            {
                string badge = unread > 0 ? " " + (unread > 99 ? "99+" : unread.ToString(CultureInfo.InvariantCulture)) : "";
                string title = String.IsNullOrEmpty(thread.Title) ? T("New conversation") : thread.Title;
                SurfaceText.Draw(surface, 0, y, marker, row.WithForeground(Theme.Accent.Foreground), 3);
                SurfaceText.Draw(surface, 3, y, title, unread > 0 ? row.WithAttribute(CellAttributes.Bold, true) : row, width - 3 - TextCells.Width(badge));
                if (badge.Length > 0) SurfaceText.Draw(surface, width - TextCells.Width(badge), y, badge, row.WithForeground(Theme.Warning.Foreground).WithAttribute(CellAttributes.Bold, true), TextCells.Width(badge));
            }

            if (y + 1 >= surface.Size.Height) return;
            CellStyle sub = (cursor ? row : Theme.Text).WithForeground(Theme.Muted.Foreground);
            int x = 3;
            bool working = AskThreadListLogic.IsWorking(thread, _Ask.Activity);
            bool replying = AskThreadListLogic.IsReplying(thread, _Ask.Activity);
            if (working) x += SurfaceText.Draw(surface, x, y + 1, "* " + T("Working") + "  ", sub.WithForeground(Theme.Info.Foreground), width - x);
            else if (replying) x += SurfaceText.Draw(surface, x, y + 1, T("Replying...") + "  ", sub.WithForeground(Theme.Info.Foreground), width - x);
            if (thread.Archived) x += SurfaceText.Draw(surface, x, y + 1, "[" + T("Archived") + "]  ", sub, width - x);
            if (thread.LastMessageUtc != null) SurfaceText.Draw(surface, x, y + 1, _Context.Loc.FormatRelative(thread.LastMessageUtc.Value, _Context.Clock.UtcNow), sub, width - x);
        }

        private void SyncCursor()
        {
            List<AskThread> threads = _Ask.Threads;
            int rows = threads.Count + (_Ask.ListHasMore ? 1 : 0);
            if (_CursorThreadId == null) _CursorThreadId = _Ask.Conversation.ThreadId;
            if (_CursorThreadId != null)
            {
                int idx = threads.FindIndex(t => t.Id == _CursorThreadId);
                if (idx >= 0) Cursor = idx;
            }

            Cursor = Math.Clamp(Cursor, 0, Math.Max(0, rows - 1));
            if (Cursor < threads.Count) _CursorThreadId = threads[Cursor].Id;
        }

        private void MoveTo(int row)
        {
            int rows = _Ask.Threads.Count + (_Ask.ListHasMore ? 1 : 0);
            Cursor = Math.Clamp(row, 0, Math.Max(0, rows - 1));
            _CursorThreadId = Cursor < _Ask.Threads.Count ? _Ask.Threads[Cursor].Id : null;
            if (Cursor >= _Ask.Threads.Count && _Ask.ListHasMore) _Ask.LoadMore();
        }

        private bool OpenCurrent()
        {
            if (Cursor >= _Ask.Threads.Count)
            {
                _Ask.LoadMore();
                return true;
            }

            AskThread? t = Current();
            if (t == null) return false;
            _Context.Navigate("/ask/" + Uri.EscapeDataString(t.Id));
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private bool HandleChar(char c)
        {
            AskThread? t = Current();
            switch (c)
            {
                case 'n':
                    _Ask.NewConversation();
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    return true;
                case '/':
                    FocusSearch();
                    return true;
                case 'A':
                    _Ask.SetIncludeArchived(!_Ask.IncludeArchived);
                    return true;
                case 'R':
                    _Ask.LoadThreads(1);
                    return true;
                case 'p':
                    if (t == null) return false;
                    _Ask.TogglePin(t);
                    return true;
                case 'e':
                    return BeginRename();
                case 's':
                    if (t == null) return false;
                    _Ask.Summarize(t);
                    return true;
                case '.':
                    return ShowRowMenu();
                default:
                    return false;
            }
        }

        private void FocusSearch()
        {
            SearchFocused = true;
            SearchInput.OnFocusChanged(true);
        }

        private bool HandleSearchKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape || key.Code == KeyCode.Enter || key.Code == KeyCode.Down || key.Code == KeyCode.Tab)
            {
                SearchFocused = false;
                SearchInput.OnFocusChanged(false);
                return key.Code != KeyCode.Tab;
            }

            return SearchInput.HandleKey(key);
        }

        private bool HandleRenameKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                RenamingId = null;
                RenameInput.OnFocusChanged(false);
                return true;
            }

            if (key.Code == KeyCode.Enter)
            {
                string id = RenamingId!;
                RenamingId = null;
                RenameInput.OnFocusChanged(false);
                AskThread? t = _Ask.Threads.FirstOrDefault(x => x.Id == id);
                if (t != null) _Ask.Rename(t, RenameInput.Value);
                return true;
            }

            RenameInput.HandleKey(key);
            return true;
        }

        #endregion
    }
}
