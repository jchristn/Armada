namespace Armada.Tui.Theming
{
    using System;

    /// <summary>
    /// Decides whether the terminal can display UTF-8 (used when the glyph mode is Auto). Windows: Windows Terminal
    /// (<c>WT_SESSION</c>), ConEmu, or a terminal that sets <c>TERM_PROGRAM</c> is UTF-8; legacy conhost is treated as
    /// ASCII. Elsewhere: <c>TERM=dumb</c> is ASCII; otherwise the first of <c>LC_ALL</c>, <c>LC_CTYPE</c>, and
    /// <c>LANG</c> that is set decides (UTF-8 only when it names UTF-8, so <c>C</c> and <c>POSIX</c> are ASCII); when none
    /// is set the terminal is assumed to be UTF-8. Thread-safe (stateless).
    /// </summary>
    public static class TerminalEncoding
    {
        #region Public-Methods

        /// <summary>
        /// True when the terminal described by the environment can display UTF-8.
        /// </summary>
        /// <param name="environment">Environment variable reader.</param>
        /// <param name="isWindows">True on Windows.</param>
        /// <returns>True for UTF-8.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="environment"/> is null.</exception>
        public static bool SupportsUtf8(Func<string, string?> environment, bool isWindows)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            string term = environment("TERM") ?? "";
            if (String.Equals(term.Trim(), "dumb", StringComparison.OrdinalIgnoreCase)) return false;
            if (isWindows)
            {
                if (!String.IsNullOrEmpty(environment("WT_SESSION"))) return true;
                if (!String.IsNullOrEmpty(environment("TERM_PROGRAM"))) return true;
                if (String.Equals(environment("ConEmuANSI"), "ON", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }

            foreach (string name in new string[] { "LC_ALL", "LC_CTYPE", "LANG" })
            {
                string? value = environment(name);
                if (String.IsNullOrWhiteSpace(value)) continue;
                string v = value!.ToLowerInvariant();
                return v.Contains("utf-8") || v.Contains("utf8");
            }

            return true;
        }

        /// <summary>
        /// <see cref="SupportsUtf8(Func{string, string?}, bool)"/> for the current process.
        /// </summary>
        /// <returns>True for UTF-8.</returns>
        public static bool CurrentSupportsUtf8()
        {
            return SupportsUtf8(Environment.GetEnvironmentVariable, OperatingSystem.IsWindows());
        }

        #endregion
    }
}
