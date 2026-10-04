namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// An editable list of records inside a form (verification definitions, runbook steps and parameters, pipeline
    /// stages, persona overrides, required inputs): one summary line per record, <c>a</c> (or <c>Insert</c>) adds,
    /// <c>Enter</c> (or <c>e</c>) edits, <c>Del</c> removes, <c>Alt+Up</c>/<c>Alt+Down</c> reorder, and <c>Space</c>
    /// runs <see cref="Toggle"/> when set (checklists). Editing is delegated to <see cref="Editor"/>, which normally
    /// opens a form dialog and calls back with the edited record. Takes part in dirty tracking through
    /// <see cref="Fingerprint"/>. Not thread-safe.
    /// </summary>
    /// <typeparam name="T">Record type.</typeparam>
    public class RecordListField<T> : ArmadaWidget, IFormField where T : class
    {
        #region Public-Members

        /// <summary>
        /// Records in order. Never null.
        /// </summary>
        public List<T> Items { get; private set; } = new List<T>();

        /// <summary>
        /// One-line summary of a record.
        /// </summary>
        public Func<T, string> Describe { get; set; }

        /// <summary>
        /// Stable text used for dirty tracking; defaults to <see cref="Describe"/>.
        /// </summary>
        public Func<T, string>? Fingerprint { get; set; } = null;

        /// <summary>
        /// Opens an editor for a record (null to create one) and calls back with the result. Null makes the list
        /// read-only for add and edit.
        /// </summary>
        public Action<T?, Action<T>>? Editor { get; set; } = null;

        /// <summary>
        /// Optional toggle (Space) that returns the toggled record, for checklists.
        /// </summary>
        public Func<T, T>? Toggle { get; set; } = null;

        /// <summary>
        /// Allow adding. Default true.
        /// </summary>
        public bool AllowAdd { get; set; } = true;

        /// <summary>
        /// Allow removing. Default true.
        /// </summary>
        public bool AllowRemove { get; set; } = true;

        /// <summary>
        /// Allow reordering. Default true.
        /// </summary>
        public bool AllowReorder { get; set; } = true;

        /// <summary>
        /// Read-only lists only move the cursor.
        /// </summary>
        public bool ReadOnly { get; set; } = false;

        /// <summary>
        /// English text shown when empty.
        /// </summary>
        public string EmptyText { get; set; } = "No items yet.";

        /// <summary>
        /// Validator returning an English error or null.
        /// </summary>
        public Func<IReadOnlyList<T>, string?>? Validator { get; set; } = null;

        /// <summary>
        /// Cursor index (-1 when empty).
        /// </summary>
        public int Cursor
        {
            get { return Items.Count == 0 ? -1 : Math.Clamp(_Cursor, 0, Items.Count - 1); }
        }

        /// <summary>
        /// The record under the cursor, or null.
        /// </summary>
        public T? Current
        {
            get { return Cursor >= 0 ? Items[Cursor] : null; }
        }

        /// <inheritdoc />
        public object? FieldValue
        {
            get
            {
                Func<T, string> print = Fingerprint ?? Describe;
                return String.Join("\u001f", Items.Select(i => print(i)));
            }
        }

        /// <inheritdoc />
        public string? FieldError { get; private set; } = null;

        /// <summary>
        /// Raised after the list changes.
        /// </summary>
        public event EventHandler? Changed;

        #endregion

        #region Private-Members

        private int _Cursor = 0;
        private int _Scroll = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="describe">One-line summary of a record.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="describe"/> is null.</exception>
        public RecordListField(Func<T, string> describe)
        {
            Describe = describe ?? throw new ArgumentNullException(nameof(describe));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the records without raising <see cref="Changed"/>.
        /// </summary>
        /// <param name="items">Records.</param>
        public void SetItems(IEnumerable<T>? items)
        {
            Items = items != null ? items.ToList() : new List<T>();
            _Cursor = Math.Clamp(_Cursor, 0, Math.Max(0, Items.Count - 1));
        }

        /// <summary>
        /// Add a record through the editor.
        /// </summary>
        /// <returns>True when the editor opened.</returns>
        public bool AddNew()
        {
            if (ReadOnly || !AllowAdd || Editor == null) return false;
            Editor(null, created =>
            {
                if (created == null) return;
                Items.Add(created);
                _Cursor = Items.Count - 1;
                RaiseChanged();
            });
            return true;
        }

        /// <summary>
        /// Edit the record under the cursor through the editor.
        /// </summary>
        /// <returns>True when the editor opened.</returns>
        public bool EditCurrent()
        {
            T? current = Current;
            if (ReadOnly || current == null || Editor == null) return false;
            int index = Cursor;
            Editor(current, edited =>
            {
                if (edited == null || index >= Items.Count) return;
                Items[index] = edited;
                RaiseChanged();
            });
            return true;
        }

        /// <summary>
        /// Remove the record under the cursor.
        /// </summary>
        /// <returns>True when removed.</returns>
        public bool RemoveCurrent()
        {
            if (ReadOnly || !AllowRemove || Cursor < 0) return false;
            Items.RemoveAt(Cursor);
            _Cursor = Math.Clamp(_Cursor, 0, Math.Max(0, Items.Count - 1));
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Move the record under the cursor up or down.
        /// </summary>
        /// <param name="down">Move down.</param>
        /// <returns>True when moved.</returns>
        public bool MoveCurrent(bool down)
        {
            if (ReadOnly || !AllowReorder || Cursor < 0) return false;
            int from = Cursor;
            int to = from + (down ? 1 : -1);
            if (to < 0 || to >= Items.Count) return true;
            T item = Items[from];
            Items.RemoveAt(from);
            Items.Insert(to, item);
            _Cursor = to;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Run <see cref="Toggle"/> on the record under the cursor.
        /// </summary>
        /// <returns>True when toggled.</returns>
        public bool ToggleCurrent()
        {
            T? current = Current;
            if (ReadOnly || current == null || Toggle == null) return false;
            Items[Cursor] = Toggle(current);
            RaiseChanged();
            return true;
        }

        /// <inheritdoc />
        public bool ValidateField()
        {
            FieldError = Validator != null ? Validator(Items) : null;
            return FieldError == null;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool alt = (key.Modifiers & KeyModifiers.Alt) != 0;
            bool plain = (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0;
            switch (key.Code)
            {
                case KeyCode.Up:
                    if (alt) return MoveCurrent(false);
                    if (Cursor <= 0) return false;
                    _Cursor = Cursor - 1;
                    return true;
                case KeyCode.Down:
                    if (alt) return MoveCurrent(true);
                    if (Cursor < 0 || Cursor >= Items.Count - 1) return false;
                    _Cursor = Cursor + 1;
                    return true;
                case KeyCode.Home:
                    _Cursor = 0;
                    return true;
                case KeyCode.End:
                    _Cursor = Math.Max(0, Items.Count - 1);
                    return true;
                case KeyCode.Enter:
                    if (Toggle != null && Editor == null) return ToggleCurrent();
                    return EditCurrent() || AddNew();
                case KeyCode.Insert:
                    return AddNew();
                case KeyCode.Delete:
                    return RemoveCurrent();
                case KeyCode.Character:
                    if (!plain) return false;
                    if (key.Rune == ' ' && Toggle != null) return ToggleCurrent();
                    if (key.Rune == 'a' || key.Rune == '+') return AddNew();
                    if (key.Rune == 'e') return EditCurrent();
                    if (key.Rune == '-') return RemoveCurrent();
                    return false;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return new Size(available.Width, Math.Min(available.Height, Math.Max(2, Items.Count + 1)));
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 4 || height < 1) return;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            int footer = height >= 2 && !ReadOnly ? 1 : 0;
            int body = height - footer;
            if (Items.Count == 0)
            {
                SurfaceText.Draw(surface, 0, 0, T(EmptyText), Theme.Muted, width);
            }
            else
            {
                int cursor = Cursor;
                if (cursor < _Scroll) _Scroll = cursor;
                if (cursor >= _Scroll + body) _Scroll = cursor - body + 1;
                _Scroll = Math.Clamp(_Scroll, 0, Math.Max(0, Items.Count - body));
                for (int r = 0; r < body; r++)
                {
                    int idx = _Scroll + r;
                    if (idx >= Items.Count) break;
                    bool isCursor = idx == cursor;
                    CellStyle style = isCursor ? (IsFocused ? Theme.GridCursor : Theme.SelectionInactive) : Theme.Text;
                    SurfaceText.FillRow(surface, 0, r, width, style);
                    string line = (idx + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " + Describe(Items[idx]);
                    SurfaceText.Draw(surface, 0, r, line, style, width);
                }
            }

            if (footer == 1)
            {
                List<string> hints = new List<string>();
                if (Toggle != null) hints.Add("Space " + T("Toggle"));
                if (AllowAdd && Editor != null) hints.Add("a " + T("Add"));
                if (Editor != null) hints.Add("Enter " + T("Edit"));
                if (AllowRemove) hints.Add("Del " + T("Remove"));
                if (AllowReorder) hints.Add("Alt+Up/Down " + T("Move"));
                SurfaceText.Draw(surface, 0, height - 1, TextCells.Truncate(String.Join("  ", hints), width), Theme.Muted, width);
            }
        }

        #endregion

        #region Private-Methods

        private void RaiseChanged()
        {
            if (FieldError != null) ValidateField();
            EventHandler? handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        #endregion
    }
}
