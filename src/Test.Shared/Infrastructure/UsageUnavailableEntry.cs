namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A runtime whose account-limit interface is explicitly unavailable, with the reason and the
    /// condition that would justify rechecking it.
    /// </summary>
    public class UsageUnavailableEntry
    {
        #region Public-Members

        /// <summary>
        /// Runtime the entry applies to, matching <c>AgentRuntimeEnum</c>.
        /// </summary>
        [JsonPropertyName("runtime")]
        public string Runtime { get; set; } = string.Empty;

        /// <summary>
        /// Why no interface is available.
        /// </summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// The observation that establishes the absence.
        /// </summary>
        [JsonPropertyName("evidence")]
        public string Evidence { get; set; } = string.Empty;

        /// <summary>
        /// What would justify rechecking the runtime.
        /// </summary>
        [JsonPropertyName("retryCondition")]
        public string RetryCondition { get; set; } = string.Empty;

        #endregion
    }
}
