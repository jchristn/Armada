namespace Armada.Server.Ask
{
    using Armada.Core.Models;

    /// <summary>
    /// Outcome of posting a message or requesting a summary: the HTTP status (202 accepted, 400 invalid, 404 not found,
    /// 409 a turn is already running), the response body for 202, and a message otherwise.
    /// </summary>
    public class AskTurnStart
    {
        #region Public-Members

        /// <summary>
        /// HTTP status for the route.
        /// </summary>
        public int StatusCode { get; set; } = 202;

        /// <summary>
        /// Accepted response (MessageId, TurnId), or null.
        /// </summary>
        public AskMessageSendResponse? Response { get; set; } = null;

        /// <summary>
        /// Explanation for non-success statuses, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskTurnStart()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="statusCode">HTTP status.</param>
        /// <param name="response">Accepted response, or null.</param>
        /// <param name="message">Message, or null.</param>
        public AskTurnStart(int statusCode, AskMessageSendResponse? response, string? message)
        {
            StatusCode = statusCode;
            Response = response;
            Message = message;
        }

        #endregion
    }
}
