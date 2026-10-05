namespace Armada.Tui.Input
{
    using System;
    using TUIKit.Input;

    /// <summary>
    /// One key with modifiers, as written in Armada key bindings (<c>ctrl+k</c>, <c>f5</c>, <c>alt+left</c>, <c>?</c>,
    /// <c>S</c>). Unlike TUIKit's <see cref="KeyChord"/>, plain characters keep their case (so <c>s</c> and <c>S</c>
    /// differ) and Shift is ignored for them (terminals disagree on reporting it for punctuation). Immutable.
    /// </summary>
    public class KeyStroke
    {
        #region Public-Members

        /// <summary>
        /// Key code.
        /// </summary>
        public KeyCode Code { get; }

        /// <summary>
        /// Character for <see cref="KeyCode.Character"/>; 0 otherwise.
        /// </summary>
        public int Rune { get; }

        /// <summary>
        /// Modifiers.
        /// </summary>
        public KeyModifiers Modifiers { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="code">Key code.</param>
        /// <param name="rune">Character, or 0.</param>
        /// <param name="modifiers">Modifiers.</param>
        public KeyStroke(KeyCode code, int rune, KeyModifiers modifiers)
        {
            Code = code;
            Rune = rune;
            Modifiers = modifiers;
        }

        /// <summary>
        /// Parse a stroke such as <c>ctrl+k</c>, <c>shift+f10</c>, <c>alt+left</c>, <c>?</c>, or <c>S</c>.
        /// </summary>
        /// <param name="text">Stroke text.</param>
        /// <returns>The stroke.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null or empty.</exception>
        /// <exception cref="FormatException">Thrown for an unrecognized key.</exception>
        public static KeyStroke Parse(string text)
        {
            if (String.IsNullOrEmpty(text)) throw new ArgumentNullException(nameof(text));
            string trimmed = text.Trim();
            if (trimmed.Length == 1)
            {
                return new KeyStroke(KeyCode.Character, trimmed[0], KeyModifiers.None);
            }

            KeyChord chord = KeyChord.Parse(trimmed);
            int rune = chord.Rune;
            if (chord.Code == KeyCode.Character)
            {
                int plus = trimmed.LastIndexOf('+');
                string token = plus >= 0 ? trimmed.Substring(plus + 1) : trimmed;
                if (token.Length == 1) rune = token[0];
            }

            return new KeyStroke(chord.Code, rune, chord.Modifiers);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when a key event is this stroke.
        /// </summary>
        /// <param name="key">Key event.</param>
        /// <returns>True on a match.</returns>
        public bool Matches(KeyEvent key)
        {
            if (key.Code != Code) return false;
            if (Code != KeyCode.Character) return key.Modifiers == Modifiers;

            bool chorded = (Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0;
            if (chorded)
            {
                return Char.ToLowerInvariant((char)key.Rune) == Char.ToLowerInvariant((char)Rune)
                    && (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == (Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt));
            }

            if ((key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0) return false;
            return key.Rune == Rune;
        }

        /// <summary>
        /// True when the stroke still reaches commands while a text field has focus: a Ctrl or Alt chord, or a function
        /// key. Plain characters, Enter, Delete, and the arrows go into the field instead.
        /// </summary>
        /// <returns>True when it works while typing.</returns>
        public bool WorksWhileTyping()
        {
            if ((Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0) return true;
            switch (Code)
            {
                case KeyCode.F1:
                case KeyCode.F2:
                case KeyCode.F3:
                case KeyCode.F4:
                case KeyCode.F5:
                case KeyCode.F6:
                case KeyCode.F7:
                case KeyCode.F8:
                case KeyCode.F9:
                case KeyCode.F10:
                case KeyCode.F11:
                case KeyCode.F12:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Display label in ASCII words (Ctrl+K, Shift+F10, Alt+Left, ?).
        /// </summary>
        /// <returns>Label.</returns>
        public string ToLabel()
        {
            string prefix = "";
            if ((Modifiers & KeyModifiers.Ctrl) != 0) prefix += "Ctrl+";
            if ((Modifiers & KeyModifiers.Alt) != 0) prefix += "Alt+";
            if ((Modifiers & KeyModifiers.Shift) != 0) prefix += "Shift+";
            if (Code == KeyCode.Character)
            {
                string ch = Rune == ' ' ? "Space" : Char.ConvertFromUtf32(Rune);
                bool chorded = (Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0;
                return prefix + (chorded ? ch.ToUpperInvariant() : ch);
            }

            switch (Code)
            {
                case KeyCode.Escape: return prefix + "Esc";
                case KeyCode.Delete: return prefix + "Del";
                case KeyCode.PageUp: return prefix + "PgUp";
                case KeyCode.PageDown: return prefix + "PgDn";
                default: return prefix + Code.ToString();
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToLabel();
        }

        #endregion
    }
}
