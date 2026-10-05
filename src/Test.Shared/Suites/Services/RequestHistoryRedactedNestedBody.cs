namespace Test.Shared.Suites.Services
{
    /// <summary>
    /// Nested object inside <see cref="RequestHistoryRedactedBody"/>.
    /// </summary>
    public class RequestHistoryRedactedNestedBody
    {
        #region Public-Members

        /// <summary>
        /// API key (sensitive key).
        /// </summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>
        /// Per-vessel GitHub token override (sensitive key).
        /// </summary>
        public string? GitHubTokenOverride { get; set; } = null;

        #endregion
    }
}
