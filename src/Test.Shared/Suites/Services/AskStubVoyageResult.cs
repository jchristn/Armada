namespace Test.Shared.Suites.Services
{
    /// <summary>
    /// The result the Ask test harness's dispatch stub returns ({ Id, Title }), read back from a proposal's ResultText.
    /// </summary>
    public class AskStubVoyageResult
    {
        #region Public-Members

        /// <summary>
        /// Voyage id (vyg_stub...).
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Voyage title.
        /// </summary>
        public string? Title { get; set; } = null;

        #endregion
    }
}
