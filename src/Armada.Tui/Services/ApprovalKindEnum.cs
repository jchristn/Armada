namespace Armada.Tui.Services
{
    /// <summary>
    /// Kind of item waiting on the user in the Approvals center.
    /// </summary>
    public enum ApprovalKindEnum
    {
        /// <summary>
        /// An Ask Armada proposal awaiting approve or reject.
        /// </summary>
        AskProposal = 0,

        /// <summary>
        /// A mission in Review.
        /// </summary>
        MissionReview = 1,

        /// <summary>
        /// A deployment pending approval.
        /// </summary>
        DeploymentApproval = 2,

        /// <summary>
        /// A mission whose landing failed.
        /// </summary>
        FailedLanding = 3,

        /// <summary>
        /// A stalled captain.
        /// </summary>
        StalledCaptain = 4
    }
}
