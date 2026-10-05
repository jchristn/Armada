namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// A REST error body read with Error as plain text, so codes outside the framework's error set (for example
    /// NotImplemented) can be compared.
    /// </summary>
    public class E2eStatusErrorBody
    {
        #region Public-Members

        /// <summary>
        /// Error code.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Status code carried in the body.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Detail message.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion
    }
}
