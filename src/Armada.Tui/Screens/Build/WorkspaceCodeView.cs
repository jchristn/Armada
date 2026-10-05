namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// A read-only, syntax-highlighted, line-numbered view of a Workspace file (binary, too-large, and read-only files,
    /// which the dashboard shows as a preview). Scrolls and searches like every viewer (<c>/</c>, <c>n</c>). Not
    /// thread-safe.
    /// </summary>
    public class WorkspaceCodeView : ScrollTextView
    {
        #region Public-Members

        /// <summary>
        /// Content.
        /// </summary>
        public string Content
        {
            get { return _Content; }
            set
            {
                _Content = value ?? "";
                Invalidate();
            }
        }

        /// <summary>
        /// Highlighting language.
        /// </summary>
        public string Language
        {
            get { return _Language; }
            set
            {
                _Language = String.IsNullOrEmpty(value) ? "plaintext" : value;
                Invalidate();
            }
        }

        /// <inheritdoc />
        public override string PlainText
        {
            get { return _Content; }
        }

        #endregion

        #region Private-Members

        private string _Content = "";
        private string _Language = "plaintext";

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override IReadOnlyList<StyledText> BuildLines()
        {
            IReadOnlyList<StyledText> code = SyntaxHighlighter.Highlight(_Content, _Language);
            int digits = Math.Max(3, code.Count.ToString(CultureInfo.InvariantCulture).Length);
            List<StyledText> lines = new List<StyledText>();
            for (int i = 0; i < code.Count; i++)
            {
                string number = (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(digits) + " ";
                lines.Add(StyledText.From(number, Theme.Muted).Append(code[i]));
            }

            return lines;
        }

        #endregion
    }
}
