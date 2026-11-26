namespace Armada.Runtimes.Tools
{
    using System;

    /// <summary>
    /// Output limits for the built-in tools of one API captain run. The hard maximums for read_file output and
    /// run_process output scale to a percentage of the endpoint's context window (RS-7.9). The default read page
    /// stays at 200 lines and 24,000 characters; only the hard maximums scale.
    /// </summary>
    public class ToolLimits
    {
        #region Public-Members

        /// <summary>
        /// Default share of the context window one tool result may fill, in percent.
        /// </summary>
        public const int DefaultWindowPercent = 12;

        /// <summary>
        /// Smallest accepted window percentage.
        /// </summary>
        public const int MinimumWindowPercent = 1;

        /// <summary>
        /// Largest accepted window percentage.
        /// </summary>
        public const int MaximumWindowPercent = 50;

        /// <summary>
        /// Characters per token assumed when converting a token budget to characters (code averages about 3.2).
        /// </summary>
        public const double CharactersPerToken = 3.2;

        /// <summary>
        /// read_file output maximum when the context window is unknown (the 1.4.0 file-size cap, 1 MB).
        /// </summary>
        public const int FallbackMaxReadCharacters = 1_048_576;

        /// <summary>
        /// run_process output maximum per stream when the context window is unknown (the 1.4.0 cap, 100 KB).
        /// </summary>
        public const int FallbackMaxProcessOutputBytes = 102_400;

        /// <summary>
        /// Floor for the scaled read maximum: the default read page, so paging behaves the same on every window.
        /// </summary>
        public const int MinimumReadCharacters = 24_000;

        /// <summary>
        /// Floor for the scaled process output maximum per stream.
        /// </summary>
        public const int MinimumProcessOutputBytes = 8_192;

        /// <summary>
        /// Ceiling for both scaled maximums.
        /// </summary>
        public const int MaximumOutputBytes = 1_048_576;

        /// <summary>
        /// Default size above which read_file refuses a file instead of paging it, 16 MB.
        /// </summary>
        public const int DefaultMaxReadFileBytes = 16_777_216;

        /// <summary>
        /// The most characters one read_file result returns before it pages, from 24,000 through 1,048,576.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is outside the range.</exception>
        public int MaxReadCharacters
        {
            get { return _MaxReadCharacters; }
            set
            {
                if (value < MinimumReadCharacters || value > MaximumOutputBytes) throw new ArgumentOutOfRangeException(nameof(MaxReadCharacters));
                _MaxReadCharacters = value;
            }
        }

        /// <summary>
        /// The most bytes kept per run_process stream before truncation, from 8,192 through 1,048,576.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is outside the range.</exception>
        public int MaxProcessOutputBytes
        {
            get { return _MaxProcessOutputBytes; }
            set
            {
                if (value < MinimumProcessOutputBytes || value > MaximumOutputBytes) throw new ArgumentOutOfRangeException(nameof(MaxProcessOutputBytes));
                _MaxProcessOutputBytes = value;
            }
        }

        /// <summary>
        /// Files larger than this many bytes are refused by read_file; smaller files are paged. At least 1.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is less than 1.</exception>
        public long MaxReadFileBytes
        {
            get { return _MaxReadFileBytes; }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(MaxReadFileBytes));
                _MaxReadFileBytes = value;
            }
        }

        /// <summary>
        /// The run_process timeout in milliseconds when the call does not give one, at least 1. Defaults to 120 seconds.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is less than 1.</exception>
        public int DefaultProcessTimeoutMs
        {
            get { return _DefaultProcessTimeoutMs; }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(DefaultProcessTimeoutMs));
                _DefaultProcessTimeoutMs = value;
            }
        }

        #endregion

        #region Private-Members

        private int _MaxReadCharacters = FallbackMaxReadCharacters;
        private int _MaxProcessOutputBytes = FallbackMaxProcessOutputBytes;
        private long _MaxReadFileBytes = DefaultMaxReadFileBytes;
        private int _DefaultProcessTimeoutMs = 120_000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Limits for an endpoint's context window. An unknown window keeps the 1.4.0 caps.
        /// </summary>
        /// <param name="contextTokens">The context window in tokens, or null when unknown.</param>
        /// <param name="windowPercent">The share of the window one tool result may fill, or null for 12 percent.</param>
        /// <returns>The scaled limits.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="windowPercent"/> is outside 1 through 50 or <paramref name="contextTokens"/> is not positive.</exception>
        public static ToolLimits ForContextWindow(int? contextTokens, int? windowPercent)
        {
            int percent = windowPercent ?? DefaultWindowPercent;
            if (percent < MinimumWindowPercent || percent > MaximumWindowPercent) throw new ArgumentOutOfRangeException(nameof(windowPercent));
            if (contextTokens.HasValue && contextTokens.Value < 1) throw new ArgumentOutOfRangeException(nameof(contextTokens));

            ToolLimits limits = new ToolLimits();
            if (!contextTokens.HasValue) return limits;

            double budget = contextTokens.Value * (percent / 100.0) * CharactersPerToken;
            int scaled = (int)Math.Min(MaximumOutputBytes, Math.Floor(budget));
            limits.MaxReadCharacters = Math.Max(MinimumReadCharacters, scaled);
            limits.MaxProcessOutputBytes = Math.Max(MinimumProcessOutputBytes, scaled);
            return limits;
        }

        #endregion
    }
}
