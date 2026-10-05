namespace Armada.Core.Models
{
    /// <summary>
    /// Wire values of <see cref="InboxItem.Kind"/>. The kind is serialized as these snake_case strings (stable API
    /// contract), so producers and consumers compare against these constants rather than literals.
    /// </summary>
    public static class InboxItemKinds
    {
        #region Public-Members

        /// <summary>A mission is awaiting review.</summary>
        public const string Review = "review";

        /// <summary>A mission's landing failed.</summary>
        public const string LandingFailed = "landing_failed";

        /// <summary>A mission failed.</summary>
        public const string Failed = "failed";

        /// <summary>A captain is stalled.</summary>
        public const string StalledCaptain = "stalled_captain";

        /// <summary>A merge queue entry failed.</summary>
        public const string MergeFailed = "merge_failed";

        /// <summary>A deployment is awaiting approval.</summary>
        public const string DeploymentApproval = "deployment_approval";

        /// <summary>A deployment failed.</summary>
        public const string DeploymentFailed = "deployment_failed";

        /// <summary>An Ask Armada action proposal is waiting for approval.</summary>
        public const string AskProposal = "ask_proposal";

        /// <summary>A CLI captain's permission prompt (for example a shell command) is waiting for an approver.</summary>
        public const string CliPermission = "cli_permission";

        #endregion
    }
}
