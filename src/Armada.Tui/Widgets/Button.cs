namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A focusable button rendered as <c>[ Label ]</c>. Enter or Space (or a click) raises <see cref="Pressed"/>.
    /// Not thread-safe.
    /// </summary>
    public class Button : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// English label (translated when drawn).
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// Optional key hint drawn after the label, for example Ctrl+S.
        /// </summary>
        public string? Hint { get; set; } = null;

        /// <summary>
        /// Disabled buttons render dimmed and ignore presses.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Raised when pressed.
        /// </summary>
        public event EventHandler? Pressed;

        /// <summary>
        /// Width in cells of the rendered button.
        /// </summary>
        public int DisplayWidth
        {
            get { return TextCells.Width(Text()); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="onPressed">Optional handler.</param>
        public Button(string label, Action? onPressed = null)
        {
            Label = label ?? "";
            if (onPressed != null) Pressed += (s, e) => onPressed();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Press the button programmatically (no-op when disabled).
        /// </summary>
        public void Press()
        {
            if (!Enabled) return;
            EventHandler? handler = Pressed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Enter || (key.Code == KeyCode.Character && key.Rune == ' ' && key.Modifiers == KeyModifiers.None))
            {
                Press();
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Press && mouse.Button == MouseButton.Left)
            {
                Press();
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return new Size(Math.Min(available.Width, DisplayWidth), 1);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            CellStyle style = !Enabled ? Theme.Disabled : IsFocused ? Theme.ButtonFocused : Theme.Button;
            SurfaceText.Draw(surface, 0, 0, Text(), style, surface.Size.Width);
        }

        #endregion

        #region Private-Methods

        private string Text()
        {
            string label = T(Label);
            return "[ " + label + (String.IsNullOrEmpty(Hint) ? "" : " " + Hint) + " ]";
        }

        #endregion
    }
}
