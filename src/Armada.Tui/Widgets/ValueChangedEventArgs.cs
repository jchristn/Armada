namespace Armada.Tui.Widgets
{
    using System;

    /// <summary>
    /// Old and new value of a field or selection (the binding layer's change event, TUIKit gap U2).
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public class ValueChangedEventArgs<T> : EventArgs
    {
        #region Public-Members

        /// <summary>
        /// Previous value.
        /// </summary>
        public T OldValue { get; }

        /// <summary>
        /// New value.
        /// </summary>
        public T NewValue { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="oldValue">Previous value.</param>
        /// <param name="newValue">New value.</param>
        public ValueChangedEventArgs(T oldValue, T newValue)
        {
            OldValue = oldValue;
            NewValue = newValue;
        }

        #endregion
    }
}
