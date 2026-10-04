namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Any / Yes / No filter field. Space, Enter, Left, and Right cycle the value; the label is always shown so the
    /// state never depends on color. Raises <see cref="ValueChanged"/>. Not thread-safe.
    /// </summary>
    public class TriStateField : ArmadaWidget, IFormField
    {
        #region Public-Members

        /// <summary>
        /// Current value.
        /// </summary>
        public TriStateEnum Value { get; private set; } = TriStateEnum.Any;

        /// <summary>
        /// The value as a nullable boolean (null for Any).
        /// </summary>
        public bool? AsBoolean
        {
            get { return Value == TriStateEnum.Any ? (bool?)null : Value == TriStateEnum.Yes; }
        }

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
        public event EventHandler<ValueChangedEventArgs<TriStateEnum>>? ValueChanged;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Set the value.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="raise">Raise the event.</param>
        public void SetValue(TriStateEnum value, bool raise = true)
        {
            if (value == Value) return;
            TriStateEnum old = Value;
            Value = value;
            if (!raise) return;
            EventHandler<ValueChangedEventArgs<TriStateEnum>>? handler = ValueChanged;
            if (handler != null) handler(this, new ValueChangedEventArgs<TriStateEnum>(old, value));
        }

        /// <inheritdoc />
        public bool ValidateField()
        {
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool forward = key.Code == KeyCode.Right || key.Code == KeyCode.Enter || (key.Code == KeyCode.Character && key.Rune == ' ' && key.Modifiers == KeyModifiers.None);
            bool backward = key.Code == KeyCode.Left;
            if (!forward && !backward) return false;
            int next = ((int)Value + (forward ? 1 : 2)) % 3;
            SetValue((TriStateEnum)next);
            return true;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press) return false;
            SetValue((TriStateEnum)(((int)Value + 1) % 3));
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
            CellStyle style = IsFocused ? Theme.InputFocused : Theme.Input;
            SurfaceText.FillRow(surface, 0, 0, surface.Size.Width, style);
            string mark = Value == TriStateEnum.Yes ? "[x] " : Value == TriStateEnum.No ? "[-] " : "[ ] ";
            SurfaceText.Draw(surface, 0, 0, mark + T(Value.ToString()), style, surface.Size.Width);
        }

        #endregion
    }
}
