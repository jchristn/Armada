namespace Test.Shared.Infrastructure
{
    using Armada.Core.Models;
    using WatsonWebserver.Core;

    /// <summary>
    /// A REST 409 body for a duplicate entity: { Error, StatusCode, Message, Data: DuplicateEntityErrorDetail }.
    /// </summary>
    public class E2eDuplicateErrorBody
    {
        #region Public-Members

        /// <summary>
        /// Result code.
        /// </summary>
        public ApiResultEnum? Error { get; set; } = null;

        /// <summary>
        /// HTTP status code of the result code.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// English message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Machine-readable detail.
        /// </summary>
        public DuplicateEntityErrorDetail? Data { get; set; } = null;

        #endregion
    }
}
