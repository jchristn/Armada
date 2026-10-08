namespace Armada.Core.Metrics.Charts
{
    using System;

    /// <summary>
    /// Token counts split by type without double counting. Recorded input already includes cache reads (Claude Code's
    /// input is input plus cache read plus cache creation; Codex's cached_input_tokens is part of input_tokens), and the
    /// recorded total is input plus output. So the parts are uncached input (input minus cached), cached input (cached,
    /// never more than input), and output, and they always add up to input plus output.
    /// </summary>
    public class TokenTypeSplit
    {
        #region Public-Members

        /// <summary>
        /// Input tokens not served from a prompt cache.
        /// </summary>
        public long UncachedInput { get; set; } = 0;

        /// <summary>
        /// Input tokens served from a prompt cache.
        /// </summary>
        public long CachedInput { get; set; } = 0;

        /// <summary>
        /// Output tokens.
        /// </summary>
        public long Output { get; set; } = 0;

        /// <summary>
        /// Uncached input plus cached input plus output (the recorded input plus output).
        /// </summary>
        public long Total
        {
            get { return UncachedInput + CachedInput + Output; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TokenTypeSplit()
        {
        }

        /// <summary>
        /// Split recorded counts. Negative counts are treated as zero, and cached is capped at input.
        /// </summary>
        /// <param name="inputTokens">Input tokens, cache reads included.</param>
        /// <param name="outputTokens">Output tokens.</param>
        /// <param name="cachedTokens">Cache-read tokens (part of the input).</param>
        /// <returns>Split.</returns>
        public static TokenTypeSplit From(long inputTokens, long outputTokens, long cachedTokens)
        {
            long input = Math.Max(0, inputTokens);
            long cached = Math.Min(input, Math.Max(0, cachedTokens));
            TokenTypeSplit split = new TokenTypeSplit();
            split.UncachedInput = input - cached;
            split.CachedInput = cached;
            split.Output = Math.Max(0, outputTokens);
            return split;
        }

        #endregion
    }
}
