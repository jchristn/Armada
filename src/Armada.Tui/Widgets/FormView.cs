namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Create and edit forms: labeled fields in sections, inline validation messages, dirty tracking against a clean
    /// snapshot, Save and Discard buttons, <c>Ctrl+S</c> to save, <c>Esc</c> to discard, Tab and Up/Down between fields,
    /// and vertical scrolling that keeps the focused field visible. Not thread-safe.
    /// </summary>
    public class FormView : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Rows in order. Never null.
        /// </summary>
        public IReadOnlyList<FormRow> Rows
        {
            get { return _Rows; }
        }

        /// <summary>
        /// Show the Save and Discard buttons. Default true.
        /// </summary>
        public bool ShowButtons { get; set; } = true;

        /// <summary>
        /// Save button.
        /// </summary>
        public Button SaveButton { get; }

        /// <summary>
        /// Discard button.
        /// </summary>
        public Button DiscardButton { get; }

        /// <summary>
        /// True when any field differs from the clean snapshot.
        /// </summary>
        public bool IsDirty
        {
            get
            {
                List<object?> current = Snapshot();
                if (current.Count != _Clean.Count) return true;
                for (int i = 0; i < current.Count; i++)
                {
                    if (!Equals(current[i], _Clean[i])) return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Raised for Save (button or Ctrl+S) after validation passes.
        /// </summary>
        public event EventHandler? SaveRequested;

        /// <summary>
        /// Raised for Discard (button or Esc).
        /// </summary>
        public event EventHandler? DiscardRequested;

        #endregion

        #region Private-Members

        private readonly List<FormRow> _Rows = new List<FormRow>();
        private List<object?> _Clean = new List<object?>();
        private int _Scroll = 0;
        private ButtonRow _ButtonRow = new ButtonRow();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FormView()
        {
            SaveButton = new Button("Save", RequestSave);
            SaveButton.Hint = "Ctrl+S";
            DiscardButton = new Button("Discard", RequestDiscard);
            _ButtonRow.Add(SaveButton);
            _ButtonRow.Add(DiscardButton);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a section heading.
        /// </summary>
        /// <param name="title">English title.</param>
        public void AddSection(string title)
        {
            _Rows.Add(new FormRow(title, null));
        }

        /// <summary>
        /// Add a labeled field.
        /// </summary>
        /// <typeparam name="T">Field type.</typeparam>
        /// <param name="label">English label.</param>
        /// <param name="field">Field widget.</param>
        /// <param name="hint">English hint, or null.</param>
        /// <param name="height">Rows the field occupies.</param>
        /// <returns>The field.</returns>
        public T AddField<T>(string label, T field, string? hint = null, int height = 1) where T : IWidget
        {
            FormRow row = new FormRow(label, field, hint);
            row.Height = height;
            _Rows.Add(row);
            AddChild(field);
            return field;
        }

        /// <summary>
        /// Record the current values as clean (after loading or saving).
        /// </summary>
        public void MarkClean()
        {
            _Clean = Snapshot();
        }

        /// <summary>
        /// Validate every field; focuses the first invalid field.
        /// </summary>
        /// <returns>True when all are valid.</returns>
        public bool ValidateAll()
        {
            bool ok = true;
            IWidget? firstBad = null;
            foreach (FormRow row in _Rows)
            {
                if (IsHidden(row)) continue;
                if (row.Field is IFormField field && !field.ValidateField())
                {
                    ok = false;
                    if (firstBad == null) firstBad = row.Field;
                }
            }

            if (firstBad != null) Scope.Focus(firstBad);
            return ok;
        }

        /// <summary>
        /// Validate and raise <see cref="SaveRequested"/>.
        /// </summary>
        public void RequestSave()
        {
            if (!ValidateAll()) return;
            EventHandler? handler = SaveRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// Raise <see cref="DiscardRequested"/>.
        /// </summary>
        public void RequestDiscard()
        {
            EventHandler? handler = DiscardRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Character && (key.Modifiers & KeyModifiers.Ctrl) != 0 && Char.ToLowerInvariant((char)key.Rune) == 's')
            {
                RequestSave();
                return true;
            }

            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Down) return Scope.Move(true);
            if (key.Code == KeyCode.Up) return Scope.Move(false);
            if (key.Code == KeyCode.Escape && DiscardRequested != null)
            {
                RequestDiscard();
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            if (ShowButtons && !Scope.Children.Contains(_ButtonRow)) AddChild(_ButtonRow);
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 4 || height < 1) return;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);

            int labelWidth = Math.Min(Math.Max(12, _Rows.Where(r => !r.IsSection).Select(r => TextCells.Width(T(r.Label))).DefaultIfEmpty(10).Max() + 2), Math.Max(10, width / 3));
            int fieldWidth = Math.Max(4, width - labelWidth - 1);

            List<int> tops = new List<int>();
            int y = 0;
            foreach (FormRow row in _Rows)
            {
                tops.Add(y);
                y += RowHeight(row);
            }

            int buttonsTop = y + 1;
            int total = buttonsTop + (ShowButtons ? 1 : 0);
            int viewport = height;
            int focusIdx = _Rows.FindIndex(r => r.Field != null && ReferenceEquals(r.Field, Scope.Focused));
            if (focusIdx >= 0)
            {
                int top = tops[focusIdx];
                int bottom = top + RowHeight(_Rows[focusIdx]);
                if (top < _Scroll) _Scroll = top;
                if (bottom > _Scroll + viewport) _Scroll = bottom - viewport;
            }
            else if (ReferenceEquals(Scope.Focused, _ButtonRow))
            {
                _Scroll = Math.Max(0, total - viewport);
            }

            _Scroll = Math.Clamp(_Scroll, 0, Math.Max(0, total - viewport));

            for (int i = 0; i < _Rows.Count; i++)
            {
                FormRow row = _Rows[i];
                int ry = tops[i] - _Scroll;
                int rh = RowHeight(row);
                if (rh == 0) continue;
                if (ry + rh <= 0 || ry >= height) continue;
                if (row.IsSection)
                {
                    if (ry >= 0) SurfaceText.Draw(surface, 0, ry, T(row.Label), Theme.Accent, width);
                    continue;
                }

                bool focused = ReferenceEquals(row.Field, Scope.Focused);
                if (ry >= 0) SurfaceText.Draw(surface, 0, ry, T(row.Label), focused ? Theme.Accent : Theme.Muted, labelWidth - 1);
                int fy = Math.Max(0, ry);
                int fh = Math.Min(row.Height, height - fy);
                if (fh > 0 && row.Field != null) Scope.RenderChild(surface, row.Field, new Rect(labelWidth, fy, fieldWidth, fh));
                int extra = ry + row.Height;
                string? error = (row.Field as IFormField)?.FieldError;
                if (error != null && extra >= 0 && extra < height)
                {
                    SurfaceText.Draw(surface, labelWidth, extra, "! " + T(error), Theme.Error, fieldWidth);
                    extra++;
                }

                if (!String.IsNullOrEmpty(row.Hint) && extra >= 0 && extra < height)
                {
                    SurfaceText.Draw(surface, labelWidth, extra, T(row.Hint!), Theme.Muted, fieldWidth);
                }
            }

            if (ShowButtons)
            {
                int by = buttonsTop - _Scroll;
                if (by >= 0 && by < height) Scope.RenderChild(surface, _ButtonRow, new Rect(labelWidth, by, fieldWidth, 1));
                if (IsDirty && by >= 0 && by < height)
                {
                    string dirty = T("Unsaved changes");
                    SurfaceText.Draw(surface, Math.Max(0, width - TextCells.Width(dirty)), by, dirty, Theme.Warning, width);
                }
            }
        }

        #endregion

        #region Private-Methods

        private int RowHeight(FormRow row)
        {
            if (IsHidden(row)) return 0;
            if (row.IsSection) return 2;
            int h = row.Height;
            if ((row.Field as IFormField)?.FieldError != null) h++;
            if (!String.IsNullOrEmpty(row.Hint)) h++;
            return h;
        }

        /// <summary>
        /// True when a row's field is an Armada widget with Visible set to false (the row takes no space and is not
        /// validated).
        /// </summary>
        /// <param name="row">Row.</param>
        /// <returns>True when hidden.</returns>
        public static bool IsHidden(FormRow row)
        {
            return row != null && row.Field is ArmadaWidget aw && !aw.Visible;
        }

        private List<object?> Snapshot()
        {
            return _Rows.Where(r => r.Field is IFormField).Select(r => ((IFormField)r.Field!).FieldValue).ToList();
        }

        #endregion
    }
}
