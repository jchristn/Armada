namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The import wizard's Browse tree over the Admiral host's allowed import roots (the dashboard's BrowseTree):
    /// folders expand lazily (<c>Enter</c> or <c>Right</c>; <c>Left</c> collapses or moves to the parent), git
    /// repositories and worktrees are marked <c>[git]</c> and <c>[worktree]</c>, and <c>Space</c> adds a folder to
    /// the discovery input (worktrees only when allowed). Not thread-safe.
    /// </summary>
    public class ImportBrowseTree : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Root entries, or null before they load.
        /// </summary>
        public List<VesselBrowseEntry>? Roots { get; set; } = null;

        /// <summary>
        /// Loaded children by folder path.
        /// </summary>
        public Dictionary<string, List<VesselBrowseEntry>> Children { get; } = new Dictionary<string, List<VesselBrowseEntry>>(StringComparer.Ordinal);

        /// <summary>
        /// Expanded folder paths.
        /// </summary>
        public HashSet<string> Expanded { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Folders being loaded.
        /// </summary>
        public HashSet<string> Loading { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Load errors by folder path.
        /// </summary>
        public Dictionary<string, string> Errors { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Selected folders in the order they were chosen.
        /// </summary>
        public List<string> Selected { get; } = new List<string>();

        /// <summary>
        /// True when worktrees can be selected.
        /// </summary>
        public bool AllowWorktrees { get; set; } = false;

        /// <summary>
        /// Called to load a folder's children.
        /// </summary>
        public Action<string>? LoadFolder { get; set; } = null;

        /// <summary>
        /// Text while the roots load or when there are none.
        /// </summary>
        public string EmptyText { get; set; } = "Loading allowed roots...";

        /// <summary>
        /// Cursor row.
        /// </summary>
        public int Cursor { get; private set; } = 0;

        #endregion

        #region Private-Members

        private int _Scroll = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The entry under the cursor, or null.
        /// </summary>
        public VesselBrowseEntry? Current
        {
            get
            {
                List<KeyValuePair<VesselBrowseEntry, int>> rows = Rows();
                return rows.Count == 0 ? null : rows[Math.Clamp(Cursor, 0, rows.Count - 1)].Key;
            }
        }

        /// <summary>
        /// Visible rows: entry and depth.
        /// </summary>
        /// <returns>Rows.</returns>
        public List<KeyValuePair<VesselBrowseEntry, int>> Rows()
        {
            List<KeyValuePair<VesselBrowseEntry, int>> rows = new List<KeyValuePair<VesselBrowseEntry, int>>();
            if (Roots != null) Add(rows, Roots, 0);
            return rows;
        }

        /// <summary>
        /// Toggle a folder's selection when it can be selected.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <returns>True when the selection changed.</returns>
        public bool Toggle(VesselBrowseEntry entry)
        {
            if (entry == null) return false;
            if (Selected.Contains(entry.Path))
            {
                Selected.Remove(entry.Path);
                return true;
            }

            if (entry.IsWorktree && !AllowWorktrees) return false;
            Selected.Add(entry.Path);
            return true;
        }

        /// <summary>
        /// Expand or collapse a folder.
        /// </summary>
        /// <param name="entry">Entry.</param>
        public void ToggleExpand(VesselBrowseEntry entry)
        {
            if (entry == null || !entry.HasSubdirectories || entry.IsGitRepository) return;
            if (Expanded.Contains(entry.Path))
            {
                Expanded.Remove(entry.Path);
                return;
            }

            Expanded.Add(entry.Path);
            if (!Children.ContainsKey(entry.Path) && !Loading.Contains(entry.Path)) LoadFolder?.Invoke(entry.Path);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            List<KeyValuePair<VesselBrowseEntry, int>> rows = Rows();
            if (rows.Count == 0) return false;
            Cursor = Math.Clamp(Cursor, 0, rows.Count - 1);
            VesselBrowseEntry current = rows[Cursor].Key;
            switch (key.Code)
            {
                case KeyCode.Up:
                    if (Cursor == 0) return false;
                    Cursor--;
                    return true;
                case KeyCode.Down:
                    if (Cursor >= rows.Count - 1) return false;
                    Cursor++;
                    return true;
                case KeyCode.PageUp:
                    Cursor = Math.Max(0, Cursor - 10);
                    return true;
                case KeyCode.PageDown:
                    Cursor = Math.Min(rows.Count - 1, Cursor + 10);
                    return true;
                case KeyCode.Enter:
                case KeyCode.Right:
                    if (current.HasSubdirectories && !current.IsGitRepository)
                    {
                        if (key.Code == KeyCode.Right && Expanded.Contains(current.Path)) return true;
                        ToggleExpand(current);
                    }
                    else if (key.Code == KeyCode.Enter)
                    {
                        Toggle(current);
                    }

                    return true;
                case KeyCode.Left:
                    if (Expanded.Contains(current.Path))
                    {
                        Expanded.Remove(current.Path);
                        return true;
                    }

                    int depth = rows[Cursor].Value;
                    for (int i = Cursor - 1; i >= 0; i--)
                    {
                        if (rows[i].Value < depth)
                        {
                            Cursor = i;
                            break;
                        }
                    }

                    return true;
                case KeyCode.Character:
                    if (key.Rune == ' ' && key.Modifiers == KeyModifiers.None)
                    {
                        Toggle(current);
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
            if (mouse.Kind != MouseEventKind.Press) return false;
            List<KeyValuePair<VesselBrowseEntry, int>> rows = Rows();
            int idx = _Scroll + mouse.Y;
            if (idx < 0 || idx >= rows.Count) return false;
            Cursor = idx;
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            List<KeyValuePair<VesselBrowseEntry, int>> rows = Rows();
            if (rows.Count == 0)
            {
                SurfaceText.Draw(surface, 0, 0, T(EmptyText), Theme.Muted, width);
                return;
            }

            Cursor = Math.Clamp(Cursor, 0, rows.Count - 1);
            if (Cursor < _Scroll) _Scroll = Cursor;
            if (Cursor >= _Scroll + height) _Scroll = Cursor - height + 1;
            for (int row = 0; row < height; row++)
            {
                int idx = _Scroll + row;
                if (idx >= rows.Count) break;
                VesselBrowseEntry e = rows[idx].Key;
                int depth = rows[idx].Value;
                bool expandable = e.HasSubdirectories && !e.IsGitRepository;
                string marker = expandable ? (Expanded.Contains(e.Path) ? "v " : "> ") : "  ";
                bool disabled = e.IsWorktree && !AllowWorktrees && !Selected.Contains(e.Path);
                string check = Selected.Contains(e.Path) ? "[x] " : disabled ? "[-] " : "[ ] ";
                string tags = (e.IsGitRepository ? "  [" + T("Git repository") + "]" : "") + (e.IsWorktree ? "  [" + T("Worktree") + "]" : "");
                string status = Loading.Contains(e.Path) ? "  " + T("Loading...") : Errors.TryGetValue(e.Path, out string? err) ? "  ! " + err : "";
                string text = new string(' ', depth * 2) + marker + check + e.Name + tags + status;
                CellStyle style = e.IsGitRepository ? Theme.Success : e.IsWorktree ? Theme.Warning : Theme.Text;
                if (disabled) style = Theme.Muted;
                if (idx == Cursor)
                {
                    CellStyle cur = IsFocused ? Theme.GridCursor : Theme.SelectionInactive;
                    SurfaceText.FillRow(surface, 0, row, width, cur);
                    style = style.WithBackground(cur.Background);
                }

                SurfaceText.Draw(surface, 0, row, text, style, width);
            }
        }

        #endregion

        #region Private-Methods

        private void Add(List<KeyValuePair<VesselBrowseEntry, int>> rows, List<VesselBrowseEntry> entries, int depth)
        {
            foreach (VesselBrowseEntry e in entries)
            {
                rows.Add(new KeyValuePair<VesselBrowseEntry, int>(e, depth));
                if (Expanded.Contains(e.Path) && Children.TryGetValue(e.Path, out List<VesselBrowseEntry>? children) && children != null) Add(rows, children, depth + 1);
            }
        }

        #endregion
    }
}
