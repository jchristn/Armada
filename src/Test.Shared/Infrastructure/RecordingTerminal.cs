namespace Test.Shared.Infrastructure
{
    using System.Text;
    using Armada.Tui.Services;

    /// <summary>
    /// Terminal output that records what was written.
    /// </summary>
    public sealed class RecordingTerminal : ITerminalOutput
    {
        private readonly StringBuilder _Written = new StringBuilder();

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="clipboard">Advertise OSC 52.</param>
        public RecordingTerminal(bool clipboard)
        {
            SupportsClipboard = clipboard;
        }

        /// <inheritdoc />
        public bool SupportsClipboard { get; }

        /// <inheritdoc />
        public bool IsInteractive
        {
            get { return true; }
        }

        /// <summary>
        /// Everything written.
        /// </summary>
        public string Written
        {
            get { return _Written.ToString(); }
        }

        /// <inheritdoc />
        public void Write(string text)
        {
            _Written.Append(text);
        }
    }
}
