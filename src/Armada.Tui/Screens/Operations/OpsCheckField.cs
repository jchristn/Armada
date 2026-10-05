namespace Armada.Tui.Screens.Operations
{
    using System;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A form checkbox (the dashboard's checkbox rows): <c>[x] label</c>, toggled with <c>Space</c>, <c>Enter</c>, or a
    /// click. Not thread-safe.
    /// </summary>
    public class OpsCheckField : ArmadaWidget, IFormField
    {
        #region Public-Members

        /// <summary>
        /// Checked state.
        /// </summary>
        public bool Checked
        {
            get { return _Checked; }
            set
            {
                if (_Checked == value) return;
                _Checked = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// English label drawn after the box.
        /// </summary>
        public string Label { get; set; } = "";

        /// <inheritdoc />
        public object? FieldValue
        {
            get { return _Checked; }
        }

        /// <inheritdoc />
        public string? FieldError
        {
            get { return null; }
        }

        /// <summary>
        /// Raised after the state changes.
        /// </summary>
        public event EventHandler? Changed;

        #endregion

        #region Private-Members

        private bool _Checked = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="isChecked">Initial state.</param>
        public OpsCheckField(string label, bool isChecked = false)
        {
            Label = label ?? "";
            _Checked = isChecked;
        }

        #endregion

        #region Public-Methods

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
                Checked = !Checked;
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press) return false;
            Checked = !Checked;
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
            CellStyle style = IsFocused ? Theme.InputFocused : Theme.Text;
            Armada.Tui.Text.SurfaceText.FillRow(surface, 0, 0, surface.Size.Width, style);
            Armada.Tui.Text.SurfaceText.Draw(surface, 0, 0, (_Checked ? "[x] " : "[ ] ") + T(Label), style, surface.Size.Width);
        }

        #endregion
    }
}
