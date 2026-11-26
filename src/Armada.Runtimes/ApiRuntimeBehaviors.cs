namespace Armada.Runtimes
{
    using System;

    /// <summary>
    /// Switches for the API runtime's open-model behaviors added in 1.5.0. Each can be turned off to reproduce 1.4.0
    /// behavior, for example when a Harbor does not advertise the matching capability flag. All are on by default.
    /// </summary>
    public sealed class ApiRuntimeBehaviors
    {
        #region Public-Members

        /// <summary>
        /// Largest accepted value for <see cref="MalformedCallRetries"/>.
        /// </summary>
        public const int MaxMalformedCallRetries = 5;

        /// <summary>
        /// Consecutive corrective retries after a malformed tool call or a degenerate generation (RS-7.3). Such a failure
        /// is not resent unchanged: the next request carries a corrective turn and a varied temperature. 0 restores the
        /// 1.4.0 behavior of resending the identical request. Default 2, range 0 to <see cref="MaxMalformedCallRetries"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is outside 0 to <see cref="MaxMalformedCallRetries"/>.</exception>
        public int MalformedCallRetries
        {
            get { return _MalformedCallRetries; }
            set
            {
                if (value < 0 || value > MaxMalformedCallRetries) throw new ArgumentOutOfRangeException(nameof(MalformedCallRetries));
                _MalformedCallRetries = value;
            }
        }

        /// <summary>
        /// Strip leaked reasoning (complete think blocks, a bare closing tag, an unclosed leading block) from visible
        /// text and from assistant text kept in history (RS-7.6). Provider continuation state is never changed.
        /// When off, only complete think blocks are removed from visible text, as in 1.4.0.
        /// </summary>
        public bool ReasoningHygiene { get; set; } = true;

        /// <summary>
        /// Load an Ollama endpoint's model with keep_alive before the first turn (RS-7.16). A failed load only logs.
        /// </summary>
        public bool ModelWarmup { get; set; } = true;

        /// <summary>
        /// Recover tool calls a model wrote as text (Hermes tool_call blocks, function tags, fenced JSON) and run them as
        /// real calls (RS-7.2). Never applied to a failed response or one cut at the output-token limit. When off, such
        /// text ends the turn as an ordinary reply, as in 1.4.0.
        /// </summary>
        public bool TextToolCallRecovery { get; set; } = true;

        #endregion

        #region Private-Members

        private int _MalformedCallRetries = 2;

        #endregion
    }
}
