namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Services;
    using TUIKit.Modals;

    /// <summary>
    /// Modal host for widget tests: records shown modals and delivers their results synchronously once they close.
    /// </summary>
    public sealed class FakeModalHost : IModalHost
    {
        #region Public-Members

        /// <summary>
        /// Shown modals, newest last.
        /// </summary>
        public List<Modal> Shown { get; } = new List<Modal>();

        /// <inheritdoc />
        public bool IsModalOpen
        {
            get { return Shown.Count > 0 && !Shown[Shown.Count - 1].IsClosed; }
        }

        #endregion

        #region Private-Members

        private readonly Dictionary<Modal, Action<object?>?> _Callbacks = new Dictionary<Modal, Action<object?>?>();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void Show(Modal modal, Action<object?>? onClosed = null)
        {
            Shown.Add(modal);
            _Callbacks[modal] = onClosed;
        }

        /// <summary>
        /// Deliver the result of the newest closed modal.
        /// </summary>
        public void Complete()
        {
            Modal modal = Shown[Shown.Count - 1];
            object? result = modal.Completion.IsCompleted ? modal.Completion.Result : null;
            if (_Callbacks.TryGetValue(modal, out Action<object?>? cb) && cb != null) cb(result);
        }

        #endregion
    }
}
