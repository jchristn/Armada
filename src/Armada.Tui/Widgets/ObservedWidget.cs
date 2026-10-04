namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Binding-layer adapter that wraps any TUIKit widget (TextField, TextEditor, ListView, Checkbox, RadioGroup, ...)
    /// and raises <see cref="Changed"/> whenever a key, paste, or mouse event changes the value read by a getter. This
    /// fills TUIKit gap U2 (no change events on most widgets) without polling. Not thread-safe.
    /// </summary>
    /// <typeparam name="T">Observed value type.</typeparam>
    public class ObservedWidget<T> : IWidget, IFocusable, IFocusAware, IMouseAware, IPasteTarget
    {
        #region Public-Members

        /// <summary>
        /// Wrapped widget.
        /// </summary>
        public IWidget Inner { get; }

        /// <summary>
        /// Current observed value.
        /// </summary>
        public T Value
        {
            get { return _Getter(); }
        }

        /// <summary>
        /// Raised when the observed value changes.
        /// </summary>
        public event EventHandler<ValueChangedEventArgs<T>>? Changed;

        #endregion

        #region Private-Members

        private readonly Func<T> _Getter;
        private readonly Func<string, bool>? _Paste;
        private T _Last;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="inner">Wrapped widget.</param>
        /// <param name="getter">Reads the observed value.</param>
        /// <param name="paste">Optional paste handler (for example <c>text => { field.Insert(text); return true; }</c>).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> or <paramref name="getter"/> is null.</exception>
        public ObservedWidget(IWidget inner, Func<T> getter, Func<string, bool>? paste = null)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _Getter = getter ?? throw new ArgumentNullException(nameof(getter));
            _Paste = paste;
            _Last = getter();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Size Measure(Size available)
        {
            return Inner.Measure(available);
        }

        /// <inheritdoc />
        public void Render(ISurface surface)
        {
            Inner.Render(surface);
        }

        /// <inheritdoc />
        public bool HandleKey(KeyEvent key)
        {
            bool handled = Inner is IFocusable f && f.HandleKey(key);
            Check();
            return handled;
        }

        /// <inheritdoc />
        public bool HandleMouse(MouseEvent mouse)
        {
            bool handled = Inner is IMouseAware m && m.HandleMouse(mouse);
            Check();
            return handled;
        }

        /// <inheritdoc />
        public void OnFocusChanged(bool focused)
        {
            if (Inner is IFocusAware aware) aware.OnFocusChanged(focused);
        }

        /// <inheritdoc />
        public bool HandlePaste(string text)
        {
            bool handled = _Paste != null && _Paste(text);
            Check();
            return handled;
        }

        /// <summary>
        /// Re-read the value and raise <see cref="Changed"/> when it differs (call after programmatic changes).
        /// </summary>
        public void Check()
        {
            T current = _Getter();
            if (EqualityComparer<T>.Default.Equals(current, _Last)) return;
            T old = _Last;
            _Last = current;
            EventHandler<ValueChangedEventArgs<T>>? handler = Changed;
            if (handler != null) handler(this, new ValueChangedEventArgs<T>(old, current));
        }

        #endregion
    }
}
