namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Dropdown field (vessel, captain, pipeline, status enums, language, theme, ...): shows the chosen label; Enter,
    /// Space, or Down opens a filterable <see cref="PickerModal{T}"/>; Left/Right step through options without opening.
    /// Raises <see cref="ValueChanged"/>. Not thread-safe.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public class SelectField<T> : ArmadaWidget, IFormField
    {
        #region Public-Members

        /// <summary>
        /// Options. Never null.
        /// </summary>
        public List<SelectOption<T>> Options { get; set; } = new List<SelectOption<T>>();

        /// <summary>
        /// Selected option, or null.
        /// </summary>
        public SelectOption<T>? Selected { get; private set; } = null;

        /// <summary>
        /// Selected value, or default.
        /// </summary>
        public T? Value
        {
            get { return Selected != null ? Selected.Value : default; }
        }

        /// <summary>
        /// English placeholder shown when nothing is selected.
        /// </summary>
        public string Placeholder { get; set; } = "Select...";

        /// <summary>
        /// English picker title.
        /// </summary>
        public string PickerTitle { get; set; } = "Select";

        /// <summary>
        /// Width multiplier for the picker modal (1.0 = default; clamped to 1.0-3.0).
        /// </summary>
        public double PickerWidthScale
        {
            get { return _PickerWidthScale; }
            set { _PickerWidthScale = Math.Clamp(value, 1.0, 3.0); }
        }

        /// <summary>
        /// Modal host used to open the picker. Required to open; without it Left/Right still cycle.
        /// </summary>
        public IModalHost? ModalHost { get; set; } = null;

        /// <summary>
        /// A selection is required (validation).
        /// </summary>
        public bool Required { get; set; } = false;

        /// <inheritdoc />
        public string? FieldError { get; private set; } = null;

        /// <inheritdoc />
        public object? FieldValue
        {
            get { return Value; }
        }

        /// <summary>
        /// Raised after the selection changes.
        /// </summary>
        public event EventHandler<ValueChangedEventArgs<T?>>? ValueChanged;

        #endregion

        #region Private-Members

        private double _PickerWidthScale = 1.0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Select by value without raising the event (initial state).
        /// </summary>
        /// <param name="value">Value.</param>
        public void SetValue(T? value)
        {
            Selected = Options.FirstOrDefault(o => EqualityComparer<T?>.Default.Equals(o.Value, value));
        }

        /// <summary>
        /// Select an option and raise the event when it changes.
        /// </summary>
        /// <param name="option">Option, or null to clear.</param>
        public void Choose(SelectOption<T>? option)
        {
            if (ReferenceEquals(option, Selected)) return;
            T? old = Value;
            Selected = option;
            if (FieldError != null) ValidateField();
            EventHandler<ValueChangedEventArgs<T?>>? handler = ValueChanged;
            if (handler != null) handler(this, new ValueChangedEventArgs<T?>(old, Value));
        }

        /// <summary>
        /// Open the picker.
        /// </summary>
        /// <returns>The modal, or null without a host.</returns>
        public PickerModal<T>? Open()
        {
            if (ModalHost == null) return null;
            PickerModal<T> picker = new PickerModal<T>(PickerTitle, Options, Localizer, Theme);
            picker.WidthScale = PickerWidthScale;
            if (Selected != null) picker.List.SelectValue(Selected.Value);
            ModalHost.Show(picker, result =>
            {
                if (result is SelectOption<T> chosen) Choose(chosen);
            });
            return picker;
        }

        /// <inheritdoc />
        public bool ValidateField()
        {
            FieldError = Required && Selected == null ? "A selection is required." : null;
            return FieldError == null;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Enter || key.Code == KeyCode.Down || (key.Code == KeyCode.Character && key.Rune == ' ' && key.Modifiers == KeyModifiers.None))
            {
                return Open() != null || key.Code != KeyCode.Down;
            }

            if (key.Code == KeyCode.Left || key.Code == KeyCode.Right)
            {
                if (Options.Count == 0) return true;
                int idx = Selected == null ? -1 : Options.IndexOf(Selected);
                idx = key.Code == KeyCode.Right ? (idx + 1) % Options.Count : (idx <= 0 ? Options.Count - 1 : idx - 1);
                Choose(Options[idx]);
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Press && mouse.Button == MouseButton.Left)
            {
                Open();
                return true;
            }

            return false;
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
            CellStyle style = IsFocused ? Theme.InputFocused : Theme.Input;
            SurfaceText.FillRow(surface, 0, 0, width, style);
            string text = Selected != null ? Selected.Label : T(Placeholder);
            CellStyle textStyle = Selected != null ? style : style.WithForeground(Theme.Muted.Foreground);
            SurfaceText.Draw(surface, 0, 0, text, textStyle, Math.Max(0, width - 2));
            if (width >= 2) surface.DrawText(width - 1, 0, "v", style);
        }

        #endregion
    }
}
