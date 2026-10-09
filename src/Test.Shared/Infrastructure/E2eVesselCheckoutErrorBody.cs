namespace Test.Shared.Infrastructure
{
    using Armada.Core.Models;
    using WatsonWebserver.Core;

    /// <summary>
    /// A REST 409 body for a vessel with no checkout Armada can use: { Error, StatusCode, Message, Data:
    /// VesselCheckoutErrorDetail }.
    /// </summary>
    public class E2eVesselCheckoutErrorBody
    {
        #region Public-Members

        /// <summary>
        /// Result code.
        /// </summary>
        public ApiResultEnum? Error { get; set; } = null;

        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Message (what to set).
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Typed detail.
        /// </summary>
        public VesselCheckoutErrorDetail? Data { get; set; } = null;

        #endregion
    }
}
