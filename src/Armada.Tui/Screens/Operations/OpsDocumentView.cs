namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// A scrollable, searchable panel over lines built with <see cref="OpsDocument"/> (detail fields, Markdown, tables).
    /// The owner rebuilds the lines through <see cref="Builder"/> whenever its data or the palette changes; the scroll
    /// position is kept. Not thread-safe.
    /// </summary>
    public class OpsDocumentView : ScrollTextView
    {
        #region Public-Members

        /// <summary>
        /// Builds the lines (called lazily on render after <see cref="ScrollTextView.Invalidate"/>).
        /// </summary>
        public Func<OpsDocument, OpsDocument>? Builder { get; set; } = null;

        /// <summary>
        /// English text shown while there is nothing to show.
        /// </summary>
        public string Empty { get; set; } = "Loading...";

        /// <inheritdoc />
        public override string PlainText
        {
            get { return String.Join("\n", _Last.Select(l => l.ToPlainString())); }
        }

        #endregion

        #region Private-Members

        private List<StyledText> _Last = new List<StyledText>();

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override IReadOnlyList<StyledText> BuildLines()
        {
            if (Builder == null)
            {
                _Last = new List<StyledText>();
                return _Last;
            }

            OpsDocument doc = new OpsDocument(Theme, Localizer);
            _Last = Builder(doc).Lines;
            return _Last;
        }

        /// <inheritdoc />
        protected override string EmptyText()
        {
            return Empty;
        }

        /// <inheritdoc />
        protected override void OnThemeChanged(Armada.Tui.Theming.ArmadaTheme theme)
        {
            Invalidate();
        }

        #endregion
    }
}
