namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Decodes a mission captain's structured stdout (Claude Code stream-json, Codex exec --json) line by line into the
    /// readable output the CLI prints in text mode, so a mission's final message, AgentOutput, and protocol lines read as
    /// they always have, plus the captain's live activity. One instance per launch: a decoder may hold state across lines
    /// (Codex's last agent message is printed when the turn ends).
    /// </summary>
    public abstract class MissionStreamDecoder
    {
        #region Public-Methods

        /// <summary>
        /// Whether a runtime has structured output a mission can stream (Claude Code and Codex).
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>True when <see cref="For"/> returns a decoder for it.</returns>
        public static bool Supports(AgentRuntimeEnum runtime)
        {
            return runtime == AgentRuntimeEnum.ClaudeCode || runtime == AgentRuntimeEnum.Codex;
        }

        /// <summary>
        /// A new decoder for a runtime, or null when the runtime has no structured mission output (it stays plain text).
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>The decoder, or null.</returns>
        public static MissionStreamDecoder? For(AgentRuntimeEnum runtime)
        {
            switch (runtime)
            {
                case AgentRuntimeEnum.ClaudeCode: return new ClaudeMissionStreamDecoder();
                case AgentRuntimeEnum.Codex: return new CodexMissionStreamDecoder();
                default: return null;
            }
        }

        /// <summary>
        /// Decode one stdout line.
        /// </summary>
        /// <param name="line">Raw line.</param>
        /// <param name="nowUtc">When it arrived, UTC.</param>
        /// <returns>What the line stands for.</returns>
        public abstract MissionStreamDecodeResult Decode(string line, DateTime nowUtc);

        /// <summary>
        /// Output still held when the process ends (for example a Codex message whose turn never completed). The default
        /// holds nothing.
        /// </summary>
        /// <param name="nowUtc">Now, UTC.</param>
        /// <returns>What is left.</returns>
        public virtual MissionStreamDecodeResult Flush(DateTime nowUtc)
        {
            return MissionStreamDecodeResult.Empty();
        }

        #endregion
    }
}
