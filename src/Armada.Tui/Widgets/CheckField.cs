namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A checkbox for <see cref="FormView"/> (dashboard <c>input type="checkbox"</c>): <c>Space</c> or <c>Enter</c>
    /// toggles, the label is drawn beside the box, and the value takes part in dirty tracking. Not thread-safe.
    /// </summary>
    public class CheckField : ArmadaWidget, IFormField
    {
        #region Public-Members

        /// <summary>
        /// Checked state.
        /// </summary>
        public bool Value { get; private set; } = false;

        /// <summary>
        /// English text drawn after the box (for example "Active"), or empty.
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Read-only fields draw dimmed and ignore input.
        /// </summary>
        public bool ReadOnly { get; set; } = false;

        /// <inheritdoc />
        public object? FieldValue
        {
            get { return Value; }
        }

        /// <inheritdoc />
        public string? FieldError
        {
            get { return null; }
        }

        /// <summary>
        /// Raised after the value changes.
        /// </summary>
        public event EventHandler<ValueChangedEventArgs<bool>>? ValueChanged;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">English text beside the box.</param>
        /// <param name="value">Initial value.</param>
        public CheckField(string text = "", bool value = false)
        {
            Text = text ?? "";
            Value = value;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set the value.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="raise">Raise <see cref="ValueChanged"/>.</param>
        public void SetValue(bool value, bool raise = true)
        {
            bool old = Value;
            Value = value;
            if (old == value || !raise) return;
            EventHandler<ValueChangedEventArgs<bool>>? handler = ValueChanged;
            if (handler != null) handler(this, new ValueChangedEventArgs<bool>(old, value));
        }

        /// <summary>
        /// Flip the value.
        /// </summary>
        public void Toggle()
        {
            if (ReadOnly) return;
            SetValue(!Value);
        }

        /// <inheritdoc />
        public bool ValidateField()
        {
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Enter || (key.Code == KeyCode.Character && key.Rune == ' ' && key.Modifiers == KeyModifiers.None))
            {
                Toggle();
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press) return false;
            Toggle();
            return true;
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return new Size(available.Width, 1);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            CellStyle style = ReadOnly ? Theme.Disabled : IsFocused ? Theme.InputFocused : Theme.Text;
            SurfaceText.FillRow(surface, 0, 0, surface.Size.Width, style);
            string mark = Value ? "[x]" : "[ ]";
            string label = Text.Length > 0 ? " " + T(Text) : "";
            SurfaceText.Draw(surface, 0, 0, mark + label, style, surface.Size.Width);
        }

        #endregion
    }
}
