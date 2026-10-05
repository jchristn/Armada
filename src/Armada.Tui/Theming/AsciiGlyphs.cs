namespace Armada.Tui.Theming
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Width-preserving ASCII equivalents for the non-ASCII glyphs that widgets draw (box drawing, block elements and
    /// sparkline bars, Braille chart dots, geometric shapes, arrows, bullets, ellipsis, dashes, and typographic quotes).
    /// Every mapped character is one terminal cell wide and maps to exactly one ASCII character, so a transliterated
    /// frame keeps its layout. Letters and CJK text are never changed. Used for ASCII icon mode, where the terminal
    /// output and text snapshots are passed through <see cref="Transliterate"/>. Thread-safe (immutable tables).
    /// </summary>
    public static class AsciiGlyphs
    {
        #region Private-Members

        private static readonly Dictionary<char, char> _Map = BuildMap();

        #endregion

        #region Public-Methods

        /// <summary>
        /// The characters that have an ASCII equivalent, and the equivalents. Never null.
        /// </summary>
        public static IReadOnlyDictionary<char, char> Table
        {
            get { return _Map; }
        }

        /// <summary>
        /// ASCII equivalent of a character, or the character itself when it has none.
        /// </summary>
        /// <param name="c">Character.</param>
        /// <returns>ASCII equivalent or the input.</returns>
        public static char Map(char c)
        {
            if (c < 0x80) return c;
            if (c >= '\u2800' && c <= '\u28FF') return c == '\u2800' ? ' ' : '*';
            if (_Map.TryGetValue(c, out char mapped)) return mapped;
            if (c >= '\u2500' && c <= '\u257F') return '+';
            return c;
        }

        /// <summary>
        /// Replace every mapped glyph in a string. Returns the same instance when nothing changes.
        /// </summary>
        /// <param name="text">Text; null returns empty.</param>
        /// <returns>Transliterated text.</returns>
        public static string Transliterate(string? text)
        {
            if (String.IsNullOrEmpty(text)) return text ?? "";
            int first = -1;
            for (int i = 0; i < text!.Length; i++)
            {
                char c = text[i];
                if (c >= 0x80 && Map(c) != c)
                {
                    first = i;
                    break;
                }
            }

            if (first < 0) return text;
            StringBuilder sb = new StringBuilder(text.Length);
            sb.Append(text, 0, first);
            for (int i = first; i < text.Length; i++) sb.Append(Map(text[i]));
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static Dictionary<char, char> BuildMap()
        {
            Dictionary<char, char> m = new Dictionary<char, char>();
            Add(m, "\u2500\u2504\u2505\u2508\u2509\u254C\u254D\u2550\u2574\u2576\u2578\u257A\u257C\u257E\u23AF", '-');
            Add(m, "\u2502\u2506\u2507\u250A\u250B\u254E\u254F\u2551\u2575\u2577\u2579\u257B\u257D\u257F", '|');
            // The focused box (heavy lines, see FocusFrame) keeps a shape of its own in ASCII: = and #.
            Add(m, "\u2501", '=');
            Add(m, "\u2503\u250F\u2513\u2517\u251B", '#');
            Add(m, "\u2571", '/');
            Add(m, "\u2572", '\\');
            Add(m, "\u2573", 'X');
            Add(m, "\u2581\u2582", '_');
            Add(m, "\u2583", '.');
            Add(m, "\u2584", '-');
            Add(m, "\u2585\u2586", '=');
            Add(m, "\u2587\u2588\u2589\u258A\u258B\u2593\u259B\u259C\u259F\u2599", '#');
            Add(m, "\u258C\u258D\u258E\u258F\u2590\u2595", '|');
            Add(m, "\u2580\u2594", '"');
            Add(m, "\u2592", '+');
            Add(m, "\u2591", '.');
            Add(m, "\u2596\u2597\u2598\u259D\u259A\u259E", '.');
            Add(m, "\u25CF\u25C9\u25C6\u2022\u2219\u25AA", '*');
            Add(m, "\u25CB\u25E6\u25C7\u25CC\u25A1\u25AB\u25EF", 'o');
            Add(m, "\u25A0", '#');
            Add(m, "\u25B2\u25B3\u25B4\u25B5\u2191\u21D1", '^');
            Add(m, "\u25BC\u25BD\u25BE\u25BF\u2193\u21D3", 'v');
            Add(m, "\u25B6\u25B7\u25B8\u25B9\u25BA\u25BB\u2192\u21D2\u2023\u276F\u203A", '>');
            Add(m, "\u25C0\u25C1\u25C2\u25C3\u25C4\u25C5\u2190\u21D0\u276E\u2039\u21B5\u21A9", '<');
            Add(m, "\u2194\u2010\u2011\u2012\u2013\u2014\u2015\u2043\u2212", '-');
            Add(m, "\u2195", '|');
            Add(m, "\u00B7\u2026\u22EF", '.');
            Add(m, "\u00A0\u2002\u2003\u2009\u200A", ' ');
            Add(m, "\u2018\u2019\u201A\u2032", '\'');
            Add(m, "\u201C\u201D\u201E\u2033", '"');
            Add(m, "\u00D7", 'x');
            return m;
        }

        private static void Add(Dictionary<char, char> map, string chars, char ascii)
        {
            foreach (char c in chars) map[c] = ascii;
        }

        #endregion
    }
}
