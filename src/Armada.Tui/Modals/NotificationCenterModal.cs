namespace Armada.Tui.Modals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The notification center (<c>Ctrl+N</c> or the bell, W1.10): the latest 100 notifications with severity, title,
    /// message, relative time, and unread state. Enter opens the related item and marks it read; <c>m</c> marks all
    /// read; <c>x</c> clears (after confirmation by pressing it twice). Closes with the route to open, or null.
    /// Not thread-safe.
    /// </summary>
    public class NotificationCenterModal : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Cursor index into the history (newest first). Follows the selected entry when newer entries arrive while
        /// the center is open, so Enter always opens the entry that is highlighted.
        /// </summary>
        public int Cursor { get; private set; } = 0;

        /// <summary>
        /// Id of the highlighted entry, or null before the first entry is shown or after the history is cleared.
        /// </summary>
        public string? SelectedEntryId { get; private set; } = null;

        #endregion

        #region Private-Members

        private readonly NotificationService _Service;
        private readonly IClock _Clock;
        private int _Scroll = 0;
        private int _Height = 10;
        private bool _ClearArmed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">Notifications.</param>
        /// <param name="clock">Clock.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        public NotificationCenterModal(NotificationService service, IClock clock, ITextLocalizer localizer, ArmadaTheme theme)
            : base("Notifications", localizer, theme)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            FooterHint = " Enter " + T("Open") + "  m " + T("Mark all read") + "  x " + T("Clear") + "  Esc " + T("Close") + " ";
            MinContentWidth = 50;
            MaxContentWidth = 100;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            IReadOnlyList<NotificationEntry> items = _Service.History;
            Follow(items);
            bool clearKey = key.Code == KeyCode.Character && key.Rune == 'x' && key.Modifiers == KeyModifiers.None;
            if (!clearKey) _ClearArmed = false;
            switch (key.Code)
            {
                case KeyCode.Up: MoveTo(items, Math.Max(0, Cursor - 1)); return true;
                case KeyCode.Down: MoveTo(items, Math.Min(Math.Max(0, items.Count - 1), Cursor + 1)); return true;
                case KeyCode.Home: MoveTo(items, 0); return true;
                case KeyCode.End: MoveTo(items, Math.Max(0, items.Count - 1)); return true;
                case KeyCode.Enter:
                    if (Cursor < items.Count)
                    {
                        NotificationEntry entry = items[Cursor];
                        _Service.MarkRead(entry.Id);
                        RequestClose(entry.Route);
                    }

                    return true;
                case KeyCode.Character:
                    if (key.Rune == 'm')
                    {
                        _Service.MarkAllRead();
                        return true;
                    }

                    if (clearKey)
                    {
                        if (_ClearArmed)
                        {
                            _Service.Clear();
                            Cursor = 0;
                            SelectedEntryId = null;
                            _ClearArmed = false;
                        }
                        else
                        {
                            _ClearArmed = true;
                        }

                        return true;
                    }

                    return true;
                default:
                    return true;
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, 96);
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Max(4, Math.Min(24, _Service.History.Count * 2 + 2));
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            IReadOnlyList<NotificationEntry> items = _Service.History;
            string head = _Service.UnreadCount + " " + T("unread") + " / " + items.Count;
            if (_ClearArmed) head += "   " + T("Press x again to clear all notifications.");
            SurfaceText.Draw(content, 0, 0, head, _ClearArmed ? On(Theme.Warning) : Dim(), width);
            if (items.Count == 0)
            {
                SurfaceText.Draw(content, 0, 2, T("No notifications yet."), Dim(), width);
                return;
            }

            _Height = Math.Max(1, (content.Size.Height - 2) / 2);
            Follow(items);
            if (Cursor < _Scroll) _Scroll = Cursor;
            if (Cursor >= _Scroll + _Height) _Scroll = Cursor - _Height + 1;
            DateTime now = _Clock.UtcNow;
            for (int r = 0; r < _Height; r++)
            {
                int i = _Scroll + r;
                if (i >= items.Count) break;
                NotificationEntry n = items[i];
                int y = 2 + r * 2;
                bool cursor = i == Cursor;
                CellStyle sev = n.Severity switch
                {
                    NotificationSeverityEnum.Success => Theme.Success,
                    NotificationSeverityEnum.Warning => Theme.Warning,
                    NotificationSeverityEnum.Error => Theme.Error,
                    _ => Theme.Info
                };
                CellStyle row = cursor ? Theme.Selection : Body();
                SurfaceText.FillRow(content, 0, y, width, row);
                string marker = (n.Read ? "  " : "* ") + "[" + T(n.Severity.ToString()) + "] ";
                int x = SurfaceText.Draw(content, 0, y, marker, cursor ? row : On(sev), width);
                string when = Loc().FormatRelative(n.TimestampUtc, now);
                SurfaceText.Draw(content, x, y, _Service.RenderTitle(n), row, width - x - TextCells.Width(when) - 1);
                SurfaceText.Draw(content, Math.Max(0, width - TextCells.Width(when)), y, when, cursor ? row : Dim(), width);
                SurfaceText.Draw(content, 4, y + 1, _Service.Render(n), Dim(), width - 4);
            }
        }

        #endregion

        #region Private-Methods

        private ITextLocalizer Loc()
        {
            return Localizer;
        }

        private void Follow(IReadOnlyList<NotificationEntry> items)
        {
            if (items.Count == 0)
            {
                Cursor = 0;
                return;
            }

            if (SelectedEntryId != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (String.Equals(items[i].Id, SelectedEntryId, StringComparison.Ordinal))
                    {
                        Cursor = i;
                        return;
                    }
                }
            }

            MoveTo(items, Math.Clamp(Cursor, 0, items.Count - 1));
        }

        private void MoveTo(IReadOnlyList<NotificationEntry> items, int index)
        {
            Cursor = index;
            SelectedEntryId = index >= 0 && index < items.Count ? items[index].Id : null;
        }

        #endregion
    }
}
