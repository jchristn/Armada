namespace Armada.Tui.Theming
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Which characters the TUI draws icons, borders, charts, and bullets with.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GlyphModeEnum
    {
        /// <summary>
        /// Unicode when the terminal encoding is UTF-8, ASCII otherwise (see <see cref="TerminalEncoding"/>).
        /// </summary>
        Auto = 0,

        /// <summary>
        /// Unicode box drawing, block, and Braille characters where widgets use them.
        /// </summary>
        Unicode = 1,

        /// <summary>
        /// ASCII only: every box-drawing, block, Braille, arrow, and bullet character is written as an ASCII
        /// equivalent (see <see cref="AsciiGlyphs"/>), and borders are drawn with ASCII.
        /// </summary>
        Ascii = 2
    }
}
