namespace Armada.Tui.Services
{
    using System;
    using System.Threading;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Terminal;

    /// <summary>
    /// A pass-through <see cref="ITerminalBackend"/> that the TUI wraps around the real backend (console or headless) so
    /// it can (1) notice input as soon as it is read, which wakes the frame governor (W8.5), and (2) write ASCII
    /// equivalents of box-drawing, block, Braille, arrow, and bullet glyphs while ASCII icon mode is on (W8.4; see
    /// <see cref="AsciiGlyphs"/>). Everything else is delegated unchanged. <see cref="ReadInput"/> and
    /// <see cref="Write"/> are called on the TUIKit loop thread; <see cref="AsciiOutput"/> may be set from any thread.
    /// </summary>
    public sealed class TerminalBackendAdapter : ITerminalBackend
    {
        #region Public-Members

        /// <summary>
        /// The wrapped backend.
        /// </summary>
        public ITerminalBackend Inner { get; }

        /// <summary>
        /// Transliterate non-ASCII glyphs in everything written to the terminal.
        /// </summary>
        public bool AsciiOutput
        {
            get { return Volatile.Read(ref _AsciiOutput); }
            set { Volatile.Write(ref _AsciiOutput, value); }
        }

        /// <summary>
        /// Number of reads that returned input bytes.
        /// </summary>
        public long InputReads
        {
            get { return Interlocked.Read(ref _InputReads); }
        }

        /// <summary>
        /// Raised after a read that returned input bytes, on the thread that read them.
        /// </summary>
        public event Action? InputReceived;

        /// <inheritdoc />
        public TerminalCapabilities Capabilities
        {
            get { return Inner.Capabilities; }
        }

        /// <inheritdoc />
        public Size Size
        {
            get { return Inner.Size; }
        }

        /// <inheritdoc />
        public bool IsInteractive
        {
            get { return Inner.IsInteractive; }
        }

        #endregion

        #region Private-Members

        private bool _AsciiOutput = false;
        private long _InputReads = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Wrap a backend.
        /// </summary>
        /// <param name="inner">Backend.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is null.</exception>
        public TerminalBackendAdapter(ITerminalBackend inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void Start()
        {
            Inner.Start();
        }

        /// <inheritdoc />
        public void Write(string data)
        {
            Inner.Write(AsciiOutput ? AsciiGlyphs.Transliterate(data) : data);
        }

        /// <inheritdoc />
        public void Flush()
        {
            Inner.Flush();
        }

        /// <inheritdoc />
        public int ReadInput(byte[] buffer, int offset, int count)
        {
            int read = Inner.ReadInput(buffer, offset, count);
            if (read > 0)
            {
                Interlocked.Increment(ref _InputReads);
                Action? handler = InputReceived;
                if (handler != null) handler();
            }

            return read;
        }

        /// <inheritdoc />
        public void Stop()
        {
            Inner.Stop();
        }

        /// <summary>
        /// Does nothing: the wrapped backend belongs to whoever created it and is disposed by them.
        /// </summary>
        public void Dispose()
        {
        }

        #endregion
    }
}
