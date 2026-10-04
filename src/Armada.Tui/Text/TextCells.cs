namespace Armada.Tui.Text
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using TUIKit.Unicode;

    /// <summary>
    /// Terminal-cell text measurement for Armada widgets. Widths come from TUIKit's grapheme and East Asian width tables
    /// (<see cref="Graphemes"/>), never <see cref="string.Length"/>, so CJK and emoji text align. Thread-safe (stateless).
    /// </summary>
    public static class TextCells
    {
        #region Public-Members

        /// <summary>
        /// Marker appended to truncated text (ASCII, one cell per character).
        /// </summary>
        public const string Ellipsis = "...";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Display width of text in terminal cells.
        /// </summary>
        /// <param name="text">Text; null is 0.</param>
        /// <returns>Width in cells.</returns>
        public static int Width(string? text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            return Graphemes.MeasureWidth(text!);
        }

        /// <summary>
        /// Cut text to at most <paramref name="width"/> cells without splitting a wide grapheme. No ellipsis.
        /// </summary>
        /// <param name="text">Text; null is empty.</param>
        /// <param name="width">Maximum cells; values below 1 return empty.</param>
        /// <returns>The clipped text.</returns>
        public static string Clip(string? text, int width)
        {
            if (String.IsNullOrEmpty(text) || width < 1) return "";
            if (Width(text) <= width) return text!;
            StringBuilder sb = new StringBuilder();
            int used = 0;
            foreach (Grapheme g in Graphemes.Split(text!))
            {
                if (used + g.Width > width) break;
                sb.Append(g.Text);
                used += g.Width;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Fit text in <paramref name="width"/> cells, ending with <see cref="Ellipsis"/> when it is cut.
        /// </summary>
        /// <param name="text">Text; null is empty.</param>
        /// <param name="width">Maximum cells.</param>
        /// <returns>The truncated text.</returns>
        public static string Truncate(string? text, int width)
        {
            if (String.IsNullOrEmpty(text) || width < 1) return "";
            if (Width(text) <= width) return text!;
            if (width <= Ellipsis.Length) return Clip(text, width);
            return Clip(text, width - Ellipsis.Length) + Ellipsis;
        }

        /// <summary>
        /// Truncate and pad with spaces to exactly <paramref name="width"/> cells (a wide grapheme that does not fit is
        /// replaced by padding).
        /// </summary>
        /// <param name="text">Text; null is empty.</param>
        /// <param name="width">Target width.</param>
        /// <returns>Text exactly <paramref name="width"/> cells wide.</returns>
        public static string PadRight(string? text, int width)
        {
            if (width < 1) return "";
            string fitted = Truncate(text, width);
            int pad = width - Width(fitted);
            return pad > 0 ? fitted + new string(' ', pad) : fitted;
        }

        /// <summary>
        /// Right-align text in exactly <paramref name="width"/> cells.
        /// </summary>
        /// <param name="text">Text; null is empty.</param>
        /// <param name="width">Target width.</param>
        /// <returns>Padded text.</returns>
        public static string PadLeft(string? text, int width)
        {
            if (width < 1) return "";
            string fitted = Truncate(text, width);
            int pad = width - Width(fitted);
            return pad > 0 ? new string(' ', pad) + fitted : fitted;
        }

        /// <summary>
        /// Center text in exactly <paramref name="width"/> cells.
        /// </summary>
        /// <param name="text">Text; null is empty.</param>
        /// <param name="width">Target width.</param>
        /// <returns>Padded text.</returns>
        public static string Center(string? text, int width)
        {
            if (width < 1) return "";
            string fitted = Truncate(text, width);
            int pad = width - Width(fitted);
            int left = pad / 2;
            return new string(' ', left) + fitted + new string(' ', pad - left);
        }

        /// <summary>
        /// Word-wrap text to lines of at most <paramref name="width"/> cells. Existing newlines are kept; words longer
        /// than a line are broken by grapheme. CJK text (no spaces) breaks between graphemes.
        /// </summary>
        /// <param name="text">Text; null returns one empty line.</param>
        /// <param name="width">Line width; values below 1 are treated as 1.</param>
        /// <returns>Lines. Never null or empty.</returns>
        public static List<string> Wrap(string? text, int width)
        {
            int w = Math.Max(1, width);
            List<string> lines = new List<string>();
            string source = (text ?? "").Replace("\r\n", "\n");
            foreach (string paragraph in source.Split('\n'))
            {
                if (paragraph.Length == 0)
                {
                    lines.Add("");
                    continue;
                }

                StringBuilder line = new StringBuilder();
                int lineWidth = 0;
                StringBuilder word = new StringBuilder();
                int wordWidth = 0;
                foreach (Grapheme g in Graphemes.Split(paragraph))
                {
                    bool isSpace = g.Text == " ";
                    bool isWide = g.Width > 1;
                    if (isSpace || isWide)
                    {
                        FlushWord(lines, line, ref lineWidth, word, ref wordWidth, w);
                        if (isSpace)
                        {
                            if (lineWidth > 0 && lineWidth + 1 <= w)
                            {
                                line.Append(' ');
                                lineWidth++;
                            }
                            else if (lineWidth + 1 > w)
                            {
                                lines.Add(line.ToString().TrimEnd());
                                line.Clear();
                                lineWidth = 0;
                            }

                            continue;
                        }

                        if (lineWidth + g.Width > w)
                        {
                            lines.Add(line.ToString().TrimEnd());
                            line.Clear();
                            lineWidth = 0;
                        }

                        line.Append(g.Text);
                        lineWidth += g.Width;
                        continue;
                    }

                    word.Append(g.Text);
                    wordWidth += g.Width;
                }

                FlushWord(lines, line, ref lineWidth, word, ref wordWidth, w);
                lines.Add(line.ToString().TrimEnd());
            }

            if (lines.Count == 0) lines.Add("");
            return lines;
        }

        #endregion

        #region Private-Methods

        private static void FlushWord(List<string> lines, StringBuilder line, ref int lineWidth, StringBuilder word, ref int wordWidth, int width)
        {
            if (wordWidth == 0) return;
            if (lineWidth + wordWidth > width && lineWidth > 0)
            {
                lines.Add(line.ToString().TrimEnd());
                line.Clear();
                lineWidth = 0;
            }

            if (wordWidth > width)
            {
                foreach (Grapheme g in Graphemes.Split(word.ToString()))
                {
                    if (lineWidth + g.Width > width)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                        lineWidth = 0;
                    }

                    line.Append(g.Text);
                    lineWidth += g.Width;
                }
            }
            else
            {
                line.Append(word);
                lineWidth += wordWidth;
            }

            word.Clear();
            wordWidth = 0;
        }

        #endregion
    }
}
