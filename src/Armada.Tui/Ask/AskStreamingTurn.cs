namespace Armada.Tui.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// The captain reply being streamed for the current turn (the dashboard's <c>StreamingTurn</c>), plus the local
    /// timing the TUI uses for its metrics line (time to first token, chunk rate, approximate tokens).
    /// </summary>
    public class AskStreamingTurn
    {
        #region Public-Members

        /// <summary>
        /// Turn id.
        /// </summary>
        public string TurnId { get; }

        /// <summary>
        /// Streamed reply text.
        /// </summary>
        public string Text
        {
            get { return _Text.ToString(); }
        }

        /// <summary>
        /// Length of the streamed text (cheap change detection).
        /// </summary>
        public int TextLength
        {
            get { return _Text.Length; }
        }

        /// <summary>
        /// Streamed thinking text.
        /// </summary>
        public string Thinking
        {
            get { return _Thinking.ToString(); }
        }

        /// <summary>
        /// Length of the streamed thinking text.
        /// </summary>
        public int ThinkingLength
        {
            get { return _Thinking.Length; }
        }

        /// <summary>
        /// Tool chips in call order. Never null.
        /// </summary>
        public List<AskToolChip> Tools { get; set; } = new List<AskToolChip>();

        /// <summary>
        /// Set once <c>ask.turn</c> reports a terminal state; the persisted message then replaces the stream.
        /// </summary>
        public bool Finished { get; set; } = false;

        /// <summary>
        /// Persisted message id reported by the terminal <c>ask.turn</c>, or null.
        /// </summary>
        public string? FinalMessageId { get; set; } = null;

        /// <summary>
        /// When the TUI started following the turn.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the first reply chunk arrived, or null.
        /// </summary>
        public DateTime? FirstChunkUtc { get; set; } = null;

        /// <summary>
        /// When the last reply chunk arrived, or null.
        /// </summary>
        public DateTime? LastChunkUtc { get; set; } = null;

        /// <summary>
        /// When the turn finished, or null.
        /// </summary>
        public DateTime? FinishedUtc { get; set; } = null;

        /// <summary>
        /// Reply chunks received.
        /// </summary>
        public int ChunkCount { get; set; } = 0;

        #endregion

        #region Private-Members

        private readonly StringBuilder _Text = new StringBuilder();
        private readonly StringBuilder _Thinking = new StringBuilder();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="turnId">Turn id.</param>
        /// <param name="startedUtc">Start time.</param>
        public AskStreamingTurn(string turnId, DateTime startedUtc)
        {
            TurnId = turnId ?? "";
            StartedUtc = startedUtc;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Append reply text.
        /// </summary>
        /// <param name="delta">Text.</param>
        /// <param name="nowUtc">Arrival time.</param>
        public void AppendText(string delta, DateTime nowUtc)
        {
            if (String.IsNullOrEmpty(delta)) return;
            _Text.Append(delta);
            ChunkCount++;
            if (FirstChunkUtc == null) FirstChunkUtc = nowUtc;
            LastChunkUtc = nowUtc;
        }

        /// <summary>
        /// Append thinking text.
        /// </summary>
        /// <param name="delta">Text.</param>
        public void AppendThinking(string delta)
        {
            if (!String.IsNullOrEmpty(delta)) _Thinking.Append(delta);
        }

        /// <summary>
        /// Build the metrics for this turn at a point in time.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Metrics.</returns>
        public AskTurnMetrics Metrics(DateTime nowUtc)
        {
            AskTurnMetrics m = new AskTurnMetrics();
            DateTime end = FinishedUtc ?? nowUtc;
            m.TotalMs = Math.Max(0, (end - StartedUtc).TotalMilliseconds);
            if (FirstChunkUtc != null) m.FirstTokenMs = Math.Max(0, (FirstChunkUtc.Value - StartedUtc).TotalMilliseconds);
            m.ApproximateTokens = (_Text.Length + 3) / 4;
            if (FirstChunkUtc != null && LastChunkUtc != null)
            {
                double seconds = (LastChunkUtc.Value - FirstChunkUtc.Value).TotalSeconds;
                if (seconds > 0.2) m.TokensPerSecond = m.ApproximateTokens / seconds;
            }

            return m;
        }

        #endregion
    }
}
