namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Protocol;

    /// <summary>
    /// Decodes Claude Code's <c>--output-format stream-json</c> for a mission: assistant messages report activity (each
    /// text, thinking, and tool_use block), and the terminal result event stands for the reply text that <c>--print</c>
    /// prints in text mode. System and user (tool result) events stand for nothing.
    /// </summary>
    public class ClaudeMissionStreamDecoder : MissionStreamDecoder
    {
        #region Public-Methods

        /// <inheritdoc />
        public override MissionStreamDecodeResult Decode(string line, DateTime nowUtc)
        {
            if (!ClaudeStreamLine.TryParse(line, out ClaudeStreamLine? evt) || evt == null) return MissionStreamDecodeResult.Plain();

            MissionStreamDecodeResult result = MissionStreamDecodeResult.Empty();
            if (String.Equals(evt.Type, ClaudeStreamLine.TypeResult, StringComparison.Ordinal))
            {
                result.AddOutputText(evt.Result);
                return result;
            }

            result.Activities.AddRange(RuntimeActivityParser.FromClaude(evt, nowUtc));
            return result;
        }

        #endregion
    }
}
