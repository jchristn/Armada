namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;
    using Armada.Core.Protocol;

    /// <summary>
    /// Decodes <c>codex exec --json</c> for a mission: items report activity (commands and MCP tool calls as they start,
    /// file changes, reasoning, and agent messages as they complete), each agent message stands for the transcript line
    /// text mode prints on stderr (so protocol lines in it are read as they arrive, as before), the turn's last agent
    /// message stands for the final message <c>codex exec</c> prints on stdout in text mode (printed when the turn
    /// completes or fails, or when the process ends first), and an error event stands for its message. The final message file (--output-last-message) is
    /// written as before.
    /// </summary>
    public class CodexMissionStreamDecoder : MissionStreamDecoder
    {
        #region Private-Members

        private string? _LastMessage = null;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override MissionStreamDecodeResult Decode(string line, DateTime nowUtc)
        {
            if (!CodexStreamEvent.TryParse(line, out CodexStreamEvent? evt) || evt == null) return MissionStreamDecodeResult.Plain();

            MissionStreamDecodeResult result = MissionStreamDecodeResult.Empty();
            RuntimeActivity? activity = RuntimeActivityParser.FromCodex(evt, nowUtc);
            if (activity != null) result.Activities.Add(activity);

            if (String.Equals(evt.Type, CodexStreamEvent.TypeItemCompleted, StringComparison.Ordinal)
                && evt.Item != null
                && String.Equals(evt.Item.Type, CodexStreamItem.TypeAgentMessage, StringComparison.Ordinal)
                && !String.IsNullOrWhiteSpace(evt.Item.Text))
            {
                _LastMessage = evt.Item.Text;
                result.AddTranscriptText(evt.Item.Text);
            }
            else if (String.Equals(evt.Type, CodexStreamEvent.TypeTurnCompleted, StringComparison.Ordinal)
                || String.Equals(evt.Type, CodexStreamEvent.TypeTurnFailed, StringComparison.Ordinal))
            {
                result.AddOutputText(_LastMessage);
                _LastMessage = null;
            }
            else if (String.Equals(evt.Type, CodexStreamEvent.TypeError, StringComparison.Ordinal))
            {
                result.AddOutputText(evt.Message);
            }

            return result;
        }

        /// <inheritdoc />
        public override MissionStreamDecodeResult Flush(DateTime nowUtc)
        {
            MissionStreamDecodeResult result = MissionStreamDecodeResult.Empty();
            result.AddOutputText(_LastMessage);
            _LastMessage = null;
            return result;
        }

        #endregion
    }
}
