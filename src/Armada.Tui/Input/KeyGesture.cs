namespace Armada.Tui.Input
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TUIKit.Input;

    /// <summary>
    /// A key binding: one stroke (<c>ctrl+k</c>) or a two-stroke sequence (<c>g a</c>, the go-to keys). Immutable.
    /// </summary>
    public class KeyGesture
    {
        #region Public-Members

        /// <summary>
        /// Strokes (one or two). Never null or empty.
        /// </summary>
        public IReadOnlyList<KeyStroke> Strokes { get; }

        /// <summary>
        /// True for a two-stroke sequence.
        /// </summary>
        public bool IsSequence
        {
            get { return Strokes.Count > 1; }
        }

        /// <summary>
        /// Source text, for example <c>g a</c>.
        /// </summary>
        public string Text { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parse a gesture: one stroke, or two separated by a space.
        /// </summary>
        /// <param name="text">Gesture text.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null or empty.</exception>
        /// <exception cref="FormatException">Thrown for more than two strokes or an unrecognized key.</exception>
        public KeyGesture(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new ArgumentNullException(nameof(text));
            Text = text.Trim();
            string[] parts = Text == " " ? new[] { " " } : Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 2) throw new FormatException("A key gesture is one stroke or two strokes: " + text);
            Strokes = parts.Select(KeyStroke.Parse).ToList().AsReadOnly();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Display label (strokes joined by a space).
        /// </summary>
        /// <returns>Label, for example Ctrl+K or g a.</returns>
        public string ToLabel()
        {
            return String.Join(" ", Strokes.Select(s => s.ToLabel()));
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToLabel();
        }

        #endregion
    }
}
