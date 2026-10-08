namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Token usage of one OpenCode step (the <c>tokens</c> of a step-finish part). Input excludes the cache, as in
    /// Anthropic's usage report; the cache counts are in <see cref="Cache"/>.
    /// </summary>
    public class OpenCodeTokens
    {
        #region Public-Members

        /// <summary>
        /// Uncached input tokens, or null.
        /// </summary>
        [JsonPropertyName("input")]
        public long? Input { get; set; } = null;

        /// <summary>
        /// Output tokens, or null.
        /// </summary>
        [JsonPropertyName("output")]
        public long? Output { get; set; } = null;

        /// <summary>
        /// Reasoning tokens (generated, billed as output), or null.
        /// </summary>
        [JsonPropertyName("reasoning")]
        public long? Reasoning { get; set; } = null;

        /// <summary>
        /// Cache reads and writes, or null.
        /// </summary>
        [JsonPropertyName("cache")]
        public OpenCodeCacheTokens? Cache { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public OpenCodeTokens()
        {
        }

        #endregion
    }
}
