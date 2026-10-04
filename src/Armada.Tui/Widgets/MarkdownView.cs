namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// Renders Markdown (descriptions, summaries, playbooks, assistant replies) with TUIKit's renderer, wrapped to the
    /// width and scrollable. <see cref="PlainText"/> is the raw Markdown (copy raw). Not thread-safe.
    /// </summary>
    public class MarkdownView : ScrollTextView
    {
        #region Public-Members

        /// <summary>
        /// Markdown source.
        /// </summary>
        public string Markdown
        {
            get { return _Markdown; }
            set
            {
                _Markdown = value ?? "";
                Invalidate();
            }
        }

        /// <inheritdoc />
        public override string PlainText
        {
            get { return _Markdown; }
        }

        #endregion

        #region Private-Members

        private string _Markdown = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="markdown">Markdown source.</param>
        public MarkdownView(string markdown = "")
        {
            Markdown = markdown;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override IReadOnlyList<StyledText> BuildLines()
        {
            if (String.IsNullOrWhiteSpace(_Markdown)) return new List<StyledText>();
            return MarkdownRenderer.Render(_Markdown);
        }

        #endregion
    }
}
