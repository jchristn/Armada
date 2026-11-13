namespace Test.Shared.Infrastructure
{
    using Armada.Core.Models;
    using WatsonWebserver.Core;

    /// <summary>
    /// REST error body for a collection over its limit (400 with a CollectionLimitErrorDetail in Data).
    /// </summary>
    public class E2eLimitErrorBody
    {
        /// <summary>
        /// Result code.
        /// </summary>
        public ApiResultEnum? Error { get; set; } = null;

        /// <summary>
        /// Message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Detail.
        /// </summary>
        public CollectionLimitErrorDetail? Data { get; set; } = null;
    }
}
