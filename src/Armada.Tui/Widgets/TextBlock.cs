namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Text;
    using TUIKit;

    /// <summary>
    /// Wrapped, non-focusable text in a single style (headings, hints, messages). Not thread-safe.
    /// </summary>
    public class TextBlock : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Text (already translated by the caller, or English to translate when <see cref="Translate"/> is true).
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Translate <see cref="Text"/> through the localizer when drawn. Default true.
        /// </summary>
        public bool Translate { get; set; } = true;

        /// <summary>
        /// Style selector applied to the current palette; null uses body text.
        /// </summary>
        public Func<Armada.Tui.Theming.ArmadaTheme, CellStyle>? Style { get; set; } = null;

        /// <summary>
        /// Center each line.
        /// </summary>
        public bool Centered { get; set; } = false;

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="style">Optional style selector.</param>
        public TextBlock(string text = "", Func<Armada.Tui.Theming.ArmadaTheme, CellStyle>? style = null)
        {
            Text = text ?? "";
            Style = style;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Lines the text wraps to at a width.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Lines.</returns>
        public List<string> Lines(int width)
        {
            return TextCells.Wrap(Translate ? T(Text) : Text, width);
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return new Size(available.Width, Math.Min(available.Height, Lines(Math.Max(1, available.Width)).Count));
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            CellStyle style = Style != null ? Style(Theme) : Theme.Text;
            List<string> lines = Lines(surface.Size.Width);
            for (int i = 0; i < lines.Count && i < surface.Size.Height; i++)
            {
                string line = Centered ? TextCells.Center(lines[i], surface.Size.Width) : lines[i];
                SurfaceText.Draw(surface, 0, i, line, style, surface.Size.Width);
            }
        }

        #endregion
    }
}
