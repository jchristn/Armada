namespace Armada.Core.Models
{
    /// <summary>
    /// What context discovery found for an endpoint's model: the window in tokens and where it was read, or why nothing
    /// was found.
    /// </summary>
    public class ModelContextDiscoveryResult
    {
        /// <summary>
        /// Context window in tokens, or null when the server did not report one.
        /// </summary>
        public int? ContextLength { get; set; } = null;

        /// <summary>
        /// Where the value was read (for example "ollama /api/ps", "ollama /api/show num_ctx", "/v1/models max_model_len"),
        /// or null when nothing was found.
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Why nothing was found, or null when a value was found.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// True when a value was found.
        /// </summary>
        public bool Found
        {
            get => ContextLength.HasValue;
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelContextDiscoveryResult()
        {
        }
    }
}
