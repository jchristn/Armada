namespace Armada.Tui.Screens.Ask
{
    using System;
    using Armada.Core.Models;
    using Armada.Tui.Services;

    /// <summary>
    /// A pending decision the highlighted part of the Ask transcript belongs to: a proposal (its card, or the captain
    /// reply of the turn that proposed it), a CLI permission request (its card, the reply of its turn, or a work card row
    /// of its mission), or a mission review (a work card row). Exactly one of <see cref="Proposal"/>,
    /// <see cref="CliRequest"/>, and <see cref="Review"/> is set, matching <see cref="Kind"/>.
    /// </summary>
    public class AskPendingDecision
    {
        #region Public-Members

        /// <summary>
        /// Kind.
        /// </summary>
        public AskPendingDecisionKindEnum Kind { get; }

        /// <summary>
        /// The proposal, for <see cref="AskPendingDecisionKindEnum.Proposal"/>.
        /// </summary>
        public AskActionProposal? Proposal { get; }

        /// <summary>
        /// The request, for <see cref="AskPendingDecisionKindEnum.CliPermission"/>.
        /// </summary>
        public CliPermissionRequest? CliRequest { get; }

        /// <summary>
        /// The approval item, for <see cref="AskPendingDecisionKindEnum.MissionReview"/>.
        /// </summary>
        public ApprovalItem? Review { get; }

        /// <summary>
        /// Identifier (proposal, request, or mission id).
        /// </summary>
        public string Id
        {
            get { return Proposal?.Id ?? CliRequest?.Id ?? Review?.EntityId ?? ""; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// A proposal decision.
        /// </summary>
        /// <param name="proposal">Pending proposal.</param>
        public AskPendingDecision(AskActionProposal proposal)
        {
            Proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
            Kind = AskPendingDecisionKindEnum.Proposal;
        }

        /// <summary>
        /// A CLI permission decision.
        /// </summary>
        /// <param name="request">Pending request.</param>
        public AskPendingDecision(CliPermissionRequest request)
        {
            CliRequest = request ?? throw new ArgumentNullException(nameof(request));
            Kind = AskPendingDecisionKindEnum.CliPermission;
        }

        /// <summary>
        /// A mission review decision.
        /// </summary>
        /// <param name="review">Mission review approval item.</param>
        public AskPendingDecision(ApprovalItem review)
        {
            Review = review ?? throw new ArgumentNullException(nameof(review));
            Kind = AskPendingDecisionKindEnum.MissionReview;
        }

        #endregion
    }
}
