namespace Test.Shared.Infrastructure
{
    using Armada.Core.Models;
    using WatsonWebserver.Core;

    /// <summary>
    /// A fleet action REST error body whose Data names an unknown template variable:
    /// { Error, Message, Data: { Code, VariableName } }.
    /// </summary>
    public class E2eFleetActionTemplateErrorBody
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
        public FleetActionTemplateErrorDetail? Data { get; set; } = null;

        #endregion
    }
}
