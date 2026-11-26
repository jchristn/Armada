namespace Armada.Runtimes
{
    using System;
    using System.Text;
    using Armada.Core.Protocol;

    /// <summary>
    /// Final-message capture and exit-code resolution for <c>opencode run --format json</c>. stdout carries JSON events,
    /// so the reply is rebuilt from the <c>text</c> events of the last turn (the text after the last tool call), never from
    /// <c>reasoning</c>, <c>tool_use</c> or <c>error</c> events. A run whose last event of note is an <c>error</c> (no
    /// reply text followed it) failed: no final message is written and exit 0 is reported as
    /// <see cref="FailedRunExitCode"/>.
    /// </summary>
    public sealed class OpenCodeFinalMessage
    {
        #region Public-Members

        /// <summary>
        /// Exit code reported for a run OpenCode ended with exit 0 that its stream shows failed.
        /// </summary>
        public const int FailedRunExitCode = 1;

        /// <summary>
        /// Most characters kept; a longer reply keeps its end, where the result and markers are.
        /// </summary>
        public const int MaxCharacters = 524288;

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly StringBuilder _Turn = new StringBuilder();
        private string? _Path = null;
        private bool _TurnClosed = false;
        private bool _ErrorAfterText = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start a launch: remember its final-message file and drop any earlier state.
        /// </summary>
        /// <param name="finalMessageFilePath">The launch's final-message file, or null.</param>
        public void Begin(string? finalMessageFilePath)
        {
            lock (_Lock)
            {
                _Path = String.IsNullOrEmpty(finalMessageFilePath) ? null : finalMessageFilePath;
                _Turn.Clear();
                _TurnClosed = false;
                _ErrorAfterText = false;
            }
        }

        /// <summary>
        /// Observe one stdout line. Lines that are not OpenCode events are ignored.
        /// </summary>
        /// <param name="line">The line.</param>
        public void Observe(string? line)
        {
            if (!OpenCodeStreamEvent.TryParse(line, out OpenCodeStreamEvent? evt) || evt == null) return;
            lock (_Lock)
            {
                if (String.Equals(evt.Type, OpenCodeStreamEvent.TypeError, StringComparison.Ordinal))
                {
                    _ErrorAfterText = true;
                    return;
                }

                if (String.Equals(evt.Type, OpenCodeStreamEvent.TypeToolUse, StringComparison.Ordinal))
                {
                    _TurnClosed = true;
                    return;
                }

                string? text = evt.AssistantText;
                if (String.IsNullOrEmpty(text)) return;
                if (_TurnClosed)
                {
                    _Turn.Clear();
                    _TurnClosed = false;
                }

                if (_Turn.Length > 0) _Turn.Append('\n');
                _Turn.Append(text);
                if (_Turn.Length > MaxCharacters) _Turn.Remove(0, _Turn.Length - MaxCharacters);
                _ErrorAfterText = false;
            }
        }

        /// <summary>
        /// The reply rebuilt so far, trimmed (empty when none).
        /// </summary>
        /// <returns>The reply text.</returns>
        public string Text()
        {
            lock (_Lock) return _Turn.ToString().Trim();
        }

        /// <summary>
        /// Resolve the launch's exit code and, on success, write the final message when the file is missing or empty. A
        /// nonzero or unknown exit code is returned unchanged. Exit 0 becomes <see cref="FailedRunExitCode"/> when an
        /// error event arrived and no reply text followed it.
        /// </summary>
        /// <param name="exitCode">The process exit code, or null when unknown.</param>
        /// <returns>The resolved exit code.</returns>
        public int? Complete(int? exitCode)
        {
            if (exitCode != 0) return exitCode;
            string? path;
            string text;
            bool failed;
            lock (_Lock)
            {
                path = _Path;
                text = _Turn.ToString().Trim();
                failed = _ErrorAfterText;
            }

            if (failed) return FailedRunExitCode;
            MuxRuntime.WriteFinalMessageIfMissing(path, text);
            return exitCode;
        }

        #endregion
    }
}
