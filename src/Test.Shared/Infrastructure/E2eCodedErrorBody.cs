namespace Test.Shared.Infrastructure
{
    using Armada.Core.Models;
    using WatsonWebserver.Core;

    /// <summary>
    /// A REST error body whose Data carries a machine code (vessel import and similar routes):
    /// { Error, StatusCode, Message, Data: { Code, Path } }.
    /// </summary>
    public class E2eCodedErrorBody
    {
        #region Public-Members

        /// <summary>
        /// Result code.
        /// </summary>
        public ApiResultEnum? Error { get; set; } = null;

        /// <summary>
        /// English message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Machine-readable detail.
        /// </summary>
        public VesselImportErrorDetail? Data { get; set; } = null;

        #endregion
    }
}
