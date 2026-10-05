namespace Armada.Tui.Modals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A single-choice picker (the TUI's dropdown): type to filter, arrows to move, Enter to choose, Esc to cancel.
    /// Closes with the chosen <see cref="SelectOption{T}"/>, or null when cancelled. Not thread-safe.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public class PickerModal<T> : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// The option list.
        /// </summary>
        public FilterList<T> List { get; }

        /// <summary>
        /// When set, <c>e</c> or <c>E</c> closes the picker (with no choice) and edits the highlighted option instead
        /// of filtering.
        /// </summary>
        public Action<SelectOption<T>>? Edit
        {
            get { return _Edit; }
            set
            {
                _Edit = value;
                FooterHint = " " + T("Type to filter") + "  Enter " + T("Select") + (_Edit != null ? "  e " + T("Edit") : "") + "  Esc " + T("Cancel") + " ";
            }
        }

        /// <summary>
        /// Which options <see cref="Edit"/> applies to (null: all enabled options).
        /// </summary>
        public Func<SelectOption<T>, bool>? CanEdit { get; set; } = null;

        /// <summary>
        /// Width multiplier applied to the measured, minimum, and maximum width (1.0 = default; clamped to 1.0-3.0).
        /// </summary>
        public double WidthScale
        {
            get { return _WidthScale; }
            set
            {
                _WidthScale = Math.Clamp(value, 1.0, 3.0);
                MinContentWidth = (int)Math.Round(40 * _WidthScale);
                MaxContentWidth = (int)Math.Round(90 * _WidthScale);
            }
        }

        #endregion

        #region Private-Members

        private double _WidthScale = 1.0;
        private Action<SelectOption<T>>? _Edit = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="options">Options.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        public PickerModal(string title, IEnumerable<SelectOption<T>> options, ITextLocalizer? localizer = null, ArmadaTheme? theme = null)
            : base(title, localizer, theme)
        {
            List = new FilterList<T>(options);
            FooterHint = " " + T("Type to filter") + "  Enter " + T("Select") + "  Esc " + T("Cancel") + " ";
            MinContentWidth = 40;
            MaxContentWidth = 90;
            MinContentHeight = 6;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            if (_Edit != null && key.Code == KeyCode.Character && (key.Rune == 'e' || key.Rune == 'E')
                && (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0)
            {
                SelectOption<T>? target = List.Current;
                if (target != null && target.Enabled && (CanEdit == null || CanEdit(target)))
                {
                    RequestClose(null);
                    _Edit(target);
                }

                return true;
            }

            if (key.Code == KeyCode.Enter)
            {
                SelectOption<T>? current = List.Current;
                if (current != null && current.Enabled) RequestClose(current);
                return true;
            }

            return List.HandleKey(key, 10);
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            List.Paste(text);
            return true;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            int widest = List.Visible.Select(o => TextCells.Width(o.Label) + TextCells.Width(o.Detail) + 6).DefaultIfEmpty(30).Max();

            // The key hints sit in the bottom border; keep the box wide enough that they are not cut off.
            int footer = TextCells.Width(FooterHint ?? "") + 2;
            return Math.Min(availableWidth, (int)Math.Round(Math.Max(Math.Max(40, footer), widest) * _WidthScale));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Max(4, Math.Min(18, List.Visible.Count + 2));
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            string query = String.IsNullOrEmpty(List.Query) ? T("Filter") + ": " : T("Filter") + ": " + List.Query;
            SurfaceText.Draw(content, 0, 0, query, On(Theme.Accent), width);
            if (List.Visible.Count == 0)
            {
                SurfaceText.Draw(content, 0, 2, T("No matches."), Dim(), width);
                return;
            }

            List.Render(content, 2, content.Size.Height - 2, Theme, Body());
        }

        #endregion
    }
}
