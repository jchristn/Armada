namespace Armada.Server.Ask
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// Outcome of an approve, reject, or quick-action request: the proposal (when found) plus the HTTP status the route
    /// should return (200, 400, 404, or 409) and an explanatory message for non-success statuses.
    /// </summary>
    public class AskProposalDecision
    {
        #region Public-Members

        /// <summary>
        /// The proposal, or null when not found.
        /// </summary>
        public AskActionProposal? Proposal { get; set; } = null;

        /// <summary>
        /// HTTP status for the route: 200 success, 400 invalid request, 404 not found, 409 not pending.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Explanation for non-success statuses, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskProposalDecision()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="proposal">Proposal, or null.</param>
        /// <param name="statusCode">HTTP status.</param>
        /// <param name="message">Message, or null.</param>
        public AskProposalDecision(AskActionProposal? proposal, int statusCode, string? message)
        {
            Proposal = proposal;
            StatusCode = statusCode;
            Message = message;
        }

        #endregion
    }
}
