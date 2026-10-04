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
    /// Multi-choice field (status filters, vessel selections): shows the chosen labels (or "N selected"); Enter or
    /// Space opens a <see cref="MultiPickerModal{T}"/>. Raises <see cref="ValueChanged"/>. Not thread-safe.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public class MultiSelectField<T> : ArmadaWidget, IFormField
    {
        #region Public-Members

        /// <summary>
        /// Options. Never null.
        /// </summary>
        public List<SelectOption<T>> Options { get; set; } = new List<SelectOption<T>>();

        /// <summary>
        /// Selected values in option order. Never null.
        /// </summary>
        public List<T> Values { get; private set; } = new List<T>();

        /// <summary>
        /// English placeholder shown when nothing is selected.
        /// </summary>
        public string Placeholder { get; set; } = "Any";

        /// <summary>
        /// English picker title.
        /// </summary>
        public string PickerTitle { get; set; } = "Select";

        /// <summary>
        /// Modal host used to open the picker.
        /// </summary>
        public IModalHost? ModalHost { get; set; } = null;

        /// <inheritdoc />
        public string? FieldError
        {
            get { return null; }
        }

        /// <inheritdoc />
        public object? FieldValue
        {
            get { return String.Join(",", Values.Select(v => v?.ToString())); }
        }

        /// <summary>
        /// Raised after the selection changes.
        /// </summary>
        public event EventHandler<ValueChangedEventArgs<IReadOnlyList<T>>>? ValueChanged;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the selection and raise the event when it changes.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <param name="raise">Raise <see cref="ValueChanged"/>.</param>
        public void SetValues(IEnumerable<T>? values, bool raise = true)
        {
            List<T> old = Values;
            HashSet<T> wanted = new HashSet<T>(values ?? Enumerable.Empty<T>());
            Values = Options.Where(o => wanted.Contains(o.Value)).Select(o => o.Value).ToList();
            if (!raise || old.SequenceEqual(Values)) return;
            EventHandler<ValueChangedEventArgs<IReadOnlyList<T>>>? handler = ValueChanged;
            if (handler != null) handler(this, new ValueChangedEventArgs<IReadOnlyList<T>>(old, Values));
        }

        /// <summary>
        /// Open the picker.
        /// </summary>
        /// <returns>The modal, or null without a host.</returns>
        public MultiPickerModal<T>? Open()
        {
            if (ModalHost == null) return null;
            MultiPickerModal<T> picker = new MultiPickerModal<T>(PickerTitle, Options, Values, Localizer, Theme);
            ModalHost.Show(picker, result =>
            {
                if (result is List<T> chosen) SetValues(chosen);
            });
            return picker;
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
                Open();
                return true;
            }

            if (key.Code == KeyCode.Delete || key.Code == KeyCode.Backspace)
            {
                SetValues(null);
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
            string text;
            if (Values.Count == 0) text = T(Placeholder);
            else
            {
                string joined = String.Join(", ", Options.Where(o => Values.Contains(o.Value)).Select(o => o.Label));
                text = TextCells.Width(joined) <= width - 2 ? joined : Values.Count + " " + T("selected");
            }

            CellStyle textStyle = Values.Count == 0 ? style.WithForeground(Theme.Muted.Foreground) : style;
            SurfaceText.Draw(surface, 0, 0, text, textStyle, Math.Max(0, width - 2));
            if (width >= 2) surface.DrawText(width - 1, 0, "v", style);
        }

        #endregion
    }
}
