namespace Armada.Tui.Services
{
    using System;
    using System.Threading.Tasks;
    using Armada.Tui.Theming;
    using TUIKit.Hosting;
    using TUIKit.Modals;

    /// <summary>
    /// <see cref="IModalHost"/> over a TUIKit application: themes the modal, pushes it, and posts the result back to
    /// the loop. Thread-safe for <see cref="Show"/> callers on any thread only when invoked via the dispatcher; call on
    /// the UI loop thread.
    /// </summary>
    public class ModalService : IModalHost
    {
        #region Public-Members

        /// <inheritdoc />
        public bool IsModalOpen
        {
            get { return _App.Modals.IsActive; }
        }

        #endregion

        #region Private-Members

        private readonly TuiApplication _App;
        private readonly IUiDispatcher _Dispatcher;
        private readonly ThemeService _Theme;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="app">Application.</param>
        /// <param name="dispatcher">Dispatcher.</param>
        /// <param name="theme">Theme service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ModalService(TuiApplication app, IUiDispatcher dispatcher, ThemeService theme)
        {
            _App = app ?? throw new ArgumentNullException(nameof(app));
            _Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void Show(Modal modal, Action<object?>? onClosed = null)
        {
            if (modal == null) throw new ArgumentNullException(nameof(modal));
            ThemeApplicator.Apply(modal, _Theme.Current);
            if (modal is Modals.ArmadaDialog dialog) dialog.IsTopmost = () => ReferenceEquals(_App.Modals.Top, dialog);
            Task<object?> completion = _App.ShowAsync(modal);
            completion.ContinueWith(t =>
            {
                object? result = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                _Dispatcher.Post(() =>
                {
                    // A modal closed outside key handling (an async submit) stays on the stack until the next key,
                    // which TUIKit would then swallow; drop it now.
                    _App.Modals.RemoveClosed();
                    onClosed?.Invoke(result);
                });
            }, TaskScheduler.Default);
        }

        #endregion
    }
}
