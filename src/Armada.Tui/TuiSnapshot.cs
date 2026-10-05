namespace Armada.Tui
{
    using System;
    using Armada.Tui.Shell;
    using TUIKit;
    using TUIKit.Hosting;
    using TUIKit.Testing;

    /// <summary>
    /// Renders the whole TUI (shell plus open modals) into an in-memory buffer and returns it as text, exactly as the
    /// host composes a frame (the shell is bound to a single full-screen region with no padding). TUIKit does not
    /// expose its composed frame, so this mirrors the compose order: shell, then modals. In ASCII icon mode the text is
    /// transliterated exactly as the terminal output is. Use on the UI loop thread.
    /// </summary>
    public static class TuiSnapshot
    {
        #region Public-Methods

        /// <summary>
        /// Render to text.
        /// </summary>
        /// <param name="shell">Shell.</param>
        /// <param name="app">Application (for the modal stack), or null.</param>
        /// <param name="width">Columns.</param>
        /// <param name="height">Rows.</param>
        /// <returns>Text, one line per row.</returns>
        public static string Render(ShellView shell, TuiApplication? app, int width, int height)
        {
            if (shell == null) throw new ArgumentNullException(nameof(shell));
            CellBuffer buffer = new CellBuffer(Math.Max(1, width), Math.Max(1, height));
            BufferSurface surface = new BufferSurface(buffer);
            shell.Render(surface);
            if (app != null && app.Modals.IsActive) app.Modals.Render(surface);
            string text = Snapshot.ToText(buffer);
            return shell.Theme.AsciiGlyphs ? Theming.AsciiGlyphs.Transliterate(text) : text;
        }

        #endregion
    }
}
