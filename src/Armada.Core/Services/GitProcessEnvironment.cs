namespace Armada.Core.Services
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Shared launch settings for git and gh processes: credential prompts are disabled, and a fixed C locale
    /// and UTF-8 decoding keep output independent of the host language and preserve verbatim (-z) paths.
    /// </summary>
    public static class GitProcessEnvironment
    {
        #region Public-Methods

        /// <summary>
        /// Apply the shared Git prompt, locale, and encoding settings to a process start info. Requires UseShellExecute = false.
        /// </summary>
        /// <param name="startInfo">Start info to update.</param>
        public static void Apply(ProcessStartInfo startInfo)
        {
            if (startInfo == null) throw new ArgumentNullException(nameof(startInfo));
            startInfo.Environment["LC_ALL"] = "C";
            startInfo.Environment["LANG"] = "C";
            startInfo.Environment.Remove("LANGUAGE");
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GCM_INTERACTIVE"] = "Never";
            if (startInfo.RedirectStandardOutput) startInfo.StandardOutputEncoding = new UTF8Encoding(false);
            if (startInfo.RedirectStandardError) startInfo.StandardErrorEncoding = new UTF8Encoding(false);
        }

        /// <summary>
        /// True when the executable is git or gh (by file name, with or without a Windows extension).
        /// </summary>
        /// <param name="executable">Executable name or path.</param>
        /// <returns>True for git or gh.</returns>
        public static bool IsGitTool(string? executable)
        {
            if (String.IsNullOrWhiteSpace(executable)) return false;
            // Accept both separators so a Windows path is recognized on any host.
            string name = Path.GetFileNameWithoutExtension(executable.Trim().Replace('\\', '/'));
            return String.Equals(name, "git", StringComparison.OrdinalIgnoreCase)
                || String.Equals(name, "gh", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
