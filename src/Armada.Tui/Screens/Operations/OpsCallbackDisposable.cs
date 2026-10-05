namespace Armada.Tui.Screens.Operations
{
    using System;

    /// <summary>
    /// Runs an action once when disposed (unhooking event handlers when a screen closes).
    /// </summary>
    public sealed class OpsCallbackDisposable : IDisposable
    {
        #region Private-Members

        private Action? _Action;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="action">Action to run on dispose.</param>
        public OpsCallbackDisposable(Action action)
        {
            _Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the action (once).
        /// </summary>
        public void Dispose()
        {
            Action? a = _Action;
            _Action = null;
            a?.Invoke();
        }

        #endregion
    }
}
