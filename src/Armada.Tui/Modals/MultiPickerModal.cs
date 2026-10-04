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
    /// A multi-choice picker: type to filter, Space toggles, Ctrl+A toggles every visible option, Enter confirms. Closes
    /// with the checked values in option order (<see cref="List{T}"/>), or null when cancelled. Pinned options stay
    /// checked (the column chooser's identifying columns). Not thread-safe.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public class MultiPickerModal<T> : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// The option list.
        /// </summary>
        public FilterList<T> List { get; }

        #endregion

        #region Private-Members

        private readonly List<SelectOption<T>> _Order;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="options">Options.</param>
        /// <param name="selected">Initially checked values.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        public MultiPickerModal(string title, IEnumerable<SelectOption<T>> options, IEnumerable<T>? selected, ITextLocalizer? localizer = null, ArmadaTheme? theme = null)
            : base(title, localizer, theme)
        {
            _Order = (options ?? Enumerable.Empty<SelectOption<T>>()).ToList();
            List = new FilterList<T>(_Order);
            List.MultiSelect = true;
            HashSet<T> initial = new HashSet<T>(selected ?? Enumerable.Empty<T>());
            foreach (SelectOption<T> option in _Order)
            {
                if (option.Pinned || initial.Contains(option.Value)) List.Checked.Add(option);
            }

            FooterHint = " Space " + T("Toggle") + "  Ctrl+A " + T("All") + "  Enter " + T("Apply") + "  Esc " + T("Cancel") + " ";
            MinContentWidth = 40;
            MaxContentWidth = 90;
            MinContentHeight = 6;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Checked values in option order.
        /// </summary>
        /// <returns>Values.</returns>
        public List<T> CheckedValues()
        {
            return _Order.Where(o => List.Checked.Contains(o)).Select(o => o.Value).ToList();
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            if (key.Code == KeyCode.Enter)
            {
                RequestClose(CheckedValues());
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
            int widest = _Order.Select(o => TextCells.Width(o.Label) + TextCells.Width(o.Detail) + 8).DefaultIfEmpty(30).Max();
            return Math.Min(availableWidth, Math.Max(40, widest));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Max(4, Math.Min(20, _Order.Count + 2));
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            string status = T("Filter") + ": " + List.Query;
            SurfaceText.Draw(content, 0, 0, status, On(Theme.Accent), width);
            string count = List.Checked.Count + " " + T("selected");
            SurfaceText.Draw(content, Math.Max(0, width - TextCells.Width(count)), 0, count, Dim(), width);
            List.Render(content, 2, content.Size.Height - 2, Theme, Body());
        }

        #endregion
    }
}
