namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TUIKit;

    /// <summary>
    /// Plain scrolling text viewer (copied text, raw output). Not thread-safe.
    /// </summary>
    public class JsonOrTextViewer : ScrollTextView
    {
        #region Public-Members

        /// <summary>
        /// Text.
        /// </summary>
        public string Text
        {
            get { return _Text; }
            set
            {
                _Text = value ?? "";
                Invalidate();
            }
        }

        /// <inheritdoc />
        public override string PlainText
        {
            get { return _Text; }
        }

        #endregion

        #region Private-Members

        private string _Text = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">Text.</param>
        public JsonOrTextViewer(string text = "")
        {
            Text = text;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override IReadOnlyList<StyledText> BuildLines()
        {
            if (_Text.Length == 0) return new List<StyledText>();
            return _Text.Replace("\r\n", "\n").Split('\n').Select(l => StyledText.From(l)).ToList();
        }

        #endregion
    }
}
