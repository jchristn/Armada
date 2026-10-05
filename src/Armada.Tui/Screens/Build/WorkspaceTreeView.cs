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
    /// The Workspace file tree (the dashboard's WorkspaceTree): folders load on demand and expand in place, files
    /// open in the editor, and entries can be marked for Plan, Dispatch, and context curation. <c>Enter</c> or
    /// <c>Right</c> opens or expands, <c>Left</c> collapses or moves to the parent, <c>Space</c> marks. Rows show
    /// <c>v</c>/<c>&gt;</c> for folders, <c>[x]</c> for marked entries, and <c>*</c> for the open file. Not thread-safe.
    /// </summary>
    public class WorkspaceTreeView : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Entries by folder path ("" is the root).
        /// </summary>
        public Dictionary<string, List<WorkspaceTreeEntry>> Entries { get; } = new Dictionary<string, List<WorkspaceTreeEntry>>(StringComparer.Ordinal);

        /// <summary>
        /// Expanded folders ("" is always expanded).
        /// </summary>
        public HashSet<string> Expanded { get; } = new HashSet<string>(StringComparer.Ordinal) { "" };

        /// <summary>
        /// Folders being loaded.
        /// </summary>
        public HashSet<string> Loading { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Marked paths, in the order they were marked.
        /// </summary>
        public List<string> Selected { get; } = new List<string>();

        /// <summary>
        /// The open file, or null.
        /// </summary>
        public string? ActivePath { get; set; } = null;

        /// <summary>
        /// Cursor row index among the visible rows.
        /// </summary>
        public int Cursor { get; private set; } = 0;

        /// <summary>
        /// Called to expand or collapse a folder.
        /// </summary>
        public Action<string>? ToggleDirectory { get; set; } = null;

        /// <summary>
        /// Called to open a file.
        /// </summary>
        public Action<string>? OpenFile { get; set; } = null;

        /// <summary>
        /// Text shown when the tree is empty.
        /// </summary>
        public string EmptyText { get; set; } = "Loading Workspace...";

        #endregion

        #region Private-Members

        private int _Scroll = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The visible rows in display order: entry and depth.
        /// </summary>
        /// <returns>Rows.</returns>
        public List<KeyValuePair<WorkspaceTreeEntry, int>> VisibleRows()
        {
            List<KeyValuePair<WorkspaceTreeEntry, int>> rows = new List<KeyValuePair<WorkspaceTreeEntry, int>>();
            Add(rows, "", 0);
            return rows;
        }

        /// <summary>
        /// The entry under the cursor, or null.
        /// </summary>
        public WorkspaceTreeEntry? Current
        {
            get
            {
                List<KeyValuePair<WorkspaceTreeEntry, int>> rows = VisibleRows();
                if (rows.Count == 0) return null;
                return rows[Math.Clamp(Cursor, 0, rows.Count - 1)].Key;
            }
        }

        /// <summary>
        /// Move the cursor to a path when it is visible.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>True when found.</returns>
        public bool Reveal(string path)
        {
            List<KeyValuePair<WorkspaceTreeEntry, int>> rows = VisibleRows();
            int idx = rows.FindIndex(r => r.Key.RelativePath == path);
            if (idx < 0) return false;
            Cursor = idx;
            return true;
        }

        /// <summary>
        /// Mark or unmark a path.
        /// </summary>
        /// <param name="path">Path.</param>
        public void ToggleSelect(string path)
        {
            string p = WorkspacePaths.Normalize(path);
            if (Selected.Contains(p)) Selected.Remove(p);
            else Selected.Add(p);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            // Alt+arrows are Back and Forward (as in ArmadaGrid); the tree only takes plain arrows.
            if ((key.Modifiers & KeyModifiers.Alt) != 0 && key.Code != KeyCode.Character) return false;
            List<KeyValuePair<WorkspaceTreeEntry, int>> rows = VisibleRows();
            if (rows.Count == 0) return false;
            Cursor = Math.Clamp(Cursor, 0, rows.Count - 1);
            WorkspaceTreeEntry current = rows[Cursor].Key;
            switch (key.Code)
            {
                case KeyCode.Up:
                    if (Cursor > 0) Cursor--;
                    return true;
                case KeyCode.Down:
                    if (Cursor < rows.Count - 1) Cursor++;
                    return true;
                case KeyCode.PageUp:
                    Cursor = Math.Max(0, Cursor - 10);
                    return true;
                case KeyCode.PageDown:
                    Cursor = Math.Min(rows.Count - 1, Cursor + 10);
                    return true;
                case KeyCode.Home:
                    Cursor = 0;
                    return true;
                case KeyCode.End:
                    Cursor = rows.Count - 1;
                    return true;
                case KeyCode.Enter:
                    Activate(current);
                    return true;
                case KeyCode.Right:
                    if (current.IsDirectory && !Expanded.Contains(current.RelativePath)) ToggleDirectory?.Invoke(current.RelativePath);
                    else if (!current.IsDirectory) OpenFile?.Invoke(current.RelativePath);
                    return true;
                case KeyCode.Left:
                    if (current.IsDirectory && Expanded.Contains(current.RelativePath))
                    {
                        ToggleDirectory?.Invoke(current.RelativePath);
                        return true;
                    }

                    string parent = WorkspacePaths.Parent(current.RelativePath);
                    if (parent.Length > 0) Reveal(parent);
                    return true;
                case KeyCode.Character:
                    if (key.Rune == ' ' && key.Modifiers == KeyModifiers.None)
                    {
                        ToggleSelect(current.RelativePath);
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
            List<KeyValuePair<WorkspaceTreeEntry, int>> rows = VisibleRows();
            int idx = _Scroll + mouse.Y;
            if (idx < 0 || idx >= rows.Count) return false;
            Cursor = idx;
            Activate(rows[idx].Key);
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            List<KeyValuePair<WorkspaceTreeEntry, int>> rows = VisibleRows();
            if (rows.Count == 0)
            {
                SurfaceText.Draw(surface, 0, 0, T(EmptyText), Theme.Muted, width);
                return;
            }

            Cursor = Math.Clamp(Cursor, 0, rows.Count - 1);
            if (Cursor < _Scroll) _Scroll = Cursor;
            if (Cursor >= _Scroll + height) _Scroll = Cursor - height + 1;
            _Scroll = Math.Clamp(_Scroll, 0, Math.Max(0, rows.Count - height));
            for (int row = 0; row < height; row++)
            {
                int idx = _Scroll + row;
                if (idx >= rows.Count) break;
                WorkspaceTreeEntry e = rows[idx].Key;
                int depth = rows[idx].Value;
                bool cursor = idx == Cursor;
                bool active = !e.IsDirectory && e.RelativePath == ActivePath;
                string marker = e.IsDirectory ? (Expanded.Contains(e.RelativePath) ? "v " : "> ") : "  ";
                string mark = Selected.Contains(e.RelativePath) ? "[x] " : "";
                string loading = Loading.Contains(e.RelativePath) ? " ..." : "";
                string text = new string(' ', depth * 2) + marker + mark + e.Name + (e.IsDirectory ? "/" : "") + (active ? " *" : "") + loading;
                CellStyle style = e.IsDirectory ? Theme.Accent : (e.IsEditable ? Theme.Text : Theme.Muted);
                if (cursor)
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

        private void Activate(WorkspaceTreeEntry entry)
        {
            if (entry.IsDirectory) ToggleDirectory?.Invoke(entry.RelativePath);
            else OpenFile?.Invoke(entry.RelativePath);
        }

        private void Add(List<KeyValuePair<WorkspaceTreeEntry, int>> rows, string folder, int depth)
        {
            if (!Entries.TryGetValue(folder, out List<WorkspaceTreeEntry>? entries) || entries == null) return;
            foreach (WorkspaceTreeEntry e in entries)
            {
                rows.Add(new KeyValuePair<WorkspaceTreeEntry, int>(e, depth));
                if (e.IsDirectory && Expanded.Contains(e.RelativePath)) Add(rows, e.RelativePath, depth + 1);
            }
        }

        #endregion
    }
}
