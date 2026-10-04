namespace Armada.Tui.Services
{
    using System;
    using TUIKit.Terminal;

    /// <summary>
    /// <see cref="ITerminalOutput"/> over a TUIKit backend. Writes are serialized with a lock.
    /// </summary>
    public class BackendTerminalOutput : ITerminalOutput
    {
        #region Public-Members

        /// <inheritdoc />
        public bool SupportsClipboard
        {
            get { return _Backend.Capabilities.ClipboardOsc52; }
        }

        /// <inheritdoc />
        public bool IsInteractive
        {
            get { return _Backend.IsInteractive; }
        }

        #endregion

        #region Private-Members

        private readonly ITerminalBackend _Backend;
        private readonly object _Lock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="backend">Backend.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="backend"/> is null.</exception>
        public BackendTerminalOutput(ITerminalBackend backend)
        {
            _Backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void Write(string text)
        {
            if (String.IsNullOrEmpty(text)) return;
            lock (_Lock)
            {
                _Backend.Write(text);
                _Backend.Flush();
            }
        }

        #endregion
    }
}
