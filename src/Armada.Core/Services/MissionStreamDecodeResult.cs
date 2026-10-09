namespace Armada.Core.Services
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// What one line of a captain's structured output means for a mission: the readable output lines it stands for (the
    /// lines the CLI prints on stdout in text mode, such as Claude Code's final reply), the transcript lines it stands for
    /// (what the CLI prints on stderr in text mode, such as Codex's running agent messages), and the activities it reports.
    /// </summary>
    public class MissionStreamDecodeResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the line was one of the runtime's protocol events. When false the line is plain text and is handled
        /// as text-mode output.
        /// </summary>
        public bool Structured { get; set; } = false;

        /// <summary>
        /// Readable output lines the event stands for, in order (never blank).
        /// </summary>
        public List<string> OutputLines { get; set; } = new List<string>();

        /// <summary>
        /// Readable transcript lines the event stands for, in order (never blank): what the CLI prints on stderr in text
        /// mode, raised as stderr so the mission reads them as before.
        /// </summary>
        public List<string> TranscriptLines { get; set; } = new List<string>();

        /// <summary>
        /// Activities the event reports, in order; the last is the newest.
        /// </summary>
        public List<RuntimeActivity> Activities { get; set; } = new List<RuntimeActivity>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// A result for a line that is not a protocol event.
        /// </summary>
        /// <returns>The result.</returns>
        public static MissionStreamDecodeResult Plain()
        {
            return new MissionStreamDecodeResult { Structured = false };
        }

        /// <summary>
        /// A result for a protocol event that stands for nothing readable.
        /// </summary>
        /// <returns>The result.</returns>
        public static MissionStreamDecodeResult Empty()
        {
            return new MissionStreamDecodeResult { Structured = true };
        }

        /// <summary>
        /// Add the non-blank lines of a text as output lines.
        /// </summary>
        /// <param name="text">Text, possibly multi-line or null.</param>
        public void AddOutputText(string? text)
        {
            AddLines(OutputLines, text);
        }

        /// <summary>
        /// Add the non-blank lines of a text as transcript lines.
        /// </summary>
        /// <param name="text">Text, possibly multi-line or null.</param>
        public void AddTranscriptText(string? text)
        {
            AddLines(TranscriptLines, text);
        }

        #endregion

        #region Private-Methods

        private static void AddLines(List<string> lines, string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (string raw in text!.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length > 0) lines.Add(line);
            }
        }

        #endregion
    }
}
