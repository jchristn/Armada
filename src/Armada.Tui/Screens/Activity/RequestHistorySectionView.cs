namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// A scrolling, searchable text view made of headed sections (summary pairs, parameter and header blocks, request
    /// and response bodies), used by the request detail drawer and the API Explorer response pane. Styles come from
    /// the active theme at render time. Not thread-safe.
    /// </summary>
    public class RequestHistorySectionView : ScrollTextView
    {
        #region Public-Members

        /// <summary>
        /// Lines in order. Never null.
        /// </summary>
        public IReadOnlyList<RequestHistorySectionLine> Lines
        {
            get { return _Lines; }
        }

        /// <inheritdoc />
        public override string PlainText
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                foreach (RequestHistorySectionLine line in _Lines)
                {
                    if (line.Kind == "pair") sb.Append(line.Text).Append(": ").Append(line.Detail).Append('\n');
                    else if (line.Kind == "heading") sb.Append(line.Text).Append(line.Detail.Length > 0 ? "  (" + line.Detail + ")" : "").Append('\n');
                    else sb.Append(line.Text).Append('\n');
                }

                return sb.ToString();
            }
        }

        #endregion

        #region Private-Members

        private readonly List<RequestHistorySectionLine> _Lines = new List<RequestHistorySectionLine>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Remove every line.
        /// </summary>
        public void Clear()
        {
            _Lines.Clear();
            Invalidate();
        }

        /// <summary>
        /// Add a section heading with an optional note.
        /// </summary>
        /// <param name="title">Title (translated).</param>
        /// <param name="note">Note (translated), or empty.</param>
        public void AddHeading(string title, string note = "")
        {
            if (_Lines.Count > 0) _Lines.Add(new RequestHistorySectionLine("text", ""));
            _Lines.Add(new RequestHistorySectionLine("heading", title, note));
            Invalidate();
        }

        /// <summary>
        /// Add a label and value.
        /// </summary>
        /// <param name="label">Label (translated).</param>
        /// <param name="value">Value.</param>
        public void AddPair(string label, string? value)
        {
            _Lines.Add(new RequestHistorySectionLine("pair", label, String.IsNullOrEmpty(value) ? "-" : value!));
            Invalidate();
        }

        /// <summary>
        /// Add text lines (split on newlines).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="muted">Draw muted.</param>
        public void AddText(string text, bool muted = false)
        {
            foreach (string line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                _Lines.Add(new RequestHistorySectionLine(muted ? "muted" : "text", line));
            }

            Invalidate();
        }

        /// <summary>
        /// Add a code block (split on newlines), JSON-highlighted when <paramref name="language"/> is "json".
        /// </summary>
        /// <param name="code">Code.</param>
        /// <param name="language">Language, or empty.</param>
        public void AddCode(string code, string language = "")
        {
            foreach (string line in (code ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                _Lines.Add(new RequestHistorySectionLine("code", line, "", language));
            }

            Invalidate();
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override IReadOnlyList<StyledText> BuildLines()
        {
            List<StyledText> result = new List<StyledText>();
            int labelWidth = Math.Min(24, _Lines.Where(l => l.Kind == "pair").Select(l => Armada.Tui.Text.TextCells.Width(l.Text)).DefaultIfEmpty(8).Max() + 2);
            foreach (RequestHistorySectionLine line in _Lines)
            {
                switch (line.Kind)
                {
                    case "heading":
                        StyledText heading = StyledText.From(line.Text, Theme.Accent.WithAttribute(CellAttributes.Bold, true));
                        if (line.Detail.Length > 0) heading = heading.Append(StyledText.From("  " + line.Detail, Theme.Muted));
                        result.Add(heading);
                        break;
                    case "pair":
                        result.Add(StyledText.From(Armada.Tui.Text.TextCells.PadRight(line.Text, labelWidth), Theme.Muted).Append(StyledText.From(line.Detail, Theme.Text)));
                        break;
                    case "muted":
                        result.Add(StyledText.From(line.Text, Theme.Muted));
                        break;
                    case "code":
                        if (line.Language.Length > 0) result.Add(SyntaxHighlighter.HighlightLine(line.Text, line.Language));
                        else result.Add(StyledText.From(line.Text, Theme.Code));
                        break;
                    default:
                        result.Add(StyledText.From(line.Text, Theme.Text));
                        break;
                }
            }

            return result;
        }

        #endregion
    }
}
