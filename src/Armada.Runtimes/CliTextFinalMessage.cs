namespace Armada.Runtimes
{
    using System;
    using System.Text;

    /// <summary>
    /// Final-message capture for CLIs that run in plain-text mode (Gemini, Cursor). In that mode stdout carries
    /// the agent's reply and stderr carries tool progress and diagnostics, so on a clean exit the collected stdout is
    /// written as the launch's final-message file, as Claude Code, Codex and Mux already do. A mission's verdict, plan and
    /// handoff are then read from the reply instead of from a transcript mixed with stderr. A file the CLI wrote itself is
    /// never replaced, and nothing is written for a failed run or an empty reply.
    /// </summary>
    public sealed class CliTextFinalMessage
    {
        #region Public-Members

        /// <summary>
        /// Most characters kept; a longer reply keeps its end, where the result and markers are.
        /// </summary>
        public const int MaxCharacters = 524288;

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly StringBuilder _Text = new StringBuilder();
        private string? _Path = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start a launch: remember its final-message file and drop any earlier reply.
        /// </summary>
        /// <param name="finalMessageFilePath">The launch's final-message file, or null.</param>
        public void Begin(string? finalMessageFilePath)
        {
            lock (_Lock)
            {
                _Path = String.IsNullOrEmpty(finalMessageFilePath) ? null : finalMessageFilePath;
                _Text.Clear();
            }
        }

        /// <summary>
        /// Record one stdout line of the reply.
        /// </summary>
        /// <param name="line">The line.</param>
        public void Observe(string? line)
        {
            if (line == null) return;
            lock (_Lock)
            {
                if (_Path == null) return;
                _Text.Append(line).Append('\n');
                if (_Text.Length > MaxCharacters) _Text.Remove(0, _Text.Length - MaxCharacters);
            }
        }

        /// <summary>
        /// The reply collected so far, trimmed.
        /// </summary>
        /// <returns>The reply text.</returns>
        public string Text()
        {
            lock (_Lock) return _Text.ToString().Trim();
        }

        /// <summary>
        /// On a clean exit, write the collected reply to the final-message file when it is missing or empty.
        /// </summary>
        /// <param name="exitCode">The process exit code, or null when unknown.</param>
        /// <returns>True when the file was written.</returns>
        public bool Complete(int? exitCode)
        {
            if (exitCode != 0) return false;
            string? path;
            string text;
            lock (_Lock)
            {
                path = _Path;
                text = _Text.ToString().Trim();
            }
            return MuxRuntime.WriteFinalMessageIfMissing(path, text);
        }

        #endregion
    }
}
