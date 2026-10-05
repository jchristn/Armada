namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// Builds the styled lines of a detail panel: section headings, label/value fields (aligned), wrapped text,
    /// Markdown, and simple tables. The result is shown in an <see cref="OpsDocumentView"/>, which scrolls, searches,
    /// and copies. Labels and headings are translated; values are shown as given. Not thread-safe.
    /// </summary>
    public class OpsDocument
    {
        #region Public-Members

        /// <summary>
        /// Lines built so far.
        /// </summary>
        public List<StyledText> Lines { get; } = new List<StyledText>();

        /// <summary>
        /// Label column width for fields. Default 22.
        /// </summary>
        public int LabelWidth
        {
            get { return _LabelWidth; }
            set { _LabelWidth = Math.Clamp(value, 6, 60); }
        }

        /// <summary>
        /// Palette.
        /// </summary>
        public ArmadaTheme Theme { get; }

        /// <summary>
        /// Localizer.
        /// </summary>
        public ITextLocalizer Loc { get; }

        #endregion

        #region Private-Members

        private int _LabelWidth = 22;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="theme">Palette.</param>
        /// <param name="loc">Localizer.</param>
        public OpsDocument(ArmadaTheme theme, ITextLocalizer loc)
        {
            Theme = theme ?? throw new ArgumentNullException(nameof(theme));
            Loc = loc ?? throw new ArgumentNullException(nameof(loc));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a section heading (preceded by a blank line unless first).
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="suffix">Untranslated suffix (for example a count), or null.</param>
        /// <returns>This builder.</returns>
        public OpsDocument Section(string title, string? suffix = null)
        {
            if (Lines.Count > 0) Lines.Add(StyledText.Empty);
            Lines.Add(StyledText.From(Loc.T(title) + (suffix ?? ""), Theme.Accent.WithAttribute(CellAttributes.Bold, true)));
            return this;
        }

        /// <summary>
        /// Add a label/value field ("-" for empty values).
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="value">Value.</param>
        /// <param name="style">Value style, or null for text.</param>
        /// <returns>This builder.</returns>
        public OpsDocument Field(string label, string? value, CellStyle? style = null)
        {
            string text = String.IsNullOrEmpty(value) ? "-" : value!.Replace("\r\n", " ").Replace('\n', ' ');
            string l = TextCells.PadRight(TextCells.Truncate(Loc.T(label), _LabelWidth - 1), _LabelWidth);
            StyledText line = StyledText.From(l, Theme.Muted).Append(StyledText.From(text, style ?? Theme.Text));
            Lines.Add(line);
            return this;
        }

        /// <summary>
        /// Add a yes/no field.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="value">Value, or null for "-".</param>
        /// <returns>This builder.</returns>
        public OpsDocument YesNo(string label, bool? value)
        {
            return Field(label, value.HasValue ? Loc.T(value.Value ? "Yes" : "No") : "-");
        }

        /// <summary>
        /// Add a timestamp field (locale date and time plus relative age).
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="utc">UTC time, or null.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>This builder.</returns>
        public OpsDocument Time(string label, DateTime? utc, DateTime nowUtc)
        {
            return Field(label, utc.HasValue ? Loc.FormatDateTime(utc.Value) + "  (" + Loc.FormatRelative(utc.Value, nowUtc) + ")" : "-");
        }

        /// <summary>
        /// Add wrapped plain text (multi-line kept).
        /// </summary>
        /// <param name="text">Text (shown as given).</param>
        /// <param name="style">Style, or null.</param>
        /// <returns>This builder.</returns>
        public OpsDocument Text(string? text, CellStyle? style = null)
        {
            if (String.IsNullOrEmpty(text)) return this;
            foreach (string line in text!.Replace("\r\n", "\n").Split('\n')) Lines.Add(StyledText.From(line.Replace("\t", "    "), style ?? Theme.Text));
            return this;
        }

        /// <summary>
        /// Add a translated line.
        /// </summary>
        /// <param name="english">English text.</param>
        /// <param name="style">Style, or null for muted.</param>
        /// <returns>This builder.</returns>
        public OpsDocument Note(string english, CellStyle? style = null)
        {
            Lines.Add(StyledText.From(Loc.T(english), style ?? Theme.Muted));
            return this;
        }

        /// <summary>
        /// Add rendered Markdown.
        /// </summary>
        /// <param name="markdown">Markdown.</param>
        /// <returns>This builder.</returns>
        public OpsDocument Markdown(string? markdown)
        {
            if (String.IsNullOrWhiteSpace(markdown)) return this;
            Lines.AddRange(MarkdownRenderer.Render(markdown!));
            return this;
        }

        /// <summary>
        /// Add a simple table: a header row and rows of cells padded to the widest cell of each column (capped).
        /// </summary>
        /// <param name="headers">English headers.</param>
        /// <param name="rows">Rows.</param>
        /// <param name="maxColumnWidth">Column cap.</param>
        /// <returns>This builder.</returns>
        public OpsDocument Table(IList<string> headers, IEnumerable<IList<string>> rows, int maxColumnWidth = 40)
        {
            List<string> h = headers.Select(x => Loc.T(x)).ToList();
            List<IList<string>> body = rows.ToList();
            if (body.Count == 0) return this;
            List<int> widths = new List<int>();
            for (int i = 0; i < h.Count; i++)
            {
                int w = TextCells.Width(h[i]);
                foreach (IList<string> r in body) if (i < r.Count) w = Math.Max(w, TextCells.Width(r[i]));
                widths.Add(Math.Min(maxColumnWidth, w));
            }

            Lines.Add(StyledText.From(Join(h, widths), Theme.GridHeader));
            foreach (IList<string> r in body) Lines.Add(StyledText.From(Join(r, widths), Theme.Text));
            return this;
        }

        /// <summary>
        /// Add a blank line.
        /// </summary>
        /// <returns>This builder.</returns>
        public OpsDocument Blank()
        {
            Lines.Add(StyledText.Empty);
            return this;
        }

        /// <summary>
        /// Add a prebuilt line.
        /// </summary>
        /// <param name="line">Line.</param>
        /// <returns>This builder.</returns>
        public OpsDocument Add(StyledText line)
        {
            if (line != null) Lines.Add(line);
            return this;
        }

        #endregion

        #region Private-Methods

        private static string Join(IList<string> cells, List<int> widths)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < widths.Count; i++)
            {
                string cell = i < cells.Count ? (cells[i] ?? "") : "";
                parts.Add(TextCells.PadRight(TextCells.Truncate(cell.Replace('\n', ' '), widths[i]), widths[i]));
            }

            return String.Join("  ", parts).TrimEnd();
        }

        #endregion
    }
}
