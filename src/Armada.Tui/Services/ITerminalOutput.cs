namespace Armada.Tui.Services
{
    /// <summary>
    /// Direct access to the terminal for out-of-band escape sequences (OSC 52 clipboard, bell, OSC 9/777
    /// notifications). Implementations are thread-safe.
    /// </summary>
    public interface ITerminalOutput
    {
        /// <summary>
        /// True when the terminal advertises OSC 52 clipboard support.
        /// </summary>
        bool SupportsClipboard { get; }

        /// <summary>
        /// True when output reaches an interactive terminal.
        /// </summary>
        bool IsInteractive { get; }

        /// <summary>
        /// Write raw text (escape sequences) and flush.
        /// </summary>
        /// <param name="text">Text.</param>
        void Write(string text);
    }
}
