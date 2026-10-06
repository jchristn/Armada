namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// One request seen by <see cref="RecordingAdmiralStub"/>.
    /// </summary>
    public sealed class RecordedAdmiralRequest
    {
        #region Public-Members

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method { get; set; } = "";

        /// <summary>
        /// Path without the query.
        /// </summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Authorization header, or null.
        /// </summary>
        public string? Authorization { get; set; } = null;

        /// <summary>
        /// X-Token header, or null.
        /// </summary>
        public string? SessionToken { get; set; } = null;

        /// <summary>
        /// X-Api-Key header, or null.
        /// </summary>
        public string? ApiKey { get; set; } = null;

        #endregion
    }
}
