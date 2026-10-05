namespace Test.Shared.Suites.Services
{
    /// <summary>
    /// The request body <see cref="RequestHistoryCaptureServiceSuite"/> captures, read back after redaction.
    /// </summary>
    public class RequestHistoryRedactedBody
    {
        #region Public-Members

        /// <summary>
        /// Title (not sensitive).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Password (sensitive key).
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// GitHub token (sensitive key).
        /// </summary>
        public string? GitHubToken { get; set; } = null;

        /// <summary>
        /// Nested object.
        /// </summary>
        public RequestHistoryRedactedNestedBody? Nested { get; set; } = null;

        #endregion
    }
}
