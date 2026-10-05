namespace Armada.Tui.Screens.Kit
{
    using System;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A two-state checkbox for forms ("[x] Enabled"). <c>Space</c> or <c>Enter</c> toggles; the mouse toggles on
    /// press. Works in a <see cref="FormView"/> (dirty tracking). Not thread-safe.
    /// </summary>
    public class ToggleField : ArmadaWidget, IFormField
    {
        #region Public-Members

        /// <summary>
        /// Current value.
        /// </summary>
        public bool Value { get; private set; } = false;

        /// <summary>
        /// English caption drawn after the box, or empty.
        /// </summary>
        public string Caption { get; set; } = "";

        /// <summary>
        /// When false the field ignores input and renders disabled. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

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
        /// <param name="value">Initial value.</param>
        /// <param name="caption">English caption, or empty.</param>
        public ToggleField(bool value = false, string caption = "")
        {
            Value = value;
            Caption = caption ?? "";
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
            if (value == Value) return;
            bool old = Value;
            Value = value;
            if (!raise) return;
            EventHandler<ValueChangedEventArgs<bool>>? handler = ValueChanged;
            if (handler != null) handler(this, new ValueChangedEventArgs<bool>(old, value));
        }

        /// <summary>
        /// Flip the value.
        /// </summary>
        public void Toggle()
        {
            if (!Enabled) return;
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
            bool toggle = key.Code == KeyCode.Enter || (key.Code == KeyCode.Character && key.Rune == ' ' && key.Modifiers == KeyModifiers.None);
            if (!toggle) return false;
            Toggle();
            return true;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press || mouse.Button != MouseButton.Left) return false;
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
            int width = surface.Size.Width;
            CellStyle style = !Enabled ? Theme.Disabled : IsFocused ? Theme.InputFocused : Theme.Text;
            SurfaceText.FillRow(surface, 0, 0, width, style);
            string text = (Value ? "[x]" : "[ ]") + (Caption.Length > 0 ? " " + T(Caption) : " " + T(Value ? "Yes" : "No"));
            SurfaceText.Draw(surface, 0, 0, text, style, width);
        }

        #endregion
    }
}
