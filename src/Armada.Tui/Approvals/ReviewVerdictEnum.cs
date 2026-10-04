namespace Armada.Tui.Approvals
{
    /// <summary>
    /// How a mission review gate is resolved (the dashboard's Resolve Review choices).
    /// </summary>
    public enum ReviewVerdictEnum
    {
        /// <summary>
        /// Accept this stage and continue (feedback optional).
        /// </summary>
        Approve = 0,

        /// <summary>
        /// Continue, but the next step must consider the feedback (feedback required).
        /// </summary>
        Conditional = 1,

        /// <summary>
        /// Redo the same step with the feedback (feedback required).
        /// </summary>
        MoreWork = 2,

        /// <summary>
        /// Reject this stage and fail the pipeline (feedback optional).
        /// </summary>
        Deny = 3
    }
}
