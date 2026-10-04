namespace Armada.Tui.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The sidebar: Dashboard, Ask Armada, then the dashboard's sections and items with the same labels, collapsible
    /// sections (Enter or Space on a heading, Left collapses, Right expands), the current route highlighted, and the
    /// Needs You badge (Critical or Warning marker, capped at 99+). In compact mode it shows one-letter icons. Not
    /// thread-safe.
    /// </summary>
    public class SidebarView : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Show icons only (narrow terminals).
        /// </summary>
        public bool Compact { get; set; } = false;

        /// <summary>
        /// Cursor index into <see cref="Entries"/>.
        /// </summary>
        public int Cursor
        {
            get { return _Cursor; }
        }

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;
        private int _Cursor = 0;
        private int _Scroll = 0;
        private int _Height = 1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Context.</param>
        public SidebarView(TuiContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Visible rows (collapsed sections hide their items).
        /// </summary>
        /// <returns>Entries.</returns>
        public List<SidebarEntry> Entries()
        {
            List<SidebarEntry> list = new List<SidebarEntry>
            {
                new SidebarEntry(null, NavCatalog.Dashboard),
                new SidebarEntry(null, NavCatalog.AskArmada)
            };
            foreach (NavSection section in NavCatalog.Sections)
            {
                list.Add(new SidebarEntry(section, null));
                if (IsCollapsed(section) && !Compact) continue;
                foreach (NavItem item in section.Items) list.Add(new SidebarEntry(section, item));
            }

            return list;
        }

        /// <summary>
        /// True when a section is collapsed.
        /// </summary>
        /// <param name="section">Section.</param>
        /// <returns>True when collapsed.</returns>
        public bool IsCollapsed(NavSection section)
        {
            return _Context.Prefs.Current.CollapsedSections.TryGetValue(section.Key, out bool c) && c;
        }

        /// <summary>
        /// Toggle a section and persist.
        /// </summary>
        /// <param name="section">Section.</param>
        /// <param name="collapsed">State, or null to toggle.</param>
        public void SetCollapsed(NavSection section, bool? collapsed = null)
        {
            bool next = collapsed ?? !IsCollapsed(section);
            _Context.Prefs.Current.CollapsedSections[section.Key] = next;
            _Context.Prefs.Save();
        }

        /// <summary>
        /// Move the cursor to the current route's item.
        /// </summary>
        public void SyncToRoute()
        {
            NavItem? item = NavCatalog.ItemForPath(_Context.Router.Current?.Path);
            if (item == null) return;
            int idx = Entries().FindIndex(e => ReferenceEquals(e.Item, item));
            if (idx >= 0) _Cursor = idx;
        }

        /// <summary>
        /// The Needs You badge text ("3", "99+"), or empty.
        /// </summary>
        /// <returns>Badge.</returns>
        public string InboxBadge()
        {
            int count = _Context.Status.Inbox.Count;
            if (count <= 0) return "";
            return count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            List<SidebarEntry> entries = Entries();
            if (entries.Count == 0) return false;
            _Cursor = Math.Clamp(_Cursor, 0, entries.Count - 1);
            SidebarEntry current = entries[_Cursor];
            switch (key.Code)
            {
                case KeyCode.Up:
                    _Cursor = Math.Max(0, _Cursor - 1);
                    return true;
                case KeyCode.Down:
                    _Cursor = Math.Min(entries.Count - 1, _Cursor + 1);
                    return true;
                case KeyCode.Home:
                    _Cursor = 0;
                    return true;
                case KeyCode.End:
                    _Cursor = entries.Count - 1;
                    return true;
                case KeyCode.PageUp:
                    _Cursor = Math.Max(0, _Cursor - Math.Max(1, _Height - 1));
                    return true;
                case KeyCode.PageDown:
                    _Cursor = Math.Min(entries.Count - 1, _Cursor + Math.Max(1, _Height - 1));
                    return true;
                case KeyCode.Left:
                    if (current.Section != null)
                    {
                        SetCollapsed(current.Section, true);
                        _Cursor = Entries().FindIndex(e => e.IsHeading && e.Section == current.Section);
                        return true;
                    }

                    return false;
                case KeyCode.Right:
                    if (current.IsHeading && current.Section != null)
                    {
                        SetCollapsed(current.Section, false);
                        return true;
                    }

                    return false;
                case KeyCode.Enter:
                    Activate(current);
                    return true;
                case KeyCode.Character:
                    if (key.Rune == ' ' && key.Modifiers == KeyModifiers.None)
                    {
                        Activate(current);
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Wheel)
            {
                _Scroll = Math.Max(0, _Scroll + (mouse.Button == MouseButton.WheelUp ? -3 : 3));
                return true;
            }

            if (mouse.Kind != MouseEventKind.Press || mouse.Button != MouseButton.Left) return false;
            List<SidebarEntry> entries = Entries();
            int idx = _Scroll + mouse.Y;
            if (idx < 0 || idx >= entries.Count) return true;
            _Cursor = idx;
            Activate(entries[idx]);
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            _Height = height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Sidebar);
            List<SidebarEntry> entries = Entries();
            NavItem? current = NavCatalog.ItemForPath(_Context.Router.Current?.Path);
            _Cursor = Math.Clamp(_Cursor, 0, Math.Max(0, entries.Count - 1));
            if (_Cursor < _Scroll) _Scroll = _Cursor;
            if (_Cursor >= _Scroll + height) _Scroll = _Cursor - height + 1;
            _Scroll = Math.Clamp(_Scroll, 0, Math.Max(0, entries.Count - height));
            for (int r = 0; r < height; r++)
            {
                int i = _Scroll + r;
                if (i >= entries.Count) break;
                SidebarEntry e = entries[i];
                bool cursor = IsFocused && i == _Cursor;
                bool selected = e.Item != null && ReferenceEquals(e.Item, current);
                CellStyle style = cursor ? Theme.SidebarFocused : selected ? Theme.SidebarSelected : e.IsHeading ? Theme.SidebarSection : Theme.Sidebar;
                SurfaceText.FillRow(surface, 0, r, width, style);
                string text;
                if (Compact)
                {
                    text = e.IsHeading ? new string('-', Math.Max(1, width - 1)) : " " + e.Item!.Icon;
                }
                else if (e.IsHeading)
                {
                    text = (IsCollapsed(e.Section!) ? "+ " : "- ") + T(e.Section!.Label);
                }
                else
                {
                    text = (selected ? "> " : "  ") + (e.Section != null ? " " : "") + T(e.Item!.Label);
                }

                SurfaceText.Draw(surface, 0, r, text, style, width);
                if (!Compact && e.Item != null && e.Item.To == "/inbox")
                {
                    string badge = InboxBadge();
                    if (badge.Length > 0)
                    {
                        bool critical = _Context.Status.InboxCritical > 0;
                        string shown = (critical ? "!" : "") + badge;
                        CellStyle bs = cursor ? style : (critical ? Theme.Error : Theme.Warning).WithBackground(style.Background);
                        SurfaceText.Draw(surface, Math.Max(0, width - TextCells.Width(shown) - 1), r, shown, bs, width);
                    }
                }
            }
        }

        #endregion

        #region Private-Methods

        private void Activate(SidebarEntry entry)
        {
            if (entry.IsHeading && entry.Section != null)
            {
                SetCollapsed(entry.Section);
                return;
            }

            if (entry.Item != null) _Context.Navigate(entry.Item.To);
        }

        #endregion
    }
}
