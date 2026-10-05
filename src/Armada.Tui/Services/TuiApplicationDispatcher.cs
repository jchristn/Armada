namespace Armada.Tui.Services
{
    using System;
    using TUIKit.Hosting;

    /// <summary>
    /// <see cref="IUiDispatcher"/> over <see cref="TuiApplication.Post"/>. After each posted action runs (on the loop
    /// thread) it calls <see cref="AfterRun"/>, which the TUI uses to wake the frame governor. Thread-safe.
    /// </summary>
    public class TuiApplicationDispatcher : IUiDispatcher
    {
        #region Public-Members

        /// <summary>
        /// Called on the loop thread after each posted action runs (also when it throws), or null.
        /// </summary>
        public Action? AfterRun { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly TuiApplication _App;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="app">Application.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public TuiApplicationDispatcher(TuiApplication app)
        {
            _App = app ?? throw new ArgumentNullException(nameof(app));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            _App.Post(() =>
            {
                try
                {
                    action();
                }
                finally
                {
                    AfterRun?.Invoke();
                }
            });
        }

        #endregion
    }
}
