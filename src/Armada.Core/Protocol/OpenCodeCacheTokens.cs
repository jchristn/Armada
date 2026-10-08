namespace Armada.Core.Protocol
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Cache token counts of one OpenCode step.
    /// </summary>
    public class OpenCodeCacheTokens
    {
        #region Public-Members

        /// <summary>
        /// Input tokens read from the prompt cache, or null.
        /// </summary>
        [JsonPropertyName("read")]
        public long? Read { get; set; } = null;

        /// <summary>
        /// Input tokens written to the prompt cache, or null.
        /// </summary>
        [JsonPropertyName("write")]
        public long? Write { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public OpenCodeCacheTokens()
        {
        }

        #endregion
    }
}
